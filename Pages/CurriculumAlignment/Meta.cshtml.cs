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

            if (UserDistrictId.HasValue)
            {
                DistrictId = UserDistrictId.Value;
            }
            
            if (DistrictId == null || DistrictId == 0)
            {
                return Page();
            }

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync(); // Now adds "All Schools"
            
            // This now works for SchoolId=null, SchoolId=0 (All), or SchoolId=123 (Specific)
            if (SchoolId.HasValue) 
            {
                await LoadMetaDataAsync(); // Now handles "All Schools"
                BuildDisplayGroups();
                
                // Set SchoolName for display
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
        
        // OnGetExcelAsync
        public async Task<IActionResult> OnGetExcelAsync()
        {
            InitializeUserDataScope();
            if (!IsSchoolAdmin && !IsDistrictAdmin && !IsOrendaUser) return Forbid();
            if (UserDistrictId.HasValue) DistrictId = UserDistrictId.Value;

            if (!SchoolId.HasValue || !DistrictId.HasValue)
            {
                return RedirectToPage();
            }

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync();
            await LoadMetaDataAsync(); 

            var schoolNameForFile = "Selected_School";
            if (SchoolId.Value == 0)
            {
                schoolNameForFile = "All_Schools";
            }
            else
            {
                schoolNameForFile = (AvailableSchools.FirstOrDefault(s => s.Value == SchoolId.ToString())?.Text ?? "School").Replace(" ", "_");
            }
            var fileName = $"Meta_Data_{schoolNameForFile}.csv";

            var sb = new StringBuilder();
            sb.AppendLine("School,Unit,Subject,Grade,GradeGroup,DemographicGroup,TotalEnrolled,TotalTested,TotalProficient");

            foreach (var row in RawDataRows)
            {
                sb.AppendLine(
                    $"{schoolNameForFile}," +
                    $"{CsvEscape(row.Unit)}," +
                    $"{CsvEscape(row.Subject)}," +
                    $"{row.Grade}," +
                    $"{CsvEscape(row.GradeGroup)}," +
                    $"{CsvEscape(row.DemographicGroup)}," +
                    $"{row.TotalEnrolled}," +
                    $"{row.TotalTested}," +
                    $"{row.TotalProficient}"
                );
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
        }

        private static string CsvEscape(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
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

        // --- UPDATED METHOD ---
        // Adds "All Schools" option for District Admins or higher
        private async Task LoadFiltersAsync()
        {
            var schools = new List<SelectListItem> { new SelectListItem { Text = "-- Select School --", Value = "" } };
            
            // [NEW] Add "All Schools" option for District/Orenda admins
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

        // --- UPDATED METHOD ---
        // Handles SchoolId = 0 for "All Schools"
        private async Task LoadMetaDataAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                
                // [FINAL QUERY]
                // - REMOVED the JOIN to [dbo].[Schools]
                // - Filters directly on ma.district_id = @DistrictId
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
                                .Select(gradeData => {
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
                                .Select(gradeData => {
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
                .ToDictionary(g => g.Key, g => new AggData {
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
    // VIEW MODELS (Unchanged from last time)
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