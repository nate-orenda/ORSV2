// ========================================
// ENHANCED FORMATIVE IMPORT WITH STANDARD INSERTION
// ========================================
// Key additions:
// 1. ConvertStandardCodeToFullFormat() - converts short codes to full format
// 2. Enhanced Preview showing NEW vs EXISTING standards
// 3. InsertMissingStandards() - safely inserts new standards
// 4. Better feedback before import commits
// ========================================

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
        private const string IdentifierCol = "Identifier";
        private const string StandardCodeCol = "Standard Code";
        private const string StandardDescCol = "Standard Description";
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
        public string? StudentValidationSummary { get; private set; }
        public string? StandardsInsertionSummary { get; private set; }

        private readonly IConfiguration _config;
        public FormativeModel(IConfiguration config) => _config = config;

        private record FormativeRecord(int LocalStudentId, string HumanCodingScheme, decimal Points, decimal MaxPoints, DateTime Timestamp);
        
        // Enhanced preview record with description
        public record StandardPreview(string StandardCode, string Description, bool ExistsInDb, int StudentCount);

        // NEW: Standard info record for insertion
        private record StandardInfo(string Code, string Description);

        // ========================================
        // NEW: STANDARD CODE CONVERSION LOGIC
        // ========================================
        
        /// <summary>
        /// Convert short Common Core codes to full database format.
        /// Examples: A.CED.1 -> HSA-CED.A.1, F.IF.3 -> HSF-IF.A.3
        /// </summary>
        private static string ConvertStandardCodeToFullFormat(string shortCode)
        {
            if (string.IsNullOrWhiteSpace(shortCode)) return shortCode;
            
            shortCode = shortCode.Trim();
            
            // Already in full format
            if (shortCode.StartsWith("HS")) return shortCode;
            
            // Match pattern: Domain.Cluster.Number[.SubLetter]
            var match = Regex.Match(shortCode, @"^([A-Z]+)\.([A-Z]+)\.(\d+)(?:\.([a-z]))?$");
            if (!match.Success) return shortCode;
            
            var domain = match.Groups[1].Value;
            var cluster = match.Groups[2].Value;
            var standardNum = match.Groups[3].Value;
            var subLetter = match.Groups[4].Value;
            
            // Default cluster letters (most common: A)
            var clusterLetterMap = new Dictionary<string, string>
            {
                {"CED", "A"}, {"REI", "A"}, {"SSE", "A"}, {"APR", "A"},
                {"IF", "A"}, {"BF", "A"}, {"LE", "A"}, {"TF", "A"}
            };
            
            // Special cases (REI.3 and REI.4 are in cluster B)
            var specialCases = new Dictionary<string, string>
            {
                {"A.REI.3", "HSA-REI.B.3"},
                {"A.REI.4", "HSA-REI.B.4"}
            };
            
            if (specialCases.TryGetValue(shortCode, out var special))
                return special;
            
            var clusterLetter = clusterLetterMap.TryGetValue(cluster, out var letter) ? letter : "A";
            
            var fullCode = $"HS{domain}-{cluster}.{clusterLetter}.{standardNum}";
            if (!string.IsNullOrEmpty(subLetter))
                fullCode += $".{subLetter}";
            
            return fullCode;
        }

        // ========================================
        // PAGE HANDLERS
        // ========================================

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

            // Validation pass
            try
            {
                foreach (var file in Uploads)
                {
                    using (var stream = file.OpenReadStream())
                    using (var reader = new StreamReader(stream))
                    using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                    {
                        await csv.ReadAsync();
                        csv.ReadHeader();

                        if (!await csv.ReadAsync())
                        {
                            return new JsonResult(new { success = false, message = $"File is empty or has no data: {file.FileName}" });
                        }

                        string? currentTestTitle = csv.GetField(TestNameCol)?.Trim();

                        if (string.IsNullOrWhiteSpace(currentTestTitle))
                        {
                            return new JsonResult(new { success = false, message = $"File is missing 'Formative Title': {file.FileName}" });
                        }

                        if (primaryTestTitle == null)
                        {
                            primaryTestTitle = currentTestTitle;
                            firstFileName = file.FileName;
                        }
                        else if (primaryTestTitle != currentTestTitle)
                        {
                            return new JsonResult(new
                            {
                                success = false,
                                message = $"File Mismatch: '{file.FileName}' has title '{currentTestTitle}', which does not match '{firstFileName}' title '{primaryTestTitle}'. Please upload files from the same assessment only."
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Error during file validation: {ex.Message}" });
            }

            // Merge pass
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"formative_merge_{Guid.NewGuid():N}.csv");
            try
            {
                await using (var mergedStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                await using (var mergedWriter = new StreamWriter(mergedStream))
                {
                    for (int i = 0; i < Uploads.Count; i++)
                    {
                        using (var stream = Uploads[i].OpenReadStream())
                        using (var reader = new StreamReader(stream))
                        {
                            if (i == 0)
                            {
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    await mergedWriter.WriteLineAsync(line);
                                }
                            }
                            else
                            {
                                await reader.ReadLineAsync(); // Skip header
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    await mergedWriter.WriteLineAsync(line);
                                }
                            }
                        }
                    }
                }

                AnalysisResults = await AnalyzeFile(tempFilePath);
                return new JsonResult(new { success = true, analysis = AnalysisResults, tempPath = tempFilePath });
            }
            catch (Exception ex)
            {
                TryDelete(tempFilePath);
                return new JsonResult(new { success = false, message = $"File merge failed: {ex.Message}" });
            }
        }

        // ========================================
        // ENHANCED PREVIEW WITH STANDARD CONVERSION
        // ========================================
        
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
                ModelState.AddModelError("", "File not found. Please re-upload.");
                return Page();
            }

            var standardsData = new Dictionary<string, (string description, HashSet<int> studentIds)>();

            using (var reader = new StreamReader(TempPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                
                while (csv.Read())
                {
                    var originalStandardCode = csv.GetField(StandardCodeCol);
                    var standardDescription = csv.GetField(StandardDescCol) ?? "";
                    string? identifier = csv.GetField(IdentifierCol);
                    string? studentIdStr = identifier?.Split('_').LastOrDefault();

                    if (!string.IsNullOrWhiteSpace(originalStandardCode) && int.TryParse(studentIdStr, out var studentId))
                    {
                        // Convert to full format
                        var convertedCode = ConvertStandardCodeToFullFormat(originalStandardCode);
                        
                        if (!standardsData.ContainsKey(convertedCode))
                        {
                            standardsData[convertedCode] = (standardDescription, new HashSet<int>());
                        }
                        standardsData[convertedCode].studentIds.Add(studentId);
                    }
                }
            }

            // Check which standards exist
            var existingStandards = await GetExistingStandards(standardsData.Keys);

            // Build preview with existence info
            Preview = standardsData
                .Select(kvp => new StandardPreview(
                    kvp.Key,
                    kvp.Value.description,
                    existingStandards.Contains(kvp.Key),
                    kvp.Value.studentIds.Count))
                .OrderBy(p => p.StandardCode)
                .ToList();

            // NEW: Standards insertion summary
            var newStandards = Preview.Where(p => !p.ExistsInDb).ToList();
            if (newStandards.Any())
            {
                StandardsInsertionSummary = $"⚠️ {newStandards.Count} NEW standards will be inserted into the database during import. " +
                    $"Examples: {string.Join(", ", newStandards.Take(5).Select(s => s.StandardCode))}";
            }
            else
            {
                StandardsInsertionSummary = "✓ All standards exist in the database.";
            }

            // Student validation
            if (DistrictId > 0)
            {
                var allStudentIds = standardsData.Values
                    .SelectMany(data => data.studentIds)
                    .ToHashSet();

                if (allStudentIds.Any())
                {
                    var (found, sampleMissing) = await ValidateLocalStudents(DistrictId, allStudentIds);
                    StudentValidationSummary = $"{found} of {allStudentIds.Count} unique Student IDs matched in district {DistrictId}"
                                            + (sampleMissing.Count > 0 ? $". Missing sample: {string.Join(", ", sampleMissing)}" : ".");
                }
                else
                {
                    StudentValidationSummary = "No valid student identifiers found.";
                }
            }
            else
            {
                StudentValidationSummary = "Select a District and run Preview again to validate student IDs.";
            }

            return Page();
        }

        // ========================================
        // ENHANCED IMPORT WITH STANDARD INSERTION
        // ========================================
        
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

            // Step 1: Parse and deduplicate records (with conversion)
            var latestRecords = new Dictionary<(int, string), FormativeRecord>();
            var standardsToInsert = new Dictionary<string, string>(); // code -> description

            using (var reader = new StreamReader(TempPath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                
                while (csv.Read())
                {
                    string? identifier = csv.GetField(IdentifierCol);
                    string? studentIdStr = identifier?.Split('_').LastOrDefault();
                    string? originalStandardCode = csv.GetField(StandardCodeCol);
                    string? standardDescription = csv.GetField(StandardDescCol);

                    if (int.TryParse(studentIdStr, out var localId) &&
                        !string.IsNullOrWhiteSpace(originalStandardCode) &&
                        decimal.TryParse(csv.GetField(ScoreCol), out var score) &&
                        decimal.TryParse(csv.GetField(MaxPointsCol), out var maxPoints) &&
                        DateTime.TryParse(csv.GetField(TimestampCol), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                    {
                        // Convert standard code
                        var convertedCode = ConvertStandardCodeToFullFormat(originalStandardCode);
                        
                        // Track for potential insertion
                        if (!standardsToInsert.ContainsKey(convertedCode))
                        {
                            standardsToInsert[convertedCode] = standardDescription ?? "";
                        }
                        
                        var record = new FormativeRecord(localId, convertedCode, score, maxPoints, timestamp);
                        var key = (localId, convertedCode);

                        if (!latestRecords.TryGetValue(key, out var existing) || record.Timestamp > existing.Timestamp)
                        {
                            latestRecords[key] = record;
                        }
                    }
                }
            }

            var deduplicatedRecords = latestRecords.Values.ToList();

            if (!deduplicatedRecords.Any())
            {
                ModelState.AddModelError("", "No valid records found to import.");
                return Page();
            }

            // Step 2: Check which standards need to be inserted
            var existingStandards = await GetExistingStandards(standardsToInsert.Keys);
            var missingStandards = standardsToInsert
                .Where(kvp => !existingStandards.Contains(kvp.Key))
                .Select(kvp => new StandardInfo(kvp.Key, kvp.Value))
                .ToList();

            // Step 3: Insert missing standards BEFORE processing records
            int insertedStandardsCount = 0;
            if (missingStandards.Any())
            {
                insertedStandardsCount = await InsertMissingStandards(missingStandards);
            }

            // Step 4: Apply additional mapping (SL.%.2 -> SL.%.3)
            var mappedRecords = deduplicatedRecords.Select(r =>
            {
                var mappedScheme = r.HumanCodingScheme;
                if (mappedScheme.StartsWith("SL.") && mappedScheme.EndsWith(".2"))
                {
                    mappedScheme = mappedScheme.Replace(".2", ".3");
                }
                return new { r.LocalStudentId, MappedScheme = mappedScheme, r.Points, r.MaxPoints };
            }).ToList();

            // Step 5: Get GUIDs for all schemes (including newly inserted ones)
            var allSchemes = mappedRecords.Select(r => r.MappedScheme).Distinct();
            var schemeToGuidMap = await GetStandardIdsForSchemes(allSchemes);

            // Step 6: Build TVP
            var dt = new DataTable();
            dt.Columns.Add("local_student_id", typeof(string));
            dt.Columns.Add("human_coding_scheme", typeof(string));
            dt.Columns.Add("points", typeof(decimal));
            dt.Columns.Add("max_points", typeof(decimal));
            dt.Columns.Add("standard_id", typeof(string));

            foreach (var record in mappedRecords)
            {
                if (schemeToGuidMap.TryGetValue(record.MappedScheme, out var standardId))
                {
                    dt.Rows.Add(
                        record.LocalStudentId.ToString(),
                        record.MappedScheme,
                        record.Points,
                        record.MaxPoints,
                        standardId.ToString("D")
                    );
                }
            }

            if (dt.Rows.Count == 0)
            {
                ModelState.AddModelError("", "No records could be matched to standards. This should not happen after insertion.");
                return Page();
            }

            // Step 7: Execute import
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            
            var sqlMessages = new List<string>();
            conn.InfoMessage += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Message)) sqlMessages.Add(e.Message); };
            
            await conn.OpenAsync();
            using var transaction = conn.BeginTransaction();

            try
            {
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
                
                var obj = await cmd.ExecuteScalarAsync();
                if (!Guid.TryParse(obj?.ToString(), out var batchGuid))
                {
                    throw new InvalidOperationException($"Import failed - invalid batch ID returned: '{obj}'");
                }

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
                    metaCmd.Parameters.AddWithValue("@ImportSource", "Formative");
                    metaCmd.Parameters.AddWithValue("@BatchId", batchGuid);
                    await metaCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();

                var successMessage = $"Import complete! Batch ID: {batchGuid}. " +
                    $"Processed {dt.Rows.Count} student-standard result rows.";
                
                if (insertedStandardsCount > 0)
                {
                    successMessage += $" Inserted {insertedStandardsCount} new standards.";
                }
                
                if (sqlMessages.Count > 0)
                {
                    successMessage += $" Messages: {string.Join(" | ", sqlMessages)}";
                }

                TempData["ImportSuccessMessage"] = successMessage;
                TempData["ImportBatchId"] = batchGuid.ToString();

                if (System.IO.File.Exists(TempPath))
                {
                    System.IO.File.Delete(TempPath);
                }

                return RedirectToPage();
            }
            catch (SqlException sqlEx)
            {
                try { if (transaction?.Connection != null) transaction.Rollback(); } catch { }

                var sb = new System.Text.StringBuilder("SQL error(s) during import:\n");
                foreach (SqlError err in sqlEx.Errors)
                    sb.AppendLine($"• {err.Number} (Severity {err.Class}) at line {err.LineNumber} in {err.Procedure}: {err.Message}");
                if (sqlMessages.Count > 0) sb.AppendLine($"Messages: {string.Join(" | ", sqlMessages)}");

                ModelState.AddModelError(string.Empty, sb.ToString());
                return Page();
            }
            catch (Exception ex)
            {
                try { if (transaction?.Connection != null) transaction.Rollback(); } catch { }

                var detail = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                if (sqlMessages.Count > 0) detail += $" | Messages: {string.Join(" | ", sqlMessages)}";
                ModelState.AddModelError(string.Empty, $"Import failed: {detail}");
                return Page();
            }
        }

        // ========================================
        // NEW: STANDARD INSERTION METHOD
        // ========================================
        
        /// <summary>
        /// Insert missing standards into the database with proper metadata
        /// </summary>
        private async Task<int> InsertMissingStandards(List<StandardInfo> standards)
        {
            if (!standards.Any()) return 0;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            int insertedCount = 0;

            foreach (var std in standards)
            {
                try
                {
                    var standardId = Guid.NewGuid();
                    
                    // Determine education level from standard code
                    var educationLevel = "9-12"; // Default for high school
                    if (std.Code.Contains("K-2")) educationLevel = "K-2";
                    else if (std.Code.Contains("3-5")) educationLevel = "3-5";
                    else if (std.Code.Contains("6-8")) educationLevel = "6-8";

                    var sql = @"
                        IF NOT EXISTS (SELECT 1 FROM dbo.standards WHERE human_coding_scheme = @Code)
                        BEGIN
                            INSERT INTO dbo.standards (id, human_coding_scheme, full_statement, education_level, last_change_date_time)
                            VALUES (@Id, @Code, @Description, @EducationLevel, GETUTCDATE())
                        END";

                    using var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@Id", standardId.ToString());
                    cmd.Parameters.AddWithValue("@Code", std.Code);
                    cmd.Parameters.AddWithValue("@Description", std.Description);
                    cmd.Parameters.AddWithValue("@EducationLevel", educationLevel);

                    await cmd.ExecuteNonQueryAsync();
                    insertedCount++;
                }
                catch (Exception ex)
                {
                    // Log but continue with other standards
                    Console.WriteLine($"Failed to insert standard {std.Code}: {ex.Message}");
                }
            }

            return insertedCount;
        }

        // ========================================
        // HELPER METHODS (UNCHANGED)
        // ========================================

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
                    if(rowCount == 0)
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

        private static int GetSchoolYearFromUtcNow()
        {
            var now = DateTime.UtcNow;
            return now.Month >= 7 ? now.Year + 1 : now.Year;
        }

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

        private async Task<(int found, List<int> missingSample)> ValidateLocalStudents(int districtId, HashSet<int> localIds)
        {
            if (localIds.Count == 0) return (0, new List<int>());

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

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

        private static void TryDelete(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { System.IO.File.Delete(path); } catch { }
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