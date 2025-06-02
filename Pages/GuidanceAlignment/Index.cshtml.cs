using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class IndexModel : GABasePageModel
    {
        public IndexModel(ApplicationDbContext context) : base(context) {}

        public List<District> Districts { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync())
                return Forbid();

            Districts = await _context.Districts
                .Where(d => !d.Inactive && AllowedDistrictIds.Contains(d.Id))
                .OrderBy(d => d.Name)
                .ToListAsync();

            return Page();
        }

    }
}