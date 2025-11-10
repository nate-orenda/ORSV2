using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class GAProfileCardModel : GABasePageModel
    {
        public GAProfileCardModel(ApplicationDbContext context) : base(context)
        {
        }
        public GAResults Student { get; set; } = default!;
        public List<IndicatorSummary> StudentIndicators { get; set; } = new();
        public int? AttendanceAbsences { get; set; }
        public double? CumulativeGPA => Student?.CumulativeGPA;
        public decimal? CreditsCompleted => Student?.CreditsCompleted;
        public List<IndicatorRequirement> IndicatorRequirements { get; set; } = new();
        public GAAGProgress? AGProgress { get; set; }
        public List<SubjectGrade> NonAGGrades { get; set; } = new();
        public List<SubjectGradesGroup> SubjectGradesByArea { get; set; } = new();
        public List<SubjectGradesGroup> AGScheduleByArea { get; set; } = new();
        public List<SubjectGrade> NonAGSchedule { get; set; } = new();

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
            // Add caching headers for better performance
            Response.Headers["Cache-Control"] = "private, max-age=300"; // 5 minutes

            // Input validation
            if (id <= 0) return BadRequest("Invalid student ID");

            Student = await _context.GAResults
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ResultId == id)
                ?? throw new InvalidOperationException("Student not found");

            // SECURITY: Verify user has access to this student's school
            if (!await AuthorizeAsync(Student.SchoolId)) 
                return Forbid();

            var school = await _context.Schools
                .AsNoTracking()
                .Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == Student.SchoolId);
            
            if (school == null) return NotFound();

            // Get schedule and calculate checkpoint
            var schedule = await _context.GACheckpointSchedule
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == Student.SchoolId);
            
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, DateTime.Today);
            int schoolYear = DateTime.Today.Month >= 8 ? DateTime.Today.Year + 1 : DateTime.Today.Year;

            // Execute queries sequentially to avoid DbContext concurrency issues
            var indicators = await GetIndicatorsAsync(cp, school.DistrictId);
            var matrix = await GetMatrixAsync(cp, school.DistrictId);
            var agProgress = await GetAGProgressAsync(schoolYear, cp, school.DistrictId);
            var attendanceAbsences = await GetAttendanceAsync();

            // Process indicators
            ProcessIndicators(indicators, matrix);
            AGProgress = agProgress;
            AttendanceAbsences = attendanceAbsences;

            // Process grades and schedule data
            await ProcessGradesAndScheduleAsync(school.DistrictId, cp);

            return Page();
        }

        private async Task<List<GAQuadrantIndicators>> GetIndicatorsAsync(int cp, int districtId)
        {
            return await _context.GAQuadrantIndicators
                .AsNoTracking()
                .Where(i => i.Grade == Student.Grade && i.CP == cp && i.IsEnabled == true &&
                           (i.SchoolId == null || i.SchoolId == Student.SchoolId) &&
                           (i.DistrictId == null || i.DistrictId == districtId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();
        }

        private async Task<List<GAMatrix>> GetMatrixAsync(int cp, int districtId)
        {
            return await _context.GAMatrix
                .AsNoTracking()
                .Where(m => m.Grade == Student.Grade && m.CP == cp &&
                           (m.DistrictId == null || m.DistrictId == districtId) &&
                           (m.SchoolId == null || m.SchoolId == Student.SchoolId))
                .GroupBy(m => m.Indicator)
                .Select(g => g.OrderByDescending(m => m.SchoolId != null ? 3 : m.DistrictId != null ? 2 : 1).First())
                .ToListAsync();
        }

        private async Task<GAAGProgress?> GetAGProgressAsync(int schoolYear, int cp, int districtId)
        {
            return await _context.GAAGProgress
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.StudentId == Student.StudentId &&
                    p.SchoolId == Student.SchoolId &&
                    p.DistrictId == districtId &&
                    p.CP == cp &&
                    p.SchoolYear == schoolYear);
        }

        private async Task<int?> GetAttendanceAsync()
        {
            return await _context.StudentAttendance
                .AsNoTracking()
                .Where(a => a.DistrictId == Student.DistrictId &&
                           a.SchoolId == Student.SchoolId &&
                           a.StudentId == Student.StudentId)
                .Select(a => (int?)a.Absences)
                .FirstOrDefaultAsync();
        }

        private void ProcessIndicators(List<GAQuadrantIndicators> indicators, List<GAMatrix> matrix)
        {
            IndicatorRequirements = matrix.Select(m => new IndicatorRequirement
            {
                Name = m.Indicator,
                RequirementText = m.ReadableValue ?? ""
            }).ToList();

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

                StudentIndicators.Add(new IndicatorSummary
                {
                    Name = ind.IndicatorName,
                    Met = met
                });
            }
        }

        private async Task ProcessGradesAndScheduleAsync(int districtId, int cp)
        {
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

            var validSubjectCodes = subjectMap.Keys.ToHashSet();

            // Get regular transcript grades
            var gradesData = await _context.Grades
                .AsNoTracking()
                .Where(g => g.StudentId == Student.StudentId && g.DistrictId == districtId)
                .Join(_context.Courses.AsNoTracking(),
                    g => new { g.DistrictId, CourseNumber = g.CN },
                    c => new { c.DistrictId, c.CourseNumber },
                    (g, c) => new {
                        g.SchoolYear, g.Term, g.CN, c.Title, g.GradeLevel, 
                        g.Mark, g.Type, g.CC,
                        SubjectCode = c.CSU_SubjectAreaCode ?? c.UC_SubjectAreaCode
                    })
                .ToListAsync();

            // Get report card grades for CP 2 or 4 only - fetch raw data first, then join in memory
            var reportCardData = new List<object>();
            if (cp == 2 || cp == 4)
            {
                var rcRaw = await _context.ReportCardGrades
                    .AsNoTracking()
                    .Where(r => r.StudentId == Student.StudentId && 
                            r.DistrictId == districtId && 
                            r.SchoolId == Student.SchoolId)
                    .ToListAsync();

                // Join with ReportingPeriods in memory to avoid parse issues
                var currentPeriods = await _context.ReportingPeriods
                    .AsNoTracking()
                    .Where(rp => rp.IsCurrent == true && rp.DistrictId == districtId && rp.SchoolId == Student.SchoolId)
                    .ToListAsync();

                var currentPeriodIds = currentPeriods.Select(rp => rp.MarkingPeriod).ToHashSet();

                rcRaw = rcRaw
                    .Where(r => r.Term != null && int.TryParse(r.Term, out var mp) && currentPeriodIds.Contains(mp))
                    .ToList();

                var courseNumbers = rcRaw.Select(r => r.CourseNumber).Distinct().ToList();
                var courses = await _context.Courses
                    .AsNoTracking()
                    .Where(c => c.DistrictId == districtId && courseNumbers.Contains(c.CourseNumber))
                    .ToListAsync();

                var courseMap = courses.ToDictionary(c => c.CourseNumber);

                reportCardData = rcRaw
                    .Where(r => !string.IsNullOrWhiteSpace(r.CourseNumber) && courseMap.ContainsKey(r.CourseNumber))
                    .Select(r => {
                        var course = courseMap[r.CourseNumber!];
                        return new {
                            Term = r.Term,
                            CN = r.CourseNumber,
                            Title = course.Title,
                            Mark = r.Mark,
                            CreditsEarned = r.CreditsEarned,
                            SubjectCode = course.CSU_SubjectAreaCode ?? course.UC_SubjectAreaCode
                        };
                    })
                    .Cast<object>()
                    .ToList();
            }

            // Process A-G grades from transcript
            var agGrades = gradesData
                .Where(x => !string.IsNullOrEmpty(x.SubjectCode) && validSubjectCodes.Contains(x.SubjectCode))
                .GroupBy(g => g.SubjectCode!)
                .ToList();

            SubjectGradesByArea = agGrades.Select(g => new SubjectGradesGroup
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
            }).ToList();

            // Append report card grades to matching A-G subject areas
            if (reportCardData.Any())
            {
                foreach (var rcItem in reportCardData)
                {
                    dynamic rc = rcItem;
                    string? subjectCode = rc.SubjectCode;

                    if (!string.IsNullOrEmpty(subjectCode) && validSubjectCodes.Contains(subjectCode))
                    {
                        var subjectGroup = SubjectGradesByArea.FirstOrDefault(s => s.SubjectCode == subjectCode);
                        
                        // *** START FIX ***

                        var newRcGrade = new SubjectGrade
                        {
                            Term = rc.Term?.ToString() ?? "",
                            CourseNumber = rc.CN,
                            Title = rc.Title,
                            Mark = rc.Mark,
                            Type = "Report Card",
                            CreditsEarned = rc.CreditsEarned
                        };

                        if (subjectGroup != null)
                        {
                            // Group exists, just add the grade
                            subjectGroup.Grades.Add(newRcGrade);
                        }
                        else
                        {
                            // Group DOES NOT EXIST, so create it
                            var newGroup = new SubjectGradesGroup
                            {
                                SubjectCode = subjectCode,
                                SubjectLabel = subjectMap[subjectCode], // Get label from your map
                                Grades = new List<SubjectGrade> { newRcGrade }
                            };
                            SubjectGradesByArea.Add(newGroup); // Add the new group to the main list
                        }
                        // *** END FIX ***
                    }
                }
            }

            // Process Non-A-G grades (unchanged)
            NonAGGrades = gradesData
                .Where(g => string.IsNullOrEmpty(g.SubjectCode) && 
                        int.TryParse(g.GradeLevel, out var gl) && gl > 8)
                .Select(g => new SubjectGrade
                {
                    SchoolYear = g.SchoolYear,
                    Term = g.Term,
                    CourseNumber = g.CN,
                    Title = g.Title,
                    GradeLevel = g.GradeLevel,
                    Mark = g.Mark,
                    Type = g.Type,
                    CreditsEarned = g.CC
                })
                .OrderBy(g => g.SchoolYear).ThenBy(g => g.Term).ThenBy(g => g.GradeLevel).ThenBy(g => g.Title)
                .ToList();

            // Get current schedule data sequentially
            await ProcessCurrentScheduleAsync(districtId, subjectMap, validSubjectCodes);
        }

        private async Task ProcessCurrentScheduleAsync(int districtId, Dictionary<string, string> subjectMap, HashSet<string> validSubjectCodes)
        {
            var scheduleData = await (from sc in _context.StudentClasses
                                     join c in _context.Courses on new { sc.DistrictId, CourseNumber = sc.CourseID } 
                                         equals new { c.DistrictId, c.CourseNumber }
                                     join ms in _context.MasterSchedule on new { sc.DistrictId, sc.SchoolId, sc.SectionNumber } 
                                         equals new { ms.DistrictId, ms.SchoolId, ms.SectionNumber }
                                     where sc.StudentId == Student.StudentId &&
                                           sc.SchoolId == Student.SchoolId &&
                                           sc.DistrictId == districtId
                                     select new
                                     {
                                         SubjectCode = c.CSU_SubjectAreaCode ?? c.UC_SubjectAreaCode,
                                         c.Title,
                                         c.CourseNumber,
                                         ms.Period,
                                         ms.Semester
                                     }).AsNoTracking().ToListAsync();

            // Process Non-A-G schedule
            NonAGSchedule = scheduleData
                .Where(x => string.IsNullOrWhiteSpace(x.SubjectCode) || !validSubjectCodes.Contains(x.SubjectCode))
                .OrderBy(x => x.Semester).ThenBy(x => x.Period).ThenBy(x => x.Title)
                .Select(x => new SubjectGrade
                {
                    Term = $"( { x.Semester } ) " + $"Period {x.Period}",
                    CourseNumber = x.CourseNumber,
                    Title = x.Title,
                    Type = "Schedule"
                }).ToList();

            // Process A-G schedule
            var agScheduleGroups = scheduleData
                .Where(x => !string.IsNullOrEmpty(x.SubjectCode) && validSubjectCodes.Contains(x.SubjectCode))
                .GroupBy(x => x.SubjectCode!)
                .Select(g => new SubjectGradesGroup
                {
                    SubjectCode = g.Key,
                    SubjectLabel = subjectMap[g.Key],
                    Grades = g.Select(x => new SubjectGrade
                    {
                        Term = $"( { x.Semester } ) " + $"Period {x.Period}",
                        CourseNumber = x.CourseNumber,
                        Title = x.Title,
                        Type = "Schedule"
                    }).OrderBy(x => x.Term).ToList()
                }).ToList();

            // Combine transcript and schedule data
            AGScheduleByArea = SubjectGradesByArea.Select(g => new SubjectGradesGroup
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
            }).ToList();

            // Add current schedule to existing transcript data
            foreach (var scheduledGroup in agScheduleGroups)
            {
                var existing = AGScheduleByArea.FirstOrDefault(x => x.SubjectCode == scheduledGroup.SubjectCode);
                if (existing != null)
                {
                    existing.Grades.AddRange(scheduledGroup.Grades);
                }
                else
                {
                    AGScheduleByArea.Add(scheduledGroup);
                }
            }
        }

        // Helper methods for the Razor page
        public bool HasIndicator(string indicatorName)
        {
            return StudentIndicators.Any(i => i.Name == indicatorName);
        }

        public string GetRequirementText(string subjectArea)
        {
            var requirement = IndicatorRequirements.FirstOrDefault(r => 
                r.Name.Contains(subjectArea, StringComparison.OrdinalIgnoreCase));
            return requirement?.RequirementText ?? "No specific requirement found";
        }
    }
}