using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;


namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class Form1Model : PageModel
    {
        public class ColDef
        {
            public string Code { get; set; } = "";
            public string ShortStatement { get; set; } = "";
        }

        public class RowVm
        {
            public string StudentName { get; set; } = "";
            public string LocalId { get; set; } = "";
            public List<decimal?> Points { get; set; } = new(); // aligns to Columns order
            public int TotalPassed { get; set; }
        }

        [BindProperty(SupportsGet = true)]
        public string? BatchId { get; set; }

        public List<ColDef> Columns { get; private set; } = new();
        public List<RowVm> Rows { get; private set; } = new();

        private readonly IConfiguration _config;
        public Form1Model(IConfiguration config) => _config = config;

        public async Task OnGet()
        {
            if (string.IsNullOrWhiteSpace(BatchId))
                return;

            if (!Guid.TryParse(BatchId, out var bid))
                return;

            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            using var cmd = new SqlCommand("dbo.GetAssessmentBatchMatrix", conn)
            {
                CommandType = CommandType.StoredProcedure
            };
            cmd.Parameters.AddWithValue("@BatchId", bid);

            using var rdr = await cmd.ExecuteReaderAsync();

            // 1) Columns mapping
            var codeOrder = new List<string>();
            while (await rdr.ReadAsync())
            {
                var code = rdr.GetString(0);
                var shortStmt = rdr.IsDBNull(1) ? "" : rdr.GetString(1);
                Columns.Add(new ColDef { Code = code, ShortStatement = shortStmt });
                codeOrder.Add(code);
            }

            // 2) Matrix rows
            if (await rdr.NextResultAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var row = new RowVm
                    {
                        StudentName = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        LocalId = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                    };

                    // Dynamic code columns start at ordinal 2 and run for Columns.Count
                    for (int i = 0; i < Columns.Count; i++)
                    {
                        var ord = 2 + i;
                        if (rdr.IsDBNull(ord)) { row.Points.Add(null); }
                        else
                        {
                            // points are returned as decimal via TRY_CONVERT in SQL
                            row.Points.Add((decimal)Convert.ChangeType(rdr.GetValue(ord), typeof(decimal)));
                        }
                    }

                    // TotalPassed is the last column
                    var totalPassedOrdinal = 2 + Columns.Count;
                    row.TotalPassed = rdr.IsDBNull(totalPassedOrdinal) ? 0 : Convert.ToInt32(rdr.GetValue(totalPassedOrdinal));

                    Rows.Add(row);
                }
            }
        }
    }
}
