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
        [BindProperty(SupportsGet = true)] public string? Filter { get; set; }

        private static readonly Dictionary<string, string> FilterLabels = new()
        {
            ["ALL"] = "All students",
            ["SWD"] = "Students with Disabilities (SWD)",
            ["SED"] = "Low Income Students (SED)",
            ["EL"] = "English Learners (EL)",
            ["RFEP"] = "Redesignated English Proficient (RFEP)",
            ["G_M"] = "Gender (Male)",
            ["G_F"] = "Gender (Female)",
            ["G_X"] = "Gender (X)",
            ["RE_BLACK"] = "Black or African American",
            ["RE_HISP"] = "Hispanic or Latino",
            ["FOSTER"] = "Foster",
            ["MIGRANT"] = "Migrant",
            ["HOMELESS"] = "Homeless"
        };
        
        public string ActiveFilterLabel => FilterLabels.TryGetValue(Filter ?? "ALL", out var v) ? v : "All students";

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

            // Combine initial data loading in parallel
            var today = DateTime.Today;
            var (school, schedule) = await GetSchoolAndScheduleAsync();
            
            if (school == null) return NotFound();
            
            SchoolName = school.Name;
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp.ToString();

            var schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;
            var districtId = school.DistrictId;

            // Build the main query with filtering
            var studentsQuery = BuildStudentsQuery(schoolYear, cp);

            // Execute all queries in parallel
            var (students, indicators, stuIdMap) = await ExecuteQueriesAsync(studentsQuery, cp, districtId);

            Students = students;
            StuIdByLocal = stuIdMap;

            // Process indicator summaries
            ProcessIndicatorSummaries(indicators);

            // Set up breadcrumbs
            SetupBreadcrumbs(school);

            return Page();
        }

        private async Task<(School?, GACheckpointSchedule?)> GetSchoolAndScheduleAsync()
        {
            var school = await _context.Schools
                .AsNoTracking()
                .Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == SchoolId);

            var schedule = await _context.GACheckpointSchedule
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);

            return (school, schedule);
        }

        private IQueryable<GAResults> BuildStudentsQuery(int schoolYear, int cp)
        {
            var query = _context.GAResults
                .AsNoTracking()
                .Where(r => r.SchoolId == SchoolId && r.Grade == Grade && r.CP == cp && r.SchoolYear == schoolYear);

            // Apply filter efficiently
            return ApplyFilter(query);
        }

        private IQueryable<GAResults> ApplyFilter(IQueryable<GAResults> query)
        {
            var filter = (Filter ?? "ALL").ToUpperInvariant();

            return filter switch
            {
                "SWD" => query.Where(r => r.SWD),
                "SED" => query.Where(r => r.SED),
                "EL" => query.Where(r => r.LF == "EL" || r.LF == "English Learner"),
                "RFEP" => query.Where(r => r.LF == "RFEP" || r.LF == "Redesignated"),
                "G_M" => query.Where(r => r.Gender == "M"),
                "G_F" => query.Where(r => r.Gender == "F"),
                "G_X" => query.Where(r => r.Gender == "X"),
                "RE_BLACK" => query.Where(r => r.RaceEthnicity == "Black or African American"),
                "RE_HISP" => query.Where(r => r.RaceEthnicity == "Hispanic or Latino"),
                "FOSTER" => query.Where(r => r.Foster),
                "MIGRANT" => query.Where(r => r.Migrant),
                "HOMELESS" => query.Where(r => r.Homeless),
                _ => query // "ALL" or default
            };
        }

        private async Task<(List<GAResults>, List<GAQuadrantIndicators>, Dictionary<string, int>)> ExecuteQueriesAsync(
            IQueryable<GAResults> studentsQuery, int cp, int districtId)
        {
            // Execute students query first
            var students = await studentsQuery.ToListAsync();
            
            // Collect local IDs from the results
            var localIds = students
                .Where(s => !string.IsNullOrWhiteSpace(s.LocalStudentId))
                .Select(s => s.LocalStudentId)
                .Distinct()
                .ToList();

            // Execute remaining queries sequentially to avoid DbContext concurrency issues
            var indicators = await _context.GAQuadrantIndicators
                .AsNoTracking()
                .Where(i => i.Grade == Grade && i.CP == cp && i.IsEnabled == true &&
                           (i.SchoolId == null || i.SchoolId == SchoolId) &&
                           (i.DistrictId == null || i.DistrictId == districtId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            var stuIdMap = localIds.Count == 0
                ? new Dictionary<string, int>()
                : await _context.STU
                    .AsNoTracking()
                    .Where(st => st.SchoolID == SchoolId && localIds.Contains(st.LocalStudentID))
                    .ToDictionaryAsync(st => st.LocalStudentID, st => st.StuId);

            return (students, indicators, stuIdMap);
        }

        private void ProcessIndicatorSummaries(List<GAQuadrantIndicators> indicators)
        {
            // Build indicator counts efficiently using a single pass
            var indicatorCounts = CalculateIndicatorCounts();
            
            // Build quadrant summaries
            BuildQuadrantSummaries();

            // Create indicator summaries
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
        }

        private Dictionary<string, int> CalculateIndicatorCounts()
        {
            var counts = new Dictionary<string, int>
            {
                ["OnTrack"] = 0, ["GPA"] = 0, ["AGGrades"] = 0, ["AGSchedule"] = 0,
                ["Affiliation"] = 0, ["FAFSA"] = 0, ["CollegeApplication"] = 0,
                ["Attendance"] = 0, ["Referrals"] = 0, ["Grades"] = 0, 
                ["ELA"] = 0, ["Math"] = 0
            };

            // Single pass through students
            foreach (var student in Students)
            {
                if (student.OnTrack == true) counts["OnTrack"]++;
                if (student.GPA == true) counts["GPA"]++;
                if (student.AGGrades == true) counts["AGGrades"]++;
                if (student.AGSchedule == true) counts["AGSchedule"]++;
                if (student.Affiliation == true) counts["Affiliation"]++;
                if (student.FAFSA == true) counts["FAFSA"]++;
                if (student.CollegeApplication == true) counts["CollegeApplication"]++;
                if (student.Attendance == true) counts["Attendance"]++;
                if (student.Referrals == true) counts["Referrals"]++;
                if (student.Grades == true) counts["Grades"]++;
                if (student.AssessmentsELA == true) counts["ELA"]++;
                if (student.AssessmentsMath == true) counts["Math"]++;
            }

            return counts;
        }

        private void BuildQuadrantSummaries()
        {
            QuadrantSummaries = Students
                .Where(s => !string.IsNullOrEmpty(s.Quadrant))
                .GroupBy(s => new { s.Grade, s.Quadrant })
                .Select(g => new QuadrantSummary(g.Key.Grade, g.Key.Quadrant!, g.Count()))
                .ToList();
        }

        private void SetupBreadcrumbs(School school)
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = school.District?.Name ?? "District", Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = school.DistrictId }) },
                new BreadcrumbItem { Title = school.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = school.Id }) },
                new BreadcrumbItem { Title = $"Grade {Grade}" }
            };
        }

        // Optimized target group creation
        public async Task<IActionResult> OnPostCreateTargetGroupAsync(
            int schoolId,
            string groupName,
            string? note,
            string studentIds)
        {
            if (!await AuthorizeAsync(schoolId)) return Forbid();
            if (string.IsNullOrWhiteSpace(groupName)) return BadRequest("Group name is required.");

            var ids = ParseStudentIds(studentIds);
            if (ids.Count == 0) return BadRequest("At least one StudentId is required.");

            // Create group and validate student IDs in a transaction
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var group = new TargetGroup
                {
                    Name = groupName.Trim(),
                    SchoolId = schoolId,
                    CreatedAt = DateTime.UtcNow,
                    Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
                };
                
                _context.TargetGroups.Add(group);
                await _context.SaveChangesAsync();

                // Validate and add students in one query
                var validStudentIds = await _context.STU
                    .AsNoTracking()
                    .Where(s => s.SchoolID == schoolId && ids.Contains(s.StuId))
                    .Select(s => s.StuId)
                    .ToListAsync();

                if (validStudentIds.Count > 0)
                {
                    var links = validStudentIds.Select(id => new TargetGroupStudent
                    {
                        TargetGroupId = group.Id,
                        StudentId = id
                    }).ToList();
                    
                    _context.TargetGroupStudents.AddRange(links);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                
                return RedirectToPage("/GuidanceAlignment/CompareCP", new { id = group.Id });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static List<int> ParseStudentIds(string studentIds)
        {
            return (studentIds ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var val) ? (int?)val : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Distinct()
                .ToList();
        }
    }
}