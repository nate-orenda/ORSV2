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

        // ===== View models =====
        public record IndicatorSummary(string Name, double PercentMet, int CountMet);

        public sealed class StudentIndicatorRow
        {
            public int StuId { get; set; }
            public string LocalStudentId { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string? Grade { get; set; } // STU grade string
            public string? Quadrant { get; set; }
            public Dictionary<string, bool?> Indicators { get; set; } = new();
        }

        public sealed class MovementRow
        {
            public string From { get; set; } = "";
            public string To   { get; set; } = "";
            public int Count   { get; set; }
            public int Delta   { get; set; } // +up / -down by rank
        }

        // Case-insensitive tuple comparer for (From, To)
        public class QuadrantMoveComparer : IEqualityComparer<(string From, string To)>
        {
            public bool Equals((string From, string To) x, (string From, string To) y) =>
                string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.To,   y.To,   StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string From, string To) obj) =>
                HashCode.Combine(
                    obj.From?.ToLowerInvariant().GetHashCode() ?? 0,
                    obj.To?.ToLowerInvariant().GetHashCode()   ?? 0
                );
        }

        // ===== Page state =====
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
        public int Grade { get; set; } // numeric Grade for GAQuadrantIndicators
        public int SchoolId { get; set; }
        public bool IsGroupMode { get; set; }

        // Movement + pp metrics
        public List<MovementRow> MovementMatrix { get; set; } = new();
        public int MovementUp { get; set; }
        public int MovementDown { get; set; }
        public int MovementSame { get; set; }

        public int AbovePrev { get; set; }
        public int AboveNow  { get; set; }
        public int TotalPrev { get; set; }
        public int TotalNow  { get; set; }
        public double AbovePrevPct { get; set; }   // 0–100
        public double AboveNowPct  { get; set; }   // 0–100
        public double AboveDeltaPp { get; set; }   // percentage points

        // ===== Helpers =====
        private static string NormalizeIndicatorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            return name.Trim() switch
            {
                // Friendly → Canonical
                "A-G Grades"      => "AGGrades",
                "A-G Schedule"    => "AGSchedule",
                "AssessmentsELA"  => "ELA",
                "AssessmentsMath" => "Math",

                // Already canonical
                "OnTrack" => "OnTrack",
                "GPA"     => "GPA",
                "AGGrades"=> "AGGrades",
                "AGSchedule" => "AGSchedule",
                "Affiliation" => "Affiliation",
                "FAFSA" => "FAFSA",
                "CollegeApplication" => "CollegeApplication",
                "Attendance" => "Attendance",
                "Referrals" => "Referrals",
                "Grades" => "Grades",
                "ELA" => "ELA",
                "Math" => "Math",
                _ => name.Trim()
            };
        }

        private static int Rank(string q) => q switch
        {
            "Intensive" => 0,
            "Strategic" => 1,
            "Benchmark" => 2,
            "Challenge" => 3,
            _ => 1 // Unknown ~ middle
        };

        private static bool IndicatorMet(GAResults r, string indicatorName)
        {
            var ind = NormalizeIndicatorName(indicatorName);
            return ind switch
            {
                "OnTrack"            => r.OnTrack == true,
                "GPA"                => r.GPA == true,
                "AGGrades"           => r.AGGrades == true,
                "AGSchedule"         => r.AGSchedule == true,
                "Affiliation"        => r.Affiliation == true,
                "FAFSA"              => r.FAFSA == true,
                "CollegeApplication" => r.CollegeApplication == true,
                "Attendance"         => r.Attendance == true,
                "Referrals"          => r.Referrals == true,
                "Grades"             => r.Grades == true,
                "ELA"                => r.AssessmentsELA == true,
                "Math"               => r.AssessmentsMath == true,
                _ => false
            };
        }

        // ===== Handler =====
        public async Task<IActionResult> OnGetAsync(int? id, int? schoolId, int? grade)
        {
            IsGroupMode = id.HasValue;

            if (IsGroupMode)
            {
                CurrentGroup = await _context.TargetGroups
                    .Include(tg => tg.School).ThenInclude(s => s!.District)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(tg => tg.Id == id!.Value);

                if (CurrentGroup?.School is null) return NotFound();
                if (!await AuthorizeAsync(CurrentGroup.SchoolId)) return Forbid();

                SchoolId = CurrentGroup.SchoolId;
                SchoolName = CurrentGroup.School.Name;
                GroupName = CurrentGroup.Name;
            }
            else
            {
                if (schoolId is null || grade is null)
                    return BadRequest("When no Target Group id is provided, pass ?schoolId={id}&grade={number}");

                var school = await _context.Schools
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

            // Checkpoint + SchoolYear
            var schedule = await _context.GACheckpointSchedule
                                .AsNoTracking()
                                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);
            var today = DateTime.Today;
            CurrentCheckpoint = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            PreviousCheckpoint = CurrentCheckpoint > 1 ? CurrentCheckpoint - 1 : null;

            var currentSchoolYear = CurrentCheckpointHelper.GetCurrentSchoolYear(today);

            // Roster
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
                var gradeStr = Grade.ToString();
                studentIds = await _context.STU.AsNoTracking()
                    .Where(s => s.SchoolID == SchoolId && s.Grade == gradeStr)
                    .Select(s => s.StuId)
                    .Distinct()
                    .ToListAsync();
            }

            if (studentIds.Count == 0) return Page();

            // Directory
            var stuDirectory = await _context.STU.AsNoTracking()
                .Where(s => s.SchoolID == SchoolId && studentIds.Contains(s.StuId))
                .Select(s => new { s.StuId, s.LocalStudentID, s.FirstName, s.LastName, s.Grade })
                .ToListAsync();

            // If group spans grades, pick max for indicator catalog
            if (IsGroupMode)
            {
                Grade = stuDirectory
                    .Select(s => int.TryParse(s.Grade, out var g) ? g : 0)
                    .DefaultIfEmpty(0)
                    .Max();
            }

            // Indicators (scope by district/school)
            int? districtId = await _context.Schools
                .Where(s => s.Id == SchoolId)
                .Select(s => (int?)s.DistrictId)
                .FirstOrDefaultAsync();

            var rawIndicators = await _context.GAQuadrantIndicators.AsNoTracking()
                .Where(i => i.Grade == Grade
                        && i.CP == CurrentCheckpoint
                        && (i.SchoolId == null || i.SchoolId == SchoolId)
                        && (i.DistrictId == null || i.DistrictId == districtId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            IndicatorNames = rawIndicators
                .Select(i => NormalizeIndicatorName(i.IndicatorName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // GAResults (filter by SchoolId, CP, SchoolYear, roster)
            var resultsCurrent = await _context.GAResults.AsNoTracking()
                .Where(r => r.SchoolId == SchoolId
                            && r.CP == CurrentCheckpoint
                            && r.SchoolYear == currentSchoolYear
                            && studentIds.Contains(r.StudentId))
                .ToListAsync();

            List<GAResults>? resultsPrev = null;
            if (PreviousCheckpoint is not null)
            {
                resultsPrev = await _context.GAResults.AsNoTracking()
                    .Where(r => r.SchoolId == SchoolId
                                && r.CP == PreviousCheckpoint
                                && r.SchoolYear == currentSchoolYear
                                && studentIds.Contains(r.StudentId))
                    .ToListAsync();
            }

            // Deduplicate by StudentId (in case multiple rows slip through)
            var byStuIdCur = resultsCurrent
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.First());
            var byStuIdPrev = resultsPrev?
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.First());

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
                    Grade = s.Grade,
                    Quadrant = cur?.Quadrant
                };

                foreach (var raw in IndicatorNames)
                {
                    var ind = NormalizeIndicatorName(raw);
                    bool? val = ind switch
                    {
                        "OnTrack"            => cur?.OnTrack,
                        "GPA"                => cur?.GPA,
                        "AGGrades"           => cur?.AGGrades,
                        "AGSchedule"         => cur?.AGSchedule,
                        "Affiliation"        => cur?.Affiliation,
                        "FAFSA"              => cur?.FAFSA,
                        "CollegeApplication" => cur?.CollegeApplication,
                        "Attendance"         => cur?.Attendance,
                        "Referrals"          => cur?.Referrals,
                        "Grades"             => cur?.Grades,
                        "ELA"                => cur?.AssessmentsELA,
                        "Math"               => cur?.AssessmentsMath,
                        _ => null
                    };
                    row.Indicators[ind] = val;
                }
                Students.Add(row);
            }

            // Movement (only if we have previous CP)
            MovementMatrix = new();
            MovementUp = MovementDown = MovementSame = 0;

            if (byStuIdPrev is not null && byStuIdPrev.Count > 0)
            {
                string Norm(string? q) => string.IsNullOrWhiteSpace(q) ? "Unknown" : q;

                var prevByStuQ = byStuIdPrev.ToDictionary(k => k.Key, v => Norm(v.Value.Quadrant));
                var curByStuQ  = byStuIdCur .ToDictionary(k => k.Key, v => Norm(v.Value.Quadrant));

                var agg = new Dictionary<(string From, string To), int>(new QuadrantMoveComparer());

                foreach (var kv in curByStuQ)
                {
                    if (!prevByStuQ.TryGetValue(kv.Key, out var from)) continue;
                    var to = kv.Value;

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
                        Count = kvp.Value,
                        Delta = Rank(kvp.Key.To) - Rank(kvp.Key.From)
                    })
                    .OrderByDescending(m => m.Count)
                    .ToList();
            }

            // Indicator summaries
            CurrentIndicatorSummaries = IndicatorNames.Select(ind =>
            {
                var met = Students.Count(s => s.Indicators.TryGetValue(ind, out var v) && v == true);
                return new IndicatorSummary(ind, Students.Count > 0 ? (met * 100.0 / Students.Count) : 0, met);
            }).ToList();

            if (resultsPrev is not null)
            {
                PreviousIndicatorSummaries = IndicatorNames.Select(ind =>
                {
                    var prevMet = resultsPrev.Count(r => IndicatorMet(r, ind));
                    var denom   = resultsPrev.Count;
                    return new IndicatorSummary(ind, denom > 0 ? (prevMet * 100.0 / denom) : 0, prevMet);
                }).ToList();
            }

            // Quadrant counts
            CurrentQuadrantCounts = resultsCurrent
                .GroupBy(r => r.Quadrant ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            if (resultsPrev is not null)
            {
                PreviousQuadrantCounts = resultsPrev
                    .GroupBy(r => r.Quadrant ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            // Above-the-line (pp) metrics
            int get(Dictionary<string,int> dict, string key) => dict.TryGetValue(key, out var v) ? v : 0;

            TotalNow  = CurrentQuadrantCounts.Values.Sum();
            AboveNow  = get(CurrentQuadrantCounts, "Challenge") + get(CurrentQuadrantCounts, "Benchmark");
            AboveNowPct = TotalNow > 0 ? (AboveNow * 100.0 / TotalNow) : 0.0;

            if (PreviousQuadrantCounts is not null)
            {
                TotalPrev = PreviousQuadrantCounts.Values.Sum();
                AbovePrev = get(PreviousQuadrantCounts, "Challenge") + get(PreviousQuadrantCounts, "Benchmark");
                AbovePrevPct = TotalPrev > 0 ? (AbovePrev * 100.0 / TotalPrev) : 0.0;
                AboveDeltaPp = Math.Round(AboveNowPct - AbovePrevPct, 1);
            }
            else
            {
                TotalPrev = 0; AbovePrev = 0; AbovePrevPct = 0; AboveDeltaPp = 0;
            }

            return Page();
        }
    }
}
