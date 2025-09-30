using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class MasteryConnectModel : PageModel
    {
        
    // --- Robust CSV line parser that respects quotes and escaped quotes ---
    private static string[] ParseCsvLine(string? line)
    {
        if (line == null) return Array.Empty<string>();
        var cells = new List<string>(64);
        var sb = new StringBuilder(line.Length);
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // Escaped quote inside quoted field
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    cells.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        cells.Add(sb.ToString().Trim());
        return cells.ToArray();
    }
private readonly IConfiguration _config;

        // Configuration constants matching Python script
        private const int ORG_ID = 0;
        private const int DEFAULT_CLASS_ID = 0;
        private const int DEFAULT_SCHOOL_ID = 0;
        private const bool KEEP_EMPTY_STUDENTS = false;
        private const bool ALLOW_RAW_CODES_IN_RESULTS = true;

        // Regex patterns for standard codes (Math and ELA)

        private static string NormalizeStd(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim()
                     .ToUpperInvariant()
                     .Replace('\u2010', '-') // hyphen
                     .Replace('\u2011', '-') // non-breaking hyphen
                     .Replace('\u2012', '-') // figure dash
                     .Replace('\u2013', '-') // en dash
                     .Replace('\u2014', '-') // em dash
                     .Replace('\u2212', '-') // minus sign
                     .Replace(" ", "");
            return t;
        }

        private static readonly Regex STD_REGEX = new Regex(
            @"^(?:[A-Z]-[A-Z]+(?:\.[A-Z0-9]+)+|[A-Z]+\.\d+-\d+\.[A-Z0-9]+(?:\.[a-z])?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex UNIT_CYCLE_PATTERN = new Regex(
            @"Cycle[\s_:-]*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Allowed subjects
        private static readonly string[] AllowedSubjects = new[]
        {
            "ELA", "Math", "DLI", "Science", "Social Science", "LOTE", "VAPA"
        };

        // Bind properties
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty] public int DistrictId { get; set; }
        [BindProperty] public string Subject { get; set; } = "";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;
        [BindProperty] public string TestId { get; set; } = "";

        // UI properties
        public bool HasPreview => Preview.Count > 0;
        public List<SelectListItem> SubjectOptions { get; private set; } = new();
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public FileAnalysisResult? AnalysisResults { get; private set; }

        // Preview data
        public record StandardPreview(
            string StandardCode,
            Guid? StandardId,
            string? FallbackCode,
            bool ExistsInDatabase,
            int QuestionCount,
            int StudentCount
        );

        public List<StandardPreview> Preview { get; private set; } = new();
        public List<string> MissingStandards { get; private set; } = new();
        public int TotalStudentStandardRecords { get; private set; }

        // Analysis result structure
        public class FileAnalysisResult
        {
            public string? DetectedTestId { get; set; }
            public string? DetectedTestName { get; set; }
            public string? DetectedSubject { get; set; }
            public int? DetectedUnitCycle { get; set; }
            public int TotalStudentRows { get; set; }
            public int QuestionCount { get; set; }
            public List<string> DistinctStandards { get; set; } = new();
            public bool HasStudentIdColumn { get; set; }
            public string? StudentIdColumnName { get; set; }
            public List<string> ValidationMessages { get; set; } = new();
            public bool IsValid { get; set; } = true;
            public DateTime AnalyzedAt { get; set; } = DateTime.Now;
        }

        public MasteryConnectModel(IConfiguration config)
        {
            _config = config;
        }

        public async Task OnGet()
        {
            BuildSubjectOptions();
            await LoadDistrictOptionsAsync();

            // Restore analysis results from TempData if available
            if (TempData["AnalysisResults"] is string analysisJson)
            {
                AnalysisResults = JsonSerializer.Deserialize<FileAnalysisResult>(analysisJson);

                // Auto-populate fields
                if (!string.IsNullOrWhiteSpace(AnalysisResults?.DetectedTestId))
                {
                    TestId = AnalysisResults.DetectedTestId;
                }
                if (!string.IsNullOrWhiteSpace(AnalysisResults?.DetectedSubject))
                {
                    Subject = AnalysisResults.DetectedSubject;
                }
                if (AnalysisResults?.DetectedUnitCycle.HasValue == true)
                {
                    UnitCycle = AnalysisResults.DetectedUnitCycle.Value;
                }
            }

            // Restore temp path
            if (TempData["TempFilePath"] is string tempPath)
            {
                TempPath = tempPath;
            }
        }

        /// <summary>
        /// AJAX endpoint for file analysis
        /// </summary>
        public async Task<IActionResult> OnPostAnalyzeFile()
        {
            if (Upload == null || Upload.Length == 0)
            {
                return new JsonResult(new { success = false, message = "No file uploaded" });
            }

            try
            {
                // Save temp file
                var temp = Path.Combine(Path.GetTempPath(), $"mc_{Guid.NewGuid():N}.csv");
                using (var fs = System.IO.File.Create(temp))
                    await Upload.CopyToAsync(fs);

                // Analyze file
                var analysis = await AnalyzeFileContents(temp);

                // Store in TempData so it persists across the redirect
                TempData["AnalysisResults"] = JsonSerializer.Serialize(analysis);
                TempData["TempFilePath"] = temp;

                return new JsonResult(new
                {
                    success = true,
                    analysis = new
                    {
                        detectedTestId = analysis.DetectedTestId,
                        detectedTestName = analysis.DetectedTestName,
                        detectedSubject = analysis.DetectedSubject,
                        detectedUnitCycle = analysis.DetectedUnitCycle,
                        totalStudentRows = analysis.TotalStudentRows,
                        questionCount = analysis.QuestionCount,
                        distinctStandards = analysis.DistinctStandards,
                        hasStudentIdColumn = analysis.HasStudentIdColumn,
                        studentIdColumnName = analysis.StudentIdColumnName,
                        validationMessages = analysis.ValidationMessages,
                        isValid = analysis.IsValid
                    },
                    tempPath = temp
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        public async Task<IActionResult> OnPostPreview()
        {
            TempData.Remove("ImportSuccessMessage");
            BuildSubjectOptions();
            await LoadDistrictOptionsAsync();

            // Validate file
            string tempFilePath;
            if (Upload == null || Upload.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
                {
                    ModelState.AddModelError("", "Please upload a CSV file.");
                    return Page();
                }
                tempFilePath = TempPath;
            }
            else
            {
                tempFilePath = Path.Combine(Path.GetTempPath(), $"mc_{Guid.NewGuid():N}.csv");
                using (var fs = System.IO.File.Create(tempFilePath))
                    await Upload.CopyToAsync(fs);
                TempPath = tempFilePath;
            }

            // Re-analyze for persistence
            AnalysisResults = await AnalyzeFileContents(tempFilePath);

            if (!AnalysisResults.IsValid)
            {
                foreach (var msg in AnalysisResults.ValidationMessages)
                {
                    ModelState.AddModelError("", msg);
                }
                return Page();
            }

            // Auto-fill fields
            if (string.IsNullOrWhiteSpace(TestId) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedTestId))
            {
                TestId = AnalysisResults.DetectedTestId;
            }
            if (string.IsNullOrWhiteSpace(Subject) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedSubject))
            {
                Subject = AnalysisResults.DetectedSubject;
            }
            if (UnitCycle == 1 && AnalysisResults.DetectedUnitCycle.HasValue)
            {
                UnitCycle = AnalysisResults.DetectedUnitCycle.Value;
            }

            // Parse file and build preview
            var (meta, byStudent, maxByStandard) = await ParseMasteryConnectFile(tempFilePath);

            // Fetch standard mappings
            var standardCodes = meta.DistinctStandards;
            var standardIdMap = await FetchStandardIdMap(standardCodes);

            // Build preview
            var standardStats = new Dictionary<string, (int questions, HashSet<string> students)>();

            foreach (var (studentId, standardScores) in byStudent)
            {
                foreach (var standardCode in standardScores.Keys)
                {
                    if (!standardStats.ContainsKey(standardCode))
                    {
                        standardStats[standardCode] = (0, new HashSet<string>());
                    }

                    var (count, students) = standardStats[standardCode];
                    students.Add(studentId);
                    standardStats[standardCode] = (count, students);
                }
            }

            // Count questions per standard
            foreach (var kvp in maxByStandard)
            {
                if (standardStats.ContainsKey(kvp.Key))
                {
                    var (_, students) = standardStats[kvp.Key];
                    // Approximate question count from max points
                    int questionCount = (int)Math.Ceiling(kvp.Value);
                    standardStats[kvp.Key] = (questionCount, students);
                }
            }

            Preview = standardCodes
                .Select(code =>
                {
                    var hasMapping = standardIdMap.TryGetValue(code, out var stdId);
                    var (qCount, students) = standardStats.GetValueOrDefault(code, (0, new HashSet<string>()));

                    return new StandardPreview(
                        code,
                        hasMapping ? stdId : null,
                        !hasMapping && ALLOW_RAW_CODES_IN_RESULTS ? code : null,
                        hasMapping,
                        qCount,
                        students.Count
                    );
                })
                .OrderBy(p => p.StandardCode)
                .ToList();

            MissingStandards = Preview.Where(p => !p.ExistsInDatabase).Select(p => p.StandardCode).ToList();
            TotalStudentStandardRecords = byStudent.Sum(kvp => kvp.Value.Count);

            return Page();
        }

        public async Task<IActionResult> OnPostImport()
        {
            BuildSubjectOptions();
            await LoadDistrictOptionsAsync();

            // Validate required fields
            if (DistrictId == 0)
            {
                ModelState.AddModelError(nameof(DistrictId), "District is required.");
            }
            if (string.IsNullOrWhiteSpace(Subject))
            {
                ModelState.AddModelError(nameof(Subject), "Subject is required.");
            }
            if (string.IsNullOrWhiteSpace(TestId))
            {
                ModelState.AddModelError(nameof(TestId), "Test ID is required.");
            }

            if (!ModelState.IsValid)
            {
                return await OnPostPreview();
            }

            if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "Temp file not found. Please re-run Preview.");
                return Page();
            }

            try
            {
                // Parse file
                var (meta, byStudent, maxByStandard) = await ParseMasteryConnectFile(TempPath);

                // Fetch standard mappings
                var standardIdMap = await FetchStandardIdMap(meta.DistinctStandards);

                // Build TVP rows for stored procedure
                var tvp = new DataTable();
                tvp.Columns.Add("local_student_id", typeof(int));
                tvp.Columns.Add("test_id", typeof(string));
                tvp.Columns.Add("human_coding_scheme", typeof(string));
                tvp.Columns.Add("points", typeof(decimal));
                tvp.Columns.Add("max_points", typeof(decimal));

                // Track what we're sending for debugging
                var debugInfo = new System.Text.StringBuilder();
                debugInfo.AppendLine($"District: {DistrictId}, TestId: {TestId}, Subject: {Subject}, Cycle: {UnitCycle}");
                debugInfo.AppendLine($"Total students in file: {byStudent.Count}");
                debugInfo.AppendLine($"Standards mapped: {standardIdMap.Count}/{meta.DistinctStandards.Count}");

                // Get human_coding_scheme for all mapped standards (batch operation)
                var standardSchemeMap = await GetHumanCodingSchemesForStandards(standardIdMap.Values.ToList());

                debugInfo.AppendLine($"Got human_coding_scheme for {standardSchemeMap.Count} standards");

                // Aggregate per student per standard
                foreach (var (localStudentId, standardScores) in byStudent)
                {
                    // Parse local student ID to int
                    if (!int.TryParse(localStudentId, out var localIdInt))
                    {
                        debugInfo.AppendLine($"Skipped student (invalid ID): {localStudentId}");
                        continue;
                    }

                    foreach (var (standardCode, earnedPoints) in standardScores)
                    {
                        // Get the standard GUID from our mapping
                        if (!standardIdMap.TryGetValue(standardCode, out var standardGuid))
                        {
                            debugInfo.AppendLine($"Skipped: StandardCode '{standardCode}' - not in standardIdMap");
                            continue;
                        }

                        // Get the human_coding_scheme from the database
                        if (!standardSchemeMap.TryGetValue(standardGuid, out var humanCodingScheme))
                        {
                            debugInfo.AppendLine($"Skipped: StandardCode '{standardCode}' (GUID: {standardGuid}) - no human_coding_scheme found");
                            continue;
                        }

                        var maxPts = maxByStandard.GetValueOrDefault(standardCode, 0m);

                        var row = tvp.NewRow();
                        row["local_student_id"] = localIdInt;
                        row["test_id"] = TestId;
                        row["human_coding_scheme"] = humanCodingScheme;
                        row["points"] = earnedPoints;
                        row["max_points"] = maxPts;
                        tvp.Rows.Add(row);
                    }
                }

                debugInfo.AppendLine($"TVP rows created: {tvp.Rows.Count}");

                if (tvp.Rows.Count == 0)
                {
                    ModelState.AddModelError("", $"No valid student-standard data found to import.\n\nDebug Info:\n{debugInfo}");
                    return Page();
                }

                // Show first few rows for debugging
                debugInfo.AppendLine("\nFirst 5 TVP rows:");
                for (int i = 0; i < Math.Min(5, tvp.Rows.Count); i++)
                {
                    var r = tvp.Rows[i];
                    debugInfo.AppendLine($"  Student: {r["local_student_id"]}, Standard: {r["human_coding_scheme"]}, Points: {r["points"]}/{r["max_points"]}");
                }

                var connStr = _config.GetConnectionString("DefaultConnection");
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                // Call stored procedure
                using var cmd = new SqlCommand("dbo.ImportAssessmentResults", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120
                };

                cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                cmd.Parameters.AddWithValue("@TestId", TestId);
                cmd.Parameters.AddWithValue("@SourceFile", Path.GetFileName(TempPath));
                cmd.Parameters.AddWithValue("@Subject", Subject);
                cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);

                var tvpParam = cmd.Parameters.AddWithValue("@Rows", tvp);
                tvpParam.SqlDbType = SqlDbType.Structured;
                tvpParam.TypeName = "dbo.AssessmentResultImportType";

                // Execute and get batch_id
                using var reader = await cmd.ExecuteReaderAsync();

                Guid? batchGuid = null;
                if (await reader.ReadAsync())
                {
                    batchGuid = reader.GetGuid(0);
                }

                // Read unmatched count from second result set
                int unmatchedCount = 0;
                if (await reader.NextResultAsync() && await reader.ReadAsync())
                {
                    unmatchedCount = reader.GetInt32(0);
                }
                reader.Close();

                if (batchGuid.HasValue)
                {
                    // Query to see how many rows were actually inserted
                    using var checkCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM assessment_results WHERE batch_id = @batchId", conn);
                    checkCmd.Parameters.AddWithValue("@batchId", batchGuid.Value);

                    // SAFER: Handle null / DBNull
                    var scalar = await checkCmd.ExecuteScalarAsync();
                    var insertedCount = (scalar == null || scalar == DBNull.Value) ? 0 : Convert.ToInt32(scalar);

                    var message = $"Import complete! Batch ID: {batchGuid}. " +
                                  $"Sent {tvp.Rows.Count} rows to stored proc. " +
                                  $"Actually inserted: {insertedCount} rows. " +
                                  $"Unmatched: {unmatchedCount} rows.";

                    if (insertedCount == 0)
                    {
                        message += $"\n\nDEBUG INFO:\n{debugInfo}\n\n" +
                                   "Possible causes:\n" +
                                   "1. Students not found: local_student_ids don't match students.LocalStudentID in district " + DistrictId + "\n" +
                                   "2. Standards not found: human_coding_scheme values don't match standards.human_coding_scheme\n" +
                                   "3. Students are marked Inactive in the database";
                    }

                    TempData["ImportSuccessMessage"] = message;
                    TempData["ImportBatchId"] = batchGuid.ToString();
                }
                else
                {
                    TempData["ImportSuccessMessage"] =
                        $"Import complete! Processed {tvp.Rows.Count} student-standard result rows.";
                }

                TryDelete(TempPath);
                TempPath = null;
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Import failed: {ex.Message}");
                return Page();
            }

            return RedirectToPage();
        }

        // ========== HELPER METHODS ==========

        private void BuildSubjectOptions()
        {
            SubjectOptions = AllowedSubjects
                .Select(s => new SelectListItem { Text = s, Value = s, Selected = s == Subject })
                .ToList();
        }

        private async Task<FileAnalysisResult> AnalyzeFileContents(string filePath)
        {
            var result = new FileAnalysisResult();

            using var sr = new StreamReader(System.IO.File.OpenRead(filePath), Encoding.UTF8, true);

            // Row 0: Standard codes
            var row0 = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(row0))
            {
                result.IsValid = false;
                result.ValidationMessages.Add("File is empty.");
                return result;
            }

            // Row 1: Headers
            var row1 = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(row1))
            {
                result.IsValid = false;
                result.ValidationMessages.Add("Missing header row (row 1).");
                return result;
            }

            var headers = ParseCsvLine(row1).Select(h => h.Trim().Trim('"')).ToArray();

            // Find student_id column
            var studentIdIdx = Array.FindIndex(headers, h =>
                h.Equals("student_id", StringComparison.OrdinalIgnoreCase));

            result.HasStudentIdColumn = studentIdIdx >= 0;
            result.StudentIdColumnName = studentIdIdx >= 0 ? headers[studentIdIdx] : null;

            if (!result.HasStudentIdColumn)
            {
                result.ValidationMessages.Add("Required 'student_id' column not found in row 1.");
                result.IsValid = false;
            }

            // Find assessment_name column
            var assessmentNameIdx = Array.FindIndex(headers, h =>
                h.Equals("assessment_name", StringComparison.OrdinalIgnoreCase));

            // Find question start
            var row0Parts = ParseCsvLine(row0).Select(h => h.Trim().Trim('"')).ToArray();
            var questionStart = FindQuestionStart(row0Parts, headers);

            // Extract distinct standards (normalized)
            var distinctStandards = new HashSet<string>();
            for (int i = questionStart; i < row0Parts.Length; i += 2)
            {
                var raw = row0Parts[i];
                var stdCode = NormalizeStd(raw);
                if (!string.IsNullOrWhiteSpace(stdCode) && STD_REGEX.IsMatch(stdCode))
                {
                    distinctStandards.Add(stdCode);
                }
            }

            result.DistinctStandards = distinctStandards.OrderBy(s => s).ToList();
            result.QuestionCount = (row0Parts.Length - questionStart) / 2;

            // Read first data row (row 2) for test name detection
            var firstDataRow = await sr.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(firstDataRow))
            {
                var cells = ParseCsvLine(firstDataRow).Select(c => c.Trim().Trim('"')).ToArray();

                if (assessmentNameIdx >= 0 && assessmentNameIdx < cells.Length)
                {
                    result.DetectedTestName = cells[assessmentNameIdx];
                    result.DetectedTestId = MakeTestId(result.DetectedTestName, Path.GetFileNameWithoutExtension(filePath));
                }

                // Detect subject
                result.DetectedSubject = DetectSubject(result.DetectedTestName, result.DistinctStandards);

                // Detect unit cycle
                result.DetectedUnitCycle = DetectUnitCycle(result.DetectedTestName);
            }

            // Count student rows
            int studentRowCount = 1; // Already read first data row
            while (await sr.ReadLineAsync() != null)
            {
                studentRowCount++;
            }
            result.TotalStudentRows = studentRowCount;

            return result;
        }

        private async Task<(ParsedMetadata meta, Dictionary<string, Dictionary<string, decimal>> byStudent, Dictionary<string, decimal> maxByStandard)>
            ParseMasteryConnectFile(string filePath)
        {
            using var sr = new StreamReader(System.IO.File.OpenRead(filePath), Encoding.UTF8, true);

            // Prevent double-counting the same question for a student when duplicate rows exist
            var seenQ = new Dictionary<string, HashSet<int>>(); // key: $"{studentId}|{stdCode}", value: set of respCol indices

            // Row 0: Standards
            var row0 = (await sr.ReadLineAsync() ?? "").Split(',').Select(h => h.Trim().Trim('"')).ToArray();

            // Row 1: Headers
            var row1 = (await sr.ReadLineAsync() ?? "").Split(',').Select(h => h.Trim().Trim('"')).ToArray();

            // Find columns
            var studentIdIdx = Array.FindIndex(row1, h => h.Equals("student_id", StringComparison.OrdinalIgnoreCase));
            var assessmentNameIdx = Array.FindIndex(row1, h => h.Equals("assessment_name", StringComparison.OrdinalIgnoreCase));
            var createdAtIdx = Array.FindIndex(row1, h => h.Equals("created_at", StringComparison.OrdinalIgnoreCase));

            if (studentIdIdx < 0)
                throw new InvalidOperationException("student_id column not found.");

            // Find question start and build triples
            var questionStart = FindQuestionStart(row0, row1);
            var triples = new List<(int respCol, int ptsCol, string stdCode)>();
            var maxByStandard = new Dictionary<string, decimal>();
            var distinctStandards = new List<string>();

            for (int j = questionStart; j < row0.Length; j += 2)
            {
                var stdCode = NormalizeStd(row0[j]);
                if (string.IsNullOrWhiteSpace(stdCode) || !STD_REGEX.IsMatch(stdCode))
                    continue;

                var respCol = j;
                var ptsCol = j + 1;
                if (ptsCol >= row1.Length) continue;

                triples.Add((respCol, ptsCol, stdCode));

                // Max points from row 1
                if (decimal.TryParse(row1[ptsCol], NumberStyles.Any, CultureInfo.InvariantCulture, out var maxPts))
                {
                    maxByStandard[stdCode] = maxByStandard.GetValueOrDefault(stdCode, 0m) + maxPts;
                }
                distinctStandards.Add(stdCode);
            }

            // Extract test metadata
            string? testName = null;
            DateTime date = DateTime.Now;

            var firstDataLine = await sr.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var cells = ParseCsvLine(firstDataLine).Select(c => c.Trim().Trim('"')).ToArray();

                if (assessmentNameIdx >= 0 && assessmentNameIdx < cells.Length)
                {
                    testName = MakeTestName(cells[assessmentNameIdx]);
                }

                if (createdAtIdx >= 0 && createdAtIdx < cells.Length)
                {
                    if (DateTime.TryParse(cells[createdAtIdx], out var dt))
                    {
                        date = dt;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(testName))
            {
                testName = MakeTestName(Path.GetFileNameWithoutExtension(filePath));
            }

            var testId = MakeTestId(testName, Path.GetFileNameWithoutExtension(filePath));
            var unit = DetectUnitCycle(testName);

            var meta = new ParsedMetadata
            {
                TestId = testId,
                TestName = testName,
                Unit = unit,
                Date = date,
                DistinctStandards = distinctStandards.Distinct().OrderBy(s => s).ToList()
            };

            // Aggregate student data
            var byStudent = new Dictionary<string, Dictionary<string, decimal>>();

            // Process one data line
            void ProcessLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                var cells = ParseCsvLine(line).Select(c => c.Trim().Trim('"')).ToArray();

                if (studentIdIdx >= cells.Length) return;
                var studentId = cells[studentIdIdx];
                if (string.IsNullOrWhiteSpace(studentId) || studentId.Equals("nan", StringComparison.OrdinalIgnoreCase))
                    return;

                bool anyNonBlank = false;
                foreach (var (respCol, ptsCol, stdCode) in triples)
                {
                    if (ptsCol >= cells.Length) continue;

                    var respCell = respCol < cells.Length ? cells[respCol] : null;
                    var ptsCell = cells[ptsCol];

                    // consider 0 a real value
                    if (!string.IsNullOrWhiteSpace(ptsCell) || ptsCell == "0")
                        anyNonBlank = true;

                    decimal earned;
                    if (decimal.TryParse(ptsCell, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    {
                        earned = parsed;
                    }
                    else if (!string.IsNullOrWhiteSpace(respCell))
                    {
                        // Student answered but received 0 — count as an attempt so the standard shows up
                        earned = 0m;
                        anyNonBlank = true;
                    }
                    else
                    {
                        // truly blank (no response, no points)
                        continue;
                    }

                    // per-question de-dup per (student, standard)
                    var key = studentId + "|" + stdCode;
                    if (!seenQ.TryGetValue(key, out var set))
                    {
                        set = new HashSet<int>();
                        seenQ[key] = set;
                    }
                    if (set.Contains(respCol)) continue;
                    set.Add(respCol);

                    if (!byStudent.ContainsKey(studentId))
                        byStudent[studentId] = new Dictionary<string, decimal>();

                    // sum and cap at max points for the standard
                    var prev = byStudent[studentId].GetValueOrDefault(stdCode, 0m);
                    var next = prev + earned;
                    var maxPtsForStd = maxByStandard.GetValueOrDefault(stdCode, 0m);
                    if (maxPtsForStd > 0m && next > maxPtsForStd)
                        next = maxPtsForStd;

                    byStudent[studentId][stdCode] = next;
                }

                if (!anyNonBlank) return; // ignore rows with no answers at all
            }

            ProcessLine(firstDataLine);

            // Process remaining lines
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                ProcessLine(line);
            }

            return (meta, byStudent, maxByStandard);
        }

        private int FindQuestionStart(string[] row0, string[] row1)
        {
            for (int j = 0; j < row0.Length - 1; j++)
            {
                var s0 = NormalizeStd(row0[j]);
                var s1 = j + 1 < row1.Length ? row1[j + 1] : "";

                if (STD_REGEX.IsMatch(s0) && decimal.TryParse(s1, out _))
                {
                    return j;
                }
            }
            return 16; // Fallback
        }

        private static List<string> BuildFallbackPatternsForStandard(string code)
        {
            var patterns = new List<string>();
            var c = NormalizeStd(code);
            if (string.IsNullOrEmpty(c)) return patterns;

            patterns.Add($"%{c}%");

            var idx = c.LastIndexOf('.');
            if (idx > 0 && idx < c.Length - 1)
            {
                var head = c.Substring(0, idx);
                var tail = c.Substring(idx + 1);
                patterns.Add($"%{head}.%." + tail + "%");
                patterns.Add($"%{head}%." + tail + "%");
            }

            if (c.StartsWith("F-"))
            {
                var rest = c;
                patterns.Add("%HS" + rest + "%");
                if (idx > 0 && idx < c.Length - 1)
                {
                    var head = c.Substring(0, idx);
                    var tail = c.Substring(idx + 1);
                    patterns.Add("%HS" + head + ".%." + tail + "%");
                }
            }

            return patterns.Distinct().ToList();
        }

        private async Task<Dictionary<string, Guid>> FetchStandardIdMap(List<string> standardCodes)
        {
            var result = new Dictionary<string, Guid>();
            if (standardCodes.Count == 0) return result;

            // Normalize incoming codes once
            var uniqueCodes = standardCodes.Select(NormalizeStd).Distinct().ToList();

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Exact matches first (bulk query)
            foreach (var chunk in uniqueCodes.Chunk(500))
            {
                var parameters = chunk.Select((code, i) => new SqlParameter($"@p{i}", code)).ToArray();
                var inClause = string.Join(",", parameters.Select(p => p.ParameterName));

                var sql = $"SELECT human_coding_scheme, id FROM standards WHERE human_coding_scheme IN ({inClause})";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddRange(parameters);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var scheme = rdr.GetString(0);
                    var id = Guid.Parse(rdr.GetString(1));
                    result[NormalizeStd(scheme)] = id;
                }
            }

            // LIKE fallbacks for missing codes
            var missing = uniqueCodes.Where(c => !result.ContainsKey(c)).ToList();

            foreach (var code in missing)
            {
                // Build a robust set of patterns to match expanded CCSS forms
                var patterns = GenerateLikePatterns(code);
                var extra = BuildFallbackPatternsForStandard(code);
                foreach (var p in extra) if (!patterns.Contains(p)) patterns.Add(p);

                foreach (var pattern in patterns)
                {
                    var sql = @"
                        SELECT TOP 1 human_coding_scheme, id 
                        FROM standards 
                        WHERE human_coding_scheme LIKE @pattern 
                        ORDER BY LEN(human_coding_scheme) ASC";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@pattern", pattern);

                    using var rdr = await cmd.ExecuteReaderAsync();
                    if (await rdr.ReadAsync())
                    {
                        var id = Guid.Parse(rdr.GetString(1));
                        result[code] = id;
                        break;
                    }
                }
            }

            return result;
        }

        private List<string> GenerateLikePatterns(string code)
        {
            var patterns = new List<string>();

            // ELA (e.g., RI.9-10.3, L.9-10.4.a)
            var elaMatch = Regex.Match(code, @"^([A-Z]+)\.(\d+-\d+)\.(.+)$", RegexOptions.IgnoreCase);
            if (elaMatch.Success)
            {
                var prefix = elaMatch.Groups[1].Value;
                var grade = elaMatch.Groups[2].Value;
                var suffix = elaMatch.Groups[3].Value;

                patterns.Add($"%{prefix}.{grade}.{suffix}");
                patterns.Add($"%{prefix}%.{suffix}");
                patterns.Add($"%{prefix}%{suffix}");
            }
            else
            {
                // Math standard logic
                var parts = code.Split('.');
                var head = parts[0];
                var tail = parts.Length > 1 ? parts[^1] : "";

                var domainMap = new Dictionary<char, string>
                {
                    {'A', "HSA"}, {'F', "HSF"}, {'G', "HSG"}, {'N', "HSN"}, {'S', "HSS"}
                };

                if (head.Contains('-'))
                {
                    var headParts = head.Split('-');
                    var domain = headParts[^1];

                    if (head.Length > 0 && domainMap.TryGetValue(head[0], out var fam))
                    {
                        if (!string.IsNullOrEmpty(tail))
                        {
                            patterns.Add($"%{fam}%-{domain}%.{tail}");
                            patterns.Add($"%{fam}%-{domain}%{tail}");
                        }
                        else
                        {
                            patterns.Add($"%{fam}%-{domain}%");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(tail))
                {
                    patterns.Add($"%{head}%.{tail}");
                    patterns.Add($"%{head}%{tail}");
                }
                else
                {
                    patterns.Add($"%{head}%");
                }
            }

            return patterns;
        }

        private async Task UpsertAssessment(SqlConnection conn, SqlTransaction trans, int districtId, string testId, string testName, int? unit, string standardsJson)
        {
            var sql = @"
                IF EXISTS (SELECT 1 FROM assessments WHERE test_id = @testId)
                    UPDATE assessments 
                    SET districtid = @districtId, test_name = @testName, unit = @unit, standards = @standards
                    WHERE test_id = @testId
                ELSE
                    INSERT INTO assessments (districtid, test_id, test_name, unit, standards)
                    VALUES (@districtId, @testId, @testName, @unit, @standards)";

            using var cmd = new SqlCommand(sql, conn, trans);
            cmd.Parameters.AddWithValue("@districtId", districtId);
            cmd.Parameters.AddWithValue("@testId", testId);
            cmd.Parameters.AddWithValue("@testName", testName);
            cmd.Parameters.AddWithValue("@unit", (object?)unit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@standards", standardsJson);

            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<Dictionary<string, Guid>> GetStudentGuidMap(SqlConnection conn, SqlTransaction trans, int districtId, List<string> localStudentIds)
        {
            var result = new Dictionary<string, Guid>();
            if (localStudentIds.Count == 0) return result;

            // Query students table to map localstudentid -> GUID
            var chunks = localStudentIds.Chunk(500);

            foreach (var chunk in chunks)
            {
                var parameters = chunk.Select((id, i) => new SqlParameter($"@p{i}", id)).ToArray();
                var inClause = string.Join(",", parameters.Select(p => p.ParameterName));

                var sql = $@"
                    SELECT localstudentid, StuId 
                    FROM students 
                    WHERE districtid = @districtId 
                      AND localstudentid IN ({inClause})
                      AND ISNULL(Inactive, 0) = 0";

                using var cmd = new SqlCommand(sql, conn, trans);
                cmd.Parameters.AddWithValue("@districtId", districtId);
                cmd.Parameters.AddRange(parameters);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var localId = rdr.GetString(0);
                    var stuId = Guid.Parse(rdr.GetString(1));
                    result[localId] = stuId;
                }
            }

            return result;
        }

        private async Task InsertAssessmentResults(SqlConnection conn, SqlTransaction trans, List<AssessmentResultRow> rows)
        {
            if (rows.Count == 0) return;

            var sql = @"
                INSERT INTO assessment_results 
                    (test_id, user_id, results, proficiency, quadrant, date, class_id, school_id)
                VALUES 
                    (@testId, @userId, @results, @proficiency, @quadrant, @date, @classId, @schoolId)";

            foreach (var row in rows)
            {
                using var cmd = new SqlCommand(sql, conn, trans);
                cmd.Parameters.AddWithValue("@testId", row.test_id);
                cmd.Parameters.AddWithValue("@userId", row.user_id);
                cmd.Parameters.AddWithValue("@results", row.results);
                cmd.Parameters.AddWithValue("@proficiency", row.proficiency);
                cmd.Parameters.AddWithValue("@quadrant", row.quadrant);
                cmd.Parameters.AddWithValue("@date", row.date);
                cmd.Parameters.AddWithValue("@classId", row.class_id);
                cmd.Parameters.AddWithValue("@schoolId", row.school_id);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static int QuadrantFromProficiency(int proficiency)
        {
            if (proficiency >= 4 && proficiency <= 5) return 1;
            if (proficiency == 3) return 2;
            if (proficiency == 2) return 3;
            return 4;
        }

        private static string MakeTestName(string raw)
        {
            var s = RemoveNonAscii(raw ?? "");
            // Remove parentheses and their content
            s = Regex.Replace(s, @"\([^)]*\)", "");
            // Remove extra dashes
            s = s.Replace("-", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string MakeTestId(string assessmentName, string fileStem)
        {
            // Use cleaned assessment name as test_id (not hash)
            var cleaned = MakeTestName(assessmentName);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = fileStem;
            }
            // Convert to lowercase with underscores
            cleaned = cleaned.Replace(" ", "_").ToLower();
            // Remove any remaining special characters except underscore
            cleaned = Regex.Replace(cleaned, @"[^a-z0-9_]", "");
            return cleaned;
        }

        private static string RemoveNonAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Regex.Replace(input, @"[^\x00-\x7F]+", "");
        }

        private static string? DetectSubject(string? testName, List<string> standards)
        {
            if (!string.IsNullOrWhiteSpace(testName))
            {
                var lower = testName.ToLowerInvariant();

                if (lower.Contains("ela") || lower.Contains("english") || lower.Contains("reading") ||
                    lower.Contains("language") || lower.Contains("literacy"))
                    return "ELA";

                // Check for IM (Integrated Math) patterns
                if (lower.Contains("im 1") || lower.Contains("im 2") || lower.Contains("im 3") ||
                    lower.Contains("im1") || lower.Contains("im2") || lower.Contains("im3") ||
                    lower.Contains("im_1") || lower.Contains("im_2") || lower.Contains("im_3"))
                    return "Math";

                if (lower.Contains("math") || lower.Contains("alg") ||
                    lower.Contains("geom") || lower.Contains("calc"))
                    return "Math";

                if (lower.Contains("science") || lower.Contains("bio") || lower.Contains("chem") ||
                    lower.Contains("physics"))
                    return "Science";

                if (lower.Contains("social") || lower.Contains("history") || lower.Contains("gov") ||
                    lower.Contains("econ"))
                    return "Social Science";

                if (lower.Contains("dli") || lower.Contains("sla"))
                    return "DLI";
            }

            // Detect from standards
            if (standards.Any(s => s.StartsWith("RI.", StringComparison.OrdinalIgnoreCase) ||
                                   s.StartsWith("RL.", StringComparison.OrdinalIgnoreCase) ||
                                   s.StartsWith("L.", StringComparison.OrdinalIgnoreCase)))
                return "ELA";

            if (standards.Any(s => s.Contains("-", StringComparison.OrdinalIgnoreCase) &&
                                  (s.StartsWith("A-") || s.StartsWith("F-") || s.StartsWith("G-"))))
                return "Math";

            return null;
        }

        private static int? DetectUnitCycle(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            var match = UNIT_CYCLE_PATTERN.Match(source);
            if (!match.Success) return null;

            if (int.TryParse(match.Groups[1].Value, out var n) && n >= 1 && n <= 5)
                return n;

            return null;
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { }
        }

        private async Task<Dictionary<Guid, string>> GetHumanCodingSchemesForStandards(List<Guid> standardIds)
        {
            var result = new Dictionary<Guid, string>();
            if (standardIds.Count == 0) return result;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var chunks = standardIds.Chunk(500);
            foreach (var chunk in chunks)
            {
                var parameters = chunk.Select((id, i) => new SqlParameter($"@p{i}", id.ToString())).ToArray();
                var inClause = string.Join(",", parameters.Select(p => p.ParameterName));

                var sql = $"SELECT id, human_coding_scheme FROM standards WHERE id IN ({inClause})";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddRange(parameters);

                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    var id = Guid.Parse(rdr.GetString(0));
                    var scheme = rdr.GetString(1);
                    result[id] = scheme;
                }
            }

            return result;
        }

        private async Task LoadDistrictOptionsAsync()
        {
            if (DistrictOptions.Any()) return;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var sql = "SELECT Id, Name FROM dbo.districts ORDER BY Name;";
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = await cmd.ExecuteReaderAsync();

            var options = new List<SelectListItem>();
            while (await rdr.ReadAsync())
            {
                var id = rdr.GetInt32(0).ToString();
                var name = rdr.GetString(1);
                options.Add(new SelectListItem { Value = id, Text = name });
            }
            DistrictOptions = options;
        }

        // Supporting classes
        private class ParsedMetadata
        {
            public string TestId { get; set; } = "";
            public string TestName { get; set; } = "";
            public int? Unit { get; set; }
            public DateTime Date { get; set; }
            public List<string> DistinctStandards { get; set; } = new();
        }

        private class AssessmentResultRow
        {
            public int organization_id { get; set; }
            public string test_id { get; set; } = "";
            public string user_id { get; set; } = "";
            public string results { get; set; } = "";
            public int proficiency { get; set; }
            public int quadrant { get; set; }
            public DateTime date { get; set; }
            public int class_id { get; set; }
            public int school_id { get; set; }
        }

        private class StandardResult
        {
            public string? standard_id { get; set; }
            public string? standard_code { get; set; }
            public double score { get; set; }
            public double max_points { get; set; }
            public bool proficient { get; set; }
        }
    }
}
