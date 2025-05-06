using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Schools
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
        public School School { get; set; } = new School();

        public IActionResult OnGet(int districtId)
        {
            School.DistrictId = districtId;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            _context.Schools.Add(School);
            await _context.SaveChangesAsync();

            return RedirectToPage("Index", new { districtId = School.DistrictId });
        }
    }
}
