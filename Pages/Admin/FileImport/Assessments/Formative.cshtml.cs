using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
    public class FormativeModel : PageModel
    {
        // Configuration: Column names in the Formative CSV file.
        private const string IdentifierCol = "Identifier";
        private const string StandardCodeCol = "Standard Code";
        private const string ScoreCol = "Total Score";
        private const string MaxPointsCol = "Total Possible";
        private const string TestNameCol = "Formative Title";
        private const string TimestampCol = "Timestamp";

        [BindProperty] public List<IFormFile>? Uploads { get; set; }
        [BindProperty] public string? TempPath { get; set; }
        [BindProperty, Required] public int DistrictId { get; set; }
        [BindProperty, Required] public string Subject { get; set; } = "ELA";
        [BindProperty, Range(1, 5)] public int UnitCycle { get; set; } = 1;
        [BindProperty, Required] public string TestId { get; set; } = "";

        public bool HasPreview => Preview.Any();
        public List<SelectListItem> DistrictOptions { get; private set; } = new();
        public List<SelectListItem> SubjectOptions { get; private set; } = new();
        public FileAnalysisResult? AnalysisResults { get; private set; }
        public List<StandardPreview> Preview { get; private set; } = new();

        private readonly IConfiguration _config;
        public FormativeModel(IConfiguration config) => _config = config;

        private record FormativeRecord(int LocalStudentId, string HumanCodingScheme, decimal Points, decimal MaxPoints, DateTime Timestamp);
        // Updated record for previewing with student count
        public record StandardPreview(string StandardCode, bool ExistsInDb, int StudentCount);
        public string? StudentValidationSummary { get; private set; }


        public async Task OnGetAsync()
        {
            await LoadDistrictOptionsAsync();
            LoadSubjectOptions();
        }

        public async Task<IActionResult> OnPostAnalyzeFile()
        {
            if (Uploads == null || !Uploads.Any())
                return new JsonResult(new { success = false, message = "No files uploaded." });

            string? primaryTestTitle = null;
            string firstFileName = "";

            // --- 1. VALIDATION PASS ---
            // Pre-scan all files to ensure their 'Formative Title' matches.
            try
            {
                foreach (var file in Uploads)
                {
                    // ✨ FIX: Use 'using', not 'await using'
                    using (var stream = file.OpenReadStream())
                    using (var reader = new StreamReader(stream))
                    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                    {
                        await csv.ReadAsync();
                        csv.ReadHeader();

                        if (!await csv.ReadAsync()) // Read first data row
                        {
                            return new JsonResult(new { success = false, message = $"File is empty or has no data: {file.FileName}" });
                        }

                        // ✨ FIX: Change type to nullable 'string?'
                        string? currentTestTitle = csv.GetField(TestNameCol)?.Trim();

                        if (string.IsNullOrWhiteSpace(currentTestTitle))
                        {
                            return new JsonResult(new { success = false, message = $"File is missing 'Formative Title' in its first row: {file.FileName}" });
                        }

                        if (primaryTestTitle == null)
                        {
                            // This is the first file. Store its title as the "master" title.
                            primaryTestTitle = currentTestTitle;
                            firstFileName = file.FileName;
                        }
                        else if (primaryTestTitle != currentTestTitle)
                        {
                            // This file's title does not match the first file's title. Reject the batch.
                            return new JsonResult(new
                            {
                                success = false,
                                message = $"File Mismatch: '{file.FileName}' has title '{currentTestTitle}', which does not match the first file's ('{firstFileName}') title of '{primaryTestTitle}'. Please upload files from the same assessment only."
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Error during file validation: {ex.Message}" });
            }

            // --- 2. MERGE PASS ---
            // All files are validated. Now, we merge them into one.
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"formative_merge_{Guid.NewGuid():N}.csv");
            try
            {
                // ✨ FIX: Use 'await using' for FileStream/StreamWriter (they support it)
                await using (var mergedStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                await using (var mergedWriter = new StreamWriter(mergedStream))
                {
                    for (int i = 0; i < Uploads.Count; i++)
                    {
                        // ✨ FIX: Use 'using' for StreamReader
                        using (var stream = Uploads[i].OpenReadStream())
                        using (var reader = new StreamReader(stream))
                        {
                            if (i == 0)
                            {
                                // First file: write the whole thing (header + data)
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    await mergedWriter.WriteLineAsync(line);
                                }
                            }
                            else
                            {
                                // Subsequent files: skip the header, write data only
                                await reader.ReadLineAsync(); // Skip header row

                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    await mergedWriter.WriteLineAsync(line);
                                }
                            }
                        }
                    }
                }

                // --- 3. ANALYSIS PASS ---
                // Now, analyze the single MERGED file
                AnalysisResults = await AnalyzeFile(tempFilePath);
                
                return new JsonResult(new { success = true, analysis = AnalysisResults, tempPath = tempFilePath });
            }
            catch (Exception ex)
            {
                // Clean up the temp file if merging fails
                TryDelete(tempFilePath); 
                return new JsonResult(new { success = false, message = $"File merge failed: {ex.Message}" });
            }
        }

        // Add this small helper method to your class (if it's not already there)
        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { /* ignore */ }
        }

        public async Task<IActionResult> OnPostPreview()
        {
            await LoadDistrictOptionsAsync();
            LoadSubjectOptions();

            if (TempData["TempPath"] is string tempPath)
            {
                TempPath = tempPath;
            }

            if (string.IsNullOrEmpty(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "File analysis data is missing. Please upload and analyze the file again.");
                return Page();
            }

            AnalysisResults = await AnalyzeFile(TempPath);
            if (AnalysisResults != null)
            {
                if (string.IsNullOrWhiteSpace(TestId) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedTestName)) TestId = AnalysisResults.DetectedTestName;
                if (string.IsNullOrWhiteSpace(Subject) && !string.IsNullOrWhiteSpace(AnalysisResults.DetectedSubject)) Subject = AnalysisResults.DetectedSubject;
                if (UnitCycle == 1 && AnalysisResults.DetectedUnitCycle.HasValue) UnitCycle = AnalysisResults.DetectedUnitCycle.Value;
            }

            // --- STUDENT COUNT LOGIC (from your existing code) ---
            var studentCountsByStandard = new Dictionary<string, HashSet<int>>();
            using (var reader = new StreamReader(TempPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    var standardCode = csv.GetField(StandardCodeCol);
                    string? identifier = csv.GetField(IdentifierCol);
                    string? studentIdStr = identifier?.Split('_').LastOrDefault();

                    if (!string.IsNullOrWhiteSpace(standardCode) && int.TryParse(studentIdStr, out var studentId))
                    {
                        if (!studentCountsByStandard.ContainsKey(standardCode))
                        {
                            studentCountsByStandard[standardCode] = new HashSet<int>();
                        }
                        studentCountsByStandard[standardCode].Add(studentId);
                    }
                }
            }
            
            var existingStandards = await GetExistingStandards(studentCountsByStandard.Keys);

            Preview = studentCountsByStandard
                .Select(kvp => new StandardPreview(
                    kvp.Key, 
                    existingStandards.Contains(kvp.Key), 
                    kvp.Value.Count))
                .OrderBy(p => p.StandardCode)
                .ToList();

            // --- NEW VALIDATION LOGIC ---
            if (DistrictId > 0)
            {
                // Get a single set of all unique student IDs from the file
                var allStudentIds = studentCountsByStandard.Values
                    .SelectMany(idSet => idSet)
                    .ToHashSet();

                if (allStudentIds.Any())
                {
                    var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, allStudentIds);
                    StudentValidationSummary = $"{found} of {allStudentIds.Count} unique Student IDs from the file were matched to active students in district {DistrictId}"
                                            + (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : ".");
                }
                else
                {
                    StudentValidationSummary = "No valid student identifiers were found in the file.";
                }
            }
            else
            {
                StudentValidationSummary = "Select a District and run Preview again to validate student IDs.";
            }
            // --- END NEW LOGIC ---

            return Page();
        }
        
        public async Task<IActionResult> OnPostImport()
        {
            await LoadDistrictOptionsAsync();
            LoadSubjectOptions();

            if (!ModelState.IsValid) return Page();

            if (string.IsNullOrEmpty(TempPath) || !System.IO.File.Exists(TempPath))
            {
                ModelState.AddModelError("", "A file must be uploaded and previewed before importing.");
                return Page();
            }

            var latestRecords = new Dictionary<(int, string), FormativeRecord>();

            using (var reader = new StreamReader(TempPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                while (csv.Read())
                {
                    string? identifier = csv.GetField(IdentifierCol);
                    string? studentIdStr = identifier?.Split('_').LastOrDefault();
                    string? standardCode = csv.GetField(StandardCodeCol);

                    if (int.TryParse(studentIdStr, out var localId) &&
                        !string.IsNullOrWhiteSpace(standardCode) &&
                        decimal.TryParse(csv.GetField(ScoreCol), out var score) &&
                        decimal.TryParse(csv.GetField(MaxPointsCol), out var maxPoints) &&
                        DateTime.TryParse(csv.GetField(TimestampCol), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    {
                        var record = new FormativeRecord(localId, standardCode, score, maxPoints, timestamp);
                        var key = (localId, standardCode);

                        // Check if this record is newer than one we've already stored
                        if (!latestRecords.TryGetValue(key, out var existing) || record.Timestamp > existing.Timestamp)
                        {
                            latestRecords[key] = record;
                        }
                    }
                }
            }

            // The final list is now just the values from our dictionary.
            // This is dramatically faster than GroupBy/OrderBy.
            var deduplicatedRecords = latestRecords.Values.ToList();

            if (!deduplicatedRecords.Any())
            {
                ModelState.AddModelError("", "No valid, deduplicated records were found to import.");
                return Page();
            }

            // --- NEW LOGIC START ---

            // 1. Apply string-based standard mapping (mimics Lennections logic)
            var mappedRecords = deduplicatedRecords.Select(r =>
            {
                var mappedScheme = r.HumanCodingScheme;
                // Apply the SL.%.2 -> SL.%.3 mapping if applicable
                if (mappedScheme.StartsWith("SL.") && mappedScheme.EndsWith(".2"))
                {
                    mappedScheme = mappedScheme.Replace(".2", ".3");
                }
                return new { r.LocalStudentId, MappedScheme = mappedScheme, r.Points, r.MaxPoints };
            }).ToList();

            // 2. Get GUIDs for all unique, mapped schemes
            var allSchemes = mappedRecords.Select(r => r.MappedScheme).Distinct();
            var schemeToGuidMap = await GetStandardIdsForSchemes(allSchemes);

            // 3. Define the TVP (DataTable) to match AssessmentResultImportType
            var dt = new DataTable();
            dt.Columns.Add("local_student_id", typeof(string)); // Match Lennections (string)
            dt.Columns.Add("human_coding_scheme", typeof(string));
            dt.Columns.Add("points", typeof(decimal));
            dt.Columns.Add("max_points", typeof(decimal));
            dt.Columns.Add("standard_id", typeof(string)); // GUID as string

            // 4. Populate the TVP, linking to the GUID
            foreach (var record in mappedRecords)
            {
                // Only add rows where we successfully found a standard GUID
                if (schemeToGuidMap.TryGetValue(record.MappedScheme, out var standardId))
                {
                    dt.Rows.Add(
                        record.LocalStudentId.ToString(), // Pass as string
                        record.MappedScheme,
                        record.Points,
                        record.MaxPoints,
                        standardId.ToString("D") // Pass GUID as string
                    );
                }
            }

            if (dt.Rows.Count == 0)
            {
                ModelState.AddModelError("", "No records could be matched to existing standards in the database. Please check your file's 'Standard Code' values.");
                return Page();
            }

            // 5. Use the robust transaction and batch logic from Lennections
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            
            // Capture PRINT / low-severity RAISERROR messages
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
                    CommandTimeout = 120 
                };
                cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                cmd.Parameters.AddWithValue("@TestId", TestId);
                var sourceFileNames = Uploads != null ? string.Join(", ", Uploads.Select(f => f.FileName)) : "Formative Import";
                cmd.Parameters.AddWithValue("@SourceFile", sourceFileNames);
                cmd.Parameters.AddWithValue("@Subject", Subject);
                cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);

                var p = cmd.Parameters.AddWithValue("@Rows", dt);
                p.SqlDbType = SqlDbType.Structured;
                p.TypeName = "dbo.AssessmentResultImportType";
                
                // Capture the returned Batch GUID
                var obj = await cmd.ExecuteScalarAsync();
                if (!Guid.TryParse(obj?.ToString(), out var batchGuid))
                {
                    throw new InvalidOperationException($"Import failed - invalid batch ID returned: '{obj}'");
                }

                // Step 2: Update batch metadata (copied from Lennections)
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
                    metaCmd.Parameters.AddWithValue("@ImportSource", "Formative"); // <-- Set correct source
                    metaCmd.Parameters.AddWithValue("@BatchId", batchGuid);
                    await metaCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();

                TempData["ImportSuccessMessage"] = 
                    $"Import complete! Batch ID: {batchGuid}. " +
                    $"Processed {dt.Rows.Count} student-standard result rows. " +
                    (sqlMessages.Count > 0 ? $"Messages: {string.Join(" | ", sqlMessages)}" : "");
                TempData["ImportBatchId"] = batchGuid.ToString(); // For a "View Results" link

                if(System.IO.File.Exists(TempPath))
                {
                    System.IO.File.Delete(TempPath);
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                try { if (transaction?.Connection != null) transaction.Rollback(); } catch { /* ignore */ }

                var sb = new System.Text.StringBuilder("SQL error(s) during import:\n");
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
            // --- NEW LOGIC END ---
        }

        private async Task<FileAnalysisResult> AnalyzeFile(string filePath)
        {
            var result = new FileAnalysisResult();
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                await csv.ReadAsync();
                csv.ReadHeader();
                
                if (csv.HeaderRecord != null) result.Headers.AddRange(csv.HeaderRecord);

                result.HasIdentifierColumn = result.Headers.Contains(IdentifierCol);
                if(result.HasIdentifierColumn) result.IdentifierColumnName = IdentifierCol;

                int rowCount = 0;
                while (await csv.ReadAsync())
                {
                    if(rowCount == 0) // First data row
                    {
                        var formativeTitle = csv.GetField(TestNameCol);
                        result.DetectedTestName = formativeTitle;
                        
                        if (!string.IsNullOrWhiteSpace(formativeTitle))
                        {
                            if (formativeTitle.Contains("_ELA")) result.DetectedSubject = "ELA";
                            else if (formativeTitle.Contains("_IM")) result.DetectedSubject = "Math";

                            var match = Regex.Match(formativeTitle, @"_Cycle (\d)_");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var cycle))
                            {
                                result.DetectedUnitCycle = cycle;
                            }
                        }
                    }
                    rowCount++;
                }
                result.TotalRows = rowCount;
            }
            
            if (!result.HasIdentifierColumn)
                result.ValidationMessages.Add($"Identifier column '{IdentifierCol}' not found.");

            return result;
        }
        
        private async Task<HashSet<string>> GetExistingStandards(IEnumerable<string> standards)
        {
            var existing = new HashSet<string>();
            var standardList = standards.ToList();
            if (!standardList.Any()) return existing;

            using(var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                var parameters = new List<string>();
                var sqlCommand = new SqlCommand();
                for (int i = 0; i < standardList.Count; i++)
                {
                    var paramName = $"@standard{i}";
                    parameters.Add(paramName);
                    sqlCommand.Parameters.AddWithValue(paramName, standardList[i]);
                }

                sqlCommand.Connection = conn;
                sqlCommand.CommandText = $"SELECT human_coding_scheme FROM dbo.standards WHERE human_coding_scheme IN ({string.Join(", ", parameters)})";

                using(var reader = await sqlCommand.ExecuteReaderAsync())
                {
                    while(await reader.ReadAsync())
                    {
                        existing.Add(reader.GetString(0));
                    }
                }
            }
            return existing;
        }

        /// <summary>
        /// Gets the current school year (e.g., 2025 for the 2024-2025 school year)
        /// </summary>
        private static int GetSchoolYearFromUtcNow()
        {
            var now = DateTime.UtcNow;
            // July 1st is the cutoff.
            return now.Month >= 7 ? now.Year + 1 : now.Year; 
        }

        /// <summary>
        /// Fetches the Standard GUID for a given list of human_coding_scheme strings.
        /// </summary>
        private async Task<Dictionary<string, Guid>> GetStandardIdsForSchemes(IEnumerable<string> schemes)
        {
            var schemeMap = new Dictionary<string, Guid>();
            var schemeList = schemes.Distinct().ToList();
            if (!schemeList.Any()) return schemeMap;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            var parameters = new List<SqlParameter>();
            var sqlBuilder = new System.Text.StringBuilder("SELECT human_coding_scheme, id FROM dbo.standards WHERE human_coding_scheme IN (");

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
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("SELECT Id, Name FROM dbo.districts ORDER BY Name", conn))
                {
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            DistrictOptions.Add(new SelectListItem
                            {
                                Value = reader.GetInt32(0).ToString(),
                                Text = reader.GetString(1)
                            });
                        }
                    }
                }
            }
        }
        // Add this helper method inside your FormativeModel class
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
                AND ISNULL(s.Inactive, 0) = 0;
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
        private void LoadSubjectOptions()
        {
            SubjectOptions = new List<SelectListItem>
            {
                new SelectListItem("ELA", "ELA"),
                new SelectListItem("Math", "Math"),
                new SelectListItem("Science", "Science"),
                new SelectListItem("Social Science", "Social Science"),
            };
        }

        public class FileAnalysisResult
        {
            public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
            public List<string> Headers { get; set; } = new List<string>();
            public bool HasIdentifierColumn { get; set; }
            public string? IdentifierColumnName { get; set; }
            public int TotalRows { get; set; }
            public string? DetectedTestName { get; set; }
            public string? DetectedSubject { get; set; }
            public int? DetectedUnitCycle { get; set; }
            public List<string> ValidationMessages { get; set; } = new List<string>();
            public bool IsValid => !ValidationMessages.Any();
        }
    }
}