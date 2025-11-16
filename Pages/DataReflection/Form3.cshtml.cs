using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using ORSV2.Models;
using ClosedXML.Excel;
using System.IO;

namespace ORSV2.Pages.DataReflection
{
    [Authorize(Policy = "CanViewCurriculumForms")]
    public class Form3Model : SecureReportPageModel
    {
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

        public class RowVm
        {
            public string StudentName { get; set; } = "";
            public string LocalId { get; set; } = "";
            public string TeacherName { get; set; } = "";
            public string Period { get; set; } = "";
            public List<decimal?> Points { get; set; } = new();
            public int TotalPassed { get; set; }
        }

        [BindProperty(SupportsGet = true), Required] public int? DistrictId { get; set; }
        [BindProperty(SupportsGet = true)] public string? UnitCycle { get; set; }
        [BindProperty(SupportsGet = true)] public string? BatchId { get; set; }
        [BindProperty(SupportsGet = true)] public int? SchoolId { get; set; }

        public List<SelectListItem> AvailableUnitCycles { get; private set; } = new();
        public List<SelectListItem> AvailableBatches { get; private set; } = new();
        public List<SelectListItem> AvailableSchools { get; private set; } = new();

        public List<ColDef> Columns { get; private set; } = new();
        public List<RowVm> Rows { get; private set; } = new();
        public List<GroupSummary> GroupSummaries { get; private set; } = new();

        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        private readonly IConfiguration _config;
        public Form3Model(IConfiguration config) => _config = config;
        public class GrandSummary
        {
            public int StudentCount { get; init; }
            public int[] PassedPerStd { get; init; } = Array.Empty<int>();
            public int[] NotPassedPerStd { get; init; } = Array.Empty<int>();
            public int[] DenomPerStd { get; init; } = Array.Empty<int>();
            public int PassedByTotal { get; init; }
            public int NotPassedByTotal { get; init; }
        }
        public GrandSummary? GrandTotals { get; private set; }


        public async Task OnGet()
        {
            // Same identity/claims scoping + validations as Form 1
            InitializeUserDataScope(); // roles + claims (district/schools/staff) enforced in UI + SQL proc :contentReference[oaicite:2]{index=2}

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            string? districtName = null;
            if (DistrictId.HasValue)
            {
                districtName = await GetDistrictNameAsync(conn, DistrictId.Value);
            }

            // Breadcrumbs: Data Reflection -> {District} Forms -> Form 3
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Data Reflection", Url = Url.Page("/DataReflection/Index") },
                new BreadcrumbItem {
                    Title = $"{(districtName ?? "District")} - Select Forms",
                    Url = DistrictId.HasValue ? Url.Page("/DataReflection/Forms", new { districtId = DistrictId }) : null
                },
                new BreadcrumbItem { Title = "Form 3" }
            };

            // Role-aware defaulting, same as Form 1
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
                // Teachers can view school-level Form 3; no teacher filter on this page.
                DistrictId = UserDistrictId;
                if (UserSchoolIds.Any()) SchoolId = UserSchoolIds.First();
            }

            // Dropdowns (no Teacher list on this page)
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

            // Load when fully filtered (district + batch + school)
            if (SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId) && Guid.TryParse(BatchId, out var bid))
            {
                await LoadMatrixData(conn, bid);
                BuildGroupSummaries(); // aggregates by Teacher + Period
            }
        }

        // ===== Data access (same procs/patterns as Form 1) =====
        private async Task<List<SelectListItem>> GetUnitCyclesByDistrictAsync(SqlConnection conn, int districtId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a unit/cycle...", "") };
            using var cmd = new SqlCommand("dbo.GetUnitCyclesByDistrict", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var uc = rdr["unit_cycle"].ToString();
                list.Add(new SelectListItem { Value = uc, Text = uc });
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

        private async Task LoadMatrixData(SqlConnection conn, Guid batchId)
        {
            // Same stored procedure + identity enforcement as Form 1; omit @TeacherId (school-level only). :contentReference[oaicite:3]{index=3}
            using var cmd = new SqlCommand("dbo.GetAssessmentBatchMatrix", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (DistrictId.HasValue) cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
            if (SchoolId.HasValue) cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);

            // Identity scope
            string userRole = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "";
            cmd.Parameters.AddWithValue("@UserRole", userRole);
            if (IsTeacher && UserStaffId.HasValue) cmd.Parameters.AddWithValue("@UserScopeId", UserStaffId.Value);
            else if (IsDistrictAdmin && UserDistrictId.HasValue) cmd.Parameters.AddWithValue("@UserScopeId", UserDistrictId.Value);

            using var rdr = await cmd.ExecuteReaderAsync();

            // 1) Standards (Code + Short text)
            while (await rdr.ReadAsync())
            {
                Columns.Add(new ColDef
                {
                    Code = rdr.GetString(0),
                    ShortStatement = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                });
            }

            // 2) Student rows (Teacher / Period / per-standard points / TotalPassed)
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

        private void BuildGroupSummaries()
        {
            if (!Rows.Any()) return;

            int m = Columns.Count;
            const decimal PASS_CUTOFF = 4m;

            // De-duplicate students for fair overall totals
            var distinctStudentRows = Rows
                .GroupBy(r => r.LocalId)
                .Select(g => g.First())
                .ToList();

            // --- Per class/period (Teacher + Period) summaries (unchanged) ---
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

            // --- NEW: Overall totals across ALL classes (weighted by student counts) ---
            {
                var passed = new int[m];
                var notPassed = new int[m];
                var denom = new int[m];

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
                    StudentCount = distinctStudentRows.Count,
                    PassedPerStd = passed,
                    NotPassedPerStd = notPassed,
                    DenomPerStd = denom,
                    PassedByTotal = distinctStudentRows.Count(r => r.TotalPassed >= 3),
                    NotPassedByTotal = distinctStudentRows.Count(r => r.TotalPassed < 3)
                };
            }
        }

        // Utils
        private static async Task<string?> GetDistrictNameAsync(SqlConnection conn, int districtId)
        {
            using var cmd = new SqlCommand("SELECT Name FROM dbo.Districts WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", districtId);
            var res = await cmd.ExecuteScalarAsync();
            return res?.ToString();
        }

        public static string Pct(int num, int den) => den <= 0 ? "—" : Math.Round((decimal)num * 100m / den).ToString("0") + "%";

        // ========================================================================
        // ===            *** NEW EXPORT METHOD START *** ===
        // ========================================================================

        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();

            if (!DistrictId.HasValue || string.IsNullOrWhiteSpace(BatchId) || !SchoolId.HasValue || !Guid.TryParse(BatchId, out var bid))
                return RedirectToPage();

            var connStr = _config.GetConnectionString("DefaultConnection");
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // --- 1. Re-run all data loading logic from OnGet ---
            if (DistrictId.HasValue)
                AvailableUnitCycles = await GetUnitCyclesByDistrictAsync(conn, DistrictId.Value);

            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(UnitCycle))
                AvailableBatches = await GetAssessmentsByUnitCycleAsync(conn, DistrictId.Value, UnitCycle);

            if (DistrictId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
                AvailableSchools = await GetSchoolsByAssessmentAsync(conn, DistrictId.Value, Guid.Parse(BatchId),
                    (IsSchoolAdmin || IsTeacher || User.IsInRole("Counselor")) ? UserSchoolIds : null);

            // Load the actual data
            await LoadMatrixData(conn, bid);
            BuildGroupSummaries(); // Builds GroupSummaries and GrandTotals

            // --- 2. Get Title String (matches CSHTML) ---
            var batchForTitle = AvailableBatches.FirstOrDefault(b => b.Value == BatchId);
            var schoolForTitle = AvailableSchools.FirstOrDefault(s => s.Value == SchoolId?.ToString());
            var dynamicTitle = batchForTitle != null
                ? $"{batchForTitle.Text} - {schoolForTitle?.Text ?? "All Schools"} - Form 3"
                : "DRS – Form 3";

            // --- 3. Build the Excel File ---
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Form 3");
            int row = 1;

            // --- Define Colors ---
            var evenBg = XLColor.FromArgb(241, 243, 244); // #f1f3f4
            var oddBg = XLColor.White;
            var headerBg = XLColor.FromArgb(248, 249, 250); // #f8f9fa (table-light)
            var border = XLColor.FromArgb(222, 226, 230); // #dee2e6

            // --- 4. Add Main Title ---
            int totalCols = 2 + Columns.Count + 1; // Teacher, Per, Standards (1 each), Overall (1)
            var titleCell = ws.Cell(row, 1);
            titleCell.Value = dynamicTitle;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 14;
            titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(row, 1, row, totalCols).Merge();
            row += 2;

            // --- 5. Build Table Header ---
            int headerRow1 = row;
            int headerRow2 = row + 1;

            // Merge Teacher Name and Period headers vertically
            ws.Range(headerRow1, 1, headerRow2, 1).Merge().Value = "Teacher Name";
            ws.Range(headerRow1, 2, headerRow2, 2).Merge().Value = "Per";

            // Loop for Standard Headers (single column each with stacked sub-headers)
            for (int i = 0; i < Columns.Count; i++)
            {
                var c = Columns[i];
                int col = 3 + i; // Single column per standard
                var bg = i % 2 == 0 ? evenBg : oddBg;

                // Row 1: Standard Code + Statement
                var h1Cell = ws.Cell(headerRow1, col);
                h1Cell.Value = $"{c.Code}\n{c.ShortStatement}";
                h1Cell.Style.Alignment.WrapText = true;
                h1Cell.Style.Fill.BackgroundColor = bg;

                // Row 2: Stacked Passed / Not Passed in same cell
                var h2Cell = ws.Cell(headerRow2, col);
                h2Cell.Value = "% (#) Passed\n% (#) Not Passed";
                h2Cell.Style.Alignment.WrapText = true;
                h2Cell.Style.Fill.BackgroundColor = bg;
                h2Cell.Style.Font.FontSize = 9; // Smaller for sub-header
            }

            // Add "Overall Proficiency" Header (single column)
            int overallCol = 3 + Columns.Count;
            var h1Overall = ws.Cell(headerRow1, overallCol);
            h1Overall.Value = "Overall Proficiency\n3+ standards passed";
            h1Overall.Style.Alignment.WrapText = true;
            h1Overall.Style.Fill.BackgroundColor = oddBg;

            var h2Overall = ws.Cell(headerRow2, overallCol);
            h2Overall.Value = "% (#) Passed\n% (#) Not Passed";
            h2Overall.Style.Alignment.WrapText = true;
            h2Overall.Style.Fill.BackgroundColor = oddBg;
            h2Overall.Style.Font.FontSize = 9;

            // Style all header rows
            var headerRange = ws.Range(headerRow1, 1, headerRow2, totalCols);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.OutsideBorderColor = border;
            headerRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.InsideBorderColor = border;
            
            // Darker border between headers and data
            ws.Range(headerRow2, 1, headerRow2, totalCols).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            ws.Range(headerRow2, 1, headerRow2, totalCols).Style.Border.BottomBorderColor = XLColor.Black;

            row = headerRow2 + 1; // Start data after the header

            // --- 6. Build Table Body (GroupSummaries) ---
            foreach (var g in GroupSummaries)
            {
                int c = 1;
                // Format teacher name with line break after comma
                var teacherName = g.Key.TeacherName.Replace(", ", ",\n");
                ws.Cell(row, c++).Value = teacherName;
                ws.Cell(row, c++).Value = g.Key.Period;

                for (int i = 0; i < Columns.Count; i++)
                {
                    var den = g.DenomPerStd[i];
                    var pass = g.PassedPerStd[i];
                    var notp = g.NotPassedPerStd[i];
                    var bg = i % 2 == 0 ? evenBg : oddBg;
                    
                    // Single cell with stacked values
                    var cell = ws.Cell(row, c++);
                    cell.Value = $"{Pct(pass, den)} ({pass})\n{Pct(notp, den)} ({notp})";
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }

                // Overall Totals for the group (stacked)
                var totDen = g.StudentCount;
                var totPass = g.PassedByTotal;
                var totNot = g.NotPassedByTotal;
                
                var totalCell = ws.Cell(row, c++);
                totalCell.Value = $"{Pct(totPass, totDen)} ({totPass})\n{Pct(totNot, totDen)} ({totNot})";
                totalCell.Style.Fill.BackgroundColor = oddBg;
                totalCell.Style.Alignment.WrapText = true;
                totalCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Prominent border between teacher rows
                ws.Range(row, 1, row, totalCols).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                ws.Range(row, 1, row, totalCols).Style.Border.BottomBorderColor = XLColor.Black;

                row++;
            }

            // --- 7. Build Table Footer (GrandTotals) ---
            if (GrandTotals != null)
            {
                int c = 1;
                var hCell1 = ws.Cell(row, c++);
                hCell1.Value = "OVERALL";
                hCell1.Style.Font.Bold = true;

                // Empty cell for Per column
                ws.Cell(row, c++).Value = "";
                
                for (int i = 0; i < Columns.Count; i++)
                {
                    var den = GrandTotals.DenomPerStd[i];
                    var pass = GrandTotals.PassedPerStd[i];
                    var notp = GrandTotals.NotPassedPerStd[i];
                    var bg = i % 2 == 0 ? evenBg : oddBg;

                    // Single cell with stacked values
                    var cell = ws.Cell(row, c++);
                    cell.Value = $"{Pct(pass, den)} ({pass})\n{Pct(notp, den)} ({notp})";
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.Bold = true;
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }

                // Grand Overall Totals (stacked)
                var totDen = GrandTotals.StudentCount;
                var totPass = GrandTotals.PassedByTotal;
                var totNot = GrandTotals.NotPassedByTotal;
                
                var totalCell = ws.Cell(row, c++);
                totalCell.Value = $"{Pct(totPass, totDen)} ({totPass})\n{Pct(totNot, totDen)} ({totNot})";
                totalCell.Style.Fill.BackgroundColor = oddBg;
                totalCell.Style.Font.Bold = true;
                totalCell.Style.Alignment.WrapText = true;
                totalCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

                // Style the whole footer row
                ws.Range(row, 1, row, totalCols).Style.Fill.BackgroundColor = headerBg;
                row++;
            }
            
            // Center all data cells
            ws.Range(headerRow2 + 1, 3, row - 1, totalCols).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // --- 8. Add Footer Note ---
            var noteCell = ws.Cell(row, 1);
            noteCell.Value = "PASS cutoff = score ≥ 4 (per standard). Overall Proficiency = student passed ≥ 3 standards.";
            noteCell.Style.Font.Italic = true;
            noteCell.Style.Font.FontSize = 9;
            ws.Range(row, 1, row, totalCols).Merge();
            row++;

            // --- 9. Final Formatting & Return ---
            ws.SheetView.FreezeRows(headerRow2);
            ws.Column(1).Width = 24; // Teacher
            ws.Column(2).Width = 10; // Period
            // Set all data columns to a consistent width
            ws.Columns(3, totalCols).Width = 15;
            
            ws.Rows().AdjustToContents();
            ws.Row(headerRow1).Height = 30;
            ws.Row(headerRow2).Height = 30;

            // Create a safe filename
            var safeTitle = dynamicTitle.Replace(" - ", "_").Replace(" ", "_");
            var fname = $"Form3_{safeTitle}_{DateTime.Now:yyyyMMdd}.xlsx";

            await using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fname);
        }

        // ========================================================================
        // ===            *** NEW EXPORT METHOD END *** ===
        // ========================================================================
    }
}
