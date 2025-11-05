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
using Microsoft.AspNetCore.Authorization;

// Note: We are in a new namespace for the Mega page
namespace ORSV2.Pages.DataReflection
{

    // ###############################################################
    // NEW VIEW MODELS FOR MEGA PAGE
    // ###############################################################

    public class MegaGradeRow
    {
        public string RowLabel { get; set; } = string.Empty;
        public RowType Type { get; set; } = RowType.Grade;

        // Data is now a dictionary where the Key is the Unit Name (e.g., "Unit 1")
        public Dictionary<string, AggData> AllData { get; set; } = new Dictionary<string, AggData>();
        public Dictionary<string, AggData> ELData { get; set; } = new Dictionary<string, AggData>();
        public Dictionary<string, AggData> SWDData { get; set; } = new Dictionary<string, AggData>();
        public Dictionary<string, AggData> AAData { get; set; } = new Dictionary<string, AggData>();
        public Dictionary<string, AggData> SEDData { get; set; } = new Dictionary<string, AggData>();
        public Dictionary<string, AggData> HISPData { get; set; } = new Dictionary<string, AggData>();

        // Targets are still at the grade level, not unit level
        public decimal? AllTarget { get; set; }
        public decimal? ELTarget { get; set; }
        public decimal? SWDTarget { get; set; }
        public decimal? AATarget { get; set; }
        public decimal? SEDTarget { get; set; }
        public decimal? HISPTarget { get; set; }

        public bool IsEmpty
        {
            get
            {
                // Check if all data dictionaries are empty or only contain empty AggData
                Func<Dictionary<string, AggData>, bool> isDataEmpty = (data) =>
                    !data.Any() || data.Values.All(v => v.TotalTested == 0);

                return isDataEmpty(AllData) &&
                       isDataEmpty(ELData) &&
                       isDataEmpty(SWDData) &&
                       isDataEmpty(AAData) &&
                       isDataEmpty(SEDData) &&
                       isDataEmpty(HISPData);
            }
        }
    }

    public class MegaSubjectGroup
    {
        public string SubjectName { get; set; } = string.Empty;
        // This list will contain the dynamic units found for this subject (e.g., "Unit 1", "Unit 2")
        public List<string> ActiveUnits { get; set; } = new List<string>();
        public List<MegaGradeRow> BodyRows { get; set; } = new List<MegaGradeRow>();
        public MegaGradeRow SubjectTotalRow { get; set; } = new MegaGradeRow();
    }


    // ###############################################################
    // MEGA MODEL
    // ###############################################################
    [Authorize(Roles = "OrendaAdmin,OrendaManager,DistrictAdmin")]
    public class MegaModel : SecureReportPageModel
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MegaModel> _logger; // Changed from MetaModel

        [BindProperty(SupportsGet = true)]
        public int? SchoolId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DistrictId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SchoolYear { get; set; }

        [BindProperty]
        public string? NewTargetGrade { get; set; }
        [BindProperty]
        public string? NewTargetDemographic { get; set; }
        [BindProperty]
        public decimal? NewTargetPercent { get; set; }

        public SelectList AvailableSchools { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableSchoolYears { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableTargetGrades { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableTargetDemographics { get; set; } = new SelectList(new List<SelectListItem>());

        // This is the new top-level display property
        public List<MegaSubjectGroup> SubjectGroups { get; set; } = new List<MegaSubjectGroup>();
        
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new List<BreadcrumbItem>();
        public string DistrictName { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;

        // Private properties for data handling
        private List<MetaDataRow> RawDataRows { get; set; } = new List<MetaDataRow>();
        private List<SchoolTarget> CurrentTargets { get; set; } = new List<SchoolTarget>();


        // Constructor
        public MegaModel(IConfiguration config, ILogger<MegaModel> logger) // Changed from MetaModel
        {
            _config = config;
            _logger = logger;
        }

        // OnGetAsync
        public async Task<IActionResult> OnGetAsync()
        {
            InitializeUserDataScope();

            if (!IsDistrictAdmin && !IsOrendaUser)
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
                BuildDisplayGroups(); // This will call our new Mega-specific builder

                if (SchoolId.Value == 0)
                {
                    SchoolName = "All Schools";
                }
                else
                {
                    var items = AvailableSchools.Items?.Cast<SelectListItem>() ?? new List<SelectListItem>();
                    SchoolName = items.FirstOrDefault(s => s.Value == SchoolId.ToString())?.Text ?? "Selected School";
                }

                if (SubjectGroups.Any()) // Changed from UnitGroups
                {
                    LoadTargetUIData();
                }
            }

            BuildBreadcrumbs();
            return Page();
        }

        // OnPostSetTargetAsync - This logic remains identical to MetaModel
        public async Task<IActionResult> OnPostSetTargetAsync()
        {
            InitializeUserDataScope();

            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) return Forbid();
            if (UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;

            if (!SchoolId.HasValue || SchoolId.Value == 0)
            {
                _logger.LogWarning("Target save failed: 'All Schools' (0) is not a valid school ID for targets.");
                return RedirectToPage(new { DistrictId = DistrictId, SchoolId = SchoolId, SchoolYear = SchoolYear });
            }
            if (!SchoolYear.HasValue || !NewTargetPercent.HasValue || string.IsNullOrEmpty(NewTargetGrade) || string.IsNullOrEmpty(NewTargetDemographic))
            {
                _logger.LogWarning("Target save failed: Missing required fields.");
                return RedirectToPage(new { DistrictId = DistrictId, SchoolId = SchoolId, SchoolYear = SchoolYear });
            }

            if (IsSchoolAdmin && !UserSchoolIds.Contains(SchoolId.Value))
            {
                _logger.LogWarning("Target save FORBIDDEN: User tried to save target for unassigned school {SchoolId}", SchoolId.Value);
                return Forbid();
            }

            if (!int.TryParse(NewTargetGrade, out var gradeInt))
            {
                _logger.LogWarning("Target save failed: Invalid grade '{Grade}'", NewTargetGrade);
                return RedirectToPage(new { DistrictId = DistrictId, SchoolId = SchoolId, SchoolYear = SchoolYear });
            }

            try
            {
                var sql = @"
                    MERGE INTO [dbo].[SchoolTargets] AS T
                    USING (VALUES (@SchoolId, @SchoolYear, @Grade, @DemographicGroup)) AS S (school_id, school_year, grade, demographic_group)
                    ON T.school_id = S.school_id AND T.school_year = S.school_year AND T.grade = S.grade AND T.demographic_group = S.demographic_group
                    WHEN MATCHED THEN
                        UPDATE SET 
                            target_pct = @TargetPct,
                            updated_at = GETUTCDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (school_id, school_year, grade, demographic_group, target_pct, created_at, updated_at)
                        VALUES (@SchoolId, @SchoolYear, @Grade, @DemographicGroup, @TargetPct, GETUTCDATE(), GETUTCDATE());";

                using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
                        cmd.Parameters.AddWithValue("@SchoolYear", SchoolYear.Value);
                        cmd.Parameters.AddWithValue("@Grade", gradeInt);
                        cmd.Parameters.AddWithValue("@DemographicGroup", NewTargetDemographic);
                        cmd.Parameters.AddWithValue("@TargetPct", NewTargetPercent.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                _logger.LogInformation("School Target saved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving school target.");
            }

            return RedirectToPage(new { DistrictId = DistrictId, SchoolId = SchoolId, SchoolYear = SchoolYear });
        }

        // OnGetExcelAsync - This needs to be completely rewritten for the new format
        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();
            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) return Forbid();
            if (UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;

            if (!SchoolId.HasValue || !DistrictId.HasValue || !SchoolYear.HasValue)
                return RedirectToPage();

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync(); // Populates AvailableSchools
            await LoadSchoolTargetsAsync();
            await LoadMetaDataAsync();
            BuildDisplayGroups(); // Build the new MegaSubjectGroups

            // SchoolName lookup (same as Meta)
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
                        // Fallback
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
                var ws = workbook.Worksheets.Add("Mega Data");
                ws.Column(1).Width = 20; // Grade column

                var headerBgColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.FromArgb(44, 62, 80) }, { "All", XLColor.FromArgb(179, 217, 255) }, { "EL", XLColor.FromArgb(255, 179, 217) }, { "SWD", XLColor.FromArgb(255, 255, 179) }, { "AA", XLColor.FromArgb(179, 255, 179) }, { "SED", XLColor.FromArgb(255, 179, 102) }, { "HISP", XLColor.FromArgb(255, 230, 179) }
                };
                var headerFontColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.White }, { "All", XLColor.FromArgb(0, 61, 153) }, { "EL", XLColor.FromArgb(153, 0, 51) }, { "SWD", XLColor.FromArgb(153, 102, 0) }, { "AA", XLColor.FromArgb(0, 77, 0) }, { "SED", XLColor.FromArgb(102, 68, 0) }, { "HISP", XLColor.FromArgb(102, 77, 0) }
                };
                var dataBgColors = new Dictionary<string, XLColor>
                {
                    { "All", XLColor.FromArgb(230, 241, 250) }, { "EL", XLColor.FromArgb(250, 230, 241) }, { "SWD", XLColor.FromArgb(250, 250, 230) }, { "AA", XLColor.FromArgb(230, 250, 230) }, { "SED", XLColor.FromArgb(250, 230, 215) }, { "HISP", XLColor.FromArgb(250, 244, 230) }
                };
                var demoKeys = new[] { "All", "EL", "SWD", "AA", "SED", "HISP" };
                var demoNames = new[] { "All Students", "EL", "SWD", "AA", "SED", "Hispanic" };


                int row = 1;
                int firstHeaderRow = 0;

                var headerCell = ws.Cell(row, 1);
                headerCell.Value = $"{DistrictName} - {SchoolName} (SY {SchoolYear - 1}-{SchoolYear})";
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Font.FontSize = 14;
                // Merge will be set later
                row += 2;

                foreach (var subjectGroup in SubjectGroups)
                {
                    var activeUnits = subjectGroup.ActiveUnits;
                    if (!activeUnits.Any()) activeUnits.Add("N/A"); // Handle case with no units
                    int unitColCount = activeUnits.Count;
                    int totalCols = 1 + (demoKeys.Length * unitColCount);
                    
                    if (row == 3) // First time, merge the main header
                    {
                         ws.Range(1, 1, 1, totalCols).Merge();
                    }

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
                    int headerRow3 = row + 2;

                    // Grade Header (Row 1)
                    var gradeHeaderCell = ws.Cell(headerRow1, 1);
                    gradeHeaderCell.Value = "Grade";
                    gradeHeaderCell.Style.Fill.BackgroundColor = headerBgColors["Grade"];
                    gradeHeaderCell.Style.Font.FontColor = headerFontColors["Grade"];
                    gradeHeaderCell.Style.Font.Bold = true;
                    gradeHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    gradeHeaderCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    ws.Range(headerRow1, 1, headerRow3, 1).Merge();
                    SetAllBorders(gradeHeaderCell, XLColor.FromArgb(52, 73, 94));

                    int currentCol = 2;
                    // Demographic Headers (Row 1)
                    for(int i = 0; i < demoKeys.Length; i++)
                    {
                        var key = demoKeys[i];
                        var cell = ws.Cell(headerRow1, currentCol);
                        cell.Value = demoNames[i];
                        cell.Style.Fill.BackgroundColor = headerBgColors[key];
                        cell.Style.Font.FontColor = headerFontColors[key];
                        cell.Style.Font.Bold = true;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ws.Range(headerRow1, currentCol, headerRow1, currentCol + unitColCount - 1).Merge();
                        SetAllBorders(cell, XLColor.FromArgb(52, 73, 94));
                        
                        // Unit Headers (Row 2)
                        for (int j = 0; j < unitColCount; j++)
                        {
                            var unitCell = ws.Cell(headerRow2, currentCol + j);
                            unitCell.Value = activeUnits[j];
                            unitCell.Style.Fill.BackgroundColor = dataBgColors[key];
                            unitCell.Style.Font.FontColor = headerFontColors[key];
                            unitCell.Style.Font.Bold = true;
                            unitCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            SetAllBorders(unitCell, XLColor.FromArgb(52, 73, 94));
                            ws.Column(currentCol + j).Width = 22;

                            // % Proficient Headers (Row 3)
                            var subCell = ws.Cell(headerRow3, currentCol + j);
                            subCell.Value = "% Proficient (Proficient/Tested)";
                            subCell.Style.Fill.BackgroundColor = dataBgColors[key];
                            subCell.Style.Font.FontColor = headerFontColors[key];
                            subCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            subCell.Style.Alignment.WrapText = true;
                            SetAllBorders(subCell, XLColor.FromArgb(52, 73, 94));
                        }
                        
                        currentCol += unitColCount;
                    }

                    ws.Row(headerRow3).Height = 30;
                    row += 3;

                    // Data Rows
                    foreach (var bodyRow in subjectGroup.BodyRows)
                    {
                        var gradeCell = ws.Cell(row, 1);
                        gradeCell.Value = bodyRow.RowLabel;
                        gradeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        gradeCell.Style.Font.Bold = true;
                        SetAllBorders(gradeCell, XLColor.Gainsboro);

                        currentCol = 2;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.AllData, "All", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.ELData, "EL", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.SWDData, "SWD", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.AAData, "AA", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.SEDData, "SED", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, bodyRow.HISPData, "HISP", activeUnits, dataBgColors, headerFontColors);
                        
                        if (bodyRow.Type == RowType.K2Total || bodyRow.Type == RowType.G3PlusTotal)
                        {
                            var subtotalStyle = ws.Range(row, 1, row, totalCols).Style;
                            subtotalStyle.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                            subtotalStyle.Font.Bold = true;
                            subtotalStyle.Border.SetTopBorder(XLBorderStyleValues.Thin);
                            subtotalStyle.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                            gradeCell.Style.Font.FontColor = XLColor.FromArgb(52, 73, 94);
                            gradeCell.Style.Alignment.Indent = 1;
                        }
                        row++;
                    }

                    // Total Row
                    if (!subjectGroup.SubjectTotalRow.IsEmpty)
                    {
                        var totalRow = subjectGroup.SubjectTotalRow;
                        var totalCell = ws.Cell(row, 1);
                        totalCell.Value = totalRow.RowLabel;
                        totalCell.Style.Font.Bold = true;
                        totalCell.Style.Font.FontColor = XLColor.FromArgb(44, 62, 80);
                        totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        SetAllBorders(totalCell, XLColor.Gainsboro);

                        currentCol = 2;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.AllData, "All", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.ELData, "EL", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.SWDData, "SWD", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.AAData, "AA", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.SEDData, "SED", activeUnits, dataBgColors, headerFontColors);
                        currentCol += unitColCount;
                        RenderExcelDataRow(ws, row, currentCol, totalRow.HISPData, "HISP", activeUnits, dataBgColors, headerFontColors);

                        var totalRowStyle = ws.Range(row, 1, row, totalCols).Style;
                        totalRowStyle.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239);
                        totalRowStyle.Font.Bold = true;
                        totalRowStyle.Border.SetTopBorder(XLBorderStyleValues.Thick);
                        totalRowStyle.Border.SetTopBorderColor(XLColor.FromArgb(52, 73, 94));
                        row++;
                    }
                    row++; // Space between subjects
                }
                
                if (firstHeaderRow > 0)
                {
                    ws.SheetView.FreezeRows(firstHeaderRow + 2); // Freeze all 3 header rows
                }

                var filename = $"Mega_Data_{SchoolName.Replace(" ", "_")}_SY{SchoolYear}_{DateTime.Now:yyyyMMdd}.xlsx";
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        filename);
                }
            }
        }

        private void SetAllBorders(IXLCell cell, XLColor color)
        {
            cell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin);
            cell.Style.Border.SetTopBorderColor(color);
            cell.Style.Border.SetBottomBorderColor(color);
            cell.Style.Border.SetLeftBorderColor(color);
            cell.Style.Border.SetRightBorderColor(color);
        }


        // Helper for Excel Data Row
        private void RenderExcelDataRow(IXLWorksheet ws, int row, int startCol, Dictionary<string, AggData> data, string key, List<string> activeUnits, Dictionary<string, XLColor> bgColors, Dictionary<string, XLColor> fontColors)
        {
            foreach (var unit in activeUnits)
            {
                data.TryGetValue(unit, out var aggData);
                RenderExcelDataCell(ws, row, startCol, aggData, bgColors[key]);
                startCol++;
            }
        }

        // RenderExcelDataCell - Reused from MetaModel
        private void RenderExcelDataCell(IXLWorksheet ws, int row, int col, AggData? data, XLColor baseDataColor)
        {
            var cell = ws.Cell(row, col);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            SetAllBorders(cell, XLColor.Gainsboro); // Add borders to data cells too

            if (data != null && data.TotalTested > 0)
            {
                cell.Value = $"{data.PctProficient}%\n({data.TotalProficient}/{data.TotalTested})";
                cell.Style.Alignment.WrapText = true;
                cell.Style.Fill.BackgroundColor = baseDataColor;
            }
            else
            {
                cell.Value = "—";
                cell.Style.Fill.BackgroundColor = baseDataColor;
            }
        }


        // LoadDistrictInfoAsync - Reused from MetaModel
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

        // LoadFiltersAsync - Reused from MetaModel
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
                        var paramName = $"@SchoolId{i}";
                        schoolParams.Add(paramName);
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

        // LoadSchoolTargetsAsync - Reused from MetaModel
        private async Task LoadSchoolTargetsAsync()
        {
            if (!SchoolId.HasValue || !SchoolYear.HasValue || SchoolId.Value == 0)
            {
                CurrentTargets = new List<SchoolTarget>();
                return;
            }

            CurrentTargets = new List<SchoolTarget>();
            var sql = @"
                SELECT grade, demographic_group, target_pct
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
                                Grade = (int)reader["grade"],
                                DemographicGroup = reader["demographic_group"].ToString() ?? string.Empty,
                                TargetPct = (decimal)reader["target_pct"]
                            });
                        }
                    }
                }
            }
        }


        // LoadMetaDataAsync - Reused from MetaModel (it gets all the data we need)
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
                            WHEN ma.grade IN (0, 1, 2) THEN 'K-2'
                            WHEN ma.grade > 2 THEN '3+'
                            ELSE 'Other'
                        END AS GradeGroup,
                        ma.demographic_group AS DemographicGroup,
                        SUM(ma.total_enrolled) AS total_enrolled,
                        SUM(ma.total_tested) AS total_tested,
                        SUM(ma.total_proficient) AS total_proficient
                    FROM [dbo].[MetaAggregation] ma
                    WHERE ma.district_id = @DistrictId
                      AND ma.school_year = @SchoolYear 
                      AND (@SchoolId = 0 OR ma.school_id = @SchoolId)
                      AND ma.grade >= 0
                    GROUP BY
                        ma.unit,
                        ma.subject_norm,
                        ma.grade,
                        CASE
                            WHEN ma.grade IN (0, 1, 2) THEN 'K-2'
                            WHEN ma.grade > 2 THEN '3+'
                            ELSE 'Other'
                        END,
                        ma.demographic_group
                    HAVING 
                        CASE
                            WHEN ma.grade IN (0, 1, 2) THEN 'K-2'
                            WHEN ma.grade > 2 THEN '3+'
                            ELSE 'Other'
                        END != 'Other'
                    ORDER BY
                        Subject,
                        Unit,
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
                                Grade = (int)reader["Grade"],
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

        // LoadTargetUIData - Reused from MetaModel
        private void LoadTargetUIData()
        {
            if (!RawDataRows.Any())
                return;

            var grades = RawDataRows
                .Select(r => r.Grade)
                .Distinct()
                .OrderBy(g => g)
                .Select(g => new SelectListItem
                {
                    Text = (g == 0 ? "K" : $"Grade {g}"),
                    Value = g.ToString()
                })
                .ToList();

            AvailableTargetGrades = new SelectList(grades, "Value", "Text");

            var demos = RawDataRows
                .Select(r => r.DemographicGroup)
                .Distinct()
                .OrderBy(d => d)
                .Select(d => new SelectListItem
                {
                    Text = d,
                    Value = d
                })
                .ToList();

            AvailableTargetDemographics = new SelectList(demos, "Value", "Text");
        }


        // BuildDisplayGroups - This is the NEW logic for the Mega view
        private void BuildDisplayGroups()
        {
            SubjectGroups = RawDataRows
                .GroupBy(r => r.Subject)
                .OrderBy(g => g.Key)
                .Select(subjectGroup =>
                {
                    var megaSubjectGroup = new MegaSubjectGroup { SubjectName = subjectGroup.Key };
                    
                    // Find all unique, non-empty units for this subject to build columns
                    var activeUnits = subjectGroup
                        .Select(r => r.Unit)
                        .Where(u => !string.IsNullOrEmpty(u))
                        .Distinct()
                        .OrderBy(u => u) // You might want custom sorting here later
                        .ToList();
                    
                    megaSubjectGroup.ActiveUnits = activeUnits;

                    var bodyRows = new List<MegaGradeRow>();
                    var allSubjectData = subjectGroup.ToList();

                    // K-2 Grades
                    var k2_data = allSubjectData.Where(s => s.GradeGroup == "K-2").ToList();
                    var k2_GradeRows = k2_data
                        .GroupBy(g => g.Grade)
                        .OrderBy(g => g.Key)
                        .Select(gradeData =>
                        {
                            var label = gradeData.Key == 0 ? "K" : $"Grade {gradeData.Key}";
                            var row = PivotUnitsAndDemographics(gradeData, label, gradeData.Key, activeUnits);
                            row.Type = RowType.Grade;
                            return row;
                        })
                        .ToList();
                    bodyRows.AddRange(k2_GradeRows);

                    // K-2 Total
                    var k2_TotalRow = PivotUnitsAndDemographics(k2_data, "K-2 Total", null, activeUnits);
                    k2_TotalRow.Type = RowType.K2Total;
                    if (!k2_TotalRow.IsEmpty)
                    {
                        bodyRows.Add(k2_TotalRow);
                    }

                    // 3+ Grades
                    var g3Plus_data = allSubjectData.Where(s => s.GradeGroup == "3+").ToList();
                    var g3Plus_GradeRows = g3Plus_data
                        .GroupBy(g => g.Grade)
                        .OrderBy(g => g.Key)
                        .Select(gradeData =>
                        {
                            var label = $"Grade {gradeData.Key}";
                            var row = PivotUnitsAndDemographics(gradeData, label, gradeData.Key, activeUnits);
                            row.Type = RowType.Grade;
                            return row;
                        })
                        .ToList();
                    bodyRows.AddRange(g3Plus_GradeRows);

                    // 3+ Total
                    var g3Plus_TotalRow = PivotUnitsAndDemographics(g3Plus_data, "3+ Total", null, activeUnits);
                    g3Plus_TotalRow.Type = RowType.G3PlusTotal;
                    if (!g3Plus_TotalRow.IsEmpty)
                    {
                        bodyRows.Add(g3Plus_TotalRow);
                    }

                    megaSubjectGroup.BodyRows = bodyRows;
                    
                    // Subject Total
                    megaSubjectGroup.SubjectTotalRow = PivotUnitsAndDemographics(allSubjectData, "Subject Total", null, activeUnits);
                    megaSubjectGroup.SubjectTotalRow.Type = RowType.SubjectTotal;

                    return megaSubjectGroup;

                }).ToList();
        }

        // PivotUnitsAndDemographics - This is the NEW pivoting logic
        private MegaGradeRow PivotUnitsAndDemographics(IEnumerable<MetaDataRow> rows, string rowLabel, int? grade, List<string> activeUnits)
        {
            var row = new MegaGradeRow { RowLabel = rowLabel };

            // Helper function to pivot the data for a specific demographic group
            Func<string, Dictionary<string, AggData>> getUnitData = (demoGroupKey) =>
            {
                return rows
                    .Where(r => r.DemographicGroup == demoGroupKey)
                    .GroupBy(r => r.Unit)
                    .ToDictionary(
                        g => g.Key,
                        g => new AggData
                        {
                            TotalTested = g.Sum(x => x.TotalTested),
                            TotalProficient = g.Sum(x => x.TotalProficient)
                        }
                    );
            };

            row.AllData = getUnitData("All");
            row.ELData = getUnitData("EL");
            row.SWDData = getUnitData("SWD");
            row.AAData = getUnitData("AA");
            row.SEDData = getUnitData("SED");
            row.HISPData = getUnitData("HISP");

            // Assign targets if this is a specific grade row
            if (grade.HasValue)
            {
                row.AllTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "All")?.TargetPct;
                row.ELTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "EL")?.TargetPct;
                row.SWDTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "SWD")?.TargetPct;
                row.AATarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "AA")?.TargetPct;
                row.SEDTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "SED")?.TargetPct;
                row.HISPTarget = CurrentTargets.FirstOrDefault(t => t.Grade == grade.Value && t.DemographicGroup == "HISP")?.TargetPct;
            }

            return row;
        }

        // BuildBreadcrumbs - Changed "Meta Data" to "Mega Data"
        private void BuildBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Data Reflection", Url = "/DataReflection/Index" },
                new BreadcrumbItem { Title = DistrictName,
                Url = DistrictId.HasValue ? Url.Page("/DataReflection/Forms", new { districtId = DistrictId }) : null },
                new BreadcrumbItem { Title = "Mega Data", Url = null } // Changed
            };
        }
    }
}

