using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public EditModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public District District { get; set; } = new District();

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var district = await _context.Districts.FindAsync(id);
            if (district == null)
            {
                return NotFound();
            }

            District = district;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var districtToUpdate = await _context.Districts.FindAsync(District.Id);
            if (districtToUpdate == null)
            {
                return NotFound();
            }

            // Only update API fields if new values were provided
            if (!string.IsNullOrWhiteSpace(District.SISApiKey))
            {
                districtToUpdate.SISApiKey = District.SISApiKey;
            }

            if (!string.IsNullOrWhiteSpace(District.SISApiSecret))
            {
                districtToUpdate.SISApiSecret = District.SISApiSecret;
            }

            // Always update basic fields
            districtToUpdate.Name = District.Name;
            districtToUpdate.CDSCode = District.CDSCode;
            districtToUpdate.SISBaseUrl = District.SISBaseUrl;
            districtToUpdate.Notes = District.Notes;

            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
