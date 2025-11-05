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
using ClosedXML.Excel;
using System.IO;
using System.Linq;

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

        // ========================================================================
        // ===            *** CORRECTED EXPORT METHOD START *** ===
        // ========================================================================

        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();

            if (!DistrictId.HasValue || string.IsNullOrWhiteSpace(BatchId) || !Guid.TryParse(BatchId, out var bid))
                return RedirectToPage();

            var connStr = _config.GetConnectionString("DefaultConnection");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // --- Populating dropdowns is required to get the text for the title ---
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
            
            // --- Load the actual report data ---
            await LoadMatrixData(conn, bid);
            BuildSummaries();

            // --- Get the dynamic title strings, matching the CSHTML file ---
            var batchForTitle = AvailableBatches.FirstOrDefault(b => b.Value == BatchId);
            var schoolForTitle = AvailableSchools.FirstOrDefault(s => s.Value == SchoolId?.ToString());
            var teacherForTitle = AvailableTeachers.FirstOrDefault(t => t.Value == TeacherId?.ToString());
            
            var dynamicTitle = batchForTitle != null 
                ? $"{batchForTitle.Text} - {schoolForTitle?.Text ?? "All Schools"} - {teacherForTitle?.Text ?? "All Teachers"} - Form 1"
                : "DRS Form 1";

            // --- Group rows for looping, matching the CSHTML file ---
            var groupedRows = Rows.GroupBy(r => r.TeacherName).Select(g => new 
            {
                TeacherName = g.Key,
                Periods = g.GroupBy(p => p.Period).Select(pGroup => new 
                {
                    Period = pGroup.Key,
                    Students = pGroup.ToList()
                }).OrderBy(p => p.Period)
            }).OrderBy(t => t.TeacherName);


            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Form 1");
            int row = 1;
            int colCount = Columns.Count;
            // Student Name, Local ID, [cols], Total Passed
            int totalDataCols = colCount + 3;

            // --- Define Styles ---
            var paletteHeaderBg = XLColor.FromArgb(243, 244, 246); // f3f4f6
            var paletteBorder = XLColor.FromArgb(229, 231, 235); // e5e7eb
            var paletteTeacherBg = XLColor.FromArgb(233, 236, 239); // e9ecef
            var palettePeriodBg = XLColor.FromArgb(248, 249, 250); // f8f9fa
            var paletteSummaryBg = XLColor.FromArgb(248, 249, 250); // f8f9fa
            var paletteGrandBg = XLColor.FromArgb(241, 245, 249); // f1f5f9
            var passBg = XLColor.FromArgb(230, 250, 230);
            var failBg = XLColor.FromArgb(250, 230, 230);
            var nullBg = XLColor.FromArgb(248, 249, 250);
            
            // Quadrant Styles
            var quadChallengeBg = XLColor.FromArgb(239, 246, 255);
            var quadChallengeFg = XLColor.FromArgb(30, 64, 175);
            var quadBenchmarkBg = XLColor.FromArgb(236, 253, 245);
            var quadBenchmarkFg = XLColor.FromArgb(6, 95, 70);
            var quadStrategicBg = XLColor.FromArgb(255, 251, 235);
            var quadStrategicFg = XLColor.FromArgb(146, 64, 14);
            var quadIntensiveBg = XLColor.FromArgb(254, 242, 242);
            var quadIntensiveFg = XLColor.FromArgb(153, 27, 27);
            var quadTotalBg = XLColor.FromArgb(255, 251, 235);
            var quadTotalBorder = XLColor.FromArgb(245, 158, 11);


            // --- 1. Report Title ---
            var titleCell = ws.Cell(row, 1);
            titleCell.Value = dynamicTitle;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 14;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(row, 1, row, totalDataCols).Merge();
            row += 2;

            // --- 2. Table Header ---
            int headerRow = row;
            int c = 1;
            ws.Cell(row, c++).Value = "Student";
            ws.Cell(row, c++).Value = "Local ID";

            foreach (var colDef in Columns)
            {
                var cell = ws.Cell(row, c++);
                cell.Value = $"{colDef.Code}\n{colDef.ShortStatement}";
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            ws.Cell(row, c).Value = "Total Passed";

            var headerRange = ws.Range(headerRow, 1, headerRow, totalDataCols);
            headerRange.Style.Fill.BackgroundColor = paletteHeaderBg;
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            headerRange.Style.Border.OutsideBorderColor = paletteBorder;
            headerRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            headerRange.Style.Border.InsideBorderColor = paletteBorder;
            row++;

            // --- 3. Main Data Body (Grouped) ---
            const decimal PASS_CUTOFF = 4m;

            foreach (var teacherGroup in groupedRows)
            {
                // --- Teacher Header ---
                var teacherCell = ws.Cell(row, 1);
                teacherCell.Value = $"Teacher: {teacherGroup.TeacherName}";
                teacherCell.Style.Font.Bold = true;
                teacherCell.Style.Fill.BackgroundColor = paletteTeacherBg;
                teacherCell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
                teacherCell.Style.Border.TopBorderColor = paletteBorder;
                ws.Range(row, 1, row, totalDataCols).Merge();
                row++;
                
                foreach (var periodGroup in teacherGroup.Periods)
                {
                    // --- Period Header ---
                    var periodCell = ws.Cell(row, 1);
                    periodCell.Value = $"Period: {periodGroup.Period}";
                    periodCell.Style.Font.Bold = true;
                    periodCell.Style.Fill.BackgroundColor = palettePeriodBg;
                    periodCell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
                    periodCell.Style.Border.TopBorderColor = paletteBorder;
                    ws.Range(row, 1, row, totalDataCols).Merge();
                    row++;

                    // --- Student Rows ---
                    foreach (var r in periodGroup.Students)
                    {
                        c = 1;
                        ws.Cell(row, c++).Value = r.StudentName;
                        ws.Cell(row, c++).Value = r.LocalId;

                        for (int i = 0; i < colCount; i++)
                        {
                            var p = r.Points[i];
                            var cell = ws.Cell(row, c++);
                            if (p.HasValue)
                            {
                                cell.Value = p.Value;
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                cell.Style.Fill.BackgroundColor = p.Value >= PASS_CUTOFF ? passBg : failBg;
                            }
                            else
                            {
                                cell.Value = "—";
                                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                                cell.Style.Fill.BackgroundColor = nullBg;
                            }
                        }

                        var totalCell = ws.Cell(row, c);
                        totalCell.Value = r.TotalPassed;
                        totalCell.Style.Font.Bold = true;
                        totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        row++;
                    }

                    // --- Group Summary Rows ---
                    var gs = GroupSummaries.FirstOrDefault(
                        x => x.Key.TeacherName == teacherGroup.TeacherName && x.Key.Period == periodGroup.Period
                    );
                    if (gs != null)
                    {
                        // "Passed" Row
                        c = 1;
                        var passedCell = ws.Cell(row, c);
                        passedCell.Value = "Passed";
                        passedCell.Style.Font.Bold = true; 
                        ws.Range(row, c, row, c + 1).Merge();
                        c += 2;

                        for (int i = 0; i < colCount; i++)
                        {
                            var num = gs.PassedPerStd[i];
                            var den = Math.Max(1, gs.DenomPerStd[i]);
                            var frac = (double)num / den;
                            var cell = ws.Cell(row, c++);
                            cell.Value = $"{num} ({frac:P2})";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                        
                        var totalNum = gs.PassedByTotal;
                        var totalDen = Math.Max(1, gs.PassedByTotal + gs.NotPassedByTotal);
                        var totalFrac = (double)totalNum / totalDen;
                        var totalPassedCell = ws.Cell(row, c);
                        totalPassedCell.Value = $"{totalNum} ({totalFrac:P2})";
                        totalPassedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        totalPassedCell.Style.Font.Bold = true; 
                        
                        ws.Range(row, 1, row, totalDataCols).Style.Fill.BackgroundColor = paletteSummaryBg;
                        ws.Range(row, 1, row, totalDataCols).Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
                        ws.Range(row, 1, row, totalDataCols).Style.Border.TopBorderColor = paletteBorder;
                        row++;

                        // "Not Passed" Row
                        c = 1;
                        var notPassedCell = ws.Cell(row, c);
                        notPassedCell.Value = "Not Passed";
                        notPassedCell.Style.Font.Bold = true;
                        ws.Range(row, c, row, c + 1).Merge();
                        c += 2;

                        for (int i = 0; i < colCount; i++)
                        {
                            var num = gs.NotPassedPerStd[i];
                            var den = Math.Max(1, gs.DenomPerStd[i]);
                            var frac = (double)num / den;
                            var cell = ws.Cell(row, c++);
                            cell.Value = $"{num} ({frac:P2})";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }

                        var totalNumNp = gs.NotPassedByTotal;
                        var totalFracNp = (double)totalNumNp / totalDen;
                        var totalNotPassedCell = ws.Cell(row, c);
                        totalNotPassedCell.Value = $"{totalNumNp} ({totalFracNp:P2})";
                        totalNotPassedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        totalNotPassedCell.Style.Font.Bold = false;
                        
                        ws.Range(row, 1, row, totalDataCols).Style.Fill.BackgroundColor = paletteSummaryBg;
                        row++;
                    }
                }
            }

            // --- 4. Grand Totals ---
            if (GrandTotals != null)
            {
                // "Grand Total — Passed" Row
                c = 1;
                var grandPassedCell = ws.Cell(row, c);
                grandPassedCell.Value = "Grand Total — Passed";
                ws.Range(row, c, row, c + 1).Merge();
                c += 2;

                for (int i = 0; i < colCount; i++)
                {
                    var num = GrandTotals.PassedPerStd[i];
                    var den = Math.Max(1, GrandTotals.DenomPerStd[i]);
                    var frac = (double)num / den;
                    var cell = ws.Cell(row, c++);
                    cell.Value = $"{num} ({frac:P2})";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                
                var totalNum = GrandTotals.PassedByTotal;
                var totalDen = Math.Max(1, GrandTotals.PassedByTotal + GrandTotals.NotPassedByTotal);
                var totalFrac = (double)totalNum / totalDen;
                var totalPassedCell = ws.Cell(row, c);
                totalPassedCell.Value = $"{totalNum} ({totalFrac:P2})";
                totalPassedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var grandPassedRange = ws.Range(row, 1, row, totalDataCols);
                grandPassedRange.Style.Fill.BackgroundColor = paletteGrandBg;
                grandPassedRange.Style.Font.Bold = true;
                grandPassedRange.Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
                grandPassedRange.Style.Border.TopBorderColor = paletteBorder;
                row++;

                // "Grand Total — Not Passed" Row
                c = 1;
                var grandNotPassedCell = ws.Cell(row, c);
                grandNotPassedCell.Value = "Grand Total — Not Passed";
                ws.Range(row, c, row, c + 1).Merge();
                c += 2;

                for (int i = 0; i < colCount; i++)
                {
                    var num = GrandTotals.NotPassedPerStd[i];
                    var den = Math.Max(1, GrandTotals.DenomPerStd[i]);
                    var frac = (double)num / den;
                    var cell = ws.Cell(row, c++);
                    cell.Value = $"{num} ({frac:P2})";
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                var totalNumNp = GrandTotals.NotPassedByTotal;
                var totalFracNp = (double)totalNumNp / totalDen;
                var totalNotPassedCell = ws.Cell(row, c);
                totalNotPassedCell.Value = $"{totalNumNp} ({totalFracNp:P2})";
                totalNotPassedCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                
                var grandNotPassedRange = ws.Range(row, 1, row, totalDataCols);
                grandNotPassedRange.Style.Fill.BackgroundColor = paletteGrandBg;
                grandNotPassedRange.Style.Font.Bold = true;
                row++;
            }
            
            // Auto-adjust rows *before* the new quadrant section
            ws.Rows(headerRow + 1, row - 1).AdjustToContents();

            // --- 5. Quadrants ---
            if (Quadrants != null)
            {
                row++; // Add a space
                var qt = Quadrants;
                int span = Math.Max(1, totalDataCols / 4);
                int remainder = Math.Max(0, totalDataCols - (span * 4));
                int colIdx = 1;

                // *** THIS IS THE NEW LOGIC ***
                
                var quadTitleRow = row;
                var quadPercentRow = row + 1;
                var quadCountRow = row + 2;

                // --- Quadrant Title Row ---
                var qTitle1 = ws.Cell(quadTitleRow, colIdx);
                qTitle1.Value = "Challenge";
                qTitle1.Style.Fill.BackgroundColor = quadChallengeBg;
                qTitle1.Style.Font.FontColor = quadChallengeFg;
                ws.Range(quadTitleRow, colIdx, quadTitleRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qTitle2 = ws.Cell(quadTitleRow, colIdx);
                qTitle2.Value = "Benchmark";
                qTitle2.Style.Fill.BackgroundColor = quadBenchmarkBg;
                qTitle2.Style.Font.FontColor = quadBenchmarkFg;
                ws.Range(quadTitleRow, colIdx, quadTitleRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qTitle3 = ws.Cell(quadTitleRow, colIdx);
                qTitle3.Value = "Strategic";
                qTitle3.Style.Fill.BackgroundColor = quadStrategicBg;
                qTitle3.Style.Font.FontColor = quadStrategicFg;
                ws.Range(quadTitleRow, colIdx, quadTitleRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qTitle4 = ws.Cell(quadTitleRow, colIdx);
                qTitle4.Value = "Intensive";
                qTitle4.Style.Fill.BackgroundColor = quadIntensiveBg;
                qTitle4.Style.Font.FontColor = quadIntensiveFg;
                ws.Range(quadTitleRow, colIdx, quadTitleRow, totalDataCols).Merge();

                var titleRange = ws.Range(quadTitleRow, 1, quadTitleRow, totalDataCols);
                titleRange.Style.Font.Bold = true;
                titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                titleRange.Style.Border.SetTopBorder(XLBorderStyleValues.Thick);
                titleRange.Style.Border.TopBorderColor = XLColor.FromHtml("#fdba74");

                // --- Quadrant Percent Row ---
                colIdx = 1;
                var qPct1 = ws.Cell(quadPercentRow, colIdx);
                qPct1.Value = (double)qt.Challenge / Math.Max(1, qt.TotalTested);
                qPct1.Style.NumberFormat.Format = "0.00%";
                qPct1.Style.Fill.BackgroundColor = quadChallengeBg;
                qPct1.Style.Font.FontColor = quadChallengeFg;
                ws.Range(quadPercentRow, colIdx, quadPercentRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qPct2 = ws.Cell(quadPercentRow, colIdx);
                qPct2.Value = (double)qt.Benchmark / Math.Max(1, qt.TotalTested);
                qPct2.Style.NumberFormat.Format = "0.00%";
                qPct2.Style.Fill.BackgroundColor = quadBenchmarkBg;
                qPct2.Style.Font.FontColor = quadBenchmarkFg;
                ws.Range(quadPercentRow, colIdx, quadPercentRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qPct3 = ws.Cell(quadPercentRow, colIdx);
                qPct3.Value = (double)qt.Strategic / Math.Max(1, qt.TotalTested);
                qPct3.Style.NumberFormat.Format = "0.00%";
                qPct3.Style.Fill.BackgroundColor = quadStrategicBg;
                qPct3.Style.Font.FontColor = quadStrategicFg;
                ws.Range(quadPercentRow, colIdx, quadPercentRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qPct4 = ws.Cell(quadPercentRow, colIdx);
                qPct4.Value = (double)qt.Intensive / Math.Max(1, qt.TotalTested);
                qPct4.Style.NumberFormat.Format = "0.00%";
                qPct4.Style.Fill.BackgroundColor = quadIntensiveBg;
                qPct4.Style.Font.FontColor = quadIntensiveFg;
                ws.Range(quadPercentRow, colIdx, quadPercentRow, totalDataCols).Merge();
                
                var percentRange = ws.Range(quadPercentRow, 1, quadPercentRow, totalDataCols);
                percentRange.Style.Font.Bold = true;
                percentRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // --- Quadrant Count Row ---
                colIdx = 1;
                var qCnt1 = ws.Cell(quadCountRow, colIdx);
                qCnt1.Value = $"{qt.Challenge} students";
                qCnt1.Style.Fill.BackgroundColor = quadChallengeBg;
                qCnt1.Style.Font.FontColor = quadChallengeFg;
                ws.Range(quadCountRow, colIdx, quadCountRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qCnt2 = ws.Cell(quadCountRow, colIdx);
                qCnt2.Value = $"{qt.Benchmark} students";
                qCnt2.Style.Fill.BackgroundColor = quadBenchmarkBg;
                qCnt2.Style.Font.FontColor = quadBenchmarkFg;
                ws.Range(quadCountRow, colIdx, quadCountRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qCnt3 = ws.Cell(quadCountRow, colIdx);
                qCnt3.Value = $"{qt.Strategic} students";
                qCnt3.Style.Fill.BackgroundColor = quadStrategicBg;
                qCnt3.Style.Font.FontColor = quadStrategicFg;
                ws.Range(quadCountRow, colIdx, quadCountRow, colIdx + span - 1).Merge();
                colIdx += span;

                var qCnt4 = ws.Cell(quadCountRow, colIdx);
                qCnt4.Value = $"{qt.Intensive} students";
                qCnt4.Style.Fill.BackgroundColor = quadIntensiveBg;
                qCnt4.Style.Font.FontColor = quadIntensiveFg;
                ws.Range(quadCountRow, colIdx, quadCountRow, totalDataCols).Merge();

                var countRange = ws.Range(quadCountRow, 1, quadCountRow, totalDataCols);
                countRange.Style.Font.Bold = false;
                countRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Adjust row height for all three new rows
                ws.Row(quadTitleRow).AdjustToContents();
                ws.Row(quadPercentRow).AdjustToContents();
                ws.Row(quadCountRow).AdjustToContents();
                
                // Update the main row counter
                row = quadCountRow + 1;
                
                // --- Quadrant Total Row ---
                var totalRowCell = ws.Cell(row, 1);
                totalRowCell.Value = $"Total Tested: {qt.TotalTested}";
                totalRowCell.Style.Font.Bold = true;
                totalRowCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                totalRowCell.Style.Fill.BackgroundColor = quadTotalBg;
                totalRowCell.Style.Border.SetTopBorder(XLBorderStyleValues.Dashed);
                totalRowCell.Style.Border.TopBorderColor = quadTotalBorder;
                ws.Range(row, 1, row, totalDataCols).Merge();
                row++;
            }

            // --- 6. Footer Note ---
            var footerCell = ws.Cell(row, 1);
            footerCell.Value = "PASS cutoff = score ≥ 4 (per standard). Overall Proficiency = student passed ≥ 3 standards.";
            footerCell.Style.Font.Italic = true;
            footerCell.Style.Font.FontSize = 9;
            ws.Range(row, 1, row, totalDataCols).Merge();
            row++;
            
            // --- 7. Final Formatting & Return ---
            ws.SheetView.FreezeRows(headerRow);
            ws.Column(1).Width = 28; // Student
            ws.Column(2).Width = 14; // Local ID
            for (int ci = 3; ci < 3 + colCount; ci++) ws.Column(ci).Width = 16;
            ws.Column(3 + colCount).Width = 14; // Total Passed
            
            ws.Row(headerRow).Height = 30; // Ensure header is tall enough

            // Create a safe filename and use the dynamic title
            var safeTitle = dynamicTitle
                .Replace(":", "-")
                .Replace("/", "-")
                .Replace("?", "")
                .Replace("*", "");

            var fname = $"{safeTitle}.xlsx";

            await using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fname);
        }

        // ========================================================================
        // ===            *** CORRECTED EXPORT METHOD END *** ===
        // ========================================================================
    }
}