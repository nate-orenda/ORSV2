using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class IlluminateModel : PageModel
    {
        public record PreviewItem(string Header, string Code, bool ExistsInStandards);

        [BindProperty, Required] public int DistrictId { get; set; }
        [BindProperty] public string TestId { get; set; } = "";
        [BindProperty] public string Delimiter { get; set; } = "tab";
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty, Required] public string Subject { get; set; } = "";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;
        [BindProperty] public decimal DefaultMaxPoints { get; set; } = 5.0m;

        public bool HasPreview => PreviewRows.Count > 0;
        public List<PreviewItem> PreviewRows { get; private set; } = new();
        public HashSet<string> MissingCodes { get; private set; } = new();
        public string? ImportBatchId { get; private set; }
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public string DetectedFormat { get; private set; } = "Unknown";

        private readonly IConfiguration _config;
        public IlluminateModel(IConfiguration config) => _config = config;

        public record HeaderDebug(string Header, bool Matched, string? Code);
        public List<HeaderDebug> DebugHeaders { get; private set; } = new();
        public int TotalHeaders { get; private set; }
        public int MatchedHeaders { get; private set; }

        // Universal pattern: tries to extract standard code from any format
        private static readonly Regex UniversalStandardRe = new(
            @"([A-Za-z0-9]+(?:\.[A-Za-z0-9\-]+)+)",
            RegexOptions.Compiled);

        private static readonly string[] ClusterLetters = { "A", "B", "C", "D", "E", "F" };

        public async Task OnGet()
        {
            await LoadDistrictOptionsAsync();
        }

        public async Task<IActionResult> OnPostPreview()
        {
            await LoadDistrictOptionsAsync();

            if (!ModelState.IsValid)
                return Page();

            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a file.");
                return Page();
            }

            ViewData["PreviewRan"] = true;

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

            // Parse headers to extract standard codes and metadata
            var headerMap = ParseHeaders(headers);
            DetectedFormat = DetermineFormat(headers, headerMap);

            // Auto-fill TestId if blank
            if (string.IsNullOrWhiteSpace(TestId))
            {
                if (headerMap.Any() && !string.IsNullOrWhiteSpace(headerMap[0].TestName))
                {
                    TestId = headerMap[0].TestName;
                }
                else
                {
                    TestId = $"{Subject} Cycle {UnitCycle}";
                }
                ModelState.Remove(nameof(TestId));
                ViewData["AutoTestId"] = true;
            }

            // Build candidate set
            var codesToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in headerMap)
            {
                codesToCheck.Add(m.CodeRaw);
                AddClusterCandidates(m.CodeRaw, codesToCheck);
            }

            var existing = await LoadExistingStandards(codesToCheck);

            // Debug table
            DebugHeaders = headers.Select(h =>
            {
                var match = UniversalStandardRe.Match(h);
                if (!match.Success) return new HeaderDebug(h, false, null);

                var original = match.Groups[1].Value.Trim();
                var stripped = StripElaPrefix(original);
                var normalized = NormalizeCode(stripped, existing);

                string display = original.Equals(stripped) && stripped.Equals(normalized)
                    ? stripped
                    : $"{original} → {normalized}";

                return new HeaderDebug(h, true, display);
            }).ToList();

            TotalHeaders = DebugHeaders.Count;
            MatchedHeaders = DebugHeaders.Count(h => h.Matched);

            PreviewRows = headerMap
                .Select(m =>
                {
                    var codeNorm = NormalizeCode(m.CodeRaw, existing);
                    return new PreviewItem(m.Header, codeNorm, existing.Contains(codeNorm));
                })
                .ToList();

            MissingCodes = new HashSet<string>(
                PreviewRows.Where(r => !r.ExistsInStandards).Select(r => r.Code),
                StringComparer.OrdinalIgnoreCase);

            if (MatchedHeaders == 0)
            {
                ModelState.AddModelError("", "No standards were detected in the headers. Please check the file format.");
            }
            if (string.IsNullOrWhiteSpace(TestId))
            {
                ModelState.AddModelError("", "Couldn't determine Test ID. Please enter a Test ID.");
            }

            // Validate student IDs
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
                var (foundCount, missingSample) = await ValidateLocalStudents(DistrictId, localIds);
                if (missingSample.Count > 0)
                {
                    var sampleStr = string.Join(", ", missingSample.Take(10));
                    ModelState.AddModelError("",
                        $"Some student IDs not found. Found {foundCount}/{localIds.Count}. Missing (sample): {sampleStr}");
                }
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

            var tvp = new DataTable();
            tvp.Columns.Add("local_student_id", typeof(string));
            tvp.Columns.Add("human_coding_scheme", typeof(string));
            tvp.Columns.Add("points", typeof(decimal));
            tvp.Columns.Add("max_points", typeof(decimal));
            tvp.Columns.Add("standard_id", typeof(string));
            tvp.Columns.Add("tested_date", typeof(DateTime));

            using (var sr = new StreamReader(System.IO.File.Open(TempPath, FileMode.Open, FileAccess.Read, FileShare.Read), 
                Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var header = await sr.ReadLineAsync() ?? "";
                var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();

                // Parse headers
                var standardColsRaw = ParseHeaders(headers).Select((h, i) => new
                {
                    Header = h.Header,
                    Index = h.Index,
                    CodeRaw = h.CodeRaw,
                    TestName = h.TestName
                }).ToList();

                // Auto-fill TestId if still blank
                if (string.IsNullOrWhiteSpace(TestId))
                {
                    if (standardColsRaw.Any() && !string.IsNullOrWhiteSpace(standardColsRaw[0].TestName))
                    {
                        TestId = standardColsRaw[0].TestName;
                    }
                    else
                    {
                        TestId = $"{Subject} Cycle {UnitCycle}";
                    }
                    ModelState.Remove(nameof(TestId));
                }

                // Build candidate set and fetch existing
                var codesToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in standardColsRaw)
                {
                    codesToCheck.Add(s.CodeRaw);
                    AddClusterCandidates(s.CodeRaw, codesToCheck);
                }
                var existingImport = await LoadExistingStandards(codesToCheck);

                // Final list with normalized codes
                var standardCols = standardColsRaw.Select(s => new
                {
                    s.Header,
                    s.Index,
                    Code = NormalizeCode(s.CodeRaw, existingImport)
                }).ToList();

                if (string.IsNullOrWhiteSpace(TestId))
                {
                    ModelState.AddModelError("", "Couldn't determine Test ID. Please enter a Test ID and re-run Preview.");
                    return Page();
                }

                // Get standard_id GUIDs for all normalized codes
                var allCodes = standardCols.Select(sc => sc.Code).Distinct().ToList();
                var schemeToGuidMap = await GetStandardIdsForSchemes(allCodes);

                // Determine if we should use default max_points
                bool hasPointsSuffix = headers.Any(h => h.EndsWith("Points", StringComparison.OrdinalIgnoreCase));
                decimal? maxPointsValue = hasPointsSuffix ? null : (decimal?)DefaultMaxPoints;

                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = line.Split(delimiterChar);

                    if (cells.Length < 5) continue;

                    var localStudentIdStr = cells[0].Trim();
                    if (string.IsNullOrWhiteSpace(localStudentIdStr)) continue;

                    foreach (var sc in standardCols)
                    {
                        if (sc.Index >= cells.Length) continue;
                        var v = cells[sc.Index].Trim();
                        if (string.IsNullOrEmpty(v)) continue;

                        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var pts))
                        {
                            if (schemeToGuidMap.TryGetValue(sc.Code, out var standardId))
                            {
                                var r = tvp.NewRow();
                                r["local_student_id"] = localStudentIdStr;
                                r["human_coding_scheme"] = sc.Code;
                                r["points"] = pts;
                                r["max_points"] = maxPointsValue.HasValue ? (object)maxPointsValue.Value : DBNull.Value;
                                r["standard_id"] = standardId.ToString("D");
                                r["tested_date"] = DateTime.Today; // Use today's date since Illuminate files don't include test date
                                tvp.Rows.Add(r);
                            }
                        }
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
            cmd.Parameters.AddWithValue("@SourceFile", Path.GetFileName(TempPath));
            cmd.Parameters.AddWithValue("@Subject", Subject);
            cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);

            var p = cmd.Parameters.AddWithValue("@Rows", tvp);
            p.SqlDbType = SqlDbType.Structured;
            p.TypeName = "dbo.AssessmentResultImportType";

            var batchId = (await cmd.ExecuteScalarAsync())?.ToString();
            ImportBatchId = batchId;

            TryDelete(TempPath);
            TempPath = null;

            return Page();
        }

        // --- Helpers --------------------------------------------------------------------------

        private record HeaderParse(string Header, int Index, string TestName, string CodeRaw);

        private List<HeaderParse> ParseHeaders(string[] headers)
        {
            var result = new List<HeaderParse>();

            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i].Trim();
                if (string.IsNullOrWhiteSpace(h)) continue;

                // Skip standard non-assessment columns
                if (h.Equals("Student ID", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Last Name", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("First Name", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Middle Name", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Current Grade Level", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("Teacher Name", StringComparison.OrdinalIgnoreCase) ||
                    h.Contains("# of Standards", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Try to extract standard code
                var match = UniversalStandardRe.Match(h);
                if (!match.Success) continue;

                var code = match.Groups[1].Value.Trim();
                code = StripElaPrefix(code);

                // Try to determine test name from header
                string testName = "";
                if (h.Contains("Points", StringComparison.OrdinalIgnoreCase))
                {
                    // C1 format: extract everything before the code
                    var idx = h.IndexOf(code, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        testName = h.Substring(0, idx).Trim();
                    }
                }

                result.Add(new HeaderParse(h, i, testName, code));
            }

            return result;
        }

        private string DetermineFormat(string[] headers, List<HeaderParse> parsed)
        {
            bool hasPointsSuffix = headers.Any(h => h.EndsWith("Points", StringComparison.OrdinalIgnoreCase));
            bool hasDateInHeader = parsed.Any(p => !string.IsNullOrWhiteSpace(p.TestName) && 
                Regex.IsMatch(p.TestName, @"\d{2,4}-\d{2}"));
            bool hasTeacherColumn = headers.Any(h => h.Equals("Teacher Name", StringComparison.OrdinalIgnoreCase));

            if (hasPointsSuffix && hasDateInHeader)
                return "C1 (with dates)";
            else if (hasPointsSuffix)
                return "C1 (simple)";
            else if (hasTeacherColumn)
                return "Cycle2 (with teacher)";
            else
                return "Cycle2 (simple)";
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string StripElaPrefix(string code)
        {
            const string prefix = "CCSS.ELA-Literacy.";
            return code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? code.Substring(prefix.Length).Trim()
                : code.Trim();
        }

        private static void AddClusterCandidates(string codeRaw, HashSet<string> dest)
        {
            var mm = Regex.Match(codeRaw, @"^(?<g>\d+)\.(?<dom>[A-Z]{1,3})\.(?<num>\d+)(?<sub>\.[a-z])?$", RegexOptions.IgnoreCase);
            if (!mm.Success) return;
            var g = mm.Groups["g"].Value;
            var dom = mm.Groups["dom"].Value.ToUpperInvariant();
            var num = mm.Groups["num"].Value;
            var sub = mm.Groups["sub"].Success ? mm.Groups["sub"].Value.ToLowerInvariant() : "";
            foreach (var cl in ClusterLetters)
                dest.Add($"{g}.{dom}.{cl}.{num}{sub}");
        }

        private string NormalizeCode(string code, HashSet<string> existing)
        {
            if (existing.Contains(code)) return code;

            var m = Regex.Match(code, @"^(?<g>\d+)\.(?<dom>[A-Z]{1,3})\.(?<num>\d+)(?<sub>\.[a-z])?$", RegexOptions.IgnoreCase);
            if (!m.Success) return code;

            var g = m.Groups["g"].Value;
            var dom = m.Groups["dom"].Value.ToUpperInvariant();
            var num = m.Groups["num"].Value;
            var sub = m.Groups["sub"].Success ? m.Groups["sub"].Value.ToLowerInvariant() : "";

            foreach (var cl in ClusterLetters)
            {
                var candidate = $"{g}.{dom}.{cl}.{num}{sub}";
                if (existing.Contains(candidate)) return candidate;
            }
            return code;
        }

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
                    if (int.TryParse(s, out var lid))
                        found.Add(lid);
                }
            }

            var missing = localIds.Where(x => !found.Contains(x)).Take(20).ToList();
            return (found.Count, missing);
        }

        private async Task<Dictionary<string, Guid>> GetStandardIdsForSchemes(IEnumerable<string> schemes)
        {
            var schemeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var schemeList = schemes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!schemeList.Any()) return schemeMap;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var parameters = new List<SqlParameter>();
            var sqlBuilder = new StringBuilder("SELECT human_coding_scheme, id FROM dbo.standards WHERE human_coding_scheme IN (");

            for (int i = 0; i < schemeList.Count; i++)
            {
                var paramName = $"@p{i}";
                sqlBuilder.Append(paramName);
                if (i < schemeList.Count - 1) sqlBuilder.Append(',');

                parameters.Add(new SqlParameter(paramName, schemeList[i]));
            }
            sqlBuilder.Append(')');

            using var cmd = new SqlCommand(sqlBuilder.ToString(), conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var scheme = rdr.GetString(0);
                
                if (Guid.TryParse(rdr.GetString(1), out var id))
                {
                    schemeMap[scheme] = id;
                }
            }
            return schemeMap;
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
            {
                DistrictOptions.Add(new SelectListItem { Value = DistrictId.ToString(), Text = $"District {DistrictId}" });
            }
        }
    }
}
