using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Data.Common;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class Form1oldModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public Form1oldModel(ApplicationDbContext context)
        {
            _context = context;
        }

        #region Page Properties
        [BindProperty(SupportsGet = true)]
        public int? SelectedDistrictId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedUnit { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTestId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedSchoolId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTeacherId { get; set; }

        public SelectList Districts { get; set; } = new(new List<object>());
        public SelectList Units { get; set; } = new(new List<object>());
        public SelectList TestNames { get; set; } = new(new List<object>());
        public SelectList Schools { get; set; } = new(new List<object>());
        public SelectList Teachers { get; set; } = new(new List<object>());
        #endregion

        public async Task OnGetAsync()
        {
            var userDistrictIds = await GetUserDistrictIdsAsync();

            var districts = await _context.VwStudentResultsClasses
                .Where(v => userDistrictIds.Contains(v.DistrictId))
                .Select(v => new { v.DistrictId })
                .Distinct()
                .Join(_context.Districts, v => v.DistrictId, d => d.Id, (v, d) => new { d.Id, d.Name })
                .OrderBy(d => d.Name)
                .ToListAsync();

            Districts = new SelectList(districts, "Id", "Name", SelectedDistrictId);
        }

        #region AJAX Handlers
        public async Task<JsonResult> OnGetUnitsAsync(int districtId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var units = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId)
                .Select(v => v.Unit).Distinct().OrderBy(u => u)
                .Select(u => new { value = u, text = "Unit " + u })
                .ToListAsync();

            return new JsonResult(units);
        }

        public async Task<JsonResult> OnGetTestNamesAsync(int districtId, int unit)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var tests = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId && v.Unit == unit)
                .Select(v => new { value = v.TestId, text = v.TestName }).Distinct()
                .OrderBy(t => t.text)
                .ToListAsync();

            return new JsonResult(tests);
        }

        public async Task<JsonResult> OnGetSchoolsAsync(int districtId, string testId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var accessibleSchoolIds = await GetUserSchoolIdsAsync();

            var schools = await _context.VwStudentResultsClasses
                .Where(v =>
                    v.DistrictId == districtId
                    && v.TestId == testId
                    && v.SchoolId.HasValue
                    && accessibleSchoolIds.Contains(v.SchoolId.Value))
                .Join(_context.Schools, v => v.SchoolId, s => s.Id, (v, s) => new { s.Id, s.Name })
                .Distinct()
                .OrderBy(s => s.Name)
                .Select(s => new { value = s.Id, text = s.Name })
                .ToListAsync();

            return new JsonResult(schools);
        }

        public async Task<JsonResult> OnGetTeachersAsync(int districtId, string testId, int schoolId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };
            if (!await HasSchoolAccessAsync(schoolId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var teachers = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId && v.TestId == testId && v.SchoolId == schoolId && !string.IsNullOrEmpty(v.TeacherId))
                .Select(v => new { Id = v.TeacherId, Name = v.TeacherLastName + ", " + v.TeacherFirstName })
                .Distinct()
                .OrderBy(t => t.Name)
                .Select(t => new { value = t.Id, text = t.Name })
                .ToListAsync();

            return new JsonResult(teachers);
        }

        // *** FAST PATH: aggregated per-student results + headers in a single DB roundtrip ***
        public async Task<JsonResult> OnGetStudentResultsAsync(int districtId, string testId, int? schoolId, string? teacherId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new { }) { StatusCode = 403 };
            if (schoolId.HasValue && !await HasSchoolAccessAsync(schoolId.Value)) return new JsonResult(new { }) { StatusCode = 403 };

            // Treat empty string as "all teachers"
            string? teacherIdParam = string.IsNullOrWhiteSpace(teacherId) ? null : teacherId;

            var (headers, aggRows) = await LoadHeadersAndAggregatesSqlAsync(districtId, testId, schoolId, teacherIdParam);
            if (aggRows.Count == 0) return new JsonResult(new StudentResultTableViewModel());

            var teacherGroups = aggRows
                .GroupBy(r => r.TeacherName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TeacherGroup
                {
                    TeacherName = g.Key,
                    ClassGroups = g.GroupBy(r => $"{r.CourseTitle} - {r.Period}")
                                   .OrderBy(cg => cg.Key, StringComparer.OrdinalIgnoreCase)
                                   .Select(cg => new ClassGroup
                                   {
                                       CourseTitle = cg.Key,
                                       StudentRows = cg.OrderBy(s => s.StudentName, StringComparer.OrdinalIgnoreCase)
                                                       .Select(s => new StudentResultRow
                                                       {
                                                           StudentName          = s.StudentName,
                                                           LocalStudentId       = s.LocalStudentId,
                                                           TotalStandardsPassed = s.TotalStandardsPassed,
                                                           StandardScores       = s.StandardScores,
                                                           _TeacherNameInternal = g.Key,
                                                           _CourseTitleInternal = s.CourseTitle,
                                                           _PeriodInternal      = s.Period
                                                       })
                                                       .ToList()
                                   })
                                   .ToList()
                })
                .ToList();

            return new JsonResult(new StudentResultTableViewModel
            {
                Headers = headers,
                TeacherGroups = teacherGroups
            });
        }
        #endregion

        #region Authorization Helpers
        private async Task<List<int>> GetUserDistrictIdsAsync()
        {
            if (User.IsInRole("OrendaAdmin"))
            {
                return await _context.Districts.Select(d => d.Id).ToListAsync();
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return new List<int>();

            var userDistrictId = await _context.Users
                .Where(u => u.Id == userId && u.DistrictId.HasValue)
                .Select(u => u.DistrictId!.Value).FirstOrDefaultAsync();

            return userDistrictId > 0 ? new List<int> { userDistrictId } : new List<int>();
        }

        private async Task<bool> HasDistrictAccessAsync(int districtId)
        {
            var userDistrictIds = await GetUserDistrictIdsAsync();
            return userDistrictIds.Contains(districtId);
        }

        private async Task<List<int>> GetUserSchoolIdsAsync()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return new List<int>();

            if (User.IsInRole("OrendaAdmin") || User.IsInRole("DistrictAdmin"))
            {
                var userDistrictIds = await GetUserDistrictIdsAsync();
                return await _context.Schools
                    .Where(s => userDistrictIds.Contains(s.DistrictId))
                    .Select(s => s.Id).ToListAsync();
            }

            return await _context.UserSchools
                .Where(us => us.UserId == userId)
                .Select(us => us.SchoolId).ToListAsync();
        }

        private async Task<bool> HasSchoolAccessAsync(int schoolId)
        {
            var accessibleSchoolIds = await GetUserSchoolIdsAsync();
            return accessibleSchoolIds.Contains(schoolId);
        }
        #endregion

        #region Fast SQL Aggregation Helpers
        private sealed class StudentAggRow
        {
            public string TeacherName { get; set; } = "";
            public string CourseTitle { get; set; } = "";
            public string Period { get; set; } = "";
            public string StudentName { get; set; } = "";
            public string LocalStudentId { get; set; } = "";
            public int TotalStandardsPassed { get; set; }
            public Dictionary<string, int> StandardScores { get; set; } = new();
        }

        /// <summary>
        /// Returns (Headers, AggregatedRows) in one DB call.
        /// Requires SQL Server 2017+ for STRING_AGG. If on 2016, swap in FOR XML / STUFF() pattern.
        /// NOTE: Adjust schema prefix if your view/table schema isn't dbo.
        /// </summary>
        private async Task<(List<StandardHeader> Headers, List<StudentAggRow> Rows)>
            LoadHeadersAndAggregatesSqlAsync(int districtId, string testId, int? schoolId, string? teacherId)
        {
            var sql = @"
-- HEADERS (distinct HCS across current filter)
WITH H AS (
    SELECT DISTINCT v.HumanCodingScheme
    FROM dbo.VwStudentResultsClasses v
    WHERE v.DistrictId = @districtId
      AND v.TestId     = @testId
      AND (@schoolId IS NULL OR v.SchoolId = @schoolId)
      AND (@teacherId IS NULL OR v.TeacherId = @teacherId)
      AND v.HumanCodingScheme IS NOT NULL AND LTRIM(RTRIM(v.HumanCodingScheme)) <> ''
)
SELECT H.HumanCodingScheme,
       COALESCE(S.FullStatement, '') AS FullStatement
FROM H
LEFT JOIN dbo.Standards S ON S.HumanCodingScheme = H.HumanCodingScheme
ORDER BY H.HumanCodingScheme;

-- AGGREGATED PER-STUDENT ROWS
WITH base AS (
    SELECT
        v.DistrictId, v.TestId, v.SchoolId,
        v.TeacherId, v.TeacherFirstName, v.TeacherLastName,
        v.CourseTitle, v.Period,
        v.StudentId, v.FirstName, v.LastName, v.LocalStudentId,
        v.HumanCodingScheme,
        CAST(ISNULL(v.Results, 0) AS int)       AS ScoreInt,
        ISNULL(v.Proficiency, 0)                AS Proficiency
    FROM dbo.VwStudentResultsClasses v
    WHERE v.DistrictId = @districtId
      AND v.TestId     = @testId
      AND (@schoolId IS NULL OR v.SchoolId = @schoolId)
      AND (@teacherId IS NULL OR v.TeacherId = @teacherId)
),
perStd AS (
    -- de-dup in case of multiple rows per (student,hcs), keep max score/proficiency
    SELECT
        b.StudentId,
        b.TeacherLastName, b.TeacherFirstName, b.TeacherId,
        b.CourseTitle, b.Period,
        b.FirstName, b.LastName, b.LocalStudentId,
        b.HumanCodingScheme,
        MAX(b.ScoreInt)       AS ScoreInt,
        MAX(b.Proficiency)    AS Proficiency
    FROM base b
    GROUP BY
        b.StudentId,
        b.TeacherLastName, b.TeacherFirstName, b.TeacherId,
        b.CourseTitle, b.Period,
        b.FirstName, b.LastName, b.LocalStudentId,
        b.HumanCodingScheme
),
perStudent AS (
    SELECT
        TeacherName = CONCAT(ps.TeacherLastName, ', ', ps.TeacherFirstName),
        ps.CourseTitle,
        ps.Period,
        StudentName = CONCAT(ps.LastName, ', ', ps.FirstName),
        LocalStudentId = CONVERT(varchar(50), ps.LocalStudentId),
        TotalStandardsPassed = MAX(ps.Proficiency),
        StandardScoresJson = '{' + STRING_AGG(CONCAT('""', ps.HumanCodingScheme, '"":', CONVERT(varchar(10), ps.ScoreInt)), ',') + '}'
    FROM perStd ps
    GROUP BY
        ps.TeacherLastName, ps.TeacherFirstName,
        ps.CourseTitle, ps.Period,
        ps.LastName, ps.FirstName, ps.LocalStudentId
)
SELECT TeacherName, CourseTitle, Period, StudentName, LocalStudentId, TotalStandardsPassed, StandardScoresJson
FROM perStudent
ORDER BY TeacherName, CourseTitle, Period, StudentName;";

            var headers = new List<StandardHeader>();
            var rows = new List<StudentAggRow>();

            await using var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var p1 = cmd.CreateParameter(); p1.ParameterName = "@districtId"; p1.Value = districtId; cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter(); p2.ParameterName = "@testId";     p2.Value = testId;     cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter(); p3.ParameterName = "@schoolId";   p3.Value = (object?)schoolId ?? DBNull.Value; cmd.Parameters.Add(p3);
            var p4 = cmd.CreateParameter(); p4.ParameterName = "@teacherId";  p4.Value = (object?)teacherId ?? DBNull.Value; cmd.Parameters.Add(p4);

            await using var rdr = await cmd.ExecuteReaderAsync();

            // Result set 1: headers
            while (await rdr.ReadAsync())
            {
                headers.Add(new StandardHeader
                {
                    HumanCodingScheme = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                    FullStatement     = rdr.IsDBNull(1) ? "" : rdr.GetString(1)
                });
            }

            // Result set 2: per-student aggregates
            if (await rdr.NextResultAsync())
            {
                while (await rdr.ReadAsync())
                {
                    var json = rdr.IsDBNull(6) ? "{}" : rdr.GetString(6);
                    rows.Add(new StudentAggRow
                    {
                        TeacherName          = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        CourseTitle          = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Period               = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                        StudentName          = rdr.IsDBNull(3) ? "" : rdr.GetString(3),
                        LocalStudentId       = rdr.IsDBNull(4) ? "" : rdr.GetString(4),
                        TotalStandardsPassed = rdr.IsDBNull(5) ? 0  : rdr.GetInt32(5),
                        StandardScores       = ParseScores(json)
                    });
                }
            }

            return (headers, rows);
        }

        private static Dictionary<string, int> ParseScores(string json)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return dict;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var v = 0;
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) v = n;
                else if (p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Value.GetString(), out var s)) v = s;
                dict[p.Name] = v;
            }
            return dict;
        }
        #endregion
    }

    #region View Models
    public class StudentResultTableViewModel
    {
        public List<StandardHeader> Headers { get; set; } = new();
        public List<TeacherGroup> TeacherGroups { get; set; } = new();
    }

    public class TeacherGroup
    {
        public string TeacherName { get; set; } = string.Empty;
        public List<ClassGroup> ClassGroups { get; set; } = new();
    }

    public class ClassGroup
    {
        public string CourseTitle { get; set; } = string.Empty; // Holds "ClassName - Period"
        public List<StudentResultRow> StudentRows { get; set; } = new();
    }

    public class StandardHeader
    {
        public string HumanCodingScheme { get; set; } = string.Empty;
        public string FullStatement { get; set; } = string.Empty;
    }

    public class StudentResultRow
    {
        public string StudentName { get; set; } = string.Empty;
        public string LocalStudentId { get; set; } = string.Empty;
        public int TotalStandardsPassed { get; set; }
        public Dictionary<string, int> StandardScores { get; set; } = new();

        [JsonIgnore]
        public string _TeacherNameInternal { get; set; } = string.Empty;
        [JsonIgnore]
        public string _CourseTitleInternal { get; set; } = string.Empty;
        [JsonIgnore]
        public string _PeriodInternal { get; set; } = string.Empty;
    }

    public class StandardResult
    {
        [JsonPropertyName("standard_id")]
        public string StandardId { get; set; } = string.Empty;
        [JsonPropertyName("score")]
        public int Score { get; set; }
        [JsonPropertyName("proficient")]
        public bool Proficient { get; set; }
    }
    #endregion
}
