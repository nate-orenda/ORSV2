using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;
using System;
using System.Security.Claims;
using ORSV2.Models;

namespace ORSV2.Pages.DataReflection
{
    [Authorize(Policy = "CanViewCurriculumForms")]
    public class Form1Model : SecureReportPageModel
    {
        // ... all of your existing class definitions (ColDef, GroupSummary, etc.) are correct and remain here ...
        public class ColDef { public string Code { get; set; } = ""; public string ShortStatement { get; set; } = ""; }
        public record GroupKey(string TeacherName, string Period);

        public class GroupSummary
        {
            public GroupKey Key { get; init; } = new("", "");
            public int StudentCount { get; init; }
            public int[] PassedPerStd { get; init; } = Array.Empty<int>();
            public int[] NotPassedPerStd { get; init; } = Array.Empty<int>();
            public int[] DenomPerStd { get; init; } = Array.Empty<int>();
            public int PassedByTotal { get; init; }
            public int NotPassedByTotal { get; init; }
        }

        public class GrandSummary
        {
            public int StudentCount { get; init; }
            public int[] PassedPerStd { get; init; } = Array.Empty<int>();
            public int[] NotPassedPerStd { get; init; } = Array.Empty<int>();
            public int[] DenomPerStd { get; init; } = Array.Empty<int>();
            public int PassedByTotal { get; init; }
            public int NotPassedByTotal { get; init; }
        }

        public class QuadrantSummary
        {
            public int TotalTested { get; init; }
            public int Challenge { get; init; }
            public int Benchmark { get; init; }
            public int Strategic { get; init; }
            public int Intensive { get; init; }
        }

        public class RowVm
        {
            public string StudentName { get; set; } = "";
            public string LocalId { get; set; } = "";
            public string TeacherName { get; set; } = "";
            public string Period { get; set; } = "";
            public List<decimal?> Points { get; set; } = new();
            public int TotalPassed { get; set; }
        }


        [BindProperty(SupportsGet = true)] public int? DistrictId { get; set; }
        [BindProperty(SupportsGet = true)] public string? UnitCycle { get; set; }
        [BindProperty(SupportsGet = true)] public string? BatchId { get; set; }
        [BindProperty(SupportsGet = true)] public int? SchoolId { get; set; }
        [BindProperty(SupportsGet = true)] public int? TeacherId { get; set; }

        public List<GroupSummary> GroupSummaries { get; private set; } = new();
        public GrandSummary? GrandTotals { get; private set; }
        public QuadrantSummary? Quadrants { get; private set; }
        public List<SelectListItem> AvailableUnitCycles { get; private set; } = new();
        public List<SelectListItem> AvailableBatches { get; private set; } = new();
        public List<SelectListItem> AvailableSchools { get; private set; } = new();
        public List<SelectListItem> AvailableTeachers { get; private set; } = new();
        public List<ColDef> Columns { get; private set; } = new();
        public List<RowVm> Rows { get; private set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
        private readonly IConfiguration _config;
        public Form1Model(IConfiguration config) => _config = config;

        public async Task OnGet()
        {
            InitializeUserDataScope();

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            string? districtName = null;
            if (DistrictId.HasValue)
            {
                districtName = await GetDistrictNameAsync(conn, DistrictId.Value);
            }

            // Build: Data Reflection -> {District} Forms -> Form 1
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Data Reflection", Url = Url.Page("/DataReflection/Index") },
                new BreadcrumbItem {
                    Title = $"{(districtName ?? "District")} - Select Forms",
                    Url = DistrictId.HasValue ? Url.Page("/DataReflection/Forms", new { districtId = DistrictId }) : null
                },
                new BreadcrumbItem { Title = "Form 1" } // current page, no URL
            };

            if (IsDistrictAdmin && UserDistrictId.HasValue)
            {
                DistrictId = UserDistrictId.Value;
            }
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

            if (DistrictId.HasValue)
            {
                AvailableUnitCycles = await GetUnitCyclesByDistrictAsync(conn, DistrictId.Value);
            }
            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(UnitCycle))
            {
                AvailableBatches = await GetAssessmentsByUnitCycleAsync(conn, DistrictId.Value, UnitCycle);
            }
            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
            {
                AvailableSchools = await GetSchoolsByAssessmentAsync(conn, DistrictId.Value, Guid.Parse(BatchId), 
                    (IsSchoolAdmin || IsTeacher || User.IsInRole("Counselor")) ? UserSchoolIds : null);
            }
            if (DistrictId.HasValue && SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
            {
                AvailableTeachers = await GetTeachersByAssessmentAsync(conn, DistrictId.Value, SchoolId.Value, Guid.Parse(BatchId), IsTeacher ? UserStaffId : null);
            }

            if (SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId) && Guid.TryParse(BatchId, out var bid))
            {
                await LoadMatrixData(conn, bid);
                BuildSummaries();
            }
        }

        // --- Data Fetching Helper Methods ---
        // GetUnitCyclesByDistrictAsync, GetAssessmentsByUnitCycleAsync, GetSchoolsByAssessmentAsync,
        // and GetTeachersByAssessmentAsync are all correct from your code and remain here.
        private async Task<List<SelectListItem>> GetUnitCyclesByDistrictAsync(SqlConnection conn, int districtId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a unit/cycle...", "") };
            using var cmd = new SqlCommand("dbo.GetUnitCyclesByDistrict", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["unit_cycle"].ToString(), Text = rdr["unit_cycle"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetAssessmentsByUnitCycleAsync(
            SqlConnection conn, 
            int districtId, 
            string unitCycle)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select an assessment...", "") };
            using var cmd = new SqlCommand("dbo.GetAssessmentsByUnitCycle", conn) 
            { 
                CommandType = CommandType.StoredProcedure 
            };
            
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@UnitCycle", unitCycle);
            
            // Add user scope parameters
            string userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "";
            cmd.Parameters.AddWithValue("@UserRole", userRole);
            
            if (IsTeacher && UserStaffId.HasValue)
                cmd.Parameters.AddWithValue("@UserStaffId", UserStaffId.Value);
            else
                cmd.Parameters.AddWithValue("@UserStaffId", DBNull.Value);
            
            if (IsSchoolAdmin && UserSchoolIds.Any())
                cmd.Parameters.AddWithValue("@AllowedSchoolIds", string.Join(",", UserSchoolIds));
            else
                cmd.Parameters.AddWithValue("@AllowedSchoolIds", DBNull.Value);
            
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem 
                { 
                    Value = rdr["batch_id"].ToString(), 
                    Text = rdr["test_id"].ToString() 
                });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetSchoolsByAssessmentAsync(SqlConnection conn, int districtId, Guid batchId, List<int>? userSchoolIds = null)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a school...", "") };
            using var cmd = new SqlCommand("dbo.GetSchoolsByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (userSchoolIds != null && userSchoolIds.Any())
            {
                cmd.Parameters.AddWithValue("@AllowedSchoolIds", string.Join(",", userSchoolIds));
            }
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["Id"].ToString(), Text = rdr["Name"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetTeachersByAssessmentAsync(SqlConnection conn, int districtId, int schoolId, Guid batchId, int? userStaffId = null)
        {
            var list = userStaffId.HasValue
                ? new List<SelectListItem>()
                : new List<SelectListItem> { new SelectListItem("All Teachers", "") };
            using var cmd = new SqlCommand("dbo.GetTeachersByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (userStaffId.HasValue)
            {
                cmd.Parameters.AddWithValue("@UserStaffId", userStaffId.Value);
            }
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["StaffID"].ToString(), Text = rdr["TeacherName"].ToString() });
            }
            return list;
        }

        // --- THIS IS THE CORRECTED METHOD ---
        private async Task LoadMatrixData(SqlConnection conn, Guid batchId)
        {
            using var cmd = new SqlCommand("dbo.GetAssessmentBatchMatrix", conn) { CommandType = CommandType.StoredProcedure };

            // Add UI filter parameters
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (DistrictId.HasValue) cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
            if (SchoolId.HasValue) cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
            if (TeacherId.HasValue) cmd.Parameters.AddWithValue("@TeacherId", TeacherId.Value);

            // Add user identity parameters for security enforcement in the stored procedure
            string userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "";
            cmd.Parameters.AddWithValue("@UserRole", userRole);

            if (IsTeacher && UserStaffId.HasValue)
            {
                cmd.Parameters.AddWithValue("@UserScopeId", UserStaffId.Value);
            }
            else if (IsDistrictAdmin && UserDistrictId.HasValue)
            {
                cmd.Parameters.AddWithValue("@UserScopeId", UserDistrictId.Value);
            }
            // Note: SchoolAdmins are scoped by the @SchoolId parameter, which is already enforced by the UI logic.

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                Columns.Add(new ColDef { Code = rdr.GetString(0), ShortStatement = rdr.IsDBNull(1) ? "" : rdr.GetString(1) });
            }
            if (await rdr.NextResultAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var row = new RowVm
                    {
                        StudentName = rdr.GetString("StudentName"),
                        LocalId = rdr.GetString("LocalId"),
                        TeacherName = rdr.GetString("TeacherName"),
                        Period = rdr.GetString("Period")
                    };
                    for (int i = 0; i < Columns.Count; i++)
                    {
                        var colName = Columns[i].Code;
                        var ord = rdr.GetOrdinal(colName);
                        row.Points.Add(rdr.IsDBNull(ord) ? null : (decimal?)rdr.GetValue(ord));
                    }
                    row.TotalPassed = rdr.GetInt32("TotalPassed");
                    Rows.Add(row);
                }
            }
        }
        private void BuildSummaries()
        {
            if (!Rows.Any()) return;

            // --- NEW: Create a de-duplicated list of students for accurate grand totals. ---
            // This groups all rows by a student's unique ID and takes the first record for each student.
            var distinctStudentRows = Rows
                .GroupBy(r => r.LocalId)
                .Select(g => g.First())
                .ToList();

            int m = Columns.Count;
            const decimal PASS_CUTOFF = 4m;

            // 1. Group Summaries (per class) - This part remains unchanged and uses the full 'Rows' list
            GroupSummaries = Rows
                .GroupBy(r => new GroupKey(r.TeacherName, r.Period))
                .Select(g =>
                {
                    var passed = new int[m];
                    var notPassed = new int[m];
                    var denom = new int[m];

                    foreach (var r in g)
                    {
                        for (int i = 0; i < m; i++)
                        {
                            var score = r.Points[i];
                            if (!score.HasValue) continue;
                            denom[i]++;
                            if (score.Value >= PASS_CUTOFF) passed[i]++; else notPassed[i]++;
                        }
                    }

                    return new GroupSummary
                    {
                        Key = g.Key,
                        StudentCount = g.Count(),
                        PassedPerStd = passed,
                        NotPassedPerStd = notPassed,
                        DenomPerStd = denom,
                        PassedByTotal = g.Count(r => r.TotalPassed >= 3),
                        NotPassedByTotal = g.Count(r => r.TotalPassed < 3)
                    };
                })
                .OrderBy(gs => gs.Key.TeacherName)
                .ThenBy(gs => gs.Key.Period)
                .ToList();

            // 2. Grand Totals - MODIFIED to use the 'distinctStudentRows' list
            {
                var passed = new int[m];
                var notPassed = new int[m];
                var denom = new int[m];
                // Use the de-duplicated list for calculations
                foreach (var r in distinctStudentRows)
                {
                    for (int i = 0; i < m; i++)
                    {
                        var score = r.Points[i];
                        if (!score.HasValue) continue;
                        denom[i]++;
                        if (score.Value >= PASS_CUTOFF) passed[i]++; else notPassed[i]++;
                    }
                }
                GrandTotals = new GrandSummary
                {
                    // Use the de-duplicated list for counts
                    StudentCount = distinctStudentRows.Count,
                    PassedPerStd = passed,
                    NotPassedPerStd = notPassed,
                    DenomPerStd = denom,
                    PassedByTotal = distinctStudentRows.Count(r => r.TotalPassed >= 3),
                    NotPassedByTotal = distinctStudentRows.Count(r => r.TotalPassed < 3)
                };
            }

            // 3. Quadrants - MODIFIED to use the 'distinctStudentRows' list
            {
                // Use the de-duplicated list for counts
                int challenge = distinctStudentRows.Count(r => r.TotalPassed >= 4);
                int benchmark = distinctStudentRows.Count(r => r.TotalPassed == 3);
                int strategic = distinctStudentRows.Count(r => r.TotalPassed == 2);
                int intensive = distinctStudentRows.Count(r => r.TotalPassed <= 1);
                Quadrants = new QuadrantSummary
                {
                    TotalTested = distinctStudentRows.Count,
                    Challenge = challenge,
                    Benchmark = benchmark,
                    Strategic = strategic,
                    Intensive = intensive
                };
            }
        }
        private static async Task<string?> GetDistrictNameAsync(SqlConnection conn, int districtId)
        {
            using var cmd = new SqlCommand("SELECT Name FROM dbo.Districts WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", districtId);
            var res = await cmd.ExecuteScalarAsync();
            return res?.ToString();
        }
    }
}