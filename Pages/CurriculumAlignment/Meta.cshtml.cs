using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ORSV2.Pages.CurriculumAlignment
{
    public class MetaModel : PageModel
    {
        private readonly IConfiguration _config;
        private readonly ILogger<MetaModel> _logger;
        
        [BindProperty(SupportsGet = true)]
        public int? SchoolId { get; set; }
        
        [BindProperty(SupportsGet = true)]
        public string? BatchId { get; set; }
        
        public SelectList AvailableSchools { get; set; } = new SelectList(new List<SelectListItem>());
        public SelectList AvailableBatches { get; set; } = new SelectList(new List<SelectListItem>());
        public List<MetaTableRow> TableRows { get; set; } = new List<MetaTableRow>();
        public List<ChartDataPoint> ChartData { get; set; } = new List<ChartDataPoint>();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new List<BreadcrumbItem>();

        public MetaModel(IConfiguration config, ILogger<MetaModel> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            await LoadFiltersAsync();
            
            if (SchoolId.HasValue && !string.IsNullOrEmpty(BatchId))
            {
                await LoadMetaDataAsync();
                BuildBreadcrumbs();
            }
        }

        private async Task LoadFiltersAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                
                // Schools for dropdown
                using (var cmd = new SqlCommand(
                    "SELECT DISTINCT school_id, school_name FROM [dbo].[schools] ORDER BY school_name", conn))
                {
                    var schools = new List<SelectListItem> { new SelectListItem { Text = "-- Select School --", Value = "" } };
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            schools.Add(new SelectListItem
                            {
                                Text = reader["school_name"].ToString(),
                                Value = reader["school_id"].ToString()
                            });
                        }
                    }
                    AvailableSchools = new SelectList(schools, "Value", "Text", SchoolId?.ToString());
                }
                
                // Batches for dropdown
                using (var cmd = new SqlCommand(
                    "SELECT DISTINCT batch_id FROM [dbo].[batch_student_snapshot] ORDER BY batch_id DESC", conn))
                {
                    var batches = new List<SelectListItem>();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            batches.Add(new SelectListItem
                            {
                                Text = reader["batch_id"].ToString(),
                                Value = reader["batch_id"].ToString()
                            });
                        }
                    }
                    AvailableBatches = new SelectList(batches, "Value", "Text", BatchId);
                }
            }
        }

        private async Task LoadMetaDataAsync()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();
                
                using (var cmd = new SqlCommand(
                    @"SELECT school_id, subject_norm, grade, demographic_group, 
                             total_enrolled, total_tested, total_proficient
                      FROM [dbo].[MetaAggregation]
                      WHERE batch_id = @batchId 
                        AND (@schoolId = 0 OR school_id = @schoolId)
                      ORDER BY subject_norm, grade, demographic_group", conn))
                {
                    cmd.Parameters.AddWithValue("@batchId", BatchId);
                    cmd.Parameters.AddWithValue("@schoolId", SchoolId ?? 0);
                    
                    TableRows = new List<MetaTableRow>();
                    ChartData = new List<ChartDataPoint>();
                    
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
                            
                            // For chart: only "All" demographic for histogram by grade
                            if (row.DemographicGroup == "All")
                            {
                                ChartData.Add(new ChartDataPoint
                                {
                                    Grade = row.Grade,
                                    Subject = subject,
                                    PctProficient = row.PctProficient,
                                    DemographicLabel = $"Grade {row.Grade}"
                                });
                            }
                        }
                    }
                }
            }
        }

        private void BuildBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Label = "Curriculum Alignment", Url = "/CurriculumAlignment/Index" },
                new BreadcrumbItem { Label = "Meta Data", Url = "/CurriculumAlignment/Meta", IsActive = true }
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

    public class ChartDataPoint
    {
        public int Grade { get; set; }
        public string Subject { get; set; } = string.Empty;
        public decimal PctProficient { get; set; }
        public string DemographicLabel { get; set; } = string.Empty;
    }

    public class BreadcrumbItem
    {
        public string Label { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}