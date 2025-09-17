using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using ORSV2.Models;
using System.Security.Claims;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize(Policy = "CanViewCurriculumForms")]
    public class Form2Model : SecureReportPageModel
    {
        public record StudentVm(
            string LastName,
            string FirstName,
            string RaceEthnicity,
            string LanguageFluency,
            bool SWD,
            int TotalPassed
        )
        {
            public bool IsAA => Form2Model.IsBlackAA(RaceEthnicity);
            public bool IsEL => Form2Model.IsEnglishLearner(LanguageFluency);
            public bool IsSWD => SWD;
            public string SWDDisplay => SWD ? "True" : "";

            public string Quadrant => TotalPassed >= 4 ? "Challenge"
                                : TotalPassed == 3 ? "Benchmark"
                                : TotalPassed == 2 ? "Strategic"
                                : "Intensive";
        }

        // New flattened student view model for DataTable
        public class FlatStudentVm
        {
            public string QuadrantName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string RaceEthnicity { get; set; } = "";
            public string LanguageFluency { get; set; } = "";
            public bool SWD { get; set; }
            public int TotalPassed { get; set; }
            public bool IsQuadrantTotal { get; set; }
            public string? TotalsSummary { get; set; }
            
            // Helper properties for CSS classes
            public bool IsAA => Form2Model.IsBlackAA(RaceEthnicity);
            public bool IsEL => Form2Model.IsEnglishLearner(LanguageFluency);
            public bool IsSWD => SWD;
            public string SWDDisplay => SWD ? "True" : "";
            public bool HasMultipleFlags => (IsAA ? 1 : 0) + (IsEL ? 1 : 0) + (IsSWD ? 1 : 0) >= 2;
            public string QuadrantCssClass => QuadrantName.ToLowerInvariant();
            
            public FlatStudentVm() { }
            
            public FlatStudentVm(StudentVm student)
            {
                QuadrantName = student.Quadrant;
                LastName = student.LastName;
                FirstName = student.FirstName;
                RaceEthnicity = student.RaceEthnicity;
                LanguageFluency = student.LanguageFluency;
                SWD = student.SWD;
                TotalPassed = student.TotalPassed;
                IsQuadrantTotal = false;
            }
        }

        public class TotalsBucket
        {
            public int Passed { get; set; }
            public int Total { get; set; }
            public string PercentString => Total == 0 ? "0.00%" : (100.0m * Passed / Total).ToString("0.00'%'");
        }

        public class QuadTotals
        {
            public TotalsBucket AA { get; } = new();
            public TotalsBucket EL { get; } = new();
            public TotalsBucket SWD { get; } = new();
            public TotalsBucket All { get; } = new();
        }

        public class QuadVm
        {
            public string Title { get; set; } = "";
            public string CssKey { get; set; } = "";
            public List<StudentVm> Students { get; set; } = new();
            public QuadTotals Totals { get; set; } = new();
        }

        // Filters (same as Form 1)
        [BindProperty(SupportsGet = true)] public int? DistrictId { get; set; }
        [BindProperty(SupportsGet = true)] public string? UnitCycle { get; set; }
        [BindProperty(SupportsGet = true)] public string? BatchId { get; set; }
        [BindProperty(SupportsGet = true)] public int? SchoolId { get; set; }
        [BindProperty(SupportsGet = true)] public int? TeacherId { get; set; }
        public string? SelectedAssessmentName { get; private set; }
        public string FormattedTitle { get; private set; } = "Quadrant Breakdown"; 
        // Shared dropdowns (mirroring Form 1)
        public List<SelectListItem> AvailableUnitCycles { get; private set; } = new();
        public List<SelectListItem> AvailableBatches { get; private set; } = new();
        public List<SelectListItem> AvailableSchools { get; private set; } = new();
        public List<SelectListItem> AvailableTeachers { get; private set; } = new();
        public string? DistrictName { get; private set; }

        // Data - keeping original for backward compatibility
        public List<StudentVm> Students { get; private set; } = new();
        
        // New flattened data for DataTable
        public List<FlatStudentVm> FlattenedStudents { get; private set; } = new();

        private readonly IConfiguration _config;
        public Form2Model(IConfiguration config) => _config = config;
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public async Task OnGet()
        {
            InitializeUserDataScope(); // role + claims scoping

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Mirror Form1 scoping behavior
            if (IsDistrictAdmin && UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;
            if (IsSchoolAdmin && UserSchoolIds.Any())
            {
                DistrictId = UserDistrictId;
                if (!SchoolId.HasValue) SchoolId = UserSchoolIds.First();
            }
            if (IsTeacher && UserStaffId.HasValue)
            {
                DistrictId = UserDistrictId;
                if (UserSchoolIds.Any()) SchoolId = UserSchoolIds.First();
                TeacherId = UserStaffId;
            }

            // Fetch the District Name based on the determined DistrictId
            if (DistrictId.HasValue)
            {
                try
                {
                    using var cmd = new SqlCommand("SELECT Name FROM dbo.Districts WHERE Id = @DistrictId", conn);
                    cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        DistrictName = result.ToString();
                    }

                    Breadcrumbs = new List<BreadcrumbItem>
                    {
                        new BreadcrumbItem { Title = "Curriculum Alignment", Url = Url.Page("/CurriculumAlignment/Index") },
                        new BreadcrumbItem {
                            Title = $"{(DistrictName ?? "District")} - Select Forms",
                            Url = DistrictId.HasValue ? Url.Page("/CurriculumAlignment/Forms", new { districtId = DistrictId }) : null
                        },
                        new BreadcrumbItem { Title = "Form 2" } // current page, no URL
                    };
                }
                catch (SqlException)
                {
                }
            }

            if (DistrictId.HasValue)
                AvailableUnitCycles = await GetUnitCyclesByDistrictAsync(conn, DistrictId.Value);

            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(UnitCycle))
                AvailableBatches = await GetAssessmentsByUnitCycleAsync(conn, DistrictId.Value, UnitCycle!);
            if (!string.IsNullOrEmpty(BatchId))
            {
                SelectedAssessmentName = AvailableBatches.FirstOrDefault(b => b.Value == BatchId)?.Text;
            }

            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
                AvailableSchools = await GetSchoolsByAssessmentAsync(conn, DistrictId.Value, Guid.Parse(BatchId!), IsSchoolAdmin ? UserSchoolIds : null);

            if (DistrictId.HasValue && SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
                AvailableTeachers = await GetTeachersByAssessmentAsync(conn, DistrictId.Value, SchoolId.Value, Guid.Parse(BatchId!), IsTeacher ? UserStaffId : null);

            if (SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId) && Guid.TryParse(BatchId, out var bid))
            {
                try
                {
                    await LoadStudents(conn, bid);
                    var quadrantOrder = new Dictionary<string, int>
                    {
                        { "Challenge", 1 },
                        { "Benchmark", 2 },
                        { "Strategic", 3 },
                        { "Intensive", 4 }
                    };

                    Students = Students.OrderBy(s => quadrantOrder.GetValueOrDefault(s.Quadrant, 99))
                                    .ThenByDescending(s => s.TotalPassed)
                                    .ToList();
                    GenerateFlattenedData(); // New method to create DataTable data
                }
                catch (SqlException)
                {
                    ModelState.AddModelError("", "We couldn't load data for this selection.");
                    Students = new();
                    FlattenedStudents = new();
                }
            }
            var titleParts = new List<string>();
            if (!string.IsNullOrEmpty(SelectedAssessmentName))
            {
                titleParts.Add(SelectedAssessmentName);
            }

            var selectedSchool = AvailableSchools.FirstOrDefault(s => s.Value == SchoolId?.ToString());
            titleParts.Add(selectedSchool != null && !string.IsNullOrEmpty(selectedSchool.Text) ? selectedSchool.Text : "All Schools");

            var selectedTeacher = AvailableTeachers.FirstOrDefault(t => t.Value == TeacherId?.ToString());
            titleParts.Add(selectedTeacher != null && !string.IsNullOrEmpty(selectedTeacher.Text) ? selectedTeacher.Text : "All Teachers");

            if (titleParts.Any())
            {
                FormattedTitle = string.Join(" - ", titleParts);
            }
        }

        // === New method to generate flattened data for DataTable ===
        private void GenerateFlattenedData()
        {
            FlattenedStudents.Clear();
            
            var quadrantOrder = new[] { "Challenge", "Benchmark", "Strategic", "Intensive" };
            
            foreach (var quadrantName in quadrantOrder)
            {
                var quadVm = GetQuadrantVm(quadrantName);
                
                // Add all students in this quadrant
                foreach (var student in quadVm.Students)
                {
                    FlattenedStudents.Add(new FlatStudentVm(student));
                }
                
                // Add totals row for this quadrant
                FlattenedStudents.Add(new FlatStudentVm
                {
                    QuadrantName = quadrantName,
                    LastName = $"{quadrantName} Totals",
                    FirstName = "",
                    RaceEthnicity = "",
                    LanguageFluency = "",
                    SWD = false,
                    TotalPassed = 0,
                    IsQuadrantTotal = true,
                    TotalsSummary = $"AA: {quadVm.Totals.AA.PercentString} ({quadVm.Totals.AA.Passed}/{quadVm.Totals.AA.Total}) · " +
                                  $"EL: {quadVm.Totals.EL.PercentString} ({quadVm.Totals.EL.Passed}/{quadVm.Totals.EL.Total}) · " +
                                  $"SWD: {quadVm.Totals.SWD.PercentString} ({quadVm.Totals.SWD.Passed}/{quadVm.Totals.SWD.Total}) · " +
                                  $"All: {quadVm.Totals.All.PercentString} ({quadVm.Totals.All.Passed}/{quadVm.Totals.All.Total})"
                });
            }
        }

        // === Dropdown helpers (reused endpoints from Form 1) ===
        private async Task<List<SelectListItem>> GetUnitCyclesByDistrictAsync(SqlConnection conn, int districtId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a unit/cycle...", "") };
            using var cmd = new SqlCommand("dbo.GetUnitCyclesByDistrict", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) list.Add(new SelectListItem { Value = rdr["unit_cycle"].ToString(), Text = rdr["unit_cycle"].ToString() });
            return list;
        }

        private async Task<List<SelectListItem>> GetAssessmentsByUnitCycleAsync(SqlConnection conn, int districtId, string unitCycle)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select an assessment...", "") };
            using var cmd = new SqlCommand("dbo.GetAssessmentsByUnitCycle", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@UnitCycle", unitCycle);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) list.Add(new SelectListItem { Value = rdr["batch_id"].ToString(), Text = rdr["test_id"].ToString() });
            return list;
        }

        private async Task<List<SelectListItem>> GetSchoolsByAssessmentAsync(SqlConnection conn, int districtId, Guid batchId, List<int>? userSchoolIds = null)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a school...", "") };
            using var cmd = new SqlCommand("dbo.GetSchoolsByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (userSchoolIds != null && userSchoolIds.Any())
                cmd.Parameters.AddWithValue("@AllowedSchoolIds", string.Join(",", userSchoolIds));
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) list.Add(new SelectListItem { Value = rdr["Id"].ToString(), Text = rdr["Name"].ToString() });
            return list;
        }

        private async Task<List<SelectListItem>> GetTeachersByAssessmentAsync(SqlConnection conn, int districtId, int schoolId, Guid batchId, int? userStaffId = null)
        {
            var list = userStaffId.HasValue ? new List<SelectListItem>() : new List<SelectListItem> { new SelectListItem("All Teachers", "") };
            using var cmd = new SqlCommand("dbo.GetTeachersByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (userStaffId.HasValue) cmd.Parameters.AddWithValue("@UserStaffId", userStaffId.Value);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) list.Add(new SelectListItem { Value = rdr["StaffID"].ToString(), Text = rdr["TeacherName"].ToString() });
            return list;
        }

        // === Data load (ADO.NET; no EF) ===
        private async Task LoadStudents(SqlConnection conn, Guid batchId)
        {
            Students.Clear();

            using var cmd = new SqlCommand("dbo.GetQuadrantStudents_FromSummary", conn)
            {
                CommandType = CommandType.StoredProcedure
            };

            // UI filters (same shape you've been using)
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (DistrictId.HasValue) cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
            if (SchoolId.HasValue) cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
            if (TeacherId.HasValue) cmd.Parameters.AddWithValue("@TeacherId", TeacherId.Value);

            // Security (teacher/district validation)
            var userRole = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            cmd.Parameters.AddWithValue("@UserRole", userRole);
            if (IsTeacher && UserStaffId.HasValue) cmd.Parameters.AddWithValue("@UserScopeId", UserStaffId.Value);
            else if (IsDistrictAdmin && UserDistrictId.HasValue) cmd.Parameters.AddWithValue("@UserScopeId", UserDistrictId.Value);
            else cmd.Parameters.AddWithValue("@UserScopeId", DBNull.Value);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                Students.Add(new StudentVm(
                    LastName: rdr["LastName"]?.ToString() ?? "",
                    FirstName: rdr["FirstName"]?.ToString() ?? "",
                    RaceEthnicity: rdr["RaceEthnicity"]?.ToString() ?? "",
                    LanguageFluency: rdr["LanguageFluency"]?.ToString() ?? "",
                    SWD: rdr["SWD"] is bool b ? b :
                                    string.Equals(rdr["SWD"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase),
                    TotalPassed: Convert.ToInt32(rdr["TotalPassed"])
                ));
            }
        }

        // === View helper: builds the VM for a given quadrant (keeping for compatibility) ===
        public QuadVm GetQuadrantVm(string quadrant)
        {
            // Students in this specific quadrant
            var q = Students
                .Where(s => s.Quadrant.Equals(quadrant, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                .ToList();

            // Denominators: Total students IN EACH SUBGROUP across all quadrants
            int totalAll = Students.Count;
            int totalAA = Students.Count(s => s.IsAA);
            int totalEL = Students.Count(s => s.IsEL);
            int totalSWD = Students.Count(s => s.IsSWD);

            // Numerators: # in this quadrant (overall + by subgroup)
            int numAll = q.Count;
            int numAA = q.Count(s => s.IsAA);
            int numEL = q.Count(s => s.IsEL);
            int numSWD = q.Count(s => s.IsSWD);

            var vm = new QuadVm
            {
                Title = quadrant,
                CssKey = quadrant.ToLowerInvariant(),
                Students = q,
                Totals = new QuadTotals()
            };

            // Assign numerators (Passed) and the correct subgroup denominators (Total)
            vm.Totals.All.Passed = numAll;
            vm.Totals.All.Total = totalAll;

            vm.Totals.AA.Passed = numAA;
            vm.Totals.AA.Total = totalAA;

            vm.Totals.EL.Passed = numEL;
            vm.Totals.EL.Total = totalEL;

            vm.Totals.SWD.Passed = numSWD;
            vm.Totals.SWD.Total = totalSWD;

            return vm;
        }

        // Add these helpers inside the Form2Model class (e.g., just above StudentVm)
        private static string Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            // keep letters/digits only, lowercased (so "African American/Black" => "africanamericanblack")
            var chars = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
            return new string(chars);
        }

        private static bool IsBlackAA(string? race)
        {
            var v = Normalize(race);
            // catch common variants across districts
            return v.Contains("black") || v.Contains("africanamerican");
        }

        private static bool IsEnglishLearner(string? lf)
        {
            var v = Normalize(lf);
            // support: EL, L (local code), ELL, LEP, and the full text
            return v == "el" || v == "L" || v == "ell" || v == "lep" || v.Contains("englishlearner");
        }
    }
}