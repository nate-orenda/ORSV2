using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class CompareCPModel : GABasePageModel
    {
        public CompareCPModel(ApplicationDbContext context) : base(context) { }

        // ✨ ViewModel for charts/heatmap
        public record IndicatorSummary(string Name, double PercentMet, int CountMet);

        public sealed class StudentIndicatorRow
        {
            public int StuId { get; set; }
            public string LocalStudentId { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string? Grade { get; set; } // Stored as string in STU
            public string? Quadrant { get; set; }
            // dynamic indicator -> nullable bool (true=Met, false=Not Met, null=N/A)
            public Dictionary<string, bool?> Indicators { get; set; } = new();
        }

        // Page state
        public TargetGroup? CurrentGroup { get; set; }
        public string SchoolName { get; set; } = "";
        public string GroupName { get; set; } = "";
        public List<StudentIndicatorRow> Students { get; set; } = new();
        public List<string> IndicatorNames { get; set; } = new();
        public List<IndicatorSummary> CurrentIndicatorSummaries { get; set; } = new();
        public List<IndicatorSummary>? PreviousIndicatorSummaries { get; set; }
        public Dictionary<string, int> CurrentQuadrantCounts { get; set; } = new();
        public Dictionary<string, int>? PreviousQuadrantCounts { get; set; }
        public int CurrentCheckpoint { get; set; }
        public int? PreviousCheckpoint { get; set; }
        public int Grade { get; set; } // numeric Grade we use for GAQuadrantIndicators
        public int SchoolId { get; set; }
        public bool IsGroupMode { get; set; }
        
        public sealed class MovementRow
        {
            public string From { get; set; } = "";
            public string To   { get; set; } = "";
            public int Count   { get; set; }
        }

        public List<MovementRow> MovementMatrix { get; set; } = new();
        public int MovementUp { get; set; }
        public int MovementDown { get; set; }
        public int MovementSame { get; set; }


        /// <summary>
        /// When id is provided => Target Group mode.
        /// When id is null => School-wide by Grade mode (requires ?schoolId=&grade=).
        /// </summary>
        /// 
        private static string NormalizeIndicatorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;

            // canonical set used across GAResults & charts
            return name.Trim() switch
            {
                // Friendly → Canonical
                "A-G Grades"        => "AGGrades",
                "A-G Schedule"      => "AGSchedule",
                "AssessmentsELA"    => "ELA",
                "AssessmentsMath"   => "Math",

                // already canonical, preserve
                "OnTrack"           => "OnTrack",
                "GPA"               => "GPA",
                "AGGrades"          => "AGGrades",
                "AGSchedule"        => "AGSchedule",
                "Affiliation"       => "Affiliation",
                "FAFSA"             => "FAFSA",
                "CollegeApplication"=> "CollegeApplication",
                "Attendance"        => "Attendance",
                "Referrals"         => "Referrals",
                "Grades"            => "Grades",
                "ELA"               => "ELA",
                "Math"              => "Math",

                _ => name.Trim()
            };
        }

        public async Task<IActionResult> OnGetAsync(int? id, int? schoolId, int? grade)
        {
            IsGroupMode = id.HasValue;

            if (IsGroupMode)
            {
                // ----- Target Group mode -----
                CurrentGroup = await _context.TargetGroups
                    .Include(tg => tg.School).ThenInclude(s => s!.District)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(tg => tg.Id == id!.Value);

                if (CurrentGroup?.School is null) return NotFound();
                if (!await AuthorizeAsync(CurrentGroup.SchoolId)) return Forbid();

                SchoolName = CurrentGroup.School.Name;
                GroupName = CurrentGroup.Name;
                SchoolId = CurrentGroup.SchoolId;
            }
            else
            {
                // ----- School-wide by Grade mode -----
                if (schoolId is null || grade is null)
                    return BadRequest("When no Target Group id is provided, pass ?schoolId={id}&grade={number}");

                var school = await _context.Schools
                    .Include(s => s.District)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == schoolId.Value);

                if (school is null) return NotFound("School not found");
                if (!await AuthorizeAsync(school.Id)) return Forbid();

                SchoolId = school.Id;
                SchoolName = school.Name;
                Grade = grade.Value;
                GroupName = $"All Students • Grade {Grade}";
            }

            ViewData["Title"] = $"Target Group - {GroupName}";

            // Determine active checkpoint for this school & year (fallback via helper)
            var schedule = await _context.GACheckpointSchedule
                                .AsNoTracking()
                                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);
            var today = DateTime.Today;
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp;
            PreviousCheckpoint = CurrentCheckpoint > 1 ? CurrentCheckpoint - 1 : null;

            // Build roster of StudentIds
            List<int> studentIds;
            if (IsGroupMode)
            {
                studentIds = await _context.TargetGroupStudents
                    .AsNoTracking()
                    .Where(x => x.TargetGroupId == id!.Value)
                    .Select(x => x.StudentId)
                    .Distinct()
                    .ToListAsync();
            }
            else
            {
                // School-wide by grade: all students for school + selected grade
                var gradeStr = grade!.Value.ToString();
                studentIds = await _context.STU.AsNoTracking()
                    .Where(s => s.SchoolID == SchoolId && s.Grade == gradeStr)
                    .Select(s => s.StuId)
                    .Distinct()
                    .ToListAsync();
            }

            if (studentIds.Count == 0) return Page(); // empty set – render "no data"

            // Student directory info (name, local id, grade)
            var stuDirectory = await _context.STU.AsNoTracking()
                .Where(s => s.SchoolID == SchoolId && studentIds.Contains(s.StuId))
                .Select(s => new
                {
                    s.StuId,
                    s.LocalStudentID,
                    s.FirstName,
                    s.LastName,
                    s.Grade // string
                })
                .ToListAsync();

            // Decide numeric Grade for indicator catalog (if not set already in school-wide mode)
            if (IsGroupMode)
            {
                Grade = stuDirectory
                    .Select(s => int.TryParse(s.Grade, out var g) ? g : 0)
                    .DefaultIfEmpty(0)
                    .Max();
            }
            // else Grade already provided from querystring

            // Indicators for this Grade + CP (scoped to School/District as available)
            var rawIndicators = await _context.GAQuadrantIndicators.AsNoTracking()
                .Where(i => i.Grade == Grade && i.CP == CurrentCheckpoint
                    && (i.SchoolId == null || i.SchoolId == SchoolId)
                    && (i.DistrictId == null || i.DistrictId ==
                        _context.Schools.Where(s => s.Id == SchoolId).Select(s => s.DistrictId).FirstOrDefault()))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            IndicatorNames = rawIndicators
                .Select(i => NormalizeIndicatorName(i.IndicatorName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // GAResults for current and (optional) previous checkpoint
            var resultsCurrent = await _context.GAResults.AsNoTracking()
                .Where(r => r.SchoolId == SchoolId
                            && r.CP == CurrentCheckpoint
                            && studentIds.Contains(r.StudentId))
                .ToListAsync();

            List<GAResults>? resultsPrev = null;
            if (PreviousCheckpoint is not null)
            {
                resultsPrev = await _context.GAResults.AsNoTracking()
                    .Where(r => r.SchoolId == SchoolId
                                && r.CP == PreviousCheckpoint
                                && studentIds.Contains(r.StudentId))
                    .ToListAsync();
            }

            var byStuIdCur = resultsCurrent.ToDictionary(r => r.StudentId, r => r);

            // Build student rows
            foreach (var s in stuDirectory.OrderBy(s => s.LastName).ThenBy(s => s.FirstName))
            {
                byStuIdCur.TryGetValue(s.StuId, out var cur);
                var row = new StudentIndicatorRow
                {
                    StuId = s.StuId,
                    LocalStudentId = s.LocalStudentID ?? "",
                    FirstName = s.FirstName ?? "",
                    LastName = s.LastName ?? "",
                    Grade = s.Grade, // keep as string for display
                    Quadrant = cur?.Quadrant
                };

                foreach (var raw in IndicatorNames)
                {
                    var ind = NormalizeIndicatorName(raw);

                    bool? val = ind switch
                    {
                        // Newly added so they show up:
                        "OnTrack"           => cur?.OnTrack,
                        "GPA"               => cur?.GPA,
                        "AGGrades"          => cur?.AGGrades,

                        // Existing:
                        "AGSchedule"        => cur?.AGSchedule,
                        "Affiliation"       => cur?.Affiliation,
                        "FAFSA"             => cur?.FAFSA,
                        "CollegeApplication"=> cur?.CollegeApplication,
                        "Attendance"        => cur?.Attendance,
                        "Referrals"         => cur?.Referrals,
                        "Grades"            => cur?.Grades,
                        "ELA"               => cur?.AssessmentsELA,
                        "Math"              => cur?.AssessmentsMath,

                        _ => null
                    };

                    row.Indicators[ind] = val;
                }


                Students.Add(row);
            }

            MovementMatrix = new();
            MovementUp = MovementDown = MovementSame = 0;

            if (resultsPrev is not null && resultsPrev.Count > 0)
            {
                string Norm(string? q) => string.IsNullOrWhiteSpace(q) ? "Unknown" : q;
                int Rank(string q) => q switch
                {
                    "Intensive" => 0,
                    "Strategic" => 1,
                    "Benchmark" => 2,
                    "Challenge" => 3,
                    _ => 1 // Unknown ~ middle
                };

                var prevByStu = resultsPrev.ToDictionary(r => r.StudentId, r => Norm(r.Quadrant));
                var curByStu  = resultsCurrent.ToDictionary(r => r.StudentId, r => Norm(r.Quadrant));

                // Aggregate movements
                var agg = new Dictionary<(string From, string To), int>();


                foreach (var kv in curByStu)
                {
                    var stuId = kv.Key;
                    var to    = kv.Value;
                    if (!prevByStu.TryGetValue(stuId, out var from)) continue;

                    var key = (from, to);
                    agg[key] = agg.TryGetValue(key, out var c) ? c + 1 : 1;

                    var delta = Rank(to) - Rank(from);
                    if (delta > 0)      MovementUp++;
                    else if (delta < 0) MovementDown++;
                    else                MovementSame++;
                }

                MovementMatrix = agg
                    .Where(kvp => !string.Equals(kvp.Key.From, kvp.Key.To, StringComparison.OrdinalIgnoreCase)) // exclude same→same
                    .Select(kvp => new MovementRow
                    {
                        From  = kvp.Key.From,
                        To    = kvp.Key.To,
                        Count = kvp.Value
                    })
                    .OrderByDescending(m => m.Count)
                    .ToList();

            }

            // Summaries: % met by indicator (current & previous)
            CurrentIndicatorSummaries = IndicatorNames.Select(ind =>
            {
                var met = Students.Count(s => s.Indicators[ind] == true);
                return new IndicatorSummary(ind, Students.Count > 0 ? (met * 100.0 / Students.Count) : 0, met);
            }).ToList();

            if (resultsPrev is not null)
            {
                PreviousIndicatorSummaries = IndicatorNames.Select(ind =>
                {
                    var prevMet = resultsPrev.Count(r => IndicatorMet(r, ind));
                    var denom = resultsPrev.Count;
                    return new IndicatorSummary(ind, denom > 0 ? (prevMet * 100.0 / denom) : 0, prevMet);
                }).ToList();
            }

            // Quadrant counts (current & previous)
            CurrentQuadrantCounts = resultsCurrent
                .GroupBy(r => r.Quadrant ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            if (resultsPrev is not null)
            {
                PreviousQuadrantCounts = resultsPrev
                    .GroupBy(r => r.Quadrant ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            return Page();
        }

        private static bool IndicatorMet(GAResults r, string indicatorName)
        {
            var ind = NormalizeIndicatorName(indicatorName);
            return ind switch
            {
                "OnTrack"           => r.OnTrack == true,
                "GPA"               => r.GPA == true,
                "AGGrades"          => r.AGGrades == true,
                "AGSchedule"        => r.AGSchedule == true,
                "Affiliation"       => r.Affiliation == true,
                "FAFSA"             => r.FAFSA == true,
                "CollegeApplication"=> r.CollegeApplication == true,
                "Attendance"        => r.Attendance == true,
                "Referrals"         => r.Referrals == true,
                "Grades"            => r.Grades == true,
                "ELA"               => r.AssessmentsELA == true,
                "Math"              => r.AssessmentsMath == true,
                _ => false
            };
        }

    }
}
