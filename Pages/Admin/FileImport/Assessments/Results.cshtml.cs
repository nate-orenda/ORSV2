using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace ORSV2.Pages.Admin.FileImport.Assessments
{
    public class ResultsModel : PageModel
    {
        public class RowVm
        {
            public string BatchId { get; set; } = "";
            public int? StudentId { get; set; }                  // Orenda StuId (nullable)
            public string LocalStudentId { get; set; } = "";     // string to preserve leading zeros / NVARCHAR
            public string TestId { get; set; } = "";
            public string HumanCodingScheme { get; set; } = "";
            public decimal? Points { get; set; }
            public bool HasStandard { get; set; }
        }

        public class SummaryVm
        {
            public string HumanCodingScheme { get; set; } = "";
            public int StudentCount { get; set; }
            public decimal? AvgPoints { get; set; }
            public decimal? MinPoints { get; set; }
            public decimal? MaxPoints { get; set; }
            public bool HasStandard { get; set; }
        }

        [BindProperty(SupportsGet = true)] public string? BatchId { get; set; }
        [BindProperty(SupportsGet = true)] public string? TestId { get; set; }
        [BindProperty(SupportsGet = true)] public string? StudentId { get; set; }
        [BindProperty(SupportsGet = true)] public string? LocalId { get; set; }  // filter by local id (string)
        [BindProperty(SupportsGet = true)] public string? ViewMode { get; set; } // "summary" or rows

        public List<RowVm> Rows { get; private set; } = new();
        public List<SummaryVm> SummaryRows { get; private set; } = new();

        private readonly IConfiguration _config;
        public ResultsModel(IConfiguration config) => _config = config;

        public async Task OnGet()
        {
            var connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            if (string.Equals(ViewMode, "summary", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(BatchId))
            {
                var sql = @"
                    SELECT
                        r.human_coding_scheme,
                        COUNT(DISTINCT r.student_id) AS student_count,
                        AVG(TRY_CONVERT(decimal(18,4), r.points)) AS avg_points,
                        MIN(TRY_CONVERT(decimal(18,4), r.points)) AS min_points,
                        MAX(TRY_CONVERT(decimal(18,4), r.points)) AS max_points,
                        CASE WHEN s.id IS NULL THEN 0 ELSE 1 END AS has_standard
                    FROM dbo.assessment_results r
                    LEFT JOIN dbo.standards s
                      ON s.human_coding_scheme = r.human_coding_scheme
                    WHERE r.batch_id = @batch
                    GROUP BY r.human_coding_scheme, CASE WHEN s.id IS NULL THEN 0 ELSE 1 END
                    ORDER BY r.human_coding_scheme;
                ";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@batch", Guid.Parse(BatchId));
                using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    SummaryRows.Add(new SummaryVm
                    {
                        HumanCodingScheme = rdr.GetString(0),
                        StudentCount = rdr.GetInt32(1),
                        AvgPoints = ReadDecimalFlexible(rdr, 2),
                        MinPoints = ReadDecimalFlexible(rdr, 3),
                        MaxPoints = ReadDecimalFlexible(rdr, 4),
                        HasStandard = rdr.GetInt32(5) == 1
                    });
                }
                return;
            }

            var where = new List<string>();
            using var cmd2 = new SqlCommand();
            cmd2.Connection = conn;

            if (!string.IsNullOrEmpty(BatchId))
            {
                where.Add("r.batch_id = @batch");
                cmd2.Parameters.AddWithValue("@batch", Guid.Parse(BatchId));
            }
            if (!string.IsNullOrEmpty(TestId))
            {
                where.Add("r.test_id = @test");
                cmd2.Parameters.AddWithValue("@test", TestId);
            }
            if (!string.IsNullOrEmpty(LocalId))
            {
                // Compare as NVARCHAR so it works whether the column is INT or NVARCHAR and preserves leading zeros
                where.Add("CAST(r.local_student_id AS nvarchar(64)) = @lid");
                cmd2.Parameters.Add("@lid", SqlDbType.NVarChar, 64).Value = LocalId.Trim();
            }
            if (!string.IsNullOrEmpty(StudentId) && int.TryParse(StudentId, out var sid))
            {
                where.Add("r.student_id = @sid");
                cmd2.Parameters.AddWithValue("@sid", sid);
            }

            var filter = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

            cmd2.CommandText = $@"
                SELECT TOP 500
                    CONVERT(nvarchar(36), r.batch_id)                           AS batch_id,
                    r.student_id,                                               -- could be NULL
                    CAST(r.local_student_id AS nvarchar(64))                    AS local_id,
                    r.test_id,
                    r.human_coding_scheme,
                    TRY_CONVERT(decimal(18,4), r.points)                        AS points,
                    CASE WHEN s.id IS NULL THEN 0 ELSE 1 END                    AS has_standard
                FROM dbo.assessment_results r
                LEFT JOIN dbo.standards s
                  ON s.human_coding_scheme = r.human_coding_scheme
                {filter}
                ORDER BY r.imported_at DESC, r.id DESC;
            ";

            using var rdr2 = await cmd2.ExecuteReaderAsync();
            while (await rdr2.ReadAsync())
            {
                Rows.Add(new RowVm
                {
                    BatchId = rdr2.GetString(0),
                    StudentId = ReadIntFlexible(rdr2, 1),    // safe even if DB type changes
                    LocalStudentId = rdr2.GetString(2),      // we CAST to NVARCHAR in SQL -> always GetString
                    TestId = rdr2.GetString(3),
                    HumanCodingScheme = rdr2.GetString(4),
                    Points = ReadDecimalFlexible(rdr2, 5),
                    HasStandard = (rdr2.IsDBNull(6) ? 0 : rdr2.GetInt32(6)) == 1
                });
            }
        }

        // ---------- helpers ----------
        private static int? ReadIntFlexible(SqlDataReader rdr, int ordinal)
        {
            if (rdr.IsDBNull(ordinal)) return null;
            var t = rdr.GetFieldType(ordinal);
            if (t == typeof(int)) return rdr.GetInt32(ordinal);
            if (t == typeof(long)) return checked((int)rdr.GetInt64(ordinal));
            if (t == typeof(decimal)) return (int)Convert.ToDecimal(rdr.GetValue(ordinal));
            if (t == typeof(double)) return (int)Convert.ToDouble(rdr.GetValue(ordinal));
            if (t == typeof(string))
            {
                var s = rdr.GetString(ordinal).Trim();
                return int.TryParse(s, out var v) ? v : (int?)null;
            }
            var obj = rdr.GetValue(ordinal)?.ToString();
            return int.TryParse(obj, out var v2) ? v2 : (int?)null;
        }

        private static decimal? ReadDecimalFlexible(SqlDataReader rdr, int ordinal)
        {
            if (rdr.IsDBNull(ordinal)) return null;
            var t = rdr.GetFieldType(ordinal);
            if (t == typeof(decimal)) return rdr.GetDecimal(ordinal);
            if (t == typeof(double)) return (decimal)rdr.GetDouble(ordinal);
            if (t == typeof(float)) return (decimal)rdr.GetFloat(ordinal);
            var s = rdr.GetValue(ordinal)?.ToString();
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d : (decimal?)null;
        }
    }
}
