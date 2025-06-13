using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;


namespace ORSV2.Pages.GuidanceAlignment
{
    public class GAProfileCardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public GAProfileCardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public GAResults Student { get; set; }

        public List<IndicatorSummary> StudentIndicators { get; set; } = new();

        public class IndicatorSummary
        {
            public string Name { get; set; } = string.Empty;
            public bool Met { get; set; }
        }
        public int? AttendanceAbsences { get; set; }
        public double? CumulativeGPA => Student?.CumulativeGPA;
        public decimal? CreditsCompleted => Student?.CreditsCompleted;
        public class IndicatorRequirement
        {
            public string Name { get; set; } = string.Empty;
            public string RequirementText { get; set; } = string.Empty;
        }
        public List<IndicatorRequirement> IndicatorRequirements { get; set; } = new();
        public GAAGProgress? AGProgress { get; set; }
        public List<SubjectGrade> NonAGGrades { get; set; } = new();
        public string QuadrantLevel => Student.Quadrant ?? "Unknown";

        public string QuadrantColorClass => (Student.Quadrant ?? "").ToLower() switch
        {
            "challenge" => "bg-primary",
            "benchmark" => "bg-success",
            "strategic" => "bg-warning text-dark",
            "intensive" => "bg-danger",
            _ => "bg-secondary"
        };
        public class SubjectGradesGroup
        {
            public string SubjectCode { get; set; } = "";
            public string SubjectLabel { get; set; } = "";
            public List<SubjectGrade> Grades { get; set; } = new();
        }

        public class SubjectGrade
        {
            public string SchoolYear { get; set; } = "";
            public string Term { get; set; } = "";
            public string CourseNumber { get; set; } = "";
            public string Title { get; set; } = "";
            public string GradeLevel { get; set; } = "";
            public string Mark { get; set; } = "";
            public string Type { get; set; } = "";
            public decimal? CreditsEarned { get; set; }
        }
        public List<SubjectGradesGroup> SubjectGradesByArea { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Student = await _context.GAResults.FirstOrDefaultAsync(s => s.ResultId == id);
            if (Student == null) return NotFound();

            var school = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == Student.SchoolId);
            if (school == null) return NotFound();

            var schedule = await _context.GACheckpointSchedule.FirstOrDefaultAsync(s => s.SchoolId == Student.SchoolId);
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, DateTime.Today);
            int schoolYear = DateTime.Today.Month >= 8 ? DateTime.Today.Year + 1 : DateTime.Today.Year;

            var indicators = await _context.GAQuadrantIndicators
                .Where(i => i.Grade == Student.Grade && i.CP == cp && i.IsEnabled == true &&
                       (i.SchoolId == null || i.SchoolId == Student.SchoolId) &&
                       (i.DistrictId == null || i.DistrictId == school.DistrictId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            foreach (var ind in indicators)
            {
                bool met = ind.IndicatorName switch
                {
                    "OnTrack" => Student.OnTrack == true,
                    "GPA" => Student.GPA == true,
                    "AGGrades" => Student.AGGrades == true,
                    "AGSchedule" => Student.AGSchedule == true,
                    "Affiliation" => Student.Affiliation == true,
                    "FAFSA" => Student.FAFSA == true,
                    "CollegeApplication" => Student.CollegeApplication == true,
                    "Attendance" => Student.Attendance == true,
                    _ => false
                };

                var matrix = await _context.GAMatrix
                    .Where(m =>
                        m.Grade == Student.Grade &&
                        m.CP == cp &&
                        m.SchoolYear == schoolYear &&
                        (m.DistrictId == null || m.DistrictId == school.DistrictId) &&
                        (m.SchoolId == null || m.SchoolId == Student.SchoolId))
                    .GroupBy(m => m.Indicator)
                    .Select(g => g.OrderByDescending(m => m.SchoolId != null ? 3 : m.DistrictId != null ? 2 : 1).First())
                    .ToListAsync();

                AGProgress = await _context.GAAGProgress.FirstOrDefaultAsync(p =>
                    p.StudentId == Student.StudentId &&
                    p.SchoolId == Student.SchoolId &&
                    p.DistrictId == school.DistrictId &&
                    p.CP == cp &&
                    p.SchoolYear == schoolYear);

                IndicatorRequirements = matrix.Select(m => new IndicatorRequirement
                {
                    Name = m.Indicator,
                    RequirementText = m.ReadableValue ?? ""
                }).ToList();

                StudentIndicators.Add(new IndicatorSummary
                {
                    Name = ind.IndicatorName,
                    Met = met
                });
            }

            AttendanceAbsences = await _context.StudentAttendance
                .Where(a => a.DistrictId == Student.DistrictId &&
                            a.SchoolId == Student.SchoolId &&
                            a.StudentId == Student.StudentId)
                .Select(a => (int?)a.Absences)
                .FirstOrDefaultAsync();

            var subjectMap = new Dictionary<string, string>
            {
                ["A"] = "History",
                ["B"] = "English (ELA)",
                ["C"] = "Math",
                ["D"] = "Science",
                ["E"] = "Language Other Than English",
                ["F"] = "Visual/Performing Arts",
                ["G"] = "College-Prep Elective"
            };

            var gradesQuery = from g in _context.Grades
                            join c in _context.Courses
                                on new { g.DistrictId, CourseNumber = g.CN } equals new { c.DistrictId, CourseNumber = c.CourseNumber }
                            where g.StudentId == Student.StudentId
                                    && g.DistrictId == school.DistrictId
                                    && subjectMap.Keys.Contains(c.CSU_SubjectAreaCode ?? c.UC_SubjectAreaCode)
                            select new
                            {
                                g.SchoolYear,
                                g.Term,
                                g.CN,
                                c.Title,
                                g.GradeLevel,
                                g.Mark,
                                g.Type,
                                g.CC,
                                SubjectCode = c.CSU_SubjectAreaCode ?? c.UC_SubjectAreaCode
                            };

            var grouped = await gradesQuery
                .GroupBy(g => g.SubjectCode)
                .ToListAsync();

            SubjectGradesByArea = grouped
                .Select(g => new SubjectGradesGroup
                {
                    SubjectCode = g.Key,
                    SubjectLabel = subjectMap[g.Key],
                    Grades = g.Select(x => new SubjectGrade
                    {
                        SchoolYear = x.SchoolYear,
                        Term = x.Term,
                        CourseNumber = x.CN,
                        Title = x.Title,
                        GradeLevel = x.GradeLevel,
                        Mark = x.Mark,
                        Type = x.Type,
                        CreditsEarned = x.CC
                    }).OrderByDescending(x => x.SchoolYear).ThenBy(x => x.Term).ToList()
                })
                .ToList();

                NonAGGrades = (await (
                    from g in _context.Grades
                    join c in _context.Courses
                        on new { g.DistrictId, CourseNumber = g.CN } equals new { c.DistrictId, c.CourseNumber }
                    where g.StudentId == Student.StudentId
                        && g.DistrictId == school.DistrictId
                        && string.IsNullOrEmpty(c.CSU_SubjectAreaCode)
                        && string.IsNullOrEmpty(c.UC_SubjectAreaCode)
                    select new SubjectGrade
                    {
                        SchoolYear = g.SchoolYear,
                        Term = g.Term,
                        CourseNumber = g.CN,
                        Title = c.Title,
                        GradeLevel = g.GradeLevel,
                        Mark = g.Mark,
                        Type = g.Type,
                        CreditsEarned = g.CC
                    }).ToListAsync())
                    .Where(g => int.TryParse(g.GradeLevel, out var gl) && gl > 8)
                    .OrderBy(g => g.SchoolYear)
                    .ThenBy(g => g.Term)
                    .ThenBy(g => g.GradeLevel)
                    .ThenBy(g => g.Title)
                    .ToList();

            return Page();
        }

        public async Task<IActionResult> OnGetAttendanceAsync(int id)
        {
            var student = await _context.GAResults.FindAsync(id);
            if (student == null) return NotFound();

            var attendance = await _context.StudentAttendance
                .Where(a => a.DistrictId == student.DistrictId &&
                            a.SchoolId == student.SchoolId &&
                            a.StudentId == student.StudentId)
                .Select(a => a.Absences)
                .FirstOrDefaultAsync();

            return Content($"<div><strong>Total Absences:</strong> {attendance}</div>", "text/html");
        }


    }
}