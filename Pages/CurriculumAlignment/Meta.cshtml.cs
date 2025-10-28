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
using System.IO; // Required for MemoryStream

namespace ORSV2.Pages.CurriculumAlignment
{
    public class MetaModel : SecureReportPageModel
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MetaModel> _logger;

        [BindProperty(SupportsGet = true)]
        public int? SchoolId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DistrictId { get; set; }

        public SelectList AvailableSchools { get; set; } = new SelectList(new List<SelectListItem>());

        public List<UnitDisplayGroup> UnitGroups { get; set; } = new List<UnitDisplayGroup>();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new List<BreadcrumbItem>();
        public string DistrictName { get; set; } = string.Empty;
        public string SchoolName { get; set; } = string.Empty;

        private List<MetaDataRow> RawDataRows { get; set; } = new List<MetaDataRow>();

        // Constructor
        public MetaModel(IConfiguration config, ILogger<MetaModel> logger)
        {
            _config = config;
            _logger = logger;
        }

        // OnGetAsync
        public async Task<IActionResult> OnGetAsync()
        {
            InitializeUserDataScope();

            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser)
            {
                return Forbid();
            }

            // SECURITY: Enforce user's district scope
            if (UserDistrictId.HasValue)
            {
                if (DistrictId.HasValue && DistrictId.Value != UserDistrictId.Value)
                {
                    return Forbid(); // User trying to access wrong district
                }
                DistrictId = UserDistrictId.Value;
            }

            if (DistrictId == null || DistrictId == 0)
            {
                return Page();
            }

            // SECURITY: For SchoolAdmins, validate SchoolId is in their assigned schools
            if (IsSchoolAdmin && SchoolId.HasValue && SchoolId.Value != 0)
            {
                if (!UserSchoolIds.Contains(SchoolId.Value))
                {
                    return Forbid(); // User trying to access unauthorized school
                }
            }

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync();

            if (SchoolId.HasValue)
            {
                await LoadMetaDataAsync();
                BuildDisplayGroups();

                if (SchoolId.Value == 0)
                {
                    SchoolName = "All Schools";
                }
                else
                {
                    SchoolName = AvailableSchools.FirstOrDefault(s => s.Value == SchoolId.ToString())?.Text ?? "Selected School";
                }
            }

            BuildBreadcrumbs();
            return Page();
        }

        // OnGetExcelAsync - UPDATED FOR CLOSEDXML
        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();
            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) return Forbid();
            if (UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;

            if (!SchoolId.HasValue || !DistrictId.HasValue)
                return RedirectToPage();

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync(); // This populates SchoolName
            await LoadMetaDataAsync();
            BuildDisplayGroups(); // This populates UnitGroups

            // Manually set SchoolName if it wasn't set by LoadFilters (e.g., if coming directly to Excel handler)
            if (string.IsNullOrEmpty(SchoolName))
            {
                if (SchoolId.Value == 0)
                {
                    SchoolName = "All Schools";
                }
                else
                {
                    // A minimal load to get the school name
                    var schoolsList = new List<SelectListItem>();
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


            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Meta Data");

                // Set column widths
                ws.Column(1).Width = 20; // Grade column
                for (int i = 2; i <= 7; i++) ws.Column(i).Width = 22; // Data columns

                // --- Define Styles ---
                // Header colors (from CSS .col-header-*)
                var headerBgColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.FromArgb(44, 62, 80) }, // #2c3e50 (default dark)
                    { "All", XLColor.FromArgb(179, 217, 255) }, // #b3d9ff
                    { "EL", XLColor.FromArgb(255, 179, 217) }, // #ffb3d9
                    { "SWD", XLColor.FromArgb(255, 255, 179) }, // #ffffb3
                    { "AA", XLColor.FromArgb(179, 255, 179) }, // #b3ffb3
                    { "SED", XLColor.FromArgb(255, 179, 102) }, // #ffb366
                    { "HISP", XLColor.FromArgb(255, 230, 179) } // #ffe6b3
                };

                var headerFontColors = new Dictionary<string, XLColor>
                {
                    { "Grade", XLColor.White },
                    { "All", XLColor.FromArgb(0, 61, 153) },   // #003d99
                    { "EL", XLColor.FromArgb(153, 0, 51) },  // #990033
                    { "SWD", XLColor.FromArgb(153, 102, 0) }, // #996600
                    { "AA", XLColor.FromArgb(0, 77, 0) },    // #004d00
                    { "SED", XLColor.FromArgb(102, 68, 0) },  // #664400
                    { "HISP", XLColor.FromArgb(102, 77, 0) }  // #664d00
                };

                // Data cell base colors (light versions of headers, from CSS .data-*)
                // Using 0.2 opacity from CSS, rendered as solid color by blending with white
                var dataBgColors = new Dictionary<string, XLColor>
                {
                    { "All", XLColor.FromArgb(230, 241, 250) }, // rgba(179, 217, 255, 0.2)
                    { "EL", XLColor.FromArgb(250, 230, 241) }, // rgba(255, 179, 217, 0.2)
                    { "SWD", XLColor.FromArgb(250, 250, 230) }, // rgba(255, 255, 179, 0.2)
                    { "AA", XLColor.FromArgb(230, 250, 230) }, // rgba(179, 255, 179, 0.2)
                    { "SED", XLColor.FromArgb(250, 230, 215) }, // rgba(255, 179, 102, 0.2)
                    { "HISP", XLColor.FromArgb(250, 244, 230) }  // rgba(255, 230, 179, 0.2)
                };

                int row = 1;
                int firstHeaderRow = 0; // For freezing panes

                // Header
                var headerCell = ws.Cell(row, 1);
                headerCell.Value = $"{DistrictName} - {SchoolName}";
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Font.FontSize = 14;
                ws.Range(row, 1, row, 7).Merge();
                row += 2;

                // Process each UnitGroup
                foreach (var unitGroup in UnitGroups)
                {
                    var unitCell = ws.Cell(row, 1);
                    unitCell.Value = $"Cycle: {unitGroup.UnitName}";
                    unitCell.Style.Font.Bold = true;
                    unitCell.Style.Fill.BackgroundColor = XLColor.FromArgb(52, 73, 94); // #34495e
                    unitCell.Style.Font.FontColor = XLColor.White;
                    ws.Range(row, 1, row, 7).Merge();
                    row++;

                    foreach (var subjectGroup in unitGroup.SubjectGroups)
                    {
                        // Subject header
                        var subjectCell = ws.Cell(row, 1);
                        subjectCell.Value = $"Subject: {subjectGroup.SubjectName}";
                        subjectCell.Style.Font.Bold = true;
                        subjectCell.Style.Fill.BackgroundColor = XLColor.FromArgb(238, 242, 255); // #eef2ff
                        subjectCell.Style.Font.FontColor = XLColor.FromArgb(44, 62, 80);
                        ws.Range(row, 1, row, 7).Merge();
                        row++;

                        // --- Column headers (Two Rows) ---
                        if (firstHeaderRow == 0) firstHeaderRow = row; // Track first header for freezing
                
                        int headerRow1 = row;
                        int headerRow2 = row + 1;

                        // Row 1: Grade + Demo Headers
                        var gradeHeaderCell = ws.Cell(headerRow1, 1);
                        gradeHeaderCell.Value = "Grade";
                        gradeHeaderCell.Style.Fill.BackgroundColor = headerBgColors["Grade"];
                        gradeHeaderCell.Style.Font.FontColor = headerFontColors["Grade"];
                        gradeHeaderCell.Style.Font.Bold = true;
                        gradeHeaderCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        gradeHeaderCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        gradeHeaderCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                        gradeHeaderCell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));

                        // Merge Grade cell
                        ws.Range(headerRow1, 1, headerRow2, 1).Merge();

                        // Demo Headers
                        Action<int, string, string> setHeader = (col, key, text) =>
                        {
                            var cell = ws.Cell(headerRow1, col);
                            cell.Value = text;
                            cell.Style.Fill.BackgroundColor = headerBgColors[key];
                            cell.Style.Font.FontColor = headerFontColors[key];
                            cell.Style.Font.Bold = true;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            cell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));


                            // Sub-header
                            var subCell = ws.Cell(headerRow2, col);
                            subCell.Value = "% Proficient (Proficient/Tested)";
                            subCell.Style.Fill.BackgroundColor = dataBgColors[key]; // Use light data color for sub-header
                            subCell.Style.Font.Bold = true;
                            subCell.Style.Font.FontColor = headerFontColors[key]; // Use dark demo font color
                            subCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            subCell.Style.Alignment.WrapText = true;
                            subCell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
                            subCell.Style.Border.SetOutsideBorderColor(XLColor.FromArgb(52, 73, 94));
                        };

                        setHeader(2, "All", "All Students");
                        setHeader(3, "EL", "EL");
                        setHeader(4, "SWD", "SWD");
                        setHeader(5, "AA", "AA");
                        setHeader(6, "SED", "SED");
                        setHeader(7, "HISP", "Hispanic");
                
                        ws.Row(headerRow2).Height = 30; // Give space for wrapped text
                        row += 2; // We used two rows
                        // --- End Column Headers ---

                        // Data rows
                        foreach (var bodyRow in subjectGroup.BodyRows)
                        {
                            var gradeCell = ws.Cell(row, 1);
                            gradeCell.Value = bodyRow.RowLabel;
                            gradeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                            gradeCell.Style.Font.Bold = true; // Match .col-core font-weight: 500

                            RenderExcelDataCell(ws, row, 2, bodyRow.All, dataBgColors["All"]);
                            RenderExcelDataCell(ws, row, 3, bodyRow.EL, dataBgColors["EL"]);
                            RenderExcelDataCell(ws, row, 4, bodyRow.SWD, dataBgColors["SWD"]);
                            RenderExcelDataCell(ws, row, 5, bodyRow.AA, dataBgColors["AA"]);
                            RenderExcelDataCell(ws, row, 6, bodyRow.SED, dataBgColors["SED"]);
                            RenderExcelDataCell(ws, row, 7, bodyRow.HISP, dataBgColors["HISP"]);

                            // Style subtotal rows
                            if (bodyRow.Type == RowType.K2Total || bodyRow.Type == RowType.G3PlusTotal)
                            {
                                var subtotalStyle = ws.Range(row, 1, row, 7).Style;
                                subtotalStyle.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250); // #f8f9fa
                                subtotalStyle.Font.Bold = true;
                                subtotalStyle.Border.SetTopBorder(XLBorderStyleValues.Thin);
                                subtotalStyle.Border.SetBottomBorder(XLBorderStyleValues.Thin);
                                subtotalStyle.Border.SetTopBorderColor(XLColor.FromArgb(173, 181, 189)); // #adb5bd
                                subtotalStyle.Border.SetBottomBorderColor(XLColor.FromArgb(173, 181, 189)); // #adb5bd

                                // Style .col-core for subtotal
                                gradeCell.Style.Font.FontColor = XLColor.FromArgb(52, 73, 94); // #34495e
                                gradeCell.Style.Alignment.Indent = 1; // Match padding-left
                            }
                            row++;
                        }

                        // Subject total row
                        if (!subjectGroup.SubjectTotalRow.IsEmpty)
                        {
                            var totalCell = ws.Cell(row, 1);
                            totalCell.Value = subjectGroup.SubjectTotalRow.RowLabel;
                            totalCell.Style.Font.Bold = true;
                            totalCell.Style.Font.FontColor = XLColor.FromArgb(44, 62, 80); // #2c3e50
                            totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                            RenderExcelDataCell(ws, row, 2, subjectGroup.SubjectTotalRow.All, dataBgColors["All"]);
                            RenderExcelDataCell(ws, row, 3, subjectGroup.SubjectTotalRow.EL, dataBgColors["EL"]);
                            RenderExcelDataCell(ws, row, 4, subjectGroup.SubjectTotalRow.SWD, dataBgColors["SWD"]);
                            RenderExcelDataCell(ws, row, 5, subjectGroup.SubjectTotalRow.AA, dataBgColors["AA"]);
                            RenderExcelDataCell(ws, row, 6, subjectGroup.SubjectTotalRow.SED, dataBgColors["SED"]);
                            RenderExcelDataCell(ws, row, 7, subjectGroup.SubjectTotalRow.HISP, dataBgColors["HISP"]);

                            // Style grand total row
                            var totalRowStyle = ws.Range(row, 1, row, 7).Style;
                            totalRowStyle.Fill.BackgroundColor = XLColor.FromArgb(233, 236, 239); // #e9ecef
                            totalRowStyle.Font.Bold = true;
                            totalRowStyle.Border.SetTopBorder(XLBorderStyleValues.Thick);
                            totalRowStyle.Border.SetTopBorderColor(XLColor.FromArgb(52, 73, 94)); // #34495e
                            row++;
                        }
                        row++; // Add space between subject tables
                    }
                }

                // Freeze panes after the first table's header
                if (firstHeaderRow > 0)
                {
                    ws.SheetView.FreezeRows(firstHeaderRow + 1); // Freezes rows 1 through headerRow2
                }

                var filename = $"Meta_Data_{SchoolName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}.xlsx";

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        filename);
                }
            }
        }

        // RenderExcelDataCell - UPDATED FOR CLOSEDXML
        private void RenderExcelDataCell(IXLWorksheet ws, int row, int col, AggData data, XLColor baseDataColor)
        {
            var cell = ws.Cell(row, col);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            if (data.TotalTested > 0)
            {
                cell.Value = $"{data.PctProficient}%\n({data.TotalProficient}/{data.TotalTested})";
                cell.Style.Alignment.WrapText = true;

                // Apply proficiency color (from CSS .proficiency-*)
                var profColor = data.PctProficient >= 75 ? XLColor.FromArgb(212, 237, 218) // .proficiency-high bg
                            : data.PctProficient >= 50 ? XLColor.FromArgb(255, 243, 205) // .proficiency-mid bg
                            : XLColor.FromArgb(248, 215, 218); // .proficiency-low bg
                
                var profFontColor = data.PctProficient >= 75 ? XLColor.FromArgb(21, 87, 36)   // .proficiency-high color
                                : data.PctProficient >= 50 ? XLColor.FromArgb(133, 100, 4)  // .proficiency-mid color
                                : XLColor.FromArgb(114, 28, 36); // .proficiency-low color

                cell.Style.Fill.BackgroundColor = profColor;
                cell.Style.Font.FontColor = profFontColor;
            }
            else
            {
                cell.Value = "—";
                cell.Style.Fill.BackgroundColor = baseDataColor; // Apply the light demo color
            }
        }

        // LoadDistrictInfoAsync
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

        // LoadFiltersAsync
        private async Task LoadFiltersAsync()
        {
            var schools = new List<SelectListItem> { new SelectListItem { Text = "-- Select School --", Value = "" } };

            // Add "All Schools" option for District/Orenda admins
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

                // SchoolAdmins only see their assigned schools
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
                    AvailableSchools = new SelectList(schools, "Value", "Text", SchoolId?.ToString());
                }
            }
        }

        // LoadMetaDataAsync
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
                        Unit,
                        Subject,
                        Grade,
                        DemographicGroup", conn))
                {
                    cmd.Parameters.AddWithValue("@DistrictId", DistrictId!.Value);
                    cmd.Parameters.AddWithValue("@SchoolId", SchoolId!.Value);

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

        // BuildDisplayGroups
        private void BuildDisplayGroups()
        {
            UnitGroups = RawDataRows
                .GroupBy(r => r.Unit)
                .OrderBy(g => g.Key)
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

                            // --- K-2 Group ---
                            var k2_data = allSubjectData.Where(s => s.GradeGroup == "K-2").ToList();
                            var k2_GradeRows = k2_data
                                .GroupBy(g => g.Grade)
                                .OrderBy(g => g.Key)
                                .Select(gradeData =>
                                {
                                    var label = gradeData.Key == 0 ? "K" : $"Grade {gradeData.Key}";
                                    var row = PivotDemographics(gradeData, label);
                                    row.Type = RowType.Grade;
                                    return row;
                                })
                                .ToList();

                            bodyRows.AddRange(k2_GradeRows);

                            var k2_TotalRow = PivotDemographics(k2_data, "K-2 Total");
                            k2_TotalRow.Type = RowType.K2Total;
                            if (!k2_TotalRow.IsEmpty)
                            {
                                bodyRows.Add(k2_TotalRow);
                            }

                            // --- 3+ Group ---
                            var g3Plus_data = allSubjectData.Where(s => s.GradeGroup == "3+").ToList();
                            var g3Plus_GradeRows = g3Plus_data
                                .GroupBy(g => g.Grade)
                                .OrderBy(g => g.Key)
                                .Select(gradeData =>
                                {
                                    var label = $"Grade {gradeData.Key}";
                                    var row = PivotDemographics(gradeData, label);
                                    row.Type = RowType.Grade;
                                    return row;
                                })
                                .ToList();

                            bodyRows.AddRange(g3Plus_GradeRows);

                            var g3Plus_TotalRow = PivotDemographics(g3Plus_data, "3+ Total");
                            g3Plus_TotalRow.Type = RowType.G3PlusTotal;
                            if (!g3Plus_TotalRow.IsEmpty)
                            {
                                bodyRows.Add(g3Plus_TotalRow);
                            }

                            // Set the final lists
                            subjectDisplayGroup.BodyRows = bodyRows;
                            subjectDisplayGroup.SubjectTotalRow = PivotDemographics(allSubjectData, "Subject Total");
                            subjectDisplayGroup.SubjectTotalRow.Type = RowType.SubjectTotal;

                            return subjectDisplayGroup;
                        }).ToList()
                }).ToList();
        }

        // PivotDemographics
        private MetaDisplayRow PivotDemographics(IEnumerable<MetaDataRow> rows, string rowLabel)
        {
            var row = new MetaDisplayRow { RowLabel = rowLabel };
            var groups = rows
                .GroupBy(r => r.DemographicGroup)
                .ToDictionary(g => g.Key, g => new AggData
                {
                    TotalTested = g.Sum(x => x.TotalTested),
                    TotalProficient = g.Sum(x => x.TotalProficient)
                });

            row.All = groups.GetValueOrDefault("All", new AggData());
            row.EL = groups.GetValueOrDefault("EL", new AggData());
            row.SWD = groups.GetValueOrDefault("SWD", new AggData());
            row.AA = groups.GetValueOrDefault("AA", new AggData());
            row.SED = groups.GetValueOrDefault("SED", new AggData());
            row.HISP = groups.GetValueOrDefault("HISP", new AggData());
            return row;
        }

        // BuildBreadcrumbs
        private void BuildBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Curriculum Alignment", Url = "/CurriculumAlignment/Index" },
                new BreadcrumbItem { Title = DistrictName, Url = null },
                new BreadcrumbItem { Title = "Meta Data", Url = null }
            };
        }
    }

    // ###############################################################
    // VIEW MODELS
    // ###############################################################

    public enum RowType { Grade, K2Total, G3PlusTotal, SubjectTotal }

    public class MetaDataRow
    {
        public string Unit { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public int Grade { get; set; }
        public string GradeGroup { get; set; } = string.Empty;
        public string DemographicGroup { get; set; } = string.Empty;
        public int TotalEnrolled { get; set; }
        public int TotalTested { get; set; }
        public int TotalProficient { get; set; }
    }

    public class MetaDisplayRow
    {
        public string RowLabel { get; set; } = string.Empty;
        public AggData All { get; set; } = new AggData();
        public AggData EL { get; set; } = new AggData();
        public AggData SWD { get; set; } = new AggData();
        public AggData AA { get; set; } = new AggData();
        public AggData SED { get; set; } = new AggData();
        public AggData HISP { get; set; } = new AggData();
        public RowType Type { get; set; } = RowType.Grade;

        public bool IsEmpty =>
            All.TotalTested == 0 &&
            EL.TotalTested == 0 &&
            SWD.TotalTested == 0 &&
            AA.TotalTested == 0 &&
            SED.TotalTested == 0 &&
            HISP.TotalTested == 0;
    }

    public class AggData
    {
        public int TotalTested { get; set; }
        public int TotalProficient { get; set; }

        public decimal PctProficient => TotalTested > 0
            ? Math.Round(100m * TotalProficient / TotalTested, 2)
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