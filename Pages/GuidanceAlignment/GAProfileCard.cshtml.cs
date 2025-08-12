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

        public GAResults Student { get; set; } = default!;
        public List<IndicatorSummary> StudentIndicators { get; set; } = new();
        public List<IndicatorRequirement> IndicatorRequirements { get; set; } = new();

        public GAAGProgress AGProgress { get; set; } = new();
        public List<SubjectGrade> NonAGGrades { get; set; } = new();
        public List<SubjectGrade> NonAGSchedule { get; set; } = new();

        public List<SubjectGradesGroup> SubjectGradesByArea { get; set; } = new();
        public List<SubjectGradesGroup> AGScheduleByArea { get; set; } = new();

        public string QuadrantLevel => Student.Quadrant ?? "Unknown";
        public string QuadrantColorClass => (Student.Quadrant ?? "").ToLower() switch
        {
            "challenge" => "bg-primary",
            "benchmark" => "bg-success",
            "strategic" => "bg-warning text-dark",
            "intensive" => "bg-danger",
            _ => "bg-secondary"
        };

        public class IndicatorSummary
        {
            public string Name { get; set; } = string.Empty;
            public bool Met { get; set; }
        }

        public class IndicatorRequirement
        {
            public string Name { get; set; } = string.Empty;
            public string RequirementText { get; set; } = string.Empty;
        }

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

        public async Task<IActionResult> OnGetAsync(int id)
        {
            // Student
            Student = await _context.GAResults.FirstOrDefaultAsync(s => s.ResultId == id)
                ?? throw new InvalidOperationException("Student not found");

            var school = await _context.Schools
                .Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == Student.SchoolId);
            if (school == null) return NotFound();

            // CP + SchoolYear (Aug–Jul model)
            var schedule = await _context.GACheckpointSchedule
                .FirstOrDefaultAsync(s => s.SchoolId == Student.SchoolId);
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, DateTime.Today);
            int schoolYear = DateTime.Today.Month >= 8 ? DateTime.Today.Year + 1 : DateTime.Today.Year;

            // Enabled indicators (School > District > Default)
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
                    "OnTrack"            => Student.OnTrack == true,
                    "GPA"                => Student.GPA == true,
                    "AGGrades"           => Student.AGGrades == true,
                    "AGSchedule"         => Student.AGSchedule == true,
                    "Affiliation"        => Student.Affiliation == true,
                    "FAFSA"              => Student.FAFSA == true,
                    "CollegeApplication" => Student.CollegeApplication == true,
                    "Attendance"         => Student.Attendance == true,
                    _ => false
                };
                StudentIndicators.Add(new IndicatorSummary { Name = ind.IndicatorName, Met = met });
            }

            // === Requirements (ReadableValue) with School/District precedence + SchoolYear fallback ===
            // Try current schoolYear first, but if rows don't exist yet (e.g., only 2025 defined), fall back to prior then null.
            var matrixCandidates = await _context.GAMatrix
                .Where(m =>
                    m.Grade == Student.Grade &&
                    m.CP == cp &&
                    (m.SchoolYear == schoolYear || m.SchoolYear == schoolYear - 1 || m.SchoolYear == null) &&
                    (m.DistrictId == null || m.DistrictId == school.DistrictId) &&
                    (m.SchoolId == null || m.SchoolId == Student.SchoolId))
                .ToListAsync();

            IndicatorRequirements = matrixCandidates
                .GroupBy(m => m.Indicator)
                .Select(g => g
                    .OrderByDescending(m => m.SchoolId != null ? 3 : m.DistrictId != null ? 2 : 1)
                    .ThenByDescending(m => m.SchoolYear ?? 0)
                    .First())
                .Select(m => new IndicatorRequirement
                {
                    Name = m.Indicator,
                    RequirementText = m.ReadableValue ?? ""
                })
                .ToList();

            // === A–G progress (% comes from GAAGProgress; we also surface raw Earned/Target values) ===
            AGProgress = await _context.GAAGProgress.FirstOrDefaultAsync(p =>
                p.StudentId == Student.StudentId &&
                p.SchoolId == Student.SchoolId &&
                p.DistrictId == school.DistrictId &&
                p.CP == cp &&
                p.SchoolYear == schoolYear) ?? new GAAGProgress();

            // === Transcript A–G grades ===
            var subjectMap = SubjectMap;
            var validSubjectCodes = subjectMap.Keys.ToList();

            var gradesQuery =
                from g in _context.Grades
                join c in _context.Courses
                    on new { g.DistrictId, CourseNumber = g.CN }
                    equals new { c.DistrictId, c.CourseNumber }
                where g.StudentId == Student.StudentId
                      && g.DistrictId == school.DistrictId
                      && (c.CSU_SubjectAreaCode != null || c.UC_SubjectAreaCode != null)
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

            var allGrades = await gradesQuery.ToListAsync();
            var filteredGrades = allGrades.Where(x => x.SubjectCode != null && validSubjectCodes.Contains(x.SubjectCode!)).ToList();

            SubjectGradesByArea = filteredGrades
                .GroupBy(g => g.SubjectCode!)
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
                .OrderBy(g => g.SubjectCode)
                .ToList();

            // === Non‑A‑G transcript ===
            NonAGGrades = (await (
                from g in _context.Grades
                join c in _context.Courses
                    on new { g.DistrictId, CourseNumber = g.CN }
                    equals new { c.DistrictId, c.CourseNumber }
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
                .OrderBy(g => g.SchoolYear).ThenBy(g => g.Term).ThenBy(g => g.GradeLevel).ThenBy(g => g.Title)
                .ToList();

            // === Current schedule (A–G & Non‑A‑G) ===
            var allScheduledCourses =
                await (from sc in _context.StudentClasses
                       join c in _context.Courses
                           on new { sc.DistrictId, CourseNumber = sc.CourseID }
                           equals new { c.DistrictId, c.CourseNumber }
                       join ms in _context.MasterSchedule
                           on new { sc.DistrictId, sc.SchoolId, sc.SectionNumber }
                           equals new { ms.DistrictId, ms.SchoolId, ms.SectionNumber }
                       where sc.StudentId == Student.StudentId
                             && sc.SchoolId == Student.SchoolId
                             && sc.DistrictId == Student.DistrictId
                       select new
                       {
                           SubjectCode = c.CSU_SubjectAreaCode ?? c.UC_SubjectAreaCode,
                           c.Title,
                           c.CourseNumber,
                           ms.Period
                       }).ToListAsync();

            // Build schedule groups from transcript groups + add current scheduled
            AGScheduleByArea = SubjectGradesByArea
                .Select(g => new SubjectGradesGroup
                {
                    SubjectCode = g.SubjectCode,
                    SubjectLabel = g.SubjectLabel,
                    Grades = g.Grades.Select(grade => new SubjectGrade
                    {
                        SchoolYear = grade.SchoolYear,
                        Term = grade.Term,
                        CourseNumber = grade.CourseNumber,
                        Title = grade.Title,
                        GradeLevel = grade.GradeLevel,
                        Mark = grade.Mark,
                        Type = "Transcript",
                        CreditsEarned = grade.CreditsEarned
                    }).ToList()
                })
                .ToList();

            var scheduledByAg = allScheduledCourses
                .Where(x => x.SubjectCode != null && validSubjectCodes.Contains(x.SubjectCode!))
                .GroupBy(x => x.SubjectCode!);

            foreach (var g in scheduledByAg)
            {
                var target = AGScheduleByArea.FirstOrDefault(x => x.SubjectCode == g.Key);
                var toAdd = g.Select(x => new SubjectGrade
                {
                    Term = $"Period {x.Period}",
                    CourseNumber = x.CourseNumber,
                    Title = x.Title,
                    Type = "Scheduled"
                }).OrderBy(x => x.Term).ToList();

                if (target != null) target.Grades.AddRange(toAdd);
                else
                {
                    AGScheduleByArea.Add(new SubjectGradesGroup
                    {
                        SubjectCode = g.Key,
                        SubjectLabel = subjectMap[g.Key],
                        Grades = toAdd
                    });
                }
            }

            // Non‑A‑G schedule
            NonAGSchedule = allScheduledCourses
                .Where(x => string.IsNullOrEmpty(x.SubjectCode) || !validSubjectCodes.Contains(x.SubjectCode!))
                .Select(x => new SubjectGrade
                {
                    Term = $"Period {x.Period}",
                    CourseNumber = x.CourseNumber,
                    Title = x.Title,
                    Type = "Scheduled"
                })
                .OrderBy(x => x.Term).ThenBy(x => x.Title)
                .ToList();

            return Page();
        }

        // ===== Helpers =====

        public bool HasIndicator(string indicatorName) =>
            StudentIndicators.Any(i => i.Name == indicatorName);

        public string GetRequirementText(string indicatorExactName)
        {
            var r = IndicatorRequirements
                .FirstOrDefault(x => string.Equals(x.Name, indicatorExactName, StringComparison.OrdinalIgnoreCase));
            return r?.RequirementText ?? "";
        }

        // A–G subject labels
        public Dictionary<string, string> SubjectMap => new()
        {
            ["A"] = "History",
            ["B"] = "English (ELA)",
            ["C"] = "Math",
            ["D"] = "Science",
            ["E"] = "Language Other Than English",
            ["F"] = "Visual/Performing Arts",
            ["G"] = "College-Prep Elective"
        };

        // Percent + formatting
        public static string Fmt(decimal? v) => v.HasValue ? $"{v:0.#}" : "—";
        public static double PctVal(decimal? earned, decimal? target)
        {
            if (!earned.HasValue || !target.HasValue || target <= 0) return 0;
            return (double)(earned.Value / target.Value) * 100.0;
        }

        public (decimal? earned, decimal? target) GetEarnedTarget(string subj, string mode) => (subj, mode) switch
        {
            ("A", "Grades")    => (AGProgress?.CreditsEarned_HIS,  AGProgress?.TargetEarned_HIS),
            ("B", "Grades")    => (AGProgress?.CreditsEarned_ELA,  AGProgress?.TargetEarned_ELA),
            ("C", "Grades")    => (AGProgress?.CreditsEarned_MATH, AGProgress?.TargetEarned_MATH),
            ("D", "Grades")    => (AGProgress?.CreditsEarned_SCI,  AGProgress?.TargetEarned_SCI),
            ("E", "Grades")    => (AGProgress?.CreditsEarned_FL,   AGProgress?.TargetEarned_FL),
            ("F", "Grades")    => (AGProgress?.CreditsEarned_VA,   AGProgress?.TargetEarned_VA),
            ("G", "Grades")    => (AGProgress?.CreditsEarned_PREP, AGProgress?.TargetEarned_PREP),

            ("A", "Schedule")  => (AGProgress?.CreditsScheduled_HIS,  AGProgress?.TargetScheduled_HIS),
            ("B", "Schedule")  => (AGProgress?.CreditsScheduled_ELA,  AGProgress?.TargetScheduled_ELA),
            ("C", "Schedule")  => (AGProgress?.CreditsScheduled_MATH, AGProgress?.TargetScheduled_MATH),
            ("D", "Schedule")  => (AGProgress?.CreditsScheduled_SCI,  AGProgress?.TargetScheduled_SCI),
            ("E", "Schedule")  => (AGProgress?.CreditsScheduled_FL,   AGProgress?.TargetScheduled_FL),
            ("F", "Schedule")  => (AGProgress?.CreditsScheduled_VA,   AGProgress?.TargetScheduled_VA),
            ("G", "Schedule")  => (AGProgress?.CreditsScheduled_PREP, AGProgress?.TargetScheduled_PREP),

            _ => (null, null)
        };
    }
}
