using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class StudentsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public StudentsModel(ApplicationDbContext context) => _context = context;

        [BindProperty(SupportsGet = true)] public int SchoolId { get; set; }
        [BindProperty(SupportsGet = true)] public int Grade { get; set; }

        public string CurrentCheckpoint { get; set; } = "";
        public string SchoolName { get; set; } = "";
        public List<GAResults> Students { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var today = DateTime.Today;
            var school = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == SchoolId);
            if (school == null) return NotFound();

            SchoolName = school.Name;

            var schedule = await _context.GACheckpointSchedule.FirstOrDefaultAsync(s => s.SchoolId == SchoolId);
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            CurrentCheckpoint = cp.ToString();

            var schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;

            Students = await _context.GAResults
                .Where(r => r.SchoolId == SchoolId && r.Grade == Grade && r.CP == cp && r.SchoolYear == schoolYear)
                .OrderBy(r => r.LastName)
                .ThenBy(r => r.FirstName)
                .ToListAsync();

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