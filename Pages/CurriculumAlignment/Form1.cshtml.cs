using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;


namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class Form1Model : PageModel
    {
        // View Model Classes
        public class ColDef { public string Code { get; set; } = ""; public string ShortStatement { get; set; } = ""; }
        public class RowVm { public string StudentName { get; set; } = ""; public string LocalId { get; set; } = ""; public List<decimal?> Points { get; set; } = new(); public int TotalPassed { get; set; } }

        // Bound Properties for Filters
        [BindProperty(SupportsGet = true)] public int? DistrictId { get; set; }
        [BindProperty(SupportsGet = true)] public int? SchoolId { get; set; }
        [BindProperty(SupportsGet = true)] public string? UnitCycle { get; set; }
        [BindProperty(SupportsGet = true)] public string? BatchId { get; set; }

        // Properties for Dropdown Lists
        public List<SelectListItem> AvailableSchools { get; private set; } = new();
        public List<SelectListItem> AvailableUnitCycles { get; private set; } = new();
        public List<SelectListItem> AvailableBatches { get; private set; } = new();

        // Properties for Matrix Data
        public List<ColDef> Columns { get; private set; } = new();
        public List<RowVm> Rows { get; private set; } = new();

        private readonly IConfiguration _config;
        public Form1Model(IConfiguration config) => _config = config;

        public async Task OnGet()
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // --- Step 1: Populate Cascading Dropdowns ---
            if (DistrictId.HasValue)
            {
                AvailableSchools = await GetSchoolsAsync(conn, DistrictId.Value);
            }
            if (DistrictId.HasValue && SchoolId.HasValue)
            {
                AvailableUnitCycles = await GetUnitCyclesAsync(conn, DistrictId.Value, SchoolId.Value);
            }
            if (DistrictId.HasValue && SchoolId.HasValue && !string.IsNullOrWhiteSpace(UnitCycle))
            {
                AvailableBatches = await GetAssessmentsAsync(conn, DistrictId.Value, SchoolId.Value, UnitCycle);
            }

            // --- Step 2: Fetch Matrix Data if an Assessment is selected ---
            if (!string.IsNullOrWhiteSpace(BatchId) && Guid.TryParse(BatchId, out var bid))
            {
                await LoadMatrixData(conn, bid);
            }
        }

        // --- Data Fetching Helper Methods ---

        private async Task<List<SelectListItem>> GetSchoolsAsync(SqlConnection conn, int districtId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a school...", "") };
            using var cmd = new SqlCommand("dbo.GetSchoolsByDistrict", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                // UPDATED column names to match the new stored procedure
                list.Add(new SelectListItem { Value = rdr["Id"].ToString(), Text = rdr["Name"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetUnitCyclesAsync(SqlConnection conn, int districtId, int schoolId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a unit/cycle...", "") };
            using var cmd = new SqlCommand("dbo.GetUnitCyclesBySchool", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["unit_cycle"].ToString(), Text = rdr["unit_cycle"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetAssessmentsAsync(SqlConnection conn, int districtId, int schoolId, string unitCycle)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select an assessment...", "") };
            using var cmd = new SqlCommand("dbo.GetAssessmentsByFilter", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
            cmd.Parameters.AddWithValue("@UnitCycle", unitCycle);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                // UPDATED column names to match the new stored procedure
                list.Add(new SelectListItem { Value = rdr["batch_id"].ToString(), Text = rdr["test_id"].ToString() });
            }
            return list;
        }

        private async Task LoadMatrixData(SqlConnection conn, Guid batchId)
        {
            using var cmd = new SqlCommand("dbo.GetAssessmentBatchMatrix", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (DistrictId.HasValue) cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
            if (SchoolId.HasValue) cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                Columns.Add(new ColDef { Code = rdr.GetString(0), ShortStatement = rdr.IsDBNull(1) ? "" : rdr.GetString(1) });
            }
            if (await rdr.NextResultAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var row = new RowVm { StudentName = rdr.IsDBNull(0) ? "" : rdr.GetString(0), LocalId = rdr.IsDBNull(1) ? "" : rdr.GetString(1) };
                    for (int i = 0; i < Columns.Count; i++)
                    {
                        var ord = 2 + i;
                        row.Points.Add(rdr.IsDBNull(ord) ? null : (decimal?)Convert.ChangeType(rdr.GetValue(ord), typeof(decimal)));
                    }
                    var totalPassedOrdinal = 2 + Columns.Count;
                    row.TotalPassed = rdr.IsDBNull(totalPassedOrdinal) ? 0 : Convert.ToInt32(rdr.GetValue(totalPassedOrdinal));
                    Rows.Add(row);
                }
            }
        }
    }
}