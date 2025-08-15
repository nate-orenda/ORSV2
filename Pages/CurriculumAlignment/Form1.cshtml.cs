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
        public class ColDef { public string Code { get; set; } = ""; public string ShortStatement { get; set; } = ""; }
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

        public List<SelectListItem> AvailableUnitCycles { get; private set; } = new();
        public List<SelectListItem> AvailableBatches { get; private set; } = new();
        public List<SelectListItem> AvailableSchools { get; private set; } = new();
        public List<SelectListItem> AvailableTeachers { get; private set; } = new();
        
        public List<ColDef> Columns { get; private set; } = new();
        public List<RowVm> Rows { get; private set; } = new();

        private readonly IConfiguration _config;
        public Form1Model(IConfiguration config) => _config = config;

        public async Task OnGet()
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

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
                AvailableSchools = await GetSchoolsByAssessmentAsync(conn, DistrictId.Value, Guid.Parse(BatchId));
            }
            if (DistrictId.HasValue && SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId))
            {
                AvailableTeachers = await GetTeachersByAssessmentAsync(conn, DistrictId.Value, SchoolId.Value, Guid.Parse(BatchId));
            }

            // UPDATED: Reverted to load as soon as a school is selected.
            if (SchoolId.HasValue && !string.IsNullOrWhiteSpace(BatchId) && Guid.TryParse(BatchId, out var bid))
            {
                await LoadMatrixData(conn, bid);
            }
        }

        // --- Data Fetching Helper Methods ---

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
        
        private async Task<List<SelectListItem>> GetAssessmentsByUnitCycleAsync(SqlConnection conn, int districtId, string unitCycle)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select an assessment...", "") };
            using var cmd = new SqlCommand("dbo.GetAssessmentsByUnitCycle", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@UnitCycle", unitCycle);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["batch_id"].ToString(), Text = rdr["test_id"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetSchoolsByAssessmentAsync(SqlConnection conn, int districtId, Guid batchId)
        {
            var list = new List<SelectListItem> { new SelectListItem("Select a school...", "") };
            using var cmd = new SqlCommand("dbo.GetSchoolsByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["Id"].ToString(), Text = rdr["Name"].ToString() });
            }
            return list;
        }

        private async Task<List<SelectListItem>> GetTeachersByAssessmentAsync(SqlConnection conn, int districtId, int schoolId, Guid batchId)
        {
            var list = new List<SelectListItem> { new SelectListItem("All Teachers", "") };
            using var cmd = new SqlCommand("dbo.GetTeachersByAssessment", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@DistrictId", districtId);
            cmd.Parameters.AddWithValue("@SchoolId", schoolId);
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new SelectListItem { Value = rdr["StaffID"].ToString(), Text = rdr["TeacherName"].ToString() });
            }
            return list;
        }

        private async Task LoadMatrixData(SqlConnection conn, Guid batchId)
        {
            using var cmd = new SqlCommand("dbo.GetAssessmentBatchMatrix", conn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@BatchId", batchId);
            if (DistrictId.HasValue) cmd.Parameters.AddWithValue("@DistrictId", DistrictId.Value);
            if (SchoolId.HasValue) cmd.Parameters.AddWithValue("@SchoolId", SchoolId.Value);
            if (TeacherId.HasValue) cmd.Parameters.AddWithValue("@TeacherId", TeacherId.Value);

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
    }
}