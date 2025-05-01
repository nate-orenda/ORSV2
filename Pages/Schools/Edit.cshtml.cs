using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Schools
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
            var schoolToUpdate = await _context.Schools.FindAsync(School.Id);
            if (schoolToUpdate == null)
                return NotFound();

            schoolToUpdate.Name = School.Name;
            schoolToUpdate.LocalSchoolId = School.LocalSchoolId;
            schoolToUpdate.SchoolType = School.SchoolType;
            schoolToUpdate.CDSCode = School.CDSCode;
            schoolToUpdate.Notes = School.Notes;

            await _context.SaveChangesAsync();
            return RedirectToPage("Index", new { districtId = School.DistrictId });
        }
    }
}
