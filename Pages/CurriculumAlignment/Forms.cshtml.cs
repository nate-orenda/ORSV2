using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.CurriculumAlignment
{
    public class FormsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public FormsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
        public string DistrictName { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync()
        {
            if (DistrictId == 0)
            {
                return NotFound("A District ID is required.");
            }

            var district = await _context.Districts.FindAsync(DistrictId);

            if (district == null)
            {
                return NotFound($"District with ID {DistrictId} not found.");
            }

            DistrictName = district.Name;

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Curriculum Alignment", Url = Url.Page("/CurriculumAlignment/Index") },
                new BreadcrumbItem { Title = $"{DistrictName} - Select Forms" } // current page; no URL
            };

            return Page();
        }
    }
}