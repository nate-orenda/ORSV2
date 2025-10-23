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
            public int ResultId { get; set; }
            public string LocalStudentId { get; set; } = "";
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string? Grade { get; set; }
            public string? Quadrant { get; set; }
            public string? PreviousQuadrant { get; set; }
            public Dictionary<string, bool?> Indicators { get; set; } = new();
            public Dictionary<string, bool?> PreviousIndicators { get; set; } = new();
            public Dictionary<string, int> IndicatorChange { get; set; } = new(); // 1=improved, 0=same, -1=declined
        }

        public sealed class MovementRow
        {
            public string From { get; set; } = "";
            public string To   { get; set; } = "";
            public int Count   { get; set; }
            public int Delta   { get; set; }
        }

        public class QuadrantMoveComparer : IEqualityComparer<(string From, string To)>
        {
            public bool Equals((string From, string To) x, (string From, string To) y) =>
                string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.To, y.To, StringComparison.OrdinalIgnoreCase);

            public int GetHashCode((string From, string To) obj) =>
                HashCode.Combine(
                    obj.From?.ToLowerInvariant().GetHashCode() ?? 0,
                    obj.To?.ToLowerInvariant().GetHashCode() ?? 0
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
        public int Grade { get; set; }
        public int SchoolId { get; set; }
        public bool IsGroupMode { get; set; }

        public List<MovementRow> MovementMatrix { get; set; } = new();
        public int MovementUp { get; set; }
        public int MovementDown { get; set; }
        public int MovementSame { get; set; }

        public int AbovePrev { get; set; }
        public int AboveNow  { get; set; }
        public int TotalPrev { get; set; }
        public int TotalNow  { get; set; }
        public double AbovePrevPct { get; set; }
        public double AboveNowPct  { get; set; }
        public double AboveDeltaPp { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CompareFrom { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? CompareTo { get; set; }

        public int CompareFromCheckpoint { get; set; }
        public int CompareToCheckpoint { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? FilterMovement { get; set; } // "up", "down", "same"
        public List<StudentIndicatorRow> FilteredStudents { get; set; } = new();

        // ===== Helpers =====
        private static string NormalizeIndicatorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            return name.Trim() switch
            {
                "A-G Grades" => "AGGrades",
                "A-G Schedule" => "AGSchedule",
                "AssessmentsELA" => "ELA",
                "AssessmentsMath" => "Math",
                "OnTrack" => "OnTrack",
                "GPA" => "GPA",
                "AGGrades" => "AGGrades",
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
            _ => 1
        };

        private static bool IndicatorMet(GAResults r, string indicatorName)
        {
            var ind = NormalizeIndicatorName(indicatorName);
            return ind switch
            {
                "OnTrack" => r.OnTrack == true,
                "GPA" => r.GPA == true,
                "AGGrades" => r.AGGrades == true,
                "AGSchedule" => r.AGSchedule == true,
                "Affiliation" => r.Affiliation == true,
                "FAFSA" => r.FAFSA == true,
                "CollegeApplication" => r.CollegeApplication == true,
                "Attendance" => r.Attendance == true,
                "Referrals" => r.Referrals == true,
                "Grades" => r.Grades == true,
                "ELA" => r.AssessmentsELA == true,
                "Math" => r.AssessmentsMath == true,
                _ => false
            };
        }

        // ===== Handler =====
        public async Task<IActionResult> OnGetAsync(int? id, int? schoolId, int? grade)
        {
            if (!await AuthorizeAsync())
                return Forbid();

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
                    return BadRequest("When no Focus Group id is provided, pass ?schoolId={id}&grade={number}");

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

            ViewData["Title"] = $"Focus Group - {GroupName}";

            var schedule = await _context.GACheckpointSchedule
                                .AsNoTracking()
                                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);
            var today = DateTime.Today;
            CurrentCheckpoint = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);

            if (CompareFrom.HasValue && CompareTo.HasValue)
            {
                if (CompareFrom.Value >= 1 && CompareFrom.Value <= CurrentCheckpoint &&
                    CompareTo.Value >= 1 && CompareTo.Value <= CurrentCheckpoint &&
                    CompareFrom.Value != CompareTo.Value)
                {
                    CompareFromCheckpoint = CompareFrom.Value;
                    CompareToCheckpoint = CompareTo.Value;
                }
                else
                {
                    CompareToCheckpoint = CurrentCheckpoint;
                    CompareFromCheckpoint = CurrentCheckpoint > 1 ? CurrentCheckpoint - 1 : CurrentCheckpoint;
                }
            }
            else
            {
                CompareToCheckpoint = CurrentCheckpoint;
                CompareFromCheckpoint = CurrentCheckpoint > 1 ? CurrentCheckpoint - 1 : CurrentCheckpoint;
            }

            PreviousCheckpoint = CompareFromCheckpoint;

            var currentSchoolYear = CurrentCheckpointHelper.GetCurrentSchoolYear(today);

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

            var stuDirectory = await _context.STU.AsNoTracking()
                .Where(s => s.SchoolID == SchoolId && studentIds.Contains(s.StuId))
                .Select(s => new { s.StuId, s.LocalStudentID, s.FirstName, s.LastName, s.Grade })
                .ToListAsync();

            if (IsGroupMode)
            {
                Grade = stuDirectory
                    .Select(s => int.TryParse(s.Grade, out var g) ? g : 0)
                    .DefaultIfEmpty(0)
                    .Max();
            }

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

            var resultsCurrent = await _context.GAResults.AsNoTracking()
                .Where(r => r.SchoolId == SchoolId
                            && r.CP == CompareToCheckpoint
                            && r.SchoolYear == currentSchoolYear
                            && studentIds.Contains(r.StudentId))
                .ToListAsync();

            var resultsPrev = await _context.GAResults.AsNoTracking()
                .Where(r => r.SchoolId == SchoolId
                            && r.CP == CompareFromCheckpoint
                            && r.SchoolYear == currentSchoolYear
                            && studentIds.Contains(r.StudentId))
                .ToListAsync();

            var byStuIdCur = resultsCurrent
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.First());
            var byStuIdPrev = resultsPrev
                .GroupBy(r => r.StudentId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var s in stuDirectory.OrderBy(s => s.LastName).ThenBy(s => s.FirstName))
            {
                byStuIdCur.TryGetValue(s.StuId, out var cur);
                byStuIdPrev.TryGetValue(s.StuId, out var prev);
                
                var row = new StudentIndicatorRow
                {
                    StuId = s.StuId,
                    ResultId = cur?.ResultId ?? 0,
                    LocalStudentId = s.LocalStudentID ?? "",
                    FirstName = s.FirstName ?? "",
                    LastName = s.LastName ?? "",
                    Grade = s.Grade,
                    Quadrant = cur?.Quadrant,
                    PreviousQuadrant = prev?.Quadrant
                };

                foreach (var raw in IndicatorNames)
                {
                    var ind = NormalizeIndicatorName(raw);
                    
                    bool? val = ind switch
                    {
                        "OnTrack" => cur?.OnTrack,
                        "GPA" => cur?.GPA,
                        "AGGrades" => cur?.AGGrades,
                        "AGSchedule" => cur?.AGSchedule,
                        "Affiliation" => cur?.Affiliation,
                        "FAFSA" => cur?.FAFSA,
                        "CollegeApplication" => cur?.CollegeApplication,
                        "Attendance" => cur?.Attendance,
                        "Referrals" => cur?.Referrals,
                        "Grades" => cur?.Grades,
                        "ELA" => cur?.AssessmentsELA,
                        "Math" => cur?.AssessmentsMath,
                        _ => null
                    };
                    
                    bool? prevVal = ind switch
                    {
                        "OnTrack" => prev?.OnTrack,
                        "GPA" => prev?.GPA,
                        "AGGrades" => prev?.AGGrades,
                        "AGSchedule" => prev?.AGSchedule,
                        "Affiliation" => prev?.Affiliation,
                        "FAFSA" => prev?.FAFSA,
                        "CollegeApplication" => prev?.CollegeApplication,
                        "Attendance" => prev?.Attendance,
                        "Referrals" => prev?.Referrals,
                        "Grades" => prev?.Grades,
                        "ELA" => prev?.AssessmentsELA,
                        "Math" => prev?.AssessmentsMath,
                        _ => null
                    };
                    
                    row.Indicators[ind] = val;
                    row.PreviousIndicators[ind] = prevVal;
                    
                    if (prevVal == null || val == null)
                        row.IndicatorChange[ind] = 0;
                    else if (prevVal == false && val == true)
                        row.IndicatorChange[ind] = 1;
                    else if (prevVal == true && val == false)
                        row.IndicatorChange[ind] = -1;
                    else
                        row.IndicatorChange[ind] = 0;
                }
                Students.Add(row);
                
            }
            if (!string.IsNullOrEmpty(FilterMovement))
            {
                FilteredStudents = FilterMovement.ToLower() switch
                {
                    "up" => Students.Where(s => {
                        if (string.IsNullOrEmpty(s.PreviousQuadrant) || string.IsNullOrEmpty(s.Quadrant)) return false;
                        var prevRank = Rank(s.PreviousQuadrant);
                        var curRank = Rank(s.Quadrant);
                        return curRank > prevRank;
                    }).ToList(),
                    
                    "down" => Students.Where(s => {
                        if (string.IsNullOrEmpty(s.PreviousQuadrant) || string.IsNullOrEmpty(s.Quadrant)) return false;
                        var prevRank = Rank(s.PreviousQuadrant);
                        var curRank = Rank(s.Quadrant);
                        return curRank < prevRank;
                    }).ToList(),
                    
                    "same" => Students.Where(s => {
                        if (string.IsNullOrEmpty(s.PreviousQuadrant) || string.IsNullOrEmpty(s.Quadrant)) return false;
                        return s.PreviousQuadrant.Equals(s.Quadrant, StringComparison.OrdinalIgnoreCase);
                    }).ToList(),
                    
                    _ => Students
                };
            }
            else
            {
                FilteredStudents = Students;
            }

            MovementMatrix = new();
            MovementUp = MovementDown = MovementSame = 0;

            if (byStuIdPrev.Count > 0)
            {
                string Norm(string? q) => string.IsNullOrWhiteSpace(q) ? "Unknown" : q;

                var prevByStuQ = byStuIdPrev.ToDictionary(k => k.Key, v => Norm(v.Value.Quadrant));
                var curByStuQ = byStuIdCur.ToDictionary(k => k.Key, v => Norm(v.Value.Quadrant));

                var agg = new Dictionary<(string From, string To), int>(new QuadrantMoveComparer());

                foreach (var kv in curByStuQ)
                {
                    if (!prevByStuQ.TryGetValue(kv.Key, out var from)) continue;
                    var to = kv.Value;

                    var key = (from, to);
                    agg[key] = agg.TryGetValue(key, out var c) ? c + 1 : 1;

                    var delta = Rank(to) - Rank(from);
                    if (delta > 0) MovementUp++;
                    else if (delta < 0) MovementDown++;
                    else MovementSame++;
                }

                MovementMatrix = agg
                    .Where(kvp => !string.Equals(kvp.Key.From, kvp.Key.To, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => new MovementRow
                    {
                        From = kvp.Key.From,
                        To = kvp.Key.To,
                        Count = kvp.Value,
                        Delta = Rank(kvp.Key.To) - Rank(kvp.Key.From)
                    })
                    .OrderByDescending(m => m.Count)
                    .ToList();
            }

            CurrentIndicatorSummaries = IndicatorNames.Select(ind =>
            {
                var met = Students.Count(s => s.Indicators.TryGetValue(ind, out var v) && v == true);
                return new IndicatorSummary(ind, Students.Count > 0 ? (met * 100.0 / Students.Count) : 0, met);
            }).ToList();

            if (resultsPrev.Any())
            {
                PreviousIndicatorSummaries = IndicatorNames.Select(ind =>
                {
                    var prevMet = resultsPrev.Count(r => IndicatorMet(r, ind));
                    var denom = resultsPrev.Count;
                    return new IndicatorSummary(ind, denom > 0 ? (prevMet * 100.0 / denom) : 0, prevMet);
                }).ToList();
            }

            CurrentQuadrantCounts = resultsCurrent
                .GroupBy(r => r.Quadrant ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            if (resultsPrev.Any())
            {
                PreviousQuadrantCounts = resultsPrev
                    .GroupBy(r => r.Quadrant ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            int get(Dictionary<string, int> dict, string key) => dict.TryGetValue(key, out var v) ? v : 0;

            TotalNow = CurrentQuadrantCounts.Values.Sum();
            AboveNow = get(CurrentQuadrantCounts, "Challenge") + get(CurrentQuadrantCounts, "Benchmark");
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