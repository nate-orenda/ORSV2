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
    public class LennectionsModel : PageModel
    {
        // --- UI state ---
        [BindProperty] public int DistrictId { get; set; }
        [BindProperty] public string TestId { get; set; } = "";
        [BindProperty] public string Delimiter { get; set; } = "comma";
        [BindProperty] public IFormFile? Upload { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty] public string Subject { get; set; } = "";
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

        // --- NEW: FILE ANALYSIS RESULTS ---
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
        }

        private readonly IConfiguration _config;
        public LennectionsModel(IConfiguration config) => _config = config;

        private static readonly string[] PossibleTestHeaders = { "Assessment Name", "Assessment", "Test Name", "Test" };
        private static readonly Regex UnitCyclePattern = new(@"(?:unit|cycle|quarter|q)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task OnGet()
        {
            await LoadDistrictOptionsAsync();
        }

        /// <summary>
        /// NEW: AJAX endpoint for file analysis without full form submission
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

                // Analyze file
                var analysis = await AnalyzeFileContents(temp, Delimiter);
                
                // Return analysis results with temp path for later use
                return new JsonResult(new { 
                    success = true, 
                    analysis = analysis,
                    tempPath = temp  // Include temp path so frontend can store it
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { 
                    success = false, 
                    message = ex.Message 
                });
            }
        }

        public async Task<IActionResult> OnPostPreview()
        {
            // ***FIX***: Clear any lingering success messages from previous imports
            TempData.Remove("ImportSuccessMessage");
            TempData.Remove("ImportBatchId");

            await LoadDistrictOptionsAsync();
            
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

            // Auto-populate fields from file analysis
            var analysis = await AnalyzeFileContents(tempFilePath, Delimiter);
            
            // Auto-fill TestId if blank
            if (string.IsNullOrWhiteSpace(TestId) && !string.IsNullOrWhiteSpace(analysis.DetectedTestId))
            {
                TestId = AutoDetectedTestId = analysis.DetectedTestId;
            }

            // Auto-fill Subject if blank
            if (string.IsNullOrWhiteSpace(Subject) && !string.IsNullOrWhiteSpace(analysis.DetectedSubject))
            {
                Subject = analysis.DetectedSubject;
            }

            // Auto-fill UnitCycle if it's the default value
            if (UnitCycle == 1 && analysis.DetectedUnitCycle.HasValue)
            {
                UnitCycle = analysis.DetectedUnitCycle.Value;
            }

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

            using var sr = new StreamReader(System.IO.File.OpenRead(tempFilePath), Encoding.UTF8, true);
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
                ModelState.AddModelError("", "Couldn't find any question columns."); return Page();
            }

            // Debug: Log the column mappings - ADD TO MODEL STATE FOR VISIBILITY
            var debugInfo = new List<string>();
            debugInfo.Add($"Found {maps.Count} question column blocks:");
            foreach (var map in maps)
            {
                debugInfo.Add($"  Question {map.Q}: Score at column {map.ScoreCol} ({map.ScoreHeader}), Standard at column {map.StdCol} ({map.StdHeader})");
            }

            // Debug: Also log first few header columns to verify structure
            debugInfo.Add($"Headers (first 20): {string.Join(", ", headers.Take(20))}");
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var firstCells = firstDataLine.Split(delimiterChar);
                debugInfo.Add($"First data row (first 20 cells): {string.Join(", ", firstCells.Take(20))}");
            }

            var seenStandardIds = new HashSet<Guid>();
            
            // First, process the first data line that we already read for test ID detection
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var cells = firstDataLine.Split(delimiterChar);
                debugInfo.Add($"Processing first data row with {cells.Length} cells");
                
                foreach (var qm in maps)
                {
                    if (qm.StdCol >= cells.Length) 
                    {
                        debugInfo.Add($"  Question {qm.Q}: Standard column {qm.StdCol} is beyond row length {cells.Length}");
                        continue;
                    }
                    
                    var standardValue = (cells[qm.StdCol] ?? "").Trim();
                    debugInfo.Add($"  Question {qm.Q}: Raw standard value = '{standardValue}'");
                    
                    if (!string.IsNullOrEmpty(standardValue))
                    {
                        // Handle pipe-separated sub-standards - take the last (rightmost) GUID
                        var standardParts = standardValue.Split('|');
                        var lastStandardId = standardParts[standardParts.Length - 1].Trim();
                        
                        debugInfo.Add($"    Split into {standardParts.Length} parts, last part: '{lastStandardId}'");
                        
                        if (Guid.TryParse(lastStandardId, out var standardId))
                        {
                            seenStandardIds.Add(standardId);
                            debugInfo.Add($"    ✓ Successfully parsed GUID: {standardId}");
                        }
                        else
                        {
                            debugInfo.Add($"    ✗ Failed to parse as GUID: '{lastStandardId}'");
                        }
                    }
                    else
                    {
                        debugInfo.Add($"    Empty standard value");
                    }
                }
            }

            // Store debug info in ViewData for display
            ViewData["DebugInfo"] = string.Join("\n", debugInfo);
            
            // Then process the remaining data rows
            string? line;
            while ((line = await sr.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cells = line.Split(delimiterChar);
                foreach (var qm in maps)
                {
                    if (qm.StdCol >= cells.Length) continue;
                    var standardValue = (cells[qm.StdCol] ?? "").Trim();
                    
                    if (!string.IsNullOrEmpty(standardValue))
                    {
                        // Handle pipe-separated sub-standards - take the last (rightmost) GUID
                        var standardParts = standardValue.Split('|');
                        var lastStandardId = standardParts[standardParts.Length - 1].Trim();
                        
                        // Debug: Log what we're processing
                        System.Diagnostics.Debug.WriteLine($"Processing standard: '{standardValue}' -> Last part: '{lastStandardId}'");
                        
                        if (Guid.TryParse(lastStandardId, out var standardId))
                        {
                            seenStandardIds.Add(standardId);
                            System.Diagnostics.Debug.WriteLine($"Successfully parsed GUID: {standardId}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to parse as GUID: '{lastStandardId}'");
                        }
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
            
            // Read the first data line again to include it in validation
            firstDataLine = sr.Peek() >= 0 ? await sr.ReadLineAsync() : null;

            var localIds = new HashSet<int>();
            int scannedCount = 0;
            
            // Process the first line for validation
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var cells = firstDataLine.Split(delimiterChar);
                if (colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                    localIds.Add(lid);
                scannedCount++;
            }

            while ((line = await sr.ReadLineAsync()) != null && scannedCount < 2000)
            {
                var cells = line.Split(delimiterChar);
                if (colLocalId < cells.Length && int.TryParse(cells[colLocalId].Trim(), out var lid))
                    localIds.Add(lid);
                scannedCount++;
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

            var delimiterChar = Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var tvp = new DataTable();
            tvp.Columns.Add("local_student_id", typeof(int));
            tvp.Columns.Add("test_id", typeof(string));
            tvp.Columns.Add("human_coding_scheme", typeof(string));
            tvp.Columns.Add("points", typeof(decimal));
            tvp.Columns.Add("max_points", typeof(decimal));

            var agg = new Dictionary<(int localId, Guid standardId), (decimal points, int count)>();
            var allIds = new HashSet<Guid>();

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

                // Local function to process a single data row to avoid duplicate code.
                void ProcessLine(string? line)
                {
                    if (string.IsNullOrWhiteSpace(line)) return;
                    var cells = line.Split(delimiterChar);
                    if (colLocalId >= cells.Length || !int.TryParse(cells[colLocalId].Trim(), out var localId)) return;

                    foreach (var m in maps)
                    {
                        if (m.ScoreCol >= cells.Length || m.StdCol >= cells.Length) continue;
                        var rawPts = (cells[m.ScoreCol] ?? "").Trim();
                        var standardValue = (cells[m.StdCol] ?? "").Trim();

                        if (!string.IsNullOrEmpty(standardValue) &&
                            decimal.TryParse(rawPts, NumberStyles.Any, CultureInfo.InvariantCulture, out var pts))
                        {
                            // Handle pipe-separated sub-standards - take the last (rightmost) GUID
                            var standardParts = standardValue.Split('|');
                            var lastStandardId = standardParts[standardParts.Length - 1].Trim();

                            if (Guid.TryParse(lastStandardId, out var standardId))
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
                }

                // Read the first data line.
                string? firstDataLine = sr.Peek() >= 0 ? await sr.ReadLineAsync() : null;

                // If TestId is blank, try to detect it from this first line.
                if (string.IsNullOrWhiteSpace(TestId) && !string.IsNullOrWhiteSpace(firstDataLine))
                {
                    var firstDataCells = firstDataLine.Split(delimiterChar);
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
                ProcessLine(firstDataLine);

                // Loop through the REST of the file.
                string? currentLine;
                while ((currentLine = await sr.ReadLineAsync()) != null)
                {
                    ProcessLine(currentLine);
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

                // CHG: validate GUID strictly so we don’t hide earlier SQL issues
                var obj = await cmd.ExecuteScalarAsync();
                if (!Guid.TryParse(obj?.ToString(), out var batchGuid))
                    throw new InvalidOperationException($"Import failed - invalid batch ID returned: '{obj}'");

                // Step 2: Update student totals
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
                }

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

        // ---------- NEW ANALYSIS METHODS ----------

            /// <summary>
            /// Analyzes file contents and extracts metadata
            /// </summary>
        private async Task<FileAnalysisResult> AnalyzeFileContents(string filePath, string delimiter)
        {
            var result = new FileAnalysisResult();
            var delimiterChar = delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

            using var sr = new StreamReader(System.IO.File.OpenRead(filePath), Encoding.UTF8, true);

            // Read header
            var header = await sr.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(header))
            {
                result.IsValid = false;
                result.ValidationMessages.Add("File is empty or missing header row.");
                return result;
            }

            var headers = header.Split(delimiterChar).Select(h => h.Trim()).ToArray();
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
            string? firstDataLine = await sr.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(firstDataLine))
            {
                var firstDataCells = firstDataLine.Split(delimiterChar);

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
                result.DetectedUnitCycle = DetectUnitCycle(result.DetectedTestId);
            }

            // Count total rows
            int rowCount = 1; // Already read first data row
            while (await sr.ReadLineAsync() != null)
            {
                rowCount++;
            }
            result.TotalRows = rowCount;

            // Count standards columns (ItemStandard pattern)
            result.StandardsColumns = headers.Count(h => h.Contains("ItemStandard", StringComparison.OrdinalIgnoreCase));

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
                
                if (testLower.Contains("ela") || testLower.Contains("english") || testLower.Contains("reading") || testLower.Contains("language"))
                    return "ELA";
                    
                if (testLower.Contains("math"))
                    return "Math";
                    
                if (testLower.Contains("science"))
                    return "Science";
                    
                if (testLower.Contains("social") || testLower.Contains("history"))
                    return "Social Science";
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
        private int? DetectUnitCycle(string? testName)
        {
            if (string.IsNullOrWhiteSpace(testName)) return null;

            var match = UnitCyclePattern.Match(testName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var cycle))
            {
                return (cycle >= 1 && cycle <= 5) ? cycle : null;
            }

            return null;
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

        // ---------- EXISTING HELPER METHODS ----------

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