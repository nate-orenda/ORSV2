using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class StudentsModel : GABasePageModel
    {
        public StudentsModel(ApplicationDbContext context) : base(context) { }

        [BindProperty(SupportsGet = true)] public int SchoolId { get; set; }
        [BindProperty(SupportsGet = true)] public int Grade { get; set; }

        public string CurrentCheckpoint { get; set; } = "";
        public string SchoolName { get; set; } = "";
        public List<GAResults> Students { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public class IndicatorSummary
        {
            public string Name { get; set; } = string.Empty;
            public double PercentMet { get; set; }
            public int CountMet { get; set; }
        }

        public List<IndicatorSummary> IndicatorSummaries { get; set; } = new();
        public record QuadrantSummary(int Grade, string Quadrant, int Count);
        public List<QuadrantSummary> QuadrantSummaries { get; set; } = new();
        public int AboveTheLineCount => QuadrantSummaries.Where(q => q.Quadrant is "Challenge" or "Benchmark").Sum(q => q.Count);
        public int BelowTheLineCount => QuadrantSummaries.Where(q => q.Quadrant is "Strategic" or "Intensive").Sum(q => q.Count);
        public int TotalCount => AboveTheLineCount + BelowTheLineCount;

        // LocalStudentId -> StuId (for UI to emit data-student-id = StuId)
        public Dictionary<string, int> StuIdByLocal { get; set; } = new();

        public Dictionary<string, int> QuadrantCounts => QuadrantSummaries
            .Where(q => !string.IsNullOrEmpty(q.Quadrant))
            .GroupBy(q => q.Quadrant!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        public string FormatPercentage(int part) =>
            TotalCount > 0 ? $"{(part * 100.0 / TotalCount):0.#}%" : "0%";

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync(SchoolId)) return Forbid();

            var today = DateTime.Today;
            var school = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == SchoolId);
            if (school == null) return NotFound();
            SchoolName = school.Name;

            var schedule = await _context.GACheckpointSchedule.FirstOrDefaultAsync(s => s.SchoolId == SchoolId);
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp.ToString();

            var schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;
            var districtId = school.DistrictId;

            Students = await _context.GAResults
                .AsNoTracking()
                .Where(r => r.SchoolId == SchoolId && r.Grade == Grade && r.CP == cp && r.SchoolYear == schoolYear)
                .Select(r => new GAResults
                {
                    ResultId = r.ResultId,
                    LocalStudentId = r.LocalStudentId,
                    LastName = r.LastName,
                    FirstName = r.FirstName,
                    Grade = r.Grade,
                    CounselorName = r.CounselorName,
                    RaceEthnicity = r.RaceEthnicity,
                    SWD = r.SWD,
                    SED = r.SED,
                    LF = r.LF,
                    YrsInProgram = r.YrsInProgram,
                    CurrentGPA = r.CurrentGPA,
                    CumulativeGPA = r.CumulativeGPA,
                    CreditsCompleted = r.CreditsCompleted,
                    OnTrack = r.OnTrack,
                    GPA = r.GPA,
                    AGGrades = r.AGGrades,
                    AGSchedule = r.AGSchedule,
                    Affiliation = r.Affiliation,
                    FAFSA = r.FAFSA,
                    CollegeApplication = r.CollegeApplication,
                    Attendance = r.Attendance,
                    Referrals = r.Referrals,
                    Grades = r.Grades,
                    AssessmentsELA = r.AssessmentsELA,
                    AssessmentsMath = r.AssessmentsMath,
                    Quadrant = r.Quadrant
                })
                .ToListAsync();

            // Build indicator/quadrant summaries (unchanged)
            var quadrantDict = new Dictionary<(int Grade, string Quadrant), int>();
            var indicatorCounts = new Dictionary<string, int>
            {
                ["OnTrack"] = 0, ["GPA"] = 0, ["AGGrades"] = 0, ["AGSchedule"] = 0,
                ["Affiliation"] = 0, ["FAFSA"] = 0, ["CollegeApplication"] = 0,
                ["Attendance"] = 0, ["Referrals"] = 0, ["Grades"] = 0, ["ELA"] = 0, ["Math"] = 0
            };

            foreach (var s in Students)
            {
                if (s.OnTrack == true) indicatorCounts["OnTrack"]++;
                if (s.GPA == true) indicatorCounts["GPA"]++;
                if (s.AGGrades == true) indicatorCounts["AGGrades"]++;
                if (s.AGSchedule == true) indicatorCounts["AGSchedule"]++;
                if (s.Affiliation == true) indicatorCounts["Affiliation"]++;
                if (s.FAFSA == true) indicatorCounts["FAFSA"]++;
                if (s.CollegeApplication == true) indicatorCounts["CollegeApplication"]++;
                if (s.Attendance == true) indicatorCounts["Attendance"]++;
                if (s.Referrals == true) indicatorCounts["Referrals"]++;
                if (s.Grades == true) indicatorCounts["Grades"]++;
                if (s.AssessmentsELA == true) indicatorCounts["ELA"]++;
                if (s.AssessmentsMath == true) indicatorCounts["Math"]++;

                if (!string.IsNullOrEmpty(s.Quadrant))
                {
                    var key = (s.Grade, s.Quadrant!);
                    quadrantDict[key] = quadrantDict.TryGetValue(key, out var q) ? q + 1 : 1;
                }
            }

            QuadrantSummaries = quadrantDict
                .Select(kvp => new QuadrantSummary(kvp.Key.Grade, kvp.Key.Quadrant, kvp.Value))
                .ToList();

            var indicators = await _context.GAQuadrantIndicators
                .AsNoTracking()
                .Where(i => i.Grade == Grade && i.CP == cp && i.IsEnabled == true &&
                            (i.SchoolId == null || i.SchoolId == SchoolId) &&
                            (i.DistrictId == null || i.DistrictId == districtId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            foreach (var ind in indicators)
            {
                indicatorCounts.TryGetValue(ind.IndicatorName, out var metCount);
                IndicatorSummaries.Add(new IndicatorSummary
                {
                    Name = ind.IndicatorName,
                    PercentMet = Students.Count > 0 ? (metCount * 100.0 / Students.Count) : 0,
                    CountMet = metCount
                });
            }

            // Build LocalStudentId -> StuId map for these rows
            var locals = Students.Select(s => s.LocalStudentId)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct()
                                .ToList();

            StuIdByLocal = locals.Count == 0
                ? new Dictionary<string, int>()
                : await _context.STU
                    .AsNoTracking()
                    .Where(st => st.SchoolID == SchoolId && locals.Contains(st.LocalStudentID))
                    .ToDictionaryAsync(st => st.LocalStudentID, st => st.StuId);


            // Breadcrumbs BEFORE returning
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = school.District?.Name ?? "District", Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = school.DistrictId }) },
                new BreadcrumbItem { Title = school.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = school.Id }) },
                new BreadcrumbItem { Title = $"Grade {Grade}" }
            };

            return Page();
        }

        public sealed class CreateGroupDto
        {
            public int SchoolId { get; set; }
            public string GroupName { get; set; } = string.Empty;
            public List<int> StudentIds { get; set; } = new();
            public string? Note { get; set; }
        }

        public async Task<IActionResult> OnPostCreateTargetGroupAsync([FromBody] CreateGroupDto data)
        {
            if (!await AuthorizeAsync(data.SchoolId)) return Forbid();
            if (string.IsNullOrWhiteSpace(data.GroupName) || data.StudentIds.Count == 0)
                return BadRequest("Group name and at least one StudentId are required.");

            var group = new TargetGroup {
                Name = data.GroupName.Trim(),
                SchoolId = data.SchoolId,
                CreatedAt = DateTime.UtcNow,
                Note = string.IsNullOrWhiteSpace(data.Note) ? null : data.Note.Trim()  // <— NEW
            };
            _context.TargetGroups.Add(group);
            await _context.SaveChangesAsync();

            // Validate provided IDs exist in STU
            // validate StudentIds belong to this school (use STU.SchoolID)
            var valid = await _context.STU.AsNoTracking()
                .Where(s => s.SchoolID == data.SchoolId && data.StudentIds.Contains(s.StuId))
                .Select(s => s.StuId)
                .ToListAsync();

            var existing = await _context.TargetGroupStudents.AsNoTracking()
                .Where(t => t.TargetGroupId == group.Id && valid.Contains(t.StudentId))
                .Select(t => t.StudentId)
                .ToListAsync();

            var toAdd = valid.Except(existing)
                .Select(id => new TargetGroupStudent { TargetGroupId = group.Id, StudentId = id })
                .ToList();

            if (toAdd.Count > 0)
            {
                _context.TargetGroupStudents.AddRange(toAdd);
                await _context.SaveChangesAsync();
            }

            return new JsonResult(new { newGroupId = group.Id, added = toAdd.Count });
        }

    }
}
