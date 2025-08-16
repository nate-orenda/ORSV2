// File: Pages/Admin/FileImport/Assessments/Lennections.cshtml.cs
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
    public class LennectionsModel : PageModel
    {
        // --- UI state ---
        [BindProperty, Required] public int DistrictId { get; set; }
        [BindProperty] public string TestId { get; set; } = "";            // auto-detected when possible
        [BindProperty] public string Delimiter { get; set; } = "comma";     // lennections export is usually CSV
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty, Required] public string Subject { get; set; } = "";  // ELA/Math/Science/Social Science
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;

        public bool HasPreview => Preview.Count > 0;
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public string? ImportBatchId { get; private set; }

        // Preview structures
        public record QuestionMap(int Q, string ScoreHeader, int ScoreCol, string StdHeader, int StdCol, string RawStandard);
        public record StandardPreview(string Code, string Normalized, bool ExistsInStandards, int QuestionsMapped);
        public List<QuestionMap> MappedQuestions { get; private set; } = new();
        
        public List<StandardPreview> Preview { get; private set; } = new();
        public string? AutoDetectedTestId { get; private set; }
        public string? StudentValidationSummary { get; private set; }

        private readonly IConfiguration _config;
        public LennectionsModel(IConfiguration config) => _config = config;

        // Patterns we observe in Lennections exports (examples, kept flexible):
        //  - Q1 Points / Q1 StandardID   (or Standard Id / Standard)
        //  - Question 1 Score / Question 1 Standard
        //  - Q01_Score / Q01_Standard
        private static readonly Regex ScoreHeaderRe = new(
            @"^(?:Question\s*)?(?<q>\d{1,2})\s*(?:_|\s|-)?\s*(?:Score|Points|Correct|Raw)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex StandardHeaderRe = new(
            @"^(?:Question\s*)?(?<q>\d{1,2})\s*(?:_|\s|-)?\s*(?:Standard(?:ID)?|Std(?:Id)?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // For auto-detecting TestId/Subject/Unit from header meta if present
        private static readonly string[] PossibleTestHeaders = { "Assessment Name", "Assessment", "Test Name", "Test" };
        private static readonly string[] PossibleSubjectHeaders = { "Subject" };
        private static readonly string[] PossibleUnitHeaders = { "Unit", "Unit Cycle" };
        private static readonly string[] PossibleLocalIdHeaders = { "Local Student ID", "LocalID", "StudentLocalId", "Student Local Id", "Local Id" };

        private static readonly string[] ClusterLetters = { "A", "B", "C", "D", "E", "F" };

        public async Task OnGet()
        {
            await LoadDistrictOptionsAsync();
        }

        // ---------- PREVIEW ----------
        public async Task<IActionResult> OnPostPreview()
        {
            await LoadDistrictOptionsAsync();
            if (!ModelState.IsValid) return Page();
            if (Upload == null || Upload.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a CSV file.");
                return Page();
            }

            // Persist to temp for Import step
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
            int colLocalId = FindFirst(headers, PossibleLocalIdHeaders) ?? 0; // fallback to first col

            // Auto-detect metadata
            if (string.IsNullOrWhiteSpace(TestId))
            {
                var ti = FindFirst(headers, PossibleTestHeaders);
                if (ti.HasValue) AutoDetectedTestId = TestId = headers[ti.Value];
            }
            var subjIdx = FindFirst(headers, PossibleSubjectHeaders);
            if (subjIdx.HasValue && string.IsNullOrWhiteSpace(Subject)) Subject = headers[subjIdx.Value];
            var unitIdx = FindFirst(headers, PossibleUnitHeaders);
            if (unitIdx.HasValue && UnitCycle == 1)
            {
                if (int.TryParse(headers[unitIdx.Value], out var u) && u >= 1 && u <= 5)
                    UnitCycle = u;
            }

            // Build question -> (scoreCol, standardCol)
            var scoreCols = new Dictionary<int, (string header, int idx)>();
            var stdCols = new Dictionary<int, (string header, int idx)>();

            for (int i = 0; i < headers.Length; i++)
            {
                var h = headers[i];
                var ms = ScoreHeaderRe.Match(h);
                if (ms.Success && int.TryParse(ms.Groups["q"].Value, out var qn))
                {
                    scoreCols[qn] = (h, i);
                    continue;
                }
                var mstd = StandardHeaderRe.Match(h);
                if (mstd.Success && int.TryParse(mstd.Groups["q"].Value, out var qn2))
                {
                    stdCols[qn2] = (h, i);
                    continue;
                }
            }

            // Pair up to 25 questions (but allow fewer/more defensively)
            var maxQ = Math.Max(scoreCols.Keys.DefaultIfEmpty(0).Max(), stdCols.Keys.DefaultIfEmpty(0).Max());
            for (int q = 1; q <= Math.Max(25, maxQ); q++)
            {
                if (scoreCols.TryGetValue(q, out var sc) && stdCols.TryGetValue(q, out var st))
                    MappedQuestions.Add(new QuestionMap(q, sc.header, sc.idx, st.header, st.idx, ""));
            }

            if (MappedQuestions.Count == 0)
            {
                ModelState.AddModelError("", "Couldn’t find any Question score/standard column pairs. Make sure headers look like ‘Q1 Points’ and ‘Q1 StandardID’ (or similar).");
                return Page();
            }

            // Scan a sample of rows to build the set of standards we need to validate
            var seenStandards = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // code -> #questions mapped
            var delimiter = delimiterChar;
            string? line;
            int scanned = 0;
            while ((line = await sr.ReadLineAsync()) != null && scanned < 500)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Split(delimiter);
                foreach (var qm in MappedQuestions)
                {
                    if (qm.StdCol >= cells.Length) continue;
                    var codeRaw = StripElaPrefix((cells[qm.StdCol] ?? "").Trim());
                    if (string.IsNullOrEmpty(codeRaw)) continue;
                    seenStandards[codeRaw] = seenStandards.TryGetValue(codeRaw, out var c) ? c + 1 : 1;
                }
                scanned++;
            }

            // Pull existing standards once
            var candidates = new HashSet<string>(seenStandards.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var c in seenStandards.Keys) AddClusterCandidates(c, candidates);
            var existing = await LoadExistingStandards(candidates);

            Preview = seenStandards
                .Select(kvp => new StandardPreview(
                    Code: kvp.Key,
                    Normalized: NormalizeCode(kvp.Key, existing),
                    ExistsInStandards: existing.Contains(kvp.Key) || existing.Contains(NormalizeCode(kvp.Key, existing)),
                    QuestionsMapped: kvp.Value))
                .OrderBy(p => p.Normalized)
                .ToList();

            // Quick student-localID validation on first ~2k rows
            sr.BaseStream.Seek(0, SeekOrigin.Begin);
            sr.DiscardBufferedData();
            await sr.ReadLineAsync(); // skip header
            var localIds = new HashSet<int>();
            int scanned2 = 0;
            while ((line = await sr.ReadLineAsync()) != null && scanned2 < 2000)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Split(delimiter);
                if (colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                    localIds.Add(lid);
                scanned2++;
            }
            if (localIds.Count > 0)
            {
                var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, localIds);
                StudentValidationSummary = $"{found}/{localIds.Count} local IDs match students in district {DistrictId}" +
                    (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : "");
            }

            return Page();
        }

        // ---------- IMPORT ----------
        public async Task<IActionResult> OnPostImport()
        {
            await LoadDistrictOptionsAsync();
            if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "Temp file not found. Please re-run Preview.");
                return Page();
            }

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

            // Build TVP for dbo.ImportAssessmentResults (dbo.AssessmentResultImportType)
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

                int colLocalId = FindFirst(headers, PossibleLocalIdHeaders) ?? 0;
                int? testMetaIdx = FindFirst(headers, PossibleTestHeaders);
                if (string.IsNullOrWhiteSpace(TestId) && testMetaIdx.HasValue)
                {
                    TestId = headers[testMetaIdx.Value];
                    ModelState.Remove(nameof(TestId));
                }

                // Map question columns again (import step may be reached directly)
                var scoreCols = new Dictionary<int, int>();
                var stdCols = new Dictionary<int, int>();
                for (int i = 0; i < headers.Length; i++)
                {
                    var h = headers[i];
                    var ms = ScoreHeaderRe.Match(h);
                    if (ms.Success && int.TryParse(ms.Groups["q"].Value, out var qn)) scoreCols[qn] = i;
                    var mstd = StandardHeaderRe.Match(h);
                    if (mstd.Success && int.TryParse(mstd.Groups["q"].Value, out var qn2)) stdCols[qn2] = i;
                }

                // Build standard code candidate set (from header only)
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var q in scoreCols.Keys.Intersect(stdCols.Keys))
                {
                    // We’ll also read rows to accumulate actual codes shortly
                }
                var existing = await LoadExistingStandards(candidates); // may be empty now; we’ll normalize per-row too

                // Read all rows, aggregate per-student per-standard
                string? line;
                var agg = new Dictionary<(int localId, string code), (decimal points, int count)>(
                    new KeyComparer());

                while ((line = await sr.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = line.Split(delimiterChar);
                    if (colLocalId >= cells.Length) continue;
                    if (!int.TryParse(cells[colLocalId].Trim(), out var localId)) continue;

                    foreach (var q in scoreCols.Keys.Intersect(stdCols.Keys))
                    {
                        var sCol = scoreCols[q];
                        var cCol = stdCols[q];
                        if (sCol >= cells.Length || cCol >= cells.Length) continue;
                        var rawPts = (cells[sCol] ?? "").Trim();
                        var rawCode = StripElaPrefix((cells[cCol] ?? "").Trim());
                        if (string.IsNullOrEmpty(rawCode) || string.IsNullOrEmpty(rawPts)) continue;

                        if (!decimal.TryParse(rawPts, NumberStyles.Any, CultureInfo.InvariantCulture, out var pts))
                        {
                            // allow common 0/1/"Correct"/"Incorrect"
                            if (string.Equals(rawPts, "Correct", StringComparison.OrdinalIgnoreCase)) pts = 1m;
                            else if (string.Equals(rawPts, "Incorrect", StringComparison.OrdinalIgnoreCase)) pts = 0m;
                            else continue;
                        }

                        var norm = NormalizeCode(rawCode, existing);
                        var key = (localId, norm);
                        if (agg.TryGetValue(key, out var cur)) agg[key] = (cur.points + pts, cur.count + 1);
                        else agg[key] = (pts, 1);
                    }
                }

                // Emit one TVP row per (student, standard) with summed points and max_points = #items (typically 5)
                foreach (var kvp in agg)
                {
                    var r = tvp.NewRow();
                    r["local_student_id"] = kvp.Key.localId;
                    r["test_id"] = TestId;
                    r["human_coding_scheme"] = kvp.Key.code;
                    r["points"] = kvp.Value.points;                 // e.g., 0..5
                    r["max_points"] = (decimal)kvp.Value.count;      // e.g., usually 5
                    tvp.Rows.Add(r);
                }
            }

            // Execute stored proc to write batch + results
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
            ImportBatchId = batchId;

            TryDelete(TempPath);
            TempPath = null;
            return Page();
        }

        // ---------- Helpers (largely mirrored from Illuminate importer for consistency) ----------
        private static int? FindFirst(string[] headers, string[] candidates)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                foreach (var c in candidates)
                {
                    if (headers[i].Equals(c, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return null;
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { }
        }

        // Strip CCSS ELA prefix so "CCSS.ELA-Literacy.L.7.4.a" -> "L.7.4.a"
        private static string StripElaPrefix(string code)
        {
            const string prefix = "CCSS.ELA-Literacy.";
            return code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? code.Substring(prefix.Length).Trim()
                : code.Trim();
        }

        // Add math cluster candidates (A..F) for codes like 7.NS.1.a  (so we can normalize to e.g., 7.NS.A.1.a)
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

        // Returns normalized code if a candidate exists in 'existing' set; otherwise returns original.
        private static string NormalizeCode(string code, HashSet<string> existing)
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
                var cand = $"{g}.{dom}.{cl}.{num}{sub}";
                if (existing.Contains(cand)) return cand;
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
            for (int i = 0; i < list.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", list[i]);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) result.Add(rdr.GetString(0));
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

        private sealed class KeyComparer : IEqualityComparer<(int localId, string code)>
        {
            public bool Equals((int localId, string code) x, (int localId, string code) y) => x.localId == y.localId && string.Equals(x.code, y.code, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((int localId, string code) obj) => HashCode.Combine(obj.localId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.code ?? string.Empty));
        }
    }
}