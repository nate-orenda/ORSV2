using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CreateModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public District District { get; set; } = new District();

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            District.Id = Guid.NewGuid(); // Set new ID
            _context.Districts.Add(District);
            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }


    }
}
