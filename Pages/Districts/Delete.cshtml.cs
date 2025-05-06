using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DeleteModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public District District { get; set; } = new District();

        [TempData]
        public string? ErrorMessage { get; set; }


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
            var district = await _context.Districts
                .Include(d => d.Users)
                .Include(d => d.Schools)
                .FirstOrDefaultAsync(d => d.Id == District.Id);

            if (district == null)
            {
                return NotFound();
            }

            if (district.Users.Any() || district.Schools.Any())
            {
                ErrorMessage = "Cannot delete district while users or schools are still assigned.";
                return RedirectToPage("Delete", new { id = District.Id });
            }

            _context.Districts.Remove(district);
            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }
    }
}
