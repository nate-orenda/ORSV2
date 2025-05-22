using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class OverviewModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public OverviewModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int SchoolId { get; set; }

        public School School { get; set; } = new();
        public string CurrentCheckpoint { get; set; } = string.Empty;
        public List<(int Grade, int Count)> GradeDistribution { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var today = DateTime.Today;
            School = await _context.Schools.Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == SchoolId);

            if (School == null) return NotFound();

            // Determine current school year
            int schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;

            // Get grade distribution
            GradeDistribution = (await _context.GAResults
                .Where(r => r.SchoolId == SchoolId && r.SchoolYear == schoolYear)
                .GroupBy(r => r.Grade)
                .Select(g => new { Grade = g.Key, Count = g.Count() })
                .OrderBy(g => g.Grade)
                .ToListAsync())
                .Select(g => (g.Grade, g.Count))
                .ToList();


            // Determine current checkpoint
            var schedule = await _context.GACheckpointSchedule
                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);

            if (schedule != null)
            {
                var dates = new[]
                {
                    (1, schedule.Checkpoint1Date),
                    (2, schedule.Checkpoint2Date),
                    (3, schedule.Checkpoint3Date),
                    (4, schedule.Checkpoint4Date),
                    (5, schedule.Checkpoint5Date)
                };

                CurrentCheckpoint = dates
                .Where(d => d.Item2.HasValue && today <= d.Item2.Value)
                .Select(d => d.Item1.ToString())
                .FirstOrDefault() ?? "5";

                if (string.IsNullOrEmpty(CurrentCheckpoint)) CurrentCheckpoint = "5"; // fallback
            }

            return Page();
        }
    }
}