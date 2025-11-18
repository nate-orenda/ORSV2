using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ORSV2.Models;
using Microsoft.Extensions.Logging;
using System.Linq;
using ClosedXML.Excel;
using System.IO;
using System;
using Microsoft.AspNetCore.Html;

namespace ORSV2.Pages.DataReflection
{
    public class SchoolTarget
    {
        public string Grade { get; set; } = string.Empty;
        public string DemographicGroup { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public decimal TargetPct { get; set; }
    }
    
    public class MetaModel : SecureReportPageModel
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MetaModel> _logger;

        [BindProperty(SupportsGet = true)]
        public int? SchoolId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DistrictId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SchoolYear { get; set; }
        
        public SelectList AvailableSchools { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableSchoolYears { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableTargetGrades { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableTargetDemographics { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableTargetSubjects { get; set; } = new SelectList(new List<SelectListItem>());

        public List<UnitDisplayGroup> UnitGroups { get; set; } = new List<UnitDisplayGroup>();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new List<BreadcrumbItem>();
        public string DistrictName { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;

        private List<MetaDataRow> RawDataRows { get; set; } = new List<MetaDataRow>();
        private List<SchoolTarget> CurrentTargets { get; set; } = new List<SchoolTarget>();

        public MetaModel(IConfiguration config, ILogger<MetaModel> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            InitializeUserDataScope();

            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser)
            {
                return Forbid();
            }

            if (UserDistrictId.HasValue)
            {
                if (DistrictId.HasValue && DistrictId.Value != UserDistrictId.Value)
                {
                    return Forbid();
                }
                DistrictId = UserDistrictId.Value;
            }

            if (DistrictId == null || DistrictId == 0)
            {
                return Page();
            }

            if (IsSchoolAdmin && SchoolId.HasValue && SchoolId.Value != 0)
            {
                if (!UserSchoolIds.Contains(SchoolId.Value))
                {
                    return Forbid();
                }
            }

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync(); 

            if (SchoolId.HasValue && SchoolYear.HasValue)
            {
                await LoadSchoolTargetsAsync(); 
                await LoadMetaDataAsync();
                BuildDisplayGroups(); 

                if (SchoolId.Value == 0)
                {
                    SchoolName = "All Schools";
                }
                else
                {
                    var items = AvailableSchools.Items?.Cast<SelectListItem>() ?? new List<SelectListItem>();
                    SchoolName = items.FirstOrDefault(s => s.Value == SchoolId.ToString())?.Text ?? "Selected School";
                }

                if (UnitGroups.Any())
                {
                    LoadTargetUIData();
                }
            }

            BuildBreadcrumbs();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateTargetInlineAsync()
        {
            try
            {
                InitializeUserDataScope();
                if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) 
                    return new JsonResult(new { success = false, message = "Forbidden" }) { StatusCode = 403 };

                if (!int.TryParse(Request.Form["schoolId"], out var schoolId) ||
                    !int.TryParse(Request.Form["schoolYear"], out var schoolYear))
                {
                    _logger.LogWarning("Inline Target save failed: Invalid SchoolId or SchoolYear.");
                    return new JsonResult(new { success = false, message = "Invalid SchoolId or SchoolYear." });
                }
                
                var gradeStr = Request.Form["grade"].ToString();
                var subject = Request.Form["subject"].ToString();
                var demo = Request.Form["demo"].ToString();
                var targetPercentStr = Request.Form["targetPercent"].ToString();

                if (UserDistrictId.HasValue && DistrictId.HasValue && DistrictId.Value != UserDistrictId.Value)
                {
                    return new JsonResult(new { success = false, message = "Forbidden" }) { StatusCode = 403 };
                }
                if (IsSchoolAdmin && !UserSchoolIds.Contains(schoolId))
                {
                    _logger.LogWarning("Target save FORBIDDEN: User tried to save target for unassigned school {SchoolId}", schoolId);
                    return new JsonResult(new { success = false, message = "Forbidden" }) { StatusCode = 403 };
                }

                if (string.IsNullOrEmpty(gradeStr) || string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(demo))
                {
                    _logger.LogWarning("Inline Target save failed: Missing grade, subject or demographic.");
                    return new JsonResult(new { success = false, message = "Missing required fields." });
                }

                // Handle 'empty' input - delete the target
                if (string.IsNullOrEmpty(targetPercentStr))
                {
                    var deleteSql = @"
                        DELETE FROM [dbo].[SchoolTargets]
                        WHERE school_id = @SchoolId 
                          AND school_year = @SchoolYear 
                          AND grade = @Grade 
                          AND demographic_group = @DemographicGroup
                          AND subject = @Subject";
                    
                    using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                    {
                        await conn.OpenAsync();
                        using (var cmd = new SqlCommand(deleteSql, conn))
                        {
                            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                            cmd.Parameters.AddWithValue("@SchoolYear", schoolYear);
                            cmd.Parameters.AddWithValue("@Grade", gradeStr);
                            cmd.Parameters.AddWithValue("@DemographicGroup", demo);
                            cmd.Parameters.AddWithValue("@Subject", subject);
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                    _logger.LogInformation("School Target deleted successfully.");
                    return new JsonResult(new { success = true });
                }

                // UPSERT Logic
                if (!decimal.TryParse(targetPercentStr, out var targetDecimal))
                {
                    _logger.LogWarning("Inline Target save failed: Invalid percent '{TargetPercent}'", targetPercentStr);
                    return new JsonResult(new { success = false, message = "Invalid Percent." });
                }

                var sql = @"
                    MERGE INTO [dbo].[SchoolTargets] AS T
                    USING (VALUES (@SchoolId, @SchoolYear, @Grade, @DemographicGroup, @Subject)) AS S (school_id, school_year, grade, demographic_group, subject)
                    ON T.school_id = S.school_id 
                       AND T.school_year = S.school_year 
                       AND T.grade = S.grade 
                       AND T.demographic_group = S.demographic_group
                       AND T.subject = S.subject
                    WHEN MATCHED THEN
                        UPDATE SET 
                            target_pct = @TargetPct,
                            updated_at = GETUTCDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (school_id, school_year, grade, demographic_group, subject, target_pct, created_at, updated_at)
                        VALUES (@SchoolId, @SchoolYear, @Grade, @DemographicGroup, @Subject, @TargetPct, GETUTCDATE(), GETUTCDATE());";

                using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SchoolId", schoolId);
                        cmd.Parameters.AddWithValue("@SchoolYear", schoolYear);
                        cmd.Parameters.AddWithValue("@Grade", gradeStr);
                        cmd.Parameters.AddWithValue("@DemographicGroup", demo);
                        cmd.Parameters.AddWithValue("@Subject", subject);
                        cmd.Parameters.AddWithValue("@TargetPct", targetDecimal);
                        
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                
                _logger.LogInformation("School Target saved successfully via inline edit.");
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving school target via inline edit.");
                return new JsonResult(new { success = false, message = "Server error." }) { StatusCode = 500 };
            }
        }
        
        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();
            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) return Forbid();
            if (UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;

            if (!SchoolId.HasValue || !DistrictId.HasValue || !SchoolYear.HasValue)
                return RedirectToPage();

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync(); 
            await LoadSchoolTargetsAsync();
            await LoadMetaDataAsync();
            BuildDisplayGroups();

            if (string.IsNullOrEmpty(SchoolName))
            {
                if (SchoolId.Value == 0)
                {
                    SchoolName = "All Schools";
                }
                else
                {
                    if (AvailableSchools.Items != null)
                    {
                        var items = AvailableSchools.Items.Cast<SelectListItem>();
                        SchoolName = items.FirstOrDefault(s => s.Value == SchoolId.ToString())?.Text ?? "Selected School";
                    }
                    else
                    {
                        using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                        {
                            await conn.OpenAsync();
                            var sql = "SELECT Id, Name FROM [dbo].[Schools] WHERE Id = @SchoolId AND DistrictId = @DistrictId";
                            using (var cmd = new SqlCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
                                cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
                                using (var reader = await cmd.ExecuteReaderAsync())
                                {
                                    if (await reader.ReadAsync())
                                    {
                                        SchoolName = reader["Name"].ToString() ?? "Selected School";
                                    }
                                }
                            }
                        }
                    }
                }
            }

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Meta Data");
                
                ws.Column(1).Width = 20;
                int dataColStart = 2;
                int demoGroupCols = 3;
                int numDemoGroups = 6;
                
                for (int g = 0; g < numDemoGroups; g++)
                {
                    int baseCol = dataColStart + (g * demoGroupCols);
                    ws.Column(baseCol).Width = 10;
                    ws.Column(baseCol + 1).Width = 20;
                    ws.Column(baseCol + 2).Width = 10;
                }
                ws.Column(dataColStart + (numDemoGroups * demoGroupCols)).Width = 20;
                
                var headerBgColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.FromArgb(44, 62, 80) }, 
                    { "All", XLColor.FromArgb(179, 217, 255) }, 
                    { "EL", XLColor.FromArgb(255, 179, 217) }, 
                    { "SWD", XLColor.FromArgb(255, 255, 179) }, 
                    { "AA", XLColor.FromArgb(179, 255, 179) }, 
                    { "SED", XLColor.FromArgb(255, 179, 102) }, 
                    { "HISP", XLColor.FromArgb(255, 230, 179) }
                };
                var headerFontColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.White }, 
                    { "All", XLColor.FromArgb(0, 61, 153) }, 
                    { "EL", XLColor.FromArgb(153, 0, 51) }, 
                    { "SWD", XLColor.FromArgb(153, 102, 0) }, 
                    { "AA", XLColor.FromArgb(0, 77, 0) }, 
                    { "SED", XLColor.FromArgb(102, 68, 0) }, 
                    { "HISP", XLColor.FromArgb(102, 77, 0) }
                };
                var dataBgColors = new Dictionary<string, XLColor>
                {
                    { "All", XLColor.FromArgb(230, 241, 250) }, 
                    { "EL", XLColor.FromArgb(250, 230, 241) }, 
                    { "SWD", XLColor.FromArgb(250, 250, 230) }, 
                    { "AA", XLColor.FromArgb(230, 250, 230) }, 
                    { "SED", XLColor.FromArgb(250, 230, 215) }, 
                    { "HISP", XLColor.FromArgb(250, 244, 230) }
                };

                int row = 1;
                int firstHeaderRow = 0; 
                int totalCols = 1 + (demoGroupCols * numDemoGroups) + 1;

                var headerCell = ws.Cell(row, 1);
                headerCell.Value = $"{DistrictName} - {SchoolName} (SY {SchoolYear - 1}-{SchoolYear})";
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Font.FontSize = 14;
                ws.Range(row, 1, row, totalCols).Merge();
                row += 2;

                foreach (var unitGroup in UnitGroups)
                {
                    var unitCell = ws.Cell(row, 1);
                    unitCell.Value = $"Cycle: {unitGroup.UnitName}";
                    unitCell.Style.Font.Bold = true;
                    unitCell.Style.Fill.BackgroundColor = XLColor.FromArgb(52, 73, 94);
                    unitCell.Style.Font.FontColor = XLColor.White;
                    ws.Range(row, 1, row, totalCols).Merge();
                    row++;

                    foreach (var subjectGroup in unitGroup.SubjectGroups)
                    {
                        if (subjectGroup.SubjectTotalRow.All.TotalTested == 0) continue;
                        
                        var subjectCell = ws.Cell(row, 1);
                        subjectCell.Value = $"Subject: {subjectGroup.SubjectName}";
                        subjectCell.Style.Font.Bold = true;
                        subjectCell.Style.Fill.BackgroundColor = XLColor.FromArgb(238, 242, 255);
                        subjectCell.Style.Font.FontColor = XLColor.FromArgb(44, 62, 80);
                        ws.Range(row, 1, row, totalCols).Merge();
                        row++;

                        if (firstHeaderRow == 0) firstHeaderRow = row; 
                
                        int headerRow1 = row;
                        int headerRow2 = row + 1;

                        var gradeHeaderCell = ws.Cell(headerRow1, 1);
                        gradeHeaderCell.Value = "Grade";
                        gradeHeaderCell.Style.Fill.BackgroundColor = headerBgColors["Grade"];
                        gradeHeaderCell.Style.Font.FontColor = headerFontColors["Grade"];
                        gradeHeaderCell.Style.Font.Bold = true;
                        gradeHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        gradeHeaderCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        gradeHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        gradeHeaderCell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));
                        ws.Range(headerRow1, 1, headerRow2, 1).Merge();

                        Action<int, string, string> setHeaderGroup = (startCol, key, text) =>
                        {
                            var cell = ws.Cell(headerRow1, startCol);
                            cell.Value = text;
                            cell.Style.Fill.BackgroundColor = headerBgColors[key];
                            cell.Style.Font.FontColor = headerFontColors[key];
                            cell.Style.Font.Bold = true;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            cell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));
                            ws.Range(headerRow1, startCol, headerRow1, startCol + 2).Merge();

                            var thTarget = ws.Cell(headerRow2, startCol);
                            thTarget.Value = "% Target";
                            thTarget.Style.Fill.BackgroundColor = dataBgColors[key];
                            thTarget.Style.Font.Bold = true;
                            thTarget.Style.Font.FontColor = headerFontColors[key];
                            thTarget.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            thTarget.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            
                            var thProf = ws.Cell(headerRow2, startCol + 1);
                            thProf.Value = "% Proficient (Prof/Tested)";
                            thProf.Style.Fill.BackgroundColor = dataBgColors[key];
                            thProf.Style.Font.Bold = true;
                            thProf.Style.Font.FontColor = headerFontColors[key];
                            thProf.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            thProf.Style.Alignment.WrapText = true;
                            thProf.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            
                            var thDiff = ws.Cell(headerRow2, startCol + 2);
                            thDiff.Value = "%/# Diff";
                            thDiff.Style.Fill.BackgroundColor = dataBgColors[key];
                            thDiff.Style.Font.Bold = true;
                            thDiff.Style.Font.FontColor = headerFontColors[key];
                            thDiff.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            thDiff.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        };

                        setHeaderGroup(2, "All", "All Students");
                        setHeaderGroup(5, "EL", "EL");
                        setHeaderGroup(8, "SWD", "SWD");
                        setHeaderGroup(11, "AA", "AA");
                        setHeaderGroup(14, "SED", "SED");
                        setHeaderGroup(17, "HISP", "Hispanic");
                        
                        var partHeaderCell = ws.Cell(headerRow1, totalCols);
                        partHeaderCell.Value = "Participation";
                        partHeaderCell.Style.Fill.BackgroundColor = headerBgColors["All"];
                        partHeaderCell.Style.Font.FontColor = headerFontColors["All"];
                        partHeaderCell.Style.Font.Bold = true;
                        partHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        partHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        partHeaderCell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));

                        var partSubHeaderCell = ws.Cell(headerRow2, totalCols);
                        partSubHeaderCell.Value = "% Participation (Tested/Enrolled)";
                        partSubHeaderCell.Style.Fill.BackgroundColor = dataBgColors["All"];
                        partSubHeaderCell.Style.Font.Bold = true;
                        partSubHeaderCell.Style.Font.FontColor = headerFontColors["All"];
                        partSubHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        partSubHeaderCell.Style.Alignment.WrapText = true;
                        partSubHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                
                        ws.Row(headerRow2).Height = 30;
                        row += 2; 

                        foreach (var bodyRow in subjectGroup.BodyRows)
                        {
                            var gradeCell = ws.Cell(row, 1);
                            gradeCell.Value = bodyRow.RowLabel;
                            gradeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                            gradeCell.Style.Font.Bold = true; 
                            
                            RenderExcelDemoGroup(ws, row, 2, bodyRow.All, bodyRow.AllTarget, dataBgColors["All"]);
                            RenderExcelDemoGroup(ws, row, 5, bodyRow.EL, bodyRow.ELTarget, dataBgColors["EL"]);
                            RenderExcelDemoGroup(ws, row, 8, bodyRow.SWD, bodyRow.SWDTarget, dataBgColors["SWD"]);
                            RenderExcelDemoGroup(ws, row, 11, bodyRow.AA, bodyRow.AATarget, dataBgColors["AA"]);
                            RenderExcelDemoGroup(ws, row, 14, bodyRow.SED, bodyRow.SEDTarget, dataBgColors["SED"]);
                            RenderExcelDemoGroup(ws, row, 17, bodyRow.HISP, bodyRow.HISPTarget, dataBgColors["HISP"]);

                            var partCell = ws.Cell(row, totalCols);
                            if (bodyRow.All.TotalEnrolled > 0)
                            {
                                partCell.Value = $"{bodyRow.All.PctParticipation}%\n({bodyRow.All.TotalTested}/{bodyRow.All.TotalEnrolled})";
                                partCell.Style.Alignment.WrapText = true;
                            }
                            else
                            {
                                partCell.Value = "—";
                            }
                            partCell.Style.Fill.BackgroundColor = dataBgColors["All"];
                            partCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            
                            if (bodyRow.Type == RowType.K2Total || bodyRow.Type == RowType.G3PlusTotal)
                            {
                                var subtotalStyle = ws.Range(row, 1, row, totalCols).Style;
                                subtotalStyle.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                                subtotalStyle.Font.Bold = true;
                                subtotalStyle.Border.SetTopBorder(XLBorderStyleValues.Thin);
                                subtotalStyle.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                                subtotalStyle.Border.SetTopBorderColor(XLColor.FromArgb(173, 181, 189));
                                subtotalStyle.Border.SetBottomBorderColor(XLColor.FromArgb(173, 181, 189)); 
                                gradeCell.Style.Font.FontColor = XLColor.FromArgb(52, 73, 94);
                                gradeCell.Style.Alignment.Indent = 1;
                            }
                            row++;
                        }
                        if (!subjectGroup.SubjectTotalRow.IsEmpty)
                        {
                            var totalCell = ws.Cell(row, 1);
                            totalCell.Value = subjectGroup.SubjectTotalRow.RowLabel;
                            totalCell.Style.Font.Bold = true;
                            totalCell.Style.Font.FontColor = XLColor.FromArgb(44, 62, 80); 
                            totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                            
                            RenderExcelDemoGroup(ws, row, 2, subjectGroup.SubjectTotalRow.All, subjectGroup.SubjectTotalRow.AllTarget, dataBgColors["All"]);
                            RenderExcelDemoGroup(ws, row, 5, subjectGroup.SubjectTotalRow.EL, subjectGroup.SubjectTotalRow.ELTarget, dataBgColors["EL"]);
                            RenderExcelDemoGroup(ws, row, 8, subjectGroup.SubjectTotalRow.SWD, subjectGroup.SubjectTotalRow.SWDTarget, dataBgColors["SWD"]);
                            RenderExcelDemoGroup(ws, row, 11, subjectGroup.SubjectTotalRow.AA, subjectGroup.SubjectTotalRow.AATarget, dataBgColors["AA"]);
                            RenderExcelDemoGroup(ws, row, 14, subjectGroup.SubjectTotalRow.SED, subjectGroup.SubjectTotalRow.SEDTarget, dataBgColors["SED"]);
                            RenderExcelDemoGroup(ws, row, 17, subjectGroup.SubjectTotalRow.HISP, subjectGroup.SubjectTotalRow.HISPTarget, dataBgColors["HISP"]);

                            var partTotalCell = ws.Cell(row, totalCols);
                            if (subjectGroup.SubjectTotalRow.All.TotalEnrolled > 0)
                            {
                                partTotalCell.Value = $"{subjectGroup.SubjectTotalRow.All.PctParticipation}%\n({subjectGroup.SubjectTotalRow.All.TotalTested}/{subjectGroup.SubjectTotalRow.All.TotalEnrolled})";
                                partTotalCell.Style.Alignment.WrapText = true;
                            }
                            else
                            {
                                partTotalCell.Value = "—";
                            }
                            partTotalCell.Style.Fill.BackgroundColor = dataBgColors["All"];
                            partTotalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                            var totalRowStyle = ws.Range(row, 1, row, totalCols).Style;
                            totalRowStyle.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
                            totalRowStyle.Font.Bold = true;
                            totalRowStyle.Border.SetTopBorder(XLBorderStyleValues.Thick);
                            totalRowStyle.Border.SetTopBorderColor(XLColor.FromArgb(52, 73, 94));
                            row++;
                        }
                        row++;
                    }
                }
                if (firstHeaderRow > 0)
                {
                    ws.SheetView.FreezeRows(firstHeaderRow + 1); 
                }
                var filename = $"Meta_Data_{SchoolName.Replace(" ", "_")}_SY{SchoolYear}_{DateTime.Now:yyyyMMdd}.xlsx";
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        filename);
                }
            }
        }

        private void RenderExcelDemoGroup(IXLWorksheet ws, int row, int startCol, AggData data, decimal? target, XLColor baseDataColor)
        {
            var cellTarget = ws.Cell(row, startCol);
            var cellProf = ws.Cell(row, startCol + 1);
            var cellDiff = ws.Cell(row, startCol + 2);

            cellTarget.Style.Fill.BackgroundColor = baseDataColor;
            cellTarget.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cellProf.Style.Fill.BackgroundColor = baseDataColor;
            cellProf.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cellDiff.Style.Fill.BackgroundColor = baseDataColor;
            cellDiff.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            if (target.HasValue)
            {
                cellTarget.Value = $"{target.Value.ToString("0.#")}%";
                cellTarget.Style.Font.Bold = true;
                cellTarget.Style.Font.FontColor = XLColor.FromArgb(0, 90, 156);
            }
            else
            {
                cellTarget.Value = "—";
            }

            if (data.TotalTested > 0)
            {
                cellProf.Value = $"{data.PctProficient}%\n({data.TotalProficient}/{data.TotalTested})";
                cellProf.Style.Alignment.WrapText = true;
            }
            else
            {
                cellProf.Value = "—";
            }
            
            if (target.HasValue && data.TotalTested > 0)
            {
                var pctDiff = target.Value - data.PctProficient;
                var nDiff = 0m;
                if (pctDiff > 0)
                {
                    nDiff = Math.Ceiling((data.TotalTested * pctDiff) / 100m);
                }
                
                cellDiff.Value = $"{pctDiff.ToString("0.#")}%\n({nDiff.ToString("0")})";
                cellDiff.Style.Alignment.WrapText = true;
                cellDiff.Style.Font.Bold = true;

                if (pctDiff > 0)
                {
                    cellDiff.Style.Fill.BackgroundColor = XLColor.FromArgb(248, 215, 218);
                    cellDiff.Style.Font.FontColor = XLColor.FromArgb(114, 28, 36);
                }
                else
                {
                    cellDiff.Style.Fill.BackgroundColor = XLColor.FromArgb(212, 237, 218);
                    cellDiff.Style.Font.FontColor = XLColor.FromArgb(21, 87, 36);
                }
            }
            else
            {
                cellDiff.Value = "—";
            }
        }

        private async Task LoadDistrictInfoAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(
                    "SELECT Name FROM [dbo].[Districts] WHERE Id = @districtId", conn))
                {
                    cmd.Parameters.AddWithValue("@districtId", DistrictId);
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null)
                    {
                        DistrictName = result.ToString() ?? string.Empty;
                    }
                }
            }
        }

        private async Task LoadFiltersAsync()
        {
            var schools = new List<SelectListItem> { new SelectListItem { Text = "-- Select School --", Value = "" } };
            var years = new List<SelectListItem> { new SelectListItem { Text = "-- Select Year --", Value = "" } }; 

            if (IsDistrictAdmin || IsOrendaUser)
            {
                schools.Add(new SelectListItem { Text = "All Schools", Value = "0" });
            }

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                var sql = @"
                    SELECT DISTINCT s.Id, s.Name 
                    FROM [dbo].[Schools] s
                    WHERE s.DistrictId = @districtId 
                      AND s.Inactive = 0
                      AND EXISTS (
                          SELECT 1 
                          FROM [dbo].[MetaAggregation] ma 
                          WHERE ma.school_id = s.Id
                      )";
                if (IsSchoolAdmin && UserSchoolIds.Any())
                {
                    var schoolParams = new List<string>();
                    for (int i = 0; i < UserSchoolIds.Count; i++)
                    {
                        schoolParams.Add($"@SchoolId{i}");
                    }
                    sql += $" AND s.Id IN ({string.Join(",", schoolParams)})";
                }
                sql += " ORDER BY s.Name";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@districtId", DistrictId);
                    if (IsSchoolAdmin && UserSchoolIds.Any())
                    {
                        for (int i = 0; i < UserSchoolIds.Count; i++)
                        {
                            cmd.Parameters.AddWithValue($"@SchoolId{i}", UserSchoolIds[i]);
                        }
                    }
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            schools.Add(new SelectListItem
                            {
                                Text = reader["Name"].ToString() ?? string.Empty,
                                Value = reader["Id"].ToString() ?? string.Empty
                            });
                        }
                    }
                }
                AvailableSchools = new SelectList(schools, "Value", "Text", SchoolId?.ToString());
                
                var yearSql = @"
                    SELECT DISTINCT ma.school_year
                    FROM [dbo].[MetaAggregation] ma
                    WHERE ma.district_id = @districtId
                      AND ma.school_year IS NOT NULL
                    ORDER BY ma.school_year DESC";

                using (var cmdYears = new SqlCommand(yearSql, conn))
                {
                    cmdYears.Parameters.AddWithValue("@districtId", DistrictId);
                    using (var reader = await cmdYears.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var yearObj = reader["school_year"];
                            if (yearObj != null && yearObj != DBNull.Value)
                            {
                                var yearInt = (int)yearObj; 
                                var yearString = yearInt.ToString();
                                years.Add(new SelectListItem
                                {
                                    Text = $"{yearInt - 1}-{yearString}",
                                    Value = yearString
                                });
                            }
                        }
                    }
                }
                AvailableSchoolYears = new SelectList(years, "Value", "Text", SchoolYear?.ToString());
            }
        }

        private async Task LoadSchoolTargetsAsync()
        {
            if (!SchoolId.HasValue || !SchoolYear.HasValue || SchoolId.Value == 0)
            {
                CurrentTargets = new List<SchoolTarget>();
                return;
            }

            CurrentTargets = new List<SchoolTarget>();
            var sql = @"
                SELECT grade, demographic_group, subject, target_pct
                FROM [dbo].[SchoolTargets]
                WHERE school_id = @SchoolId AND school_year = @SchoolYear";

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
                    cmd.Parameters.AddWithValue("@SchoolYear", SchoolYear.Value);
                    
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            CurrentTargets.Add(new SchoolTarget
                            {
                                Grade = reader["grade"].ToString() ?? string.Empty,
                                DemographicGroup = reader["demographic_group"].ToString() ?? string.Empty,
                                Subject = reader["subject"].ToString() ?? "All",
                                TargetPct = (decimal)reader["target_pct"]
                            });
                        }
                    }
                }
            }
        }

        private async Task LoadMetaDataAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                using (var cmd = new SqlCommand(
                    @"SELECT
                        ma.unit AS Unit,
                        ma.subject_norm AS Subject,
                        ma.grade AS Grade,
                        CASE
                            WHEN ma.grade IN ('0', '1', '2') THEN 'K-2'
                            ELSE '3+'
                        END AS GradeGroup,
                        ma.demographic_group AS DemographicGroup,
                        SUM(ma.total_enrolled) AS total_enrolled,
                        SUM(ma.total_tested) AS total_tested,
                        SUM(ma.total_proficient) AS total_proficient
                    FROM [dbo].[MetaAggregation] ma
                    WHERE ma.district_id = @DistrictId
                      AND ma.school_year = @SchoolYear 
                      AND (@SchoolId = 0 OR ma.school_id = @SchoolId)
                      AND ma.grade IS NOT NULL
                    GROUP BY
                        ma.unit,
                        ma.subject_norm,
                        ma.grade,
                        CASE
                            WHEN ma.grade IN ('0', '1', '2') THEN 'K-2'
                            ELSE '3+'
                        END,
                        ma.demographic_group
                    ORDER BY
                        Unit DESC,
                        Subject,
                        Grade,
                        DemographicGroup", conn))
                {
                    cmd.Parameters.AddWithValue("@DistrictId", DistrictId!.Value);
                    cmd.Parameters.AddWithValue("@SchoolId", SchoolId!.Value);
                    cmd.Parameters.AddWithValue("@SchoolYear", SchoolYear!.Value); 
                    RawDataRows = new List<MetaDataRow>();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            RawDataRows.Add(new MetaDataRow
                            {
                                Unit = reader["Unit"]?.ToString() ?? string.Empty,
                                Subject = reader["Subject"]?.ToString() ?? string.Empty,
                                Grade = reader["Grade"].ToString() ?? string.Empty,
                                GradeGroup = reader["GradeGroup"]?.ToString() ?? string.Empty,
                                DemographicGroup = reader["DemographicGroup"]?.ToString() ?? string.Empty,
                                TotalEnrolled = (int)reader["total_enrolled"],
                                TotalTested = (int)reader["total_tested"],
                                TotalProficient = (int)reader["total_proficient"]
                            });
                        }
                    }
                }
            }
        }

        private void LoadTargetUIData()
        {
            if (!RawDataRows.Any())
                return;

            var grades = RawDataRows
                .Select(r => r.Grade)
                .Distinct()
                .OrderBy(g => g)
                .Select(g => new SelectListItem {
                    Text = (g == "0" ? "K" : int.TryParse(g, out _) ? $"Grade {g}" : g),
                    Value = g
                })
                .ToList();
            
            AvailableTargetGrades = new SelectList(grades, "Value", "Text");

            var demos = RawDataRows
                .Select(r => r.DemographicGroup)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => new SelectListItem {
                    Text = d,
                    Value = d
                })
                .ToList();
            
            AvailableTargetDemographics = new SelectList(demos, "Value", "Text");
            
            var subjects = RawDataRows
                .Select(r => r.Subject)
                .Distinct()
                .OrderBy(s => s)
                .Select(s => new SelectListItem {
                    Text = s,
                    Value = s
                })
                .ToList();
            
            AvailableTargetSubjects = new SelectList(subjects, "Value", "Text");
        }

        private void BuildDisplayGroups()
        {
            UnitGroups = RawDataRows
                .GroupBy(r => r.Unit)
                .OrderByDescending(g => g.Key)
                .Select(unitGroup => new UnitDisplayGroup
                {
                    UnitName = unitGroup.Key,
                    SubjectGroups = unitGroup
                        .GroupBy(u => u.Subject)
                        .OrderBy(s => s.Key)
                        .Select(subjectGroup =>
                        {
                            var subjectDisplayGroup = new SubjectDisplayGroup { SubjectName = subjectGroup.Key };
                            var bodyRows = new List<MetaDisplayRow>();
                            var allSubjectData = subjectGroup.ToList();

                            var k2_data = allSubjectData.Where(s => s.GradeGroup == "K-2").ToList();
                            var k2_GradeRows = k2_data
                                .GroupBy(g => g.Grade)
                                .OrderBy(g => g.Key)
                                .Select(gradeData =>
                                {
                                    var label = gradeData.Key == "0" ? "K" : $"Grade {gradeData.Key}";
                                    var row = PivotDemographics(gradeData, label, gradeData.Key, subjectGroup.Key);
                                    row.Type = RowType.Grade;
                                    row.Grade = gradeData.Key;
                                    return row;
                                })
                                .ToList();

                            bodyRows.AddRange(k2_GradeRows);

                            var k2_TotalRow = PivotDemographics(k2_data, "K-2 Total", null, subjectGroup.Key, 
                                isGroupTotal: true, childRows: k2_GradeRows);
                            k2_TotalRow.Type = RowType.K2Total;
                            if (!k2_TotalRow.IsEmpty)
                            {
                                bodyRows.Add(k2_TotalRow);
                            }

                            var g3Plus_data = allSubjectData.Where(s => s.GradeGroup == "3+").ToList();
                            var g3Plus_GradeRows = g3Plus_data
                                .GroupBy(g => g.Grade)
                                .OrderBy(g => g.Key)
                                .Select(gradeData =>
                                {
                                    var label = int.TryParse(gradeData.Key, out _) ? $"Grade {gradeData.Key}" : gradeData.Key;
                                    var row = PivotDemographics(gradeData, label, gradeData.Key, subjectGroup.Key);
                                    row.Type = RowType.Grade;
                                    row.Grade = gradeData.Key;
                                    return row;
                                })
                                .ToList();

                            bodyRows.AddRange(g3Plus_GradeRows);

                            var g3Plus_TotalRow = PivotDemographics(g3Plus_data, "3+ Total", null, subjectGroup.Key,
                                isGroupTotal: true, childRows: g3Plus_GradeRows);
                            g3Plus_TotalRow.Type = RowType.G3PlusTotal;
                            if (!g3Plus_TotalRow.IsEmpty)
                            {
                                bodyRows.Add(g3Plus_TotalRow);
                            }

                            subjectDisplayGroup.BodyRows = bodyRows;
                            subjectDisplayGroup.SubjectTotalRow = PivotDemographics(allSubjectData, "Subject Total", null, subjectGroup.Key,
                                isGroupTotal: true, childRows: bodyRows.Where(r => r.Type == RowType.Grade).ToList());
                            subjectDisplayGroup.SubjectTotalRow.Type = RowType.SubjectTotal;

                            return subjectDisplayGroup;
                        }).ToList()
                }).ToList();
        }

        private MetaDisplayRow PivotDemographics(IEnumerable<MetaDataRow> rows, string rowLabel, string? grade, string subject, 
            bool isGroupTotal = false, List<MetaDisplayRow>? childRows = null)
        {
            var row = new MetaDisplayRow { RowLabel = rowLabel };
            var groups = rows
                .GroupBy(r => r.DemographicGroup)
                .ToDictionary(g => g.Key, g => new AggData
                {
                    TotalEnrolled = g.Sum(x => x.TotalEnrolled),
                    TotalTested = g.Sum(x => x.TotalTested),
                    TotalProficient = g.Sum(x => x.TotalProficient)
                });

            row.All = groups.GetValueOrDefault("All", new AggData());
            row.EL = groups.GetValueOrDefault("EL", new AggData());
            row.SWD = groups.GetValueOrDefault("SWD", new AggData());
            row.AA = groups.GetValueOrDefault("AA", new AggData());
            row.SED = groups.GetValueOrDefault("SED", new AggData());
            row.HISP = groups.GetValueOrDefault("HISP", new AggData());

            if (!string.IsNullOrEmpty(grade))
            {
                row.AllTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "All")?.TargetPct;
                row.ELTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "EL")?.TargetPct;
                row.SWDTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "SWD")?.TargetPct;
                row.AATarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "AA")?.TargetPct;
                row.SEDTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "SED")?.TargetPct;
                row.HISPTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade && t.Subject == subject && t.DemographicGroup == "HISP")?.TargetPct;
            }
            else if (isGroupTotal && childRows != null && childRows.Any())
            {
                row.AllTarget = AverageTargets(childRows.Select(r => r.AllTarget));
                row.ELTarget = AverageTargets(childRows.Select(r => r.ELTarget));
                row.SWDTarget = AverageTargets(childRows.Select(r => r.SWDTarget));
                row.AATarget = AverageTargets(childRows.Select(r => r.AATarget));
                row.SEDTarget = AverageTargets(childRows.Select(r => r.SEDTarget));
                row.HISPTarget = AverageTargets(childRows.Select(r => r.HISPTarget));
            }
            
            return row;
        }

        private decimal? AverageTargets(IEnumerable<decimal?> targets)
        {
            var nonNullTargets = targets.Where(t => t.HasValue).Select(t => t!.Value).ToList();
            return nonNullTargets.Any() ? Math.Round(nonNullTargets.Average(), 2) : null;
        }

        private void BuildBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Data Reflection", Url = "/DataReflection/Index" },
                new BreadcrumbItem { Title = DistrictName,
                Url = DistrictId.HasValue ? Url.Page("/DataReflection/Forms", new { districtId = DistrictId }) : null },
                new BreadcrumbItem { Title = "Meta Data", Url = null }
            };
        }
    }

    public enum RowType { Grade, K2Total, G3PlusTotal, SubjectTotal }

    public class MetaDataRow
    {
        public string Unit { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string GradeGroup { get; set; } = string.Empty;
        public string DemographicGroup { get; set; } = string.Empty;
        public int TotalEnrolled { get; set; }
        public int TotalTested { get; set; }
        public int TotalProficient { get; set; }
    }

    public class MetaDisplayRow
    {
        public string RowLabel { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public AggData All { get; set; } = new AggData();
        public AggData EL { get; set; } = new AggData();
        public AggData SWD { get; set; } = new AggData();
        public AggData AA { get; set; } = new AggData();
        public AggData SED { get; set; } = new AggData();
        public AggData HISP { get; set; } = new AggData();
        public RowType Type { get; set; } = RowType.Grade;

        public decimal? AllTarget { get; set; }
        public decimal? ELTarget { get; set; }
        public decimal? SWDTarget { get; set; }
        public decimal? AATarget { get; set; }
        public decimal? SEDTarget { get; set; }
        public decimal? HISPTarget { get; set; }

        public bool IsEmpty =>
            All.TotalEnrolled == 0 &&
            EL.TotalEnrolled == 0 &&
            SWD.TotalEnrolled == 0 &&
            AA.TotalEnrolled == 0 &&
            SED.TotalEnrolled == 0 &&
            HISP.TotalEnrolled == 0;
    }

    public class AggData
    {
        public int TotalEnrolled { get; set; }
        public int TotalTested { get; set; }
        public int TotalProficient { get; set; }
        public decimal PctProficient => TotalTested > 0
            ? Math.Round(100m * TotalProficient / TotalTested, 2)
            : 0m;
        
        public decimal PctParticipation => TotalEnrolled > 0
            ? Math.Round(100m * TotalTested / TotalEnrolled, 2)
            : 0m;
    }

    public class SubjectDisplayGroup
    {
        public string SubjectName { get; set; } = string.Empty;
        public List<MetaDisplayRow> BodyRows { get; set; } = new List<MetaDisplayRow>();
        public MetaDisplayRow SubjectTotalRow { get; set; } = new MetaDisplayRow();
    }

    public class UnitDisplayGroup
    {
        public string UnitName { get; set; } = string.Empty;
        public List<SubjectDisplayGroup> SubjectGroups { get; set; } = new List<SubjectDisplayGroup>();
    }
}
