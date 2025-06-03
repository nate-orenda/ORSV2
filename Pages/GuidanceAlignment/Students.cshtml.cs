using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class StudentsModel : GABasePageModel
    {
        public StudentsModel(ApplicationDbContext context) : base(context) {}

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
        public Dictionary<string, int> QuadrantCounts => QuadrantSummaries
            .Where(q => !string.IsNullOrEmpty(q.Quadrant))
            .GroupBy(q => q.Quadrant!)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        public string FormatPercentage(int part) =>
            TotalCount > 0 ? $"{(part * 100.0 / TotalCount):0.#}%" : "0%";

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync(SchoolId))
                return Forbid();

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
                .Where(r => r.SchoolId == SchoolId && r.Grade == Grade && r.CP == cp && r.SchoolYear == schoolYear)
                .ToListAsync();

            // Quadrant breakdown for this grade level
           QuadrantSummaries = await _context.GAResults
            .Where(r => r.SchoolId == SchoolId && r.CP == cp && r.Grade == Grade)
            .GroupBy(r => new { r.Grade, r.Quadrant })
            .Select(g => new QuadrantSummary(g.Key.Grade, g.Key.Quadrant, g.Count()))
            .ToListAsync();

            // Indicator performance based on enabled indicators
            var indicators = await _context.GAQuadrantIndicators
                .Where(i => i.Grade == Grade && i.CP == cp && i.IsEnabled == true &&
                    (i.SchoolId == null || i.SchoolId == SchoolId) &&
                    (i.DistrictId == null || i.DistrictId == districtId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            foreach (var ind in indicators)
            {
                double metCount = ind.IndicatorName switch
                {
                    "OnTrack" => Students.Count(s => s.OnTrack == true),
                    "GPA" => Students.Count(s => s.GPA == true),
                    "AGGrades" => Students.Count(s => s.AGGrades == true),
                    "AGSchedule" => Students.Count(s => s.AGSchedule == true),
                    "Affiliation" => Students.Count(s => s.Affiliation == true),
                    "FAFSA" => Students.Count(s => s.FAFSA == true),
                    "CollegeApplication" => Students.Count(s => s.CollegeApplication == true),
                    "Attendance" => Students.Count(s => s.Attendance == true), // ✅ Added line
                    _ => 0
                };

                IndicatorSummaries.Add(new IndicatorSummary
                {
                    Name = ind.IndicatorName,
                    PercentMet = Students.Count > 0 ? (metCount * 100.0 / Students.Count) : 0,
                    CountMet = (int)metCount
                });
            }

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = school.District.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = school.DistrictId }) },
                new BreadcrumbItem { Title = school.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = school.Id }) },
                new BreadcrumbItem { Title = $"Grade {Grade}" }
            };

            return Page();
        }
    }
}