using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class LennectionsModel : PageModel
    {
        // --- UI state ---
        [BindProperty, Required] public int DistrictId { get; set; }
        [BindProperty] public string TestId { get; set; } = "";
        [BindProperty] public string Delimiter { get; set; } = "comma";
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty, Required] public string Subject { get; set; } = "";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;

        public bool HasPreview => Preview.Count > 0;
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public string? ImportBatchId { get; private set; }

        // --- PREVIEW STRUCTURES ---
        public record StandardPreview(Guid StandardId, string? HumanCodingScheme, bool ExistsInStandards);
        public List<StandardPreview> Preview { get; private set; } = new();
        public List<Guid> MissingIds { get; private set; } = new();
        public string? AutoDetectedTestId { get; private set; }
        public string? StudentValidationSummary { get; private set; }

        private readonly IConfiguration _config;
        public LennectionsModel(IConfiguration config) => _config = config;

        private static readonly string[] PossibleTestHeaders = { "Assessment Name", "Assessment", "Test Name", "Test" };

        public async Task OnGet()
        {
            await LoadDistrictOptionsAsync();
        }

        public async Task<IActionResult> OnPostPreview()
        {
            // ***FIX***: Clear any lingering success messages from previous imports
            TempData.Remove("ImportSuccessMessage");
            TempData.Remove("ImportBatchId");

            await LoadDistrictOptionsAsync();
            ModelState.Remove(nameof(Subject));
            ModelState.Remove(nameof(DistrictId));

            if (!ModelState.IsValid) return Page();
            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a CSV file.");
                return Page();
            }

            var temp = Path.Combine(Path.GetTempPath(), $"lenx_{Guid.NewGuid():N}.csv");
            using (var fs = System.IO.File.Create(temp))
                await Upload.CopyToAsync(fs);
            TempPath = temp;

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

            using var sr = new StreamReader(System.IO.File.OpenRead(temp), Encoding.UTF8, true);
            var header = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header))
            {
                ModelState.AddModelError("", "Missing header row.");
                return Page();
            }

            var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();

            int colLocalId;
            try { colLocalId = FindStudentIdColumnOrThrow(headers); }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message); return Page();
            }

            // ***FIX***: Read the first data row to find the Assessment Name
            string? firstDataLine = sr.Peek() >= 0 ? await sr.ReadLineAsync() : null;
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var firstDataCells = firstDataLine.Split(delimiterChar);
                int? testNameCol = FindFirst(headers, PossibleTestHeaders);
                if (testNameCol.HasValue && testNameCol.Value < firstDataCells.Length)
                {
                    var detectedTestId = firstDataCells[testNameCol.Value].Trim();
                    if (!string.IsNullOrWhiteSpace(detectedTestId))
                    {
                        AutoDetectedTestId = TestId = detectedTestId;
                    }
                }
            }

            var maps = FindItemColumnBlocks(headers);
            if (maps.Count == 0)
            {
                ModelState.AddModelError("", "Couldn’t find any question columns."); return Page();
            }

            var seenStandardIds = new HashSet<Guid>();
            string? line;
            // The stream reader is already past the first data row, so the loop starts on the second
            while ((line = await sr.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Split(delimiterChar);
                foreach (var qm in maps)
                {
                    if (qm.StdCol >= cells.Length) continue;
                    if (Guid.TryParse((cells[qm.StdCol] ?? "").Trim(), out var standardId))
                    {
                        seenStandardIds.Add(standardId);
                    }
                }
            }

            var existingStandardsMap = await LoadSchemesForIds(seenStandardIds);
            Preview = seenStandardIds
                .Select(id => new StandardPreview(id, existingStandardsMap.GetValueOrDefault(id), existingStandardsMap.ContainsKey(id)))
                .OrderBy(p => p.HumanCodingScheme).ToList();
            MissingIds = Preview.Where(r => !r.ExistsInStandards).Select(r => r.StandardId).ToList();

            // Rewind stream to validate student IDs from the beginning (skipping header)
            sr.BaseStream.Seek(0, SeekOrigin.Begin);
            sr.DiscardBufferedData();
            await sr.ReadLineAsync(); // Skip header
            await sr.ReadLineAsync(); // Skip first data row (with assessment name)

            var localIds = new HashSet<int>();
            int scanned2 = 0;
            while ((line = await sr.ReadLineAsync()) != null && scanned2 < 2000)
            {
                var cells = line.Split(delimiterChar);
                if (colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                    localIds.Add(lid);
                scanned2++;
            }
            if (localIds.Count > 0)
            {
                var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, localIds);
                StudentValidationSummary = $"{found}/{localIds.Count} StudentId values match students in district {DistrictId}"
                                           + (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : "");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostImport()
        {
            await LoadDistrictOptionsAsync();
            if (!ModelState.IsValid)
            {
                return await OnPostPreview();
            }
            if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "Temp file not found. Please re-run Preview."); return Page();
            }

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var tvp = new DataTable();
            tvp.Columns.Add("local_student_id", typeof(int));
            tvp.Columns.Add("test_id", typeof(string));
            tvp.Columns.Add("human_coding_scheme", typeof(string));
            tvp.Columns.Add("points", typeof(decimal));
            tvp.Columns.Add("max_points", typeof(decimal));

            using (var sr = new StreamReader(System.IO.File.Open(TempPath, FileMode.Open, FileAccess.Read, FileShare.Read), Encoding.UTF8, true))
            {
                var header = await sr.ReadLineAsync() ?? string.Empty;
                var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();
                int colLocalId;
                try { colLocalId = FindStudentIdColumnOrThrow(headers); }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", ex.Message); return Page();
                }

                var maps = FindItemColumnBlocks(headers);
                if (maps.Count == 0)
                {
                    ModelState.AddModelError("", "Could not find any question columns."); return Page();
                }

                // ***FIX***: If TestId is blank, read it from the first data row
                if (string.IsNullOrWhiteSpace(TestId))
                {
                    var firstDataLine = sr.Peek() >= 0 ? await sr.ReadLineAsync() : null;
                    if (!string.IsNullOrWhiteSpace(firstDataLine))
                    {
                        var firstDataCells = firstDataLine.Split(delimiterChar);
                        int? testNameCol = FindFirst(headers, PossibleTestHeaders);
                        if (testNameCol.HasValue && testNameCol.Value < firstDataCells.Length)
                        {
                            TestId = firstDataCells[testNameCol.Value].Trim();
                        }
                    }
                }
                else
                {
                     // If TestId was already set, we still need to skip the first data row
                     if(sr.Peek() >= 0) await sr.ReadLineAsync();
                }


                if (string.IsNullOrWhiteSpace(TestId))
                {
                    ModelState.AddModelError("", "Could not detect Test ID. Please enter one manually."); return Page();
                }

                string? line;
                var agg = new Dictionary<(int localId, Guid standardId), (decimal points, int count)>();
                var allIds = new HashSet<Guid>();

                while ((line = await sr.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = line.Split(delimiterChar);
                    if (colLocalId >= cells.Length) continue;
                    if (!int.TryParse(cells[colLocalId].Trim(), out var localId)) continue;

                    foreach (var m in maps)
                    {
                        if (m.ScoreCol >= cells.Length || m.StdCol >= cells.Length) continue;
                        var rawPts = (cells[m.ScoreCol] ?? "").Trim();
                        var idString = (cells[m.StdCol] ?? "").Trim();

                        if (!string.IsNullOrEmpty(idString) && Guid.TryParse(idString, out var standardId) &&
                            decimal.TryParse(rawPts, NumberStyles.Any, CultureInfo.InvariantCulture, out var pts))
                        {
                            allIds.Add(standardId);
                            var key = (localId, standardId);
                            if (agg.TryGetValue(key, out var cur))
                            {
                                agg[key] = (cur.points + pts, cur.count + 1);
                            }
                            else
                            {
                                agg[key] = (pts, 1);
                            }
                        }
                    }
                }

                if (agg.Count == 0)
                {
                    ModelState.AddModelError("", "No valid student rows were found to import. Please check that the 'StudentId' column contains the correct integer-based Local Student IDs, not GUIDs.");
                    return await OnPostPreview();
                }

                var schemesMap = await LoadSchemesForIds(allIds);
                foreach (var kvp in agg)
                {
                    var standardId = kvp.Key.standardId;
                    if (schemesMap.TryGetValue(standardId, out var scheme))
                    {
                        var r = tvp.NewRow();
                        r["local_student_id"] = kvp.Key.localId;
                        r["test_id"] = TestId;
                        r["human_coding_scheme"] = scheme;
                        r["points"] = kvp.Value.points;
                        r["max_points"] = (decimal)kvp.Value.count;
                        tvp.Rows.Add(r);
                    }
                }
            }

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            using var cmd = new SqlCommand("dbo.ImportAssessmentResults", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
            cmd.Parameters.AddWithValue("@TestId", TestId);
            cmd.Parameters.AddWithValue("@SourceFile", Path.GetFileName(TempPath!));
            cmd.Parameters.AddWithValue("@Subject", Subject);
            cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);
            var p = cmd.Parameters.AddWithValue("@Rows", tvp);
            p.SqlDbType = SqlDbType.Structured;
            p.TypeName = "dbo.AssessmentResultImportType";
            var batchId = (await cmd.ExecuteScalarAsync())?.ToString();
            
            // ***FIX***: Use TempData for the success message
            TempData["ImportSuccessMessage"] = $"Import complete! Batch ID: {batchId}. Processed {tvp.Rows.Count} student-standard result rows.";
            TempData["ImportBatchId"] = batchId;

            TryDelete(TempPath);
            TempPath = null;
            return Page();
        }

        // ---------- Helpers ----------

        private record QuestionMap(int Q, string ScoreHeader, int ScoreCol, string StdHeader, int StdCol);

        private List<QuestionMap> FindItemColumnBlocks(string[] headers)
        {
            var maps = new List<QuestionMap>();
            const int blockSize = 7;
            const string itemHeader = "Item";
            const string scoreHeader = "Score";
            const string standardHeader = "ItemStandard";

            int firstItemIndex = -1;
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].Trim().Equals(itemHeader, StringComparison.OrdinalIgnoreCase))
                {
                    firstItemIndex = i;
                    break;
                }
            }
            if (firstItemIndex == -1) return maps;

            for (int i = firstItemIndex; i < headers.Length; i += blockSize)
            {
                if (i + 4 >= headers.Length) break;
                int scoreCol = i + 3;
                int stdCol = i + 4;
                if (headers[scoreCol].Trim().Equals(scoreHeader, StringComparison.OrdinalIgnoreCase) &&
                    headers[stdCol].Trim().Equals(standardHeader, StringComparison.OrdinalIgnoreCase))
                {
                    maps.Add(new QuestionMap(maps.Count + 1, headers[scoreCol], scoreCol, headers[stdCol], stdCol));
                }
            }
            return maps;
        }

        private static string? FindHeaderValue(string[] headers, string[] candidates)
        {
            int? index = FindFirst(headers, candidates);
            return index.HasValue ? headers[index.Value] : null;
        }

        private static int? FindFirst(string[] headers, string[] candidates)
        {
            for (int i = 0; i < headers.Length; i++)
                foreach (var c in candidates)
                    if (headers[i].Trim().Equals(c, StringComparison.OrdinalIgnoreCase))
                        return i;
            return null;
        }

        private static int FindStudentIdColumnOrThrow(string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
                if (headers[i].Trim().Equals("StudentId", StringComparison.OrdinalIgnoreCase))
                    return i;
            
            for (int i = 0; i < headers.Length; i++)
                if (headers[i].Trim().Equals("Local Student ID", StringComparison.OrdinalIgnoreCase))
                    return i;

            throw new InvalidOperationException("Required column 'StudentId' or 'Local Student ID' was not found in the file header.");
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { }
        }

        private static string StripElaPrefix(string code)
        {
            const string prefix = "CCSS.ELA-Literacy.";
            return code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? code.Substring(prefix.Length).Trim()
                : code.Trim();
        }

        private async Task<Dictionary<Guid, string>> LoadSchemesForIds(IEnumerable<Guid> ids)
        {
            var result = new Dictionary<Guid, string>();
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return result;

            var sb = new StringBuilder();
            // Assuming the 'id' column in 'dbo.standards' is NVARCHAR(36) or similar
            sb.Append("SELECT id, human_coding_scheme FROM dbo.standards WHERE id IN (");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"@p{i}");
            }
            sb.Append(')');

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(sb.ToString(), conn);
            for (int i = 0; i < list.Count; i++)
            {
                // Send the GUID as a string to match the likely NVARCHAR column type
                cmd.Parameters.AddWithValue($"@p{i}", list[i].ToString());
            }

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                // ***FIXED***: Read the ID as a string and parse it, preventing the cast error.
                var idString = rdr.GetString(0);
                if (Guid.TryParse(idString, out var guid))
                {
                    result[guid] = rdr.GetString(1);
                }
            }
            return result;
        }

        private async Task<(int found, List<int> missingSample)> ValidateLocalStudents(int districtId, HashSet<int> localIds)
        {
            if (localIds.Count == 0) return (0, new List<int>());
            var idStrings = localIds.Select(x => x.ToString()).ToList();

            var sb = new StringBuilder();
            sb.Append(@"
                SELECT DISTINCT s.localstudentid
                FROM dbo.students s
                WHERE s.districtid = @district
                AND s.localstudentid IN (");
            for (int i = 0; i < idStrings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"@id{i}");
            }
            sb.Append(')');

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(sb.ToString(), conn);
            cmd.Parameters.AddWithValue("@district", districtId);
            for (int i = 0; i < idStrings.Count; i++)
            {
                var p = cmd.Parameters.Add($"@id{i}", SqlDbType.NVarChar, 64);
                p.Value = idStrings[i];
            }

            var found = new HashSet<int>();
            using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var s = rdr.GetString(0);
                    if (int.TryParse(s, out var lid)) found.Add(lid);
                }
            }
            var missing = localIds.Where(x => !found.Contains(x)).Take(20).ToList();
            return (found.Count, missing);
        }

        private async Task LoadDistrictOptionsAsync()
        {
            DistrictOptions.Clear();
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = @"SELECT Id, Name FROM dbo.districts ORDER BY Name;";
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var id = rdr.GetInt32(0);
                var name = rdr.IsDBNull(1) ? id.ToString() : rdr.GetString(1);
                DistrictOptions.Add(new SelectListItem { Value = id.ToString(), Text = name });
            }
            if (DistrictOptions.Count == 0 && DistrictId > 0)
                DistrictOptions.Add(new SelectListItem { Value = DistrictId.ToString(), Text = $"District {DistrictId}" });
        }
    }
}