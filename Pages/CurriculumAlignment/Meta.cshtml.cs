using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using ORSV2.Models;

namespace ORSV2.Pages.CurriculumAlignment
{
    public class MetaModel : PageModel
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MetaModel> _logger;
        
        [BindProperty(SupportsGet = true)]
        public int? SchoolId { get; set; }
        
        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }
        
        public SelectList AvailableSchools { get; set; } = new SelectList(new List<SelectListItem>());
        public List<MetaTableRow> TableRows { get; set; } = new List<MetaTableRow>();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new List<BreadcrumbItem>();
        public string DistrictName { get; set; } = string.Empty;

        public MetaModel(IConfiguration config, ILogger<MetaModel> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            // District must be provided
            if (DistrictId == 0)
            {
                return;
            }

            await LoadDistrictInfoAsync();
            await LoadFiltersAsync();
            
            if (SchoolId.HasValue)
            {
                await LoadMetaDataAsync();
                BuildBreadcrumbs();
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
                    
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            DistrictName = reader["Name"].ToString() ?? string.Empty;
                        }
                    }
                }
            }
        }

        private async Task LoadFiltersAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                
                // Schools for this district
                using (var cmd = new SqlCommand(
                    "SELECT DISTINCT Id, Name FROM [dbo].[Schools] WHERE DistrictId = @districtId AND Inactive = 0 ORDER BY Name", conn))
                {
                    cmd.Parameters.AddWithValue("@districtId", DistrictId);
                    
                    var schools = new List<SelectListItem> { new SelectListItem { Text = "-- Select School --", Value = "" } };
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

        private async Task LoadMetaDataAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                
                // Aggregate ALL batches for this school, grouped by subject/grade/demographic
                using (var cmd = new SqlCommand(
                    @"SELECT 
                        school_id, 
                        subject_norm, 
                        grade, 
                        demographic_group, 
                        SUM(total_enrolled) AS total_enrolled,
                        SUM(total_tested) AS total_tested, 
                        SUM(total_proficient) AS total_proficient
                      FROM [dbo].[MetaAggregation]
                      WHERE school_id = @schoolId
                      GROUP BY school_id, subject_norm, grade, demographic_group
                      ORDER BY subject_norm, grade, demographic_group", conn))
                {
                    cmd.Parameters.AddWithValue("@schoolId", SchoolId!.Value);
                    
                    TableRows = new List<MetaTableRow>();
                    
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var subject = reader["subject_norm"]?.ToString() ?? string.Empty;
                            var demographicGroup = reader["demographic_group"]?.ToString() ?? string.Empty;
                            
                            var row = new MetaTableRow
                            {
                                SchoolId = (int)reader["school_id"],
                                Subject = subject,
                                Grade = reader["grade"] != System.DBNull.Value ? (int)reader["grade"] : 0,
                                DemographicGroup = demographicGroup,
                                TotalEnrolled = (int)reader["total_enrolled"],
                                TotalTested = (int)reader["total_tested"],
                                TotalProficient = (int)reader["total_proficient"]
                            };
                            
                            TableRows.Add(row);
                        }
                    }
                }
            }
        }

        private void BuildBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Curriculum Alignment", Url = "/CurriculumAlignment/Index" },
                new BreadcrumbItem { Title = "Meta Data", Url = null }  // null = current page
            };
        }
    }

    public class MetaTableRow
    {
        public int SchoolId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public int Grade { get; set; }
        public string DemographicGroup { get; set; } = string.Empty;
        public int TotalEnrolled { get; set; }
        public int TotalTested { get; set; }
        public int TotalProficient { get; set; }
        
        // Calculated properties (no storage, calculated at render time)
        public decimal PctProficient => TotalTested > 0 
            ? Math.Round(100m * TotalProficient / TotalTested, 2) 
            : 0m;

        public decimal ParticipationRate => TotalEnrolled > 0 
            ? Math.Round(100m * TotalTested / TotalEnrolled, 2) 
            : 0m;
    }
}