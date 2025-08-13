using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class SchoolsModel : GABasePageModel
    {
        public SchoolsModel(ApplicationDbContext context) : base(context) { }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }

        public List<School> Schools { get; set; } = new();

        public string DistrictName { get; set; } = "";

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync())
                return Forbid();

            // Get district name
            var district = await _context.Districts
                .AsNoTracking()
                .Where(d => d.Id == DistrictId)
                .FirstOrDefaultAsync();

            DistrictName = district?.Name ?? "Unknown District";

            ViewData["Breadcrumbs"] = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = DistrictName } // current page, no URL
            };

            // Load schools
            Schools = await _context.Schools
                .AsNoTracking()
                .Where(s => !s.Inactive &&
                            s.DistrictId == DistrictId &&
                            AllowedSchoolIds.Contains(s.Id))
                .OrderBy(s => s.Name)
                .ToListAsync();

            return Page();
        }

    }
}