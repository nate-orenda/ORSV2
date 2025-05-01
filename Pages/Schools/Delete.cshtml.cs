using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Schools
{
    [Authorize(Roles = "OrendaAdmin")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DeleteModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public School School { get; set; } = new School();

        public async Task<IActionResult> OnGetAsync(Guid id)
        {
            var school = await _context.Schools.FindAsync(id);
            if (school == null)
                return NotFound();

            School = school;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var school = await _context.Schools.FindAsync(School.Id);
            if (school == null)
                return NotFound();

            _context.Schools.Remove(school);
            await _context.SaveChangesAsync();

            return RedirectToPage("Index", new { districtId = School.DistrictId });
        }
    }
}
