using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class SchoolsModel : GABasePageModel
    {
        public SchoolsModel(ApplicationDbContext context) : base(context) {}

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }

        public List<School> Schools { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync())
                return Forbid();

            Schools = await _context.Schools
                .Where(s => !s.Inactive &&
                    s.DistrictId == DistrictId &&
                    AllowedSchoolIds.Contains(s.Id))
                .OrderBy(s => s.Name)
                .ToListAsync();

            return Page();
        }
    }
}