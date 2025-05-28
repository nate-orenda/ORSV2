using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

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


            var schedule = await _context.GACheckpointSchedule
                .FirstOrDefaultAsync(s => s.SchoolId == SchoolId);

            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp.ToString();

            return Page();
        }
    }
}