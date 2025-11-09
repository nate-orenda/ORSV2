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
using CsvHelper; // Added for robust CSV parsing
using CsvHelper.Configuration; // Added for robust CSV parsing

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class LennectionsModel : PageModel
    {
        // --- UI state ---
        [BindProperty] public int DistrictId { get; set; }
        [BindProperty] public string TestId { get; set; } = "";
        [BindProperty] public string Delimiter { get; set; } = "comma";
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty] public string Subject { get; set; } = "ELA";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;

        public bool HasPreview => Preview.Count > 0;
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public string? ImportBatchId { get; private set; }

        // --- PREVIEW STRUCTURES ---
        public record StandardPreview(Guid StandardId, string? HumanCodingScheme, bool ExistsInStandards, int QuestionCount);
        public List<StandardPreview> Preview { get; private set; } = new();
        public List<Guid> MissingIds { get; private set; } = new();
        public string? AutoDetectedTestId { get; private set; }
        public string? StudentValidationSummary { get; private set; }

        // --- ENHANCED: PERSISTENT FILE ANALYSIS ---
        public FileAnalysisResult? AnalysisResults { get; private set; }
        public ColumnMatchingMetrics? MatchingMetrics { get; private set; }

        // Allowed subjects for both UI and server-side validation
        private static readonly string[] AllowedSubjects = new[]
        {
            "ELA", "Math", "DLI", "Science", "Social Science", "LOTE", "VAPA"
        };

        public List<SelectListItem> SubjectOptions { get; private set; } = new();

        private void BuildSubjectOptions(string? selected = null)
        {
            var sel = string.IsNullOrWhiteSpace(selected) ? Subject : selected;
            SubjectOptions = AllowedSubjects
                .Select(s => new SelectListItem { Text = s, Value = s, Selected = string.Equals(s, sel, StringComparison.OrdinalIgnoreCase) })
                .ToList();
        }

        // Add this new helper method inside the LennectionsModel class

        /// <summary>
        /// Creates a mapping from 'SL.%.2' standard IDs to their 'SL.%.3' counterparts.
        /// </summary>
        /// <returns>A dictionary mapping the old Guid to the new Guid.</returns>
        private async Task<Dictionary<Guid, Guid>> GetStandardMappingAsync()
        {
            var map = new Dictionary<Guid, Guid>();
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);

            var sql = @"
        SELECT
            s3.id AS OldId,
            s2.id AS NewId
        FROM dbo.standards s3
        JOIN dbo.standards s2
            ON REPLACE(s3.human_coding_scheme, '.2', '.3') = s2.human_coding_scheme
        WHERE s3.human_coding_scheme LIKE 'SL.%.2'
          AND s2.human_coding_scheme LIKE 'SL.%.3';
    ";

            using var cmd = new SqlCommand(sql, conn);
            await conn.OpenAsync();
            using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                // ✨ FIX: Read the columns as strings and then parse them into GUIDs.
                // This prevents the InvalidCastException if the DB column is NVARCHAR.
                if (Guid.TryParse(rdr.GetString(0), out var oldId) &&
                    Guid.TryParse(rdr.GetString(1), out var newId))
                {
                    map[oldId] = newId;
                }
            }
            return map;
        }

        // --- NEW: COLUMN MATCHING METRICS ---
        public class ColumnMatchingMetrics
        {
            public int TotalQuestionBlocks { get; set; }
            public int ValidQuestionBlocks { get; set; }
            public int ExpectedColumnsPerBlock { get; set; } = 5; // Item, blank, blank, Score, ItemStandard
            public List<QuestionBlockStatus> QuestionBlocks { get; set; } = new();
            public double MatchPercentage => TotalQuestionBlocks > 0 ? (double)ValidQuestionBlocks / TotalQuestionBlocks * 100 : 0;
        }

        public class QuestionBlockStatus
        {
            public int QuestionNumber { get; set; }
            public int StartColumn { get; set; }
            public bool HasItemColumn { get; set; }
            public bool HasScoreColumn { get; set; }
            public bool HasStandardColumn { get; set; }
            public string? ItemHeader { get; set; }
            public string? ScoreHeader { get; set; }
            public string? StandardHeader { get; set; }
            public bool IsComplete => HasItemColumn && HasScoreColumn && HasStandardColumn;
            public int MatchedColumns => (HasItemColumn ? 1 : 0) + (HasScoreColumn ? 1 : 0) + (HasStandardColumn ? 1 : 0);
        }

        // --- ENHANCED FILE ANALYSIS RESULTS ---
        public class FileAnalysisResult
        {
            public string? DetectedTestId { get; set; }
            public string? TestIdSource { get; set; }
            public string? DetectedSubject { get; set; }
            public int? DetectedUnitCycle { get; set; }
            public int TotalRows { get; set; }
            public int StandardsColumns { get; set; }
            public bool HasStudentId { get; set; }
            public string? StudentIdColumn { get; set; }
            public List<string> Headers { get; set; } = new();
            public List<string> ValidationMessages { get; set; } = new();
            public bool IsValid { get; set; } = true;
            public DateTime AnalyzedAt { get; set; } = DateTime.Now;
        }

        private readonly IConfiguration _config;
        public LennectionsModel(IConfiguration config) => _config = config;

        private static readonly string[] PossibleTestHeaders = { "Assessment Name", "Assessment", "Test Name", "Test" };
        private static readonly Regex UnitCyclePattern =
            new(@"(?:unit|cycle|quarter|q)\s*(?<num>\d+|I|II|III|IV|V)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public async Task OnGet()
        {
            BuildSubjectOptions();
            await LoadDistrictOptionsAsync();
        }

        /// <summary>
        /// ENHANCED: AJAX endpoint for file analysis with column matching metrics
        /// </summary>
        public async Task<IActionResult> OnPostAnalyzeFile()
        {
            if (Upload == null || Upload.Length == 0)
            {
                return new JsonResult(new { success = false, message = "No file uploaded" });
            }

            try
            {
                // Save temp file for later use in preview/import
                var temp = Path.Combine(Path.GetTempPath(), $"lenx_{Guid.NewGuid():N}.csv");
                using (var fs = System.IO.File.Create(temp))
                    await Upload.CopyToAsync(fs);

                // Analyze file with enhanced metrics
                var analysis = await AnalyzeFileContents(temp, Delimiter, Upload?.FileName);
                var metrics = await AnalyzeColumnMatching(temp, Delimiter);

                // Store results for persistence across postbacks
                AnalysisResults = analysis;
                MatchingMetrics = metrics;

                // Return analysis results with temp path for later use
                return new JsonResult(new
                {
                    success = true,
                    analysis = analysis,
                    metrics = metrics,
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
            // Clear any lingering success messages from previous imports
            TempData.Remove("ImportSuccessMessage");
            TempData.Remove("ImportBatchId");

            await LoadDistrictOptionsAsync();

            // ✨ FIX: Call the mapping helper method at the top-level of the function.
            var standardMap = await GetStandardMappingAsync();

            // Remove validation for fields that can be auto-detected
            ModelState.Remove(nameof(Subject));
            ModelState.Remove(nameof(DistrictId));
            ModelState.Remove(nameof(TestId));

            string tempFilePath;

            // Check if we have a file upload OR a temp path from previous analysis
            if (Upload == null || Upload.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
                {
                    ModelState.AddModelError("", "Please upload a CSV file.");
                    return Page();
                }

                // Use existing temp file - no need to recreate
                tempFilePath = TempPath;
            }
            else
            {
                // New file uploaded, create temp file
                tempFilePath = Path.Combine(Path.GetTempPath(), $"lenx_{Guid.NewGuid():N}.csv");
                using (var fs = System.IO.File.Create(tempFilePath))
                    await Upload.CopyToAsync(fs);
                TempPath = tempFilePath;
            }

            // ENHANCED: Re-analyze file to get persistent metrics
            AnalysisResults = await AnalyzeFileContents(tempFilePath, Delimiter);
            MatchingMetrics = await AnalyzeColumnMatching(tempFilePath, Delimiter);

            // Auto-fill TestId if blank
            if (string.IsNullOrWhiteSpace(TestId) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedTestId))
            {
                TestId = AutoDetectedTestId = AnalysisResults.DetectedTestId;
            }

            // Auto-fill Subject if blank
            if (string.IsNullOrWhiteSpace(Subject) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedSubject))
            {
                Subject = AnalysisResults.DetectedSubject;
            }

            // Auto-fill UnitCycle if it's the default value
            if (UnitCycle == 1 && AnalysisResults.DetectedUnitCycle.HasValue)
            {
                UnitCycle = AnalysisResults.DetectedUnitCycle.Value;
            }

            // ✅ Keep the dropdown in sync with whatever Subject is now
            BuildSubjectOptions(Subject);

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            
            // --- PATCH: Use CsvReader ---
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = delimiterChar.ToString(),
            };

            var standardQuestionCounts = new Dictionary<Guid, HashSet<int>>(); // standard -> set of question numbers
            var seenStandardIds = new HashSet<Guid>();
            int colLocalId = -1;
            var localIds = new HashSet<int>();
            int scannedCount = 0;
            string[] headers;
            
            // --- PATCH: Define helper function here to fix scope ---
            void RecordStandardForQuestion(int qNumber, Guid standardId)
            {
                if (!standardQuestionCounts.TryGetValue(standardId, out var set))
                {
                    set = new HashSet<int>();
                    standardQuestionCounts[standardId] = set;
                }
                set.Add(qNumber);
                seenStandardIds.Add(standardId);
            }

            using (var reader = new StreamReader(System.IO.File.OpenRead(tempFilePath), Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, csvConfig))
            {
                if (!await csv.ReadAsync())
                {
                    ModelState.AddModelError("", "File is empty.");
                    return Page();
                }
                csv.ReadHeader();
                if (csv.HeaderRecord == null)
                {
                    ModelState.AddModelError("", "Missing header row.");
                    return Page();
                }
                headers = csv.HeaderRecord;

                try { colLocalId = FindStudentIdColumnOrThrow(headers); }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", ex.Message); return Page();
                }

                // Read the first data row to find the Assessment Name
                string[]? firstDataCells = null;
                if (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    firstDataCells = csv.Context.Parser.Record;
                }
                
                if (firstDataCells != null)
                {
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
                    ModelState.AddModelError("", "Couldn't find any question columns."); return Page();
                }

                // First, try the very first data line (often fully populated)
                if (firstDataCells != null)
                {
                    foreach (var qm in maps)
                    {
                        if (qm.StdCol >= firstDataCells.Length) continue;

                        var standardValue = (firstDataCells[qm.StdCol] ?? "").Trim();
                        if (string.IsNullOrEmpty(standardValue)) continue;

                        var lastStandardId = standardValue.Split('|')[^1].Trim();
                        if (Guid.TryParse(lastStandardId, out var standardId))
                        {
                            if (standardMap.TryGetValue(standardId, out var mapped)) standardId = mapped;
                            RecordStandardForQuestion(qm.Q, standardId);
                        }
                    }
                }

                // Reset reader to scan for all standards and students
            } // CsvReader is disposed, file is closed.

            // Re-open file to scan all rows for standards and students
            using (var reader = new StreamReader(System.IO.File.OpenRead(tempFilePath), Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, csvConfig))
            {
                csv.Read();
                csv.ReadHeader(); // Skip header
                colLocalId = FindStudentIdColumnOrThrow(csv.HeaderRecord!); // Re-find student ID col
                var maps = FindItemColumnBlocks(csv.HeaderRecord!); // Re-find maps

                // Process first data line for student validation
                if (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    var cells = csv.Context.Parser.Record;
                    if (cells != null && colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                        localIds.Add(lid);
                    scannedCount++;
                }

                // Scan remaining rows
                while (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.RawRecord ---
                    if (string.IsNullOrWhiteSpace(csv.Context.Parser.RawRecord)) continue;
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    var cells = csv.Context.Parser.Record;
                    if (cells == null) continue;

                    // Student validation
                    if (scannedCount < 2000)
                    {
                        if (colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                            localIds.Add(lid);
                        scannedCount++;
                    }

                    // Standard finding
                    foreach (var qm in maps)
                    {
                        // If we already captured a standard for this question, skip
                        var alreadyCaptured = standardQuestionCounts.Values.Any(set => set.Contains(qm.Q));
                        if (alreadyCaptured) continue;

                        if (qm.StdCol >= cells.Length) continue;

                        var standardValue = (cells[qm.StdCol] ?? "").Trim();
                        if (string.IsNullOrEmpty(standardValue)) continue;

                        var lastStandardId = standardValue.Split('|')[^1].Trim();
                        if (Guid.TryParse(lastStandardId, out var standardId))
                        {
                            if (standardMap.TryGetValue(standardId, out var mapped)) standardId = mapped;
                            RecordStandardForQuestion(qm.Q, standardId);
                        }
                    }

                    // Optional micro-optimization: stop if every question has a captured standard
                    var capturedQuestions = standardQuestionCounts.Values.SelectMany(v => v).ToHashSet();
                    if (maps.All(m => capturedQuestions.Contains(m.Q)) && scannedCount >= 2000) break;
                }
            }
            // --- END OF PATCH ---

            // Build preview objects
            var existingStandardsMap = await LoadSchemesForIds(seenStandardIds);
            Preview = seenStandardIds
                .Select(id => new StandardPreview(
                    id,
                    existingStandardsMap.GetValueOrDefault(id),
                    existingStandardsMap.ContainsKey(id),
                    standardQuestionCounts.TryGetValue(id, out var qs) ? qs.Count : 0))
                .OrderBy(p => p.HumanCodingScheme)
                .ToList();

            MissingIds = Preview.Where(r => !r.ExistsInStandards).Select(r => r.StandardId).ToList();

            if (localIds.Count > 0)
            {
                var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, localIds);
                StudentValidationSummary = $"{found}/{localIds.Count} StudentId values match students in district {DistrictId}"
                                           + (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : "");
            }

            return Page();
        }

        // ---------- NEW ANALYSIS METHODS ----------

        /// <summary>
        /// Analyzes file contents and extracts metadata
        /// </summary>
        private async Task<FileAnalysisResult> AnalyzeFileContents(string filePath, string delimiter, string? originalFileName = null)
        {
            var result = new FileAnalysisResult();
            var delimiterChar = delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

            // --- PATCH: Use CsvReader ---
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = delimiterChar.ToString(),
            };
            
            using (var reader = new StreamReader(System.IO.File.OpenRead(filePath), Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, csvConfig))
            {
                // Read header
                if (!await csv.ReadAsync())
                {
                    result.IsValid = false;
                    result.ValidationMessages.Add("File is empty.");
                    return result;
                }

                csv.ReadHeader();
                if (csv.HeaderRecord == null)
                {
                    result.IsValid = false;
                    result.ValidationMessages.Add("File is empty or missing header row.");
                    return result;
                }

                var headers = csv.HeaderRecord;
                result.Headers = headers.ToList();

                // Check for StudentId column
                var studentIdIndex = FindStudentIdColumn(headers);
                result.HasStudentId = studentIdIndex >= 0;
                result.StudentIdColumn = studentIdIndex >= 0 ? headers[studentIdIndex] : null;

                if (!result.HasStudentId)
                {
                    result.ValidationMessages.Add("Required 'StudentId' or 'Local Student ID' column not found.");
                    result.IsValid = false;
                }

                // Read first data row for test ID detection
                string[]? firstDataCells = null;
                if (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    firstDataCells = csv.Context.Parser.Record;
                }
                
                if (firstDataCells != null)
                {
                    // Detect Test ID from known columns
                    foreach (var testHeader in PossibleTestHeaders)
                    {
                        var testIndex = Array.FindIndex(headers, h => h.Equals(testHeader, StringComparison.OrdinalIgnoreCase));
                        if (testIndex >= 0 && testIndex < firstDataCells.Length && !string.IsNullOrWhiteSpace(firstDataCells[testIndex]))
                        {
                            result.DetectedTestId = firstDataCells[testIndex].Trim();
                            result.TestIdSource = testHeader;
                            break;
                        }
                    }

                    // Detect subject from test name or file content
                    result.DetectedSubject = DetectSubject(result.DetectedTestId, headers);

                    // Detect unit cycle from test name
                    result.DetectedUnitCycle =
                        DetectUnitCycle(result.DetectedTestId) ??
                        DetectUnitCycle(originalFileName);
                }

                // Count total rows
                int rowCount = 1; // Already read first data row
                while (await csv.ReadAsync())
                {
                    rowCount++;
                }
                result.TotalRows = rowCount;

                // Count standards columns (ItemStandard pattern)
                result.StandardsColumns = headers.Count(h => h.Contains("ItemStandard", StringComparison.OrdinalIgnoreCase));
            }
            // --- END OF PATCH ---

            return result;
        }

        /// <summary>
        /// Detects subject from test name and headers
        /// </summary>
        private string? DetectSubject(string? testName, string[] headers)
        {
            if (!string.IsNullOrWhiteSpace(testName))
            {
                var testLower = testName.ToLowerInvariant();

                if (testLower.Contains("ela") || testLower.Contains("english") || testLower.Contains("reading") || testLower.Contains("language")
                    || testLower.Contains("literacy"))
                    return "ELA";

                if (testLower.Contains("math") || testLower.Contains("im 1") || testLower.Contains("im 2") || testLower.Contains("im 3")
                    || testLower.Contains("alg") || testLower.Contains("geom") || testLower.Contains("calc"))
                    return "Math";

                if (testLower.Contains("science") || testLower.Contains("bio") || testLower.Contains("chem") || testLower.Contains("physics")
                    || testLower.Contains("IS1") || testLower.Contains("IS2") || testLower.Contains("IS3"))
                    return "Science";

                if (testLower.Contains("social") || testLower.Contains("history") || testLower.Contains("gov") || testLower.Contains("econ") || testLower.Contains("world"))
                    return "Social Science";
                if (testLower.Contains("dli") || testLower.Contains("sla"))
                    return "DLI";
            }

            // Check standards in headers for subject clues
            var standardHeaders = headers.Where(h => h.Contains("standard", StringComparison.OrdinalIgnoreCase) ||
                                                    h.Contains("ccss", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var header in standardHeaders)
            {
                var headerLower = header.ToLowerInvariant();
                if (headerLower.Contains("ela") || headerLower.Contains("literacy"))
                    return "ELA";
                if (headerLower.Contains("math"))
                    return "Math";
                if (headerLower.Contains("ngss") || headerLower.Contains("science"))
                    return "Science";
            }

            return null;
        }

        /// <summary>
        /// Detects unit cycle from test name
        /// </summary>
        private int? DetectUnitCycle(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            var m = UnitCyclePattern.Match(source);
            if (!m.Success) return null;

            var token = m.Groups["num"].Value;

            // 1) Arabic numerals
            if (int.TryParse(token, out var n))
                return (n >= 1 && n <= 5) ? n : null;

            // 2) Roman numerals I..V
            n = token.ToUpperInvariant() switch
            {
                "I" => 1,
                "II" => 2,
                "III" => 3,
                "IV" => 4,
                "V" => 5,
                _ => 0
            };

            return (n >= 1 && n <= 5) ? n : null;
        }


        /// <summary>
        /// Finds StudentId column with flexible matching
        /// </summary>
        private int FindStudentIdColumn(string[] headers)
        {
            // Exact matches first
            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i].Trim();
                if (header.Equals("StudentId", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Local Student ID", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("LocalStudentId", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            // Fuzzy matches
            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i].Trim().ToLowerInvariant();
                if (header.Contains("student") && (header.Contains("id") || header.Contains("local")))
                {
                    return i;
                }
            }

            return -1;
        }

        // --- COLUMN MATCHING ANALYSIS ---
        private async Task<ColumnMatchingMetrics> AnalyzeColumnMatching(string filePath, string delimiter)
        {
            var metrics = new ColumnMatchingMetrics();
            var delimiterChar = delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            
            // --- PATCH: Use CsvReader ---
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = delimiterChar.ToString(),
            };

            using (var reader = new StreamReader(System.IO.File.OpenRead(filePath), Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, csvConfig))
            {
                if (!await csv.ReadAsync()) return metrics; // Empty file
                
                csv.ReadHeader();
                if (csv.HeaderRecord == null) return metrics; // No header
                
                var headers = csv.HeaderRecord;

                // Find all Item positions first
                var itemPositions = new List<int>();
                for (int i = 0; i < headers.Length; i++)
                {
                    if (headers[i].Trim().Equals("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        itemPositions.Add(i);
                    }
                }

                metrics.TotalQuestionBlocks = itemPositions.Count;

                // For each Item position, analyze the block structure
                for (int itemIndex = 0; itemIndex < itemPositions.Count; itemIndex++)
                {
                    var itemPos = itemPositions[itemIndex];
                    var blockStatus = new QuestionBlockStatus
                    {
                        QuestionNumber = itemIndex + 1,
                        StartColumn = itemPos,
                        HasItemColumn = true,
                        ItemHeader = headers[itemPos]
                    };

                    // Look for Score and ItemStandard in the next few columns after Item
                    for (int offset = 1; offset <= 6 && itemPos + offset < headers.Length; offset++)
                    {
                        var columnHeader = headers[itemPos + offset].Trim();

                        if (columnHeader.Equals("Score", StringComparison.OrdinalIgnoreCase) && !blockStatus.HasScoreColumn)
                        {
                            blockStatus.HasScoreColumn = true;
                            blockStatus.ScoreHeader = columnHeader;
                        }
                        else if (columnHeader.Equals("ItemStandard", StringComparison.OrdinalIgnoreCase) && !blockStatus.HasStandardColumn)
                        {
                            blockStatus.HasStandardColumn = true;
                            blockStatus.StandardHeader = columnHeader;
                        }
                    }

                    metrics.QuestionBlocks.Add(blockStatus);

                    if (blockStatus.IsComplete)
                    {
                        metrics.ValidQuestionBlocks++;
                    }
                }
            }
            // --- END OF PATCH ---

            return metrics;
        }

        public async Task<IActionResult> OnPostImport()
        {
            await LoadDistrictOptionsAsync();

            BuildSubjectOptions(Subject);

            // Only validate required fields for import (not preview)
            if (DistrictId == 0)
            {
                ModelState.AddModelError(nameof(DistrictId), "District is required for import.");
            }
            if (string.IsNullOrWhiteSpace(Subject))
            {
                ModelState.AddModelError(nameof(Subject), "Subject is required for import.");
            }

            if (!ModelState.IsValid)
            {
                return await OnPostPreview();
            }
            if (string.IsNullOrWhiteSpace(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "Temp file not found. Please re-run Preview."); return Page();
            }

            // ✨ FIX: Call the mapping helper method at the top-level of the function.
            var standardMap = await GetStandardMappingAsync();

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var tvp = new DataTable();
            tvp.Columns.Add("local_student_id", typeof(int));
            tvp.Columns.Add("test_id", typeof(string));
            tvp.Columns.Add("human_coding_scheme", typeof(string));
            tvp.Columns.Add("points", typeof(decimal));
            tvp.Columns.Add("max_points", typeof(decimal));

            var agg = new Dictionary<(int localId, Guid standardId), (decimal points, int count)>();
            var allIds = new HashSet<Guid>();

            // --- PATCH: Use CsvReader ---
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                Delimiter = delimiterChar.ToString(),
            };

            using (var reader = new StreamReader(System.IO.File.Open(TempPath, FileMode.Open, FileAccess.Read, FileShare.Read), Encoding.UTF8, true))
            using (var csv = new CsvReader(reader, csvConfig))
            {
                if (!await csv.ReadAsync())
                {
                    ModelState.AddModelError("", "File is empty.");
                    return Page();
                }

                csv.ReadHeader();
                if (csv.HeaderRecord == null)
                {
                    ModelState.AddModelError("", "Could not read file header.");
                    return Page();
                }

                var headers = csv.HeaderRecord;

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

                // Local function to process a single data row to avoid duplicate code.
                void ProcessLine(string[]? cells) // Changed parameter to string[]
                {
                    if (cells == null) return; // Skip if record is null
                    if (colLocalId >= cells.Length || !int.TryParse(cells[colLocalId].Trim(), out var localId)) return;

                    foreach (var m in maps)
                    {
                        if (m.ScoreCol >= cells.Length || m.StdCol >= cells.Length) continue;
                        var rawPts = (cells[m.ScoreCol] ?? "").Trim();
                        var standardValue = (cells[m.StdCol] ?? "").Trim();

                        if (!string.IsNullOrEmpty(standardValue))
                        {
                            // rightmost GUID after pipes
                            var standardParts = standardValue.Split('|');
                            var lastStandardId = standardParts[^1].Trim();

                            if (Guid.TryParse(lastStandardId, out var standardId))
                            {
                                if (standardMap.TryGetValue(standardId, out var newStandardId))
                                    standardId = newStandardId;

                                // 🔑 NEW: coerce non-numeric/blank scores to 0
                                decimal pts;
                                if (!decimal.TryParse(rawPts, NumberStyles.Any, CultureInfo.InvariantCulture, out pts))
                                    pts = 0m;

                                allIds.Add(standardId);
                                var key = (localId, standardId);
                                if (agg.TryGetValue(key, out var cur))
                                    agg[key] = (cur.points + pts, cur.count + 1);
                                else
                                    agg[key] = (pts, 1);
                            }
                        }
                    }
                }

                // Read the first data line.
                string[]? firstDataCells = null;
                if (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    firstDataCells = csv.Context.Parser.Record;
                }

                // If TestId is blank, try to detect it from this first line.
                if (string.IsNullOrWhiteSpace(TestId) && firstDataCells != null)
                {
                    int? testNameCol = FindFirst(headers, PossibleTestHeaders);
                    if (testNameCol.HasValue && testNameCol.Value < firstDataCells.Length)
                    {
                        TestId = firstDataCells[testNameCol.Value].Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(TestId))
                {
                    ModelState.AddModelError("", "Could not detect Test ID. Please enter one manually."); return Page();
                }

                // CRITICAL: Process the first data line that was just read.
                ProcessLine(firstDataCells);

                // Loop through the REST of the file.
                while (await csv.ReadAsync())
                {
                    // --- PATCH: Use csv.Context.Parser.Record ---
                    ProcessLine(csv.Context.Parser.Record);
                }
            }
            // --- END OF PATCH ---

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

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);

            // ADD: capture PRINT / low-severity RAISERROR messages from SQL Server
            var sqlMessages = new List<string>();
            conn.InfoMessage += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Message)) sqlMessages.Add(e.Message); };

            await conn.OpenAsync();

            using var transaction = conn.BeginTransaction();

            try
            {
                // Step 1: Import assessment results
                using var cmd = new SqlCommand("dbo.ImportAssessmentResults", conn, transaction)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120 // ADD: avoid short default timeouts
                };
                cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                cmd.Parameters.AddWithValue("@TestId", TestId);
                cmd.Parameters.AddWithValue("@SourceFile", Path.GetFileName(TempPath!));
                cmd.Parameters.AddWithValue("@Subject", Subject);
                cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);
                var p = cmd.Parameters.AddWithValue("@Rows", tvp);
                p.SqlDbType = SqlDbType.Structured;
                p.TypeName = "dbo.AssessmentResultImportType";

                // CHG: validate GUID strictly so we don't hide earlier SQL issues
                var obj = await cmd.ExecuteScalarAsync();
                if (!Guid.TryParse(obj?.ToString(), out var batchGuid))
                    throw new InvalidOperationException($"Import failed - invalid batch ID returned: '{obj}'");

                // --- Ensure batch metadata is filled before FreezeBatch is called downstream ---
                var testYear = GetSchoolYearFromUtcNow();
                using (var metaCmd = new SqlCommand(@"
                    UPDATE dbo.assessment_import_batches
                    SET 
                        districtid   = COALESCE(districtid, @DistrictId),
                        test_year    = COALESCE(test_year, @TestYear),
                        import_source= COALESCE(import_source, @ImportSource)
                    WHERE batch_id = @BatchId;", conn, transaction))
                {
                    metaCmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                    metaCmd.Parameters.AddWithValue("@TestYear", testYear);
                    metaCmd.Parameters.AddWithValue("@ImportSource", "Lennections"); // or "CASA/Lennections" if you prefer
                    metaCmd.Parameters.AddWithValue("@BatchId", batchGuid);
                    await metaCmd.ExecuteNonQueryAsync();
                }

                // Step 2: Update student totals
                /*
                using (var totalsCmd = new SqlCommand("dbo.UpsertAssessmentStudentTotals", conn, transaction)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120 // ADD
                })
                {
                    totalsCmd.Parameters.AddWithValue("@BatchId", batchGuid);
                    await totalsCmd.ExecuteNonQueryAsync();
                }

                // Step 3: Update student roster
                using (var rosterCmd = new SqlCommand("dbo.UpsertAssessmentStudentRoster", conn, transaction)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 120 // ADD
                })
                {
                    rosterCmd.Parameters.AddWithValue("@BatchId", batchGuid);
                    rosterCmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                    await rosterCmd.ExecuteNonQueryAsync();
                }*/

                transaction.Commit();

                // CHG: include SQL messages so you can see PRINTs
                TempData["ImportSuccessMessage"] =
                    $"Import complete! Batch ID: {batchGuid}. " +
                    $"Processed {tvp.Rows.Count} student-standard result rows. " +
                    (sqlMessages.Count > 0 ? $"Messages: {string.Join(" | ", sqlMessages)}" : "");
                TempData["ImportBatchId"] = batchGuid.ToString();

                TryDelete(TempPath);
                TempPath = null;
                return Page();
            }
            catch (SqlException sqlEx)
            {
                try { if (transaction?.Connection != null) transaction.Rollback(); } catch { /* ignore */ }

                var sb = new StringBuilder("SQL error(s) during import:\n");
                foreach (SqlError err in sqlEx.Errors)
                    sb.AppendLine($"• {err.Number} (Severity {err.Class}) at line {err.LineNumber} in {err.Procedure}: {err.Message}");
                if (sqlMessages.Count > 0) sb.AppendLine($"Messages: {string.Join(" | ", sqlMessages)}");

                ModelState.AddModelError(string.Empty, sb.ToString());
                return Page();
            }
            catch (Exception ex)
            {
                try { if (transaction?.Connection != null) transaction.Rollback(); } catch { /* ignore */ }

                var detail = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                if (sqlMessages.Count > 0) detail += $" | Messages: {string.Join(" | ", sqlMessages)}";
                ModelState.AddModelError(string.Empty, $"Import failed: {detail}");
                return Page();
            }
        }

        // ---------- HELPER METHODS ----------

        private static int GetSchoolYearFromUtcNow()
        {
            var now = DateTime.UtcNow;
            return now.Month >= 7 ? now.Year + 1 : now.Year; // July–Dec => next year; Jan–Jun => current
        }

        private record QuestionMap(int Q, string ScoreHeader, int ScoreCol, string StdHeader, int StdCol);
        
        private List<QuestionMap> FindItemColumnBlocks(string[] headers)
        {
            var maps = new List<QuestionMap>();

            // Find all Item positions first
            var itemPositions = new List<int>();
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].Trim().Equals("Item", StringComparison.OrdinalIgnoreCase))
                {
                    itemPositions.Add(i);
                }
            }

            if (itemPositions.Count == 0) return maps;

            // For each Item position, find the corresponding Score and ItemStandard columns
            for (int itemIndex = 0; itemIndex < itemPositions.Count; itemIndex++)
            {
                var itemPos = itemPositions[itemIndex];

                // Look for Score and ItemStandard in the next few columns after Item
                int? scoreCol = null;
                int? standardCol = null;

                // Search in the next 6 positions for Score and ItemStandard
                for (int offset = 1; offset <= 6 && itemPos + offset < headers.Length; offset++)
                {
                    var header = headers[itemPos + offset].Trim();

                    if (header.Equals("Score", StringComparison.OrdinalIgnoreCase) && !scoreCol.HasValue)
                    {
                        scoreCol = itemPos + offset;
                    }
                    else if (header.Equals("ItemStandard", StringComparison.OrdinalIgnoreCase) && !standardCol.HasValue)
                    {
                        standardCol = itemPos + offset;
                    }
                }

                // Only add if we found both Score and ItemStandard
                if (scoreCol.HasValue && standardCol.HasValue)
                {
                    maps.Add(new QuestionMap(
                        maps.Count + 1,
                        headers[scoreCol.Value],
                        scoreCol.Value,
                        headers[standardCol.Value],
                        standardCol.Value
                    ));
                }
            }

            return maps;
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

            // PATCH: Add LocalStudentId as a fallback
            for (int i = 0; i < headers.Length; i++)
                if (headers[i].Trim().Equals("LocalStudentId", StringComparison.OrdinalIgnoreCase))
                    return i;

            throw new InvalidOperationException("Required column 'StudentId' or 'Local Student ID' was not found in the file header.");
        }

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { }
        }

        private async Task<Dictionary<Guid, string>> LoadSchemesForIds(IEnumerable<Guid> ids)
        {
            var result = new Dictionary<Guid, string>();
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return result;

            var sb = new StringBuilder("SELECT id, human_coding_scheme FROM dbo.standards WHERE id IN (");
            var parameters = new List<SqlParameter>();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var paramName = $"@p{i}";
                sb.Append(paramName);
                parameters.Add(new SqlParameter(paramName, list[i].ToString())); // Convert GUID to string for parameter
            }
            sb.Append(')');

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(sb.ToString(), conn);
            cmd.Parameters.AddRange(parameters.ToArray());

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                // Try to parse the id column as either GUID or string
                Guid guidId;
                var idValue = rdr.GetValue(0);

                if (idValue is Guid directGuid)
                {
                    guidId = directGuid;
                }
                else if (idValue is string stringId && Guid.TryParse(stringId, out guidId))
                {
                    // Successfully parsed string as GUID
                }
                else
                {
                    continue; // Skip this row if we can't parse the ID
                }

                result[guidId] = rdr.GetString(1);
            }
            return result;
        }

        private async Task<(int found, List<int> missingSample)> ValidateLocalStudents(int districtId, HashSet<int> localIds)
        {
            if (localIds.Count == 0) return (0, new List<int>());

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Use a temp table for better performance with large ID sets
            await using (var createTempCmd = new SqlCommand("CREATE TABLE #LocalIds (Id NVARCHAR(64) PRIMARY KEY);", conn))
            {
                await createTempCmd.ExecuteNonQueryAsync();
            }

            using (var bulkCopy = new SqlBulkCopy(conn))
            {
                bulkCopy.DestinationTableName = "#LocalIds";
                var idTable = new DataTable();
                idTable.Columns.Add("Id", typeof(string));
                foreach (var id in localIds)
                {
                    idTable.Rows.Add(id.ToString());
                }
                await bulkCopy.WriteToServerAsync(idTable);
            }

            var sql = @"
                SELECT s.localstudentid
                FROM dbo.students s
                JOIN #LocalIds temp ON s.localstudentid = temp.Id
                WHERE s.districtid = @districtId
                AND ISNULL(s.Inactive, 0) = 0;   -- NEW
            ";


            var found = new HashSet<int>();
            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@districtId", districtId);
                using (var rdr = await cmd.ExecuteReaderAsync())
                {
                    while (await rdr.ReadAsync())
                    {
                        if (int.TryParse(rdr.GetString(0), out var lid))
                        {
                            found.Add(lid);
                        }
                    }
                }
            }

            var missing = localIds.Except(found).Take(20).ToList();
            return (found.Count, missing);
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
    }
}