using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;


namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class IlluminateModel : PageModel
    {
        public record PreviewItem(string Header, string Code, bool ExistsInStandards);

        [BindProperty, Required] public int DistrictId { get; set; }
        [BindProperty, Required] public string TestId { get; set; } = "";
        [BindProperty] public string Delimiter { get; set; } = "tab";
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty, Required] public string Subject { get; set; } = "";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;


        public bool HasPreview => PreviewRows.Count > 0;
        public List<PreviewItem> PreviewRows { get; private set; } = new();
        public HashSet<string> MissingCodes { get; private set; } = new();
        public string? ImportBatchId { get; private set; }
        public List<SelectListItem> DistrictOptions { get; private set; } = new();

        private readonly IConfiguration _config;
        public IlluminateModel(IConfiguration config) => _config = config;

        // Debug: full header scan
        public record HeaderDebug(string Header, bool Matched, string? Code);
        public List<HeaderDebug> DebugHeaders { get; private set; } = new();
        public int TotalHeaders { get; private set; }
        public int MatchedHeaders { get; private set; }

        // CCSS.ELA-Literacy.<CODE> Points -> capture <CODE>
        private static readonly Regex CcssRe = new(
            @"CCSS\.ELA-Literacy\.([A-Z]{1,4}\.\d+(?:\.\d+)*(?:\.[a-z])?)\s+Points?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task OnGet()
        {
            await LoadDistrictOptionsAsync();
        }

        public async Task<IActionResult> OnPostPreview()
        {
            await LoadDistrictOptionsAsync();

            if (!ModelState.IsValid)
                return Page();

            if (!ModelState.IsValid)
                return Page();

            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a file.");
                return Page();
            }

            ViewData["PreviewRan"] = true;

            // Persist the upload to a temp file for Import
            var temp = Path.Combine(Path.GetTempPath(), $"assess_{Guid.NewGuid():N}.txt");
            using (var fs = System.IO.File.Create(temp))
                await Upload.CopyToAsync(fs);
            TempPath = temp;

            var delimiterChar = Delimiter?.Equals("comma", StringComparison.OrdinalIgnoreCase) == true ? ',' : '\t';

            using var sr = new StreamReader(System.IO.File.OpenRead(temp), Encoding.UTF8, true);
            var header = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header))
            {
                ModelState.AddModelError("", "Missing header row.");
                return Page();
            }

            var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();

            // Build full debug scan so you can see what matched
            DebugHeaders = headers.Select(h =>
            {
                var m = CcssRe.Match(h);
                return new HeaderDebug(h, m.Success, m.Success ? m.Groups[1].Value : null);
            }).ToList();
            TotalHeaders = DebugHeaders.Count;
            MatchedHeaders = DebugHeaders.Count(h => h.Matched);

            // Extract the standards from headers that match
            var headerMap = DebugHeaders
                .Where(h => h.Matched && !string.IsNullOrWhiteSpace(h.Code))
                .Select(h => new { Header = h.Header, Code = h.Code! })
                .ToList();

            // Load existing standards (no TVP required; parameterized IN)
            var codes = headerMap.Select(m => m.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var existing = await LoadExistingStandards(codes);

            PreviewRows = headerMap
                .Select(m => new PreviewItem(m.Header, m.Code, existing.Contains(m.Code)))
                .ToList();

            MissingCodes = new HashSet<string>(codes.Where(c => !existing.Contains(c)), StringComparer.OrdinalIgnoreCase);

            if (MatchedHeaders == 0)
            {
                ModelState.AddModelError("",
                    "No standards were detected in the headers. " +
                    "Check the delimiter (TSV vs CSV) and confirm columns end with 'CCSS.ELA-Literacy.<code> Points'.");
            }

            // Optional: quick student-localID validation on the first ~2000 rows
            var localIds = new HashSet<int>();
            string? line;
            int scanned = 0;
            while ((line = await sr.ReadLineAsync()) != null && scanned < 2000)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Split(delimiterChar);
                if (cells.Length > 0 && int.TryParse(cells[0].Trim(), out var lid))
                    localIds.Add(lid);
                scanned++;
            }
            if (localIds.Count > 0)
            {
                var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, localIds);
                ViewData["StudentValidationSummary"] =
                    $"{found}/{localIds.Count} local IDs match students in district {DistrictId}" +
                    (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : "");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostImport()
        {
            await LoadDistrictOptionsAsync();


            if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "Temp file not found. Please re-run preview.");
                return Page();
            }

            var delimiterChar = Delimiter?.Equals("comma", StringComparison.OrdinalIgnoreCase) == true ? ',' : '\t';

            // Build TVP rows from the file **inside** a using block so we release the handle
            var tvp = new DataTable();
            tvp.Columns.Add("local_student_id", typeof(int));
            tvp.Columns.Add("test_id", typeof(string));
            tvp.Columns.Add("human_coding_scheme", typeof(string));
            tvp.Columns.Add("points", typeof(decimal));
            tvp.Columns.Add("max_points", typeof(decimal));

            // Read and parse file (header + rows)
            using (var sr = new StreamReader(System.IO.File.Open(TempPath, FileMode.Open, FileAccess.Read, FileShare.Read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var header = await sr.ReadLineAsync() ?? "";
                var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();

                var standardCols = headers
                    .Select((h, i) => new { Header = h, Index = i, M = CcssRe.Match(h) })
                    .Where(x => x.M.Success)
                    .Select(x => new { x.Header, x.Index, Code = x.M.Groups[1].Value })
                    .ToList();

                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = line.Split(delimiterChar);

                    // 0..4 are student info; points start after
                    if (cells.Length < 5) continue;

                    // FIRST COLUMN is the file's localstudentid
                    if (!int.TryParse(cells[0].Trim(), out var localStudentId)) continue;

                    foreach (var sc in standardCols)
                    {
                        if (sc.Index >= cells.Length) continue;
                        var v = cells[sc.Index].Trim();
                        if (string.IsNullOrEmpty(v)) continue;

                        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var pts))
                        {
                            var r = tvp.NewRow();
                            r["local_student_id"] = localStudentId;
                            r["test_id"] = TestId;
                            r["human_coding_scheme"] = sc.Code;
                            r["points"] = pts;
                            r["max_points"] = DBNull.Value;
                            tvp.Rows.Add(r);
                        }
                    }
                }
            } // <— StreamReader disposed here; file no longer locked

            // Execute proc (unchanged)
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.ImportAssessmentResults", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
            cmd.Parameters.AddWithValue("@TestId", TestId);
            cmd.Parameters.AddWithValue("@SourceFile", Path.GetFileName(TempPath));
            cmd.Parameters.AddWithValue("@Subject", Subject);        // NEW
            cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);     // NEW

            var p = cmd.Parameters.AddWithValue("@Rows", tvp);
            p.SqlDbType = SqlDbType.Structured;
            p.TypeName = "dbo.AssessmentResultImportType";


            var batchId = (await cmd.ExecuteScalarAsync())?.ToString();
            ImportBatchId = batchId;

            // Try to delete the temp file now that the handle is closed
            TryDelete(TempPath);
            TempPath = null;

            return Page();
        }

        // Helper: best-effort delete to avoid crashing on a stale lock
        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); }
            catch (IOException) { /* log if you like; safe to ignore */ }
            catch (UnauthorizedAccessException) { /* log if you like; safe to ignore */ }
        }


        // ---- Helpers -------------------------------------------------------------------------

        // Parameterized IN (no user-defined TVP required)
        private async Task<HashSet<string>> LoadExistingStandards(IEnumerable<string> codes)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return result;

            var sb = new StringBuilder();
            sb.Append("SELECT human_coding_scheme FROM dbo.standards WHERE human_coding_scheme IN (");
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
                cmd.Parameters.AddWithValue($"@p{i}", list[i]);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(rdr.GetString(0));

            return result;
        }

        // Validate that localstudentid exists in students for the selected district
        private async Task<(int found, List<int> missingSample)> ValidateLocalStudents(int districtId, HashSet<int> localIds)
        {
            if (localIds.Count == 0) return (0, new List<int>());

            // Convert our ids to strings since students.localstudentid is NVARCHAR
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

            // Ensure params are NVARCHAR to match the column type
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
                    // localstudentid is NVARCHAR -> read as string, then TryParse
                    var s = rdr.GetString(0);
                    if (int.TryParse(s, out var lid))
                        found.Add(lid);
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

            // If your table uses a different display column (e.g., Name), change [districtname] below.
            var sql = @"SELECT Id, Name FROM dbo.districts ORDER BY Name;";
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                var id = rdr.GetInt32(0);
                var name = rdr.IsDBNull(1) ? id.ToString() : rdr.GetString(1);
                DistrictOptions.Add(new SelectListItem { Value = id.ToString(), Text = name });
            }

            // If nothing loaded, at least offer the current DistrictId (if provided)
            if (DistrictOptions.Count == 0 && DistrictId > 0)
            {
                DistrictOptions.Add(new SelectListItem { Value = DistrictId.ToString(), Text = $"District {DistrictId}" });
            }
        }

    }
}
