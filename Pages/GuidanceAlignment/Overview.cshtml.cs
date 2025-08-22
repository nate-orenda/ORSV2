using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class OverviewModel : GABasePageModel
    {
        public OverviewModel(ApplicationDbContext context) : base(context) {}

        [BindProperty(SupportsGet = true)]
        public int SchoolId { get; set; }
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
        public School? School { get; set; } = new();
        public string CurrentCheckpoint { get; set; } = string.Empty;
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
            School = await _context.Schools.Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == SchoolId);

            if (School == null) return NotFound();

            // 1. Define the valid grade range for this school based on its properties.
            //    Explicitly cast the short? to int to resolve the method ambiguity.
            var startGrade = Math.Max(0, (int)(School.LowGrade ?? 0));
            var endGrade = Math.Min(12, (int)(School.HighGrade ?? 12));

            // Determine current school year
            int schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;

            var schedule = await _context.GACheckpointSchedule
                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);

            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp.ToString();

            // 2. The query for QuadrantSummaries is now filtered by the grade range.
            //    This ensures all subsequent calculations (QuadrantCounts, TotalCount, etc.)
            //    are based only on data from the relevant grades.
            QuadrantSummaries = await _context.GAResults
                .Where(r => r.SchoolId == SchoolId && 
                            r.CP == cp && 
                            r.SchoolYear == schoolYear &&
                            r.Grade >= startGrade && r.Grade <= endGrade) // <-- Filter added here
                .GroupBy(r => new { r.Grade, r.Quadrant })
                .Select(g => new QuadrantSummary(g.Key.Grade, g.Key.Quadrant ?? "Unknown", g.Count()))
                .ToListAsync();

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = School!.District!.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
                new BreadcrumbItem { Title = School.Name }
            };

            return Page();
        }
    }
}
