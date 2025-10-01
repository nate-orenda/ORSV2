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

        [BindProperty] public IFormFile? Upload { get; set; }
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


        public async Task OnGetAsync()
        {
            await LoadDistrictOptionsAsync();
            LoadSubjectOptions();
        }

        public async Task<IActionResult> OnPostAnalyzeFile()
        {
            if (Upload == null || Upload.Length == 0)
                return new JsonResult(new { success = false, message = "No file uploaded." });

            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            using (var stream = new FileStream(tempFilePath, FileMode.Create))
            {
                await Upload.CopyToAsync(stream);
            }

            TempData["TempPath"] = tempFilePath;
            
            AnalysisResults = await AnalyzeFile(tempFilePath);

            if (AnalysisResults != null)
            {
                TempData["AnalysisResults"] = System.Text.Json.JsonSerializer.Serialize(AnalysisResults);
            }

            return new JsonResult(new { success = true, analysis = AnalysisResults, tempPath = tempFilePath });
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

            // --- STUDENT COUNT LOGIC ---
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

            var records = new List<FormativeRecord>();
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
                        records.Add(new FormativeRecord(localId, standardCode, score, maxPoints, timestamp));
                    }
                }
            }

            var deduplicatedRecords = records
                .GroupBy(r => new { r.LocalStudentId, r.HumanCodingScheme })
                .Select(g => g.OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.Points).First())
                .ToList();

            var dt = new DataTable();
            dt.Columns.Add("local_student_id", typeof(int));
            dt.Columns.Add("test_id", typeof(string)); // --- ADD THIS LINE ---
            dt.Columns.Add("human_coding_scheme", typeof(string));
            dt.Columns.Add("points", typeof(decimal));
            dt.Columns.Add("max_points", typeof(decimal));

            foreach (var record in deduplicatedRecords)
            {
                // --- UPDATE THE ROW CREATION TO INCLUDE THE TEST ID ---
                dt.Rows.Add(record.LocalStudentId, TestId, record.HumanCodingScheme, record.Points, record.MaxPoints);
            }

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand("dbo.ImportAssessmentResults", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@DistrictId", DistrictId);
                    cmd.Parameters.AddWithValue("@TestId", TestId);
                    cmd.Parameters.AddWithValue("@SourceFile", Upload?.FileName ?? "Formative Import");
                    cmd.Parameters.AddWithValue("@Subject", Subject);
                    cmd.Parameters.AddWithValue("@UnitCycle", UnitCycle);

                    var tvp = cmd.Parameters.AddWithValue("@Rows", dt);
                    tvp.SqlDbType = SqlDbType.Structured;
                    tvp.TypeName = "dbo.AssessmentResultImportType";
                    
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            
            TempData["ImportSuccessMessage"] = $"Successfully imported {dt.Rows.Count} unique records.";
            if(System.IO.File.Exists(TempPath))
            {
                System.IO.File.Delete(TempPath);
            }

            return RedirectToPage();
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