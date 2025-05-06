using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Students
{
    public class ProfileCardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ProfileCardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public STU Student { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Student = await _context.STU.FirstOrDefaultAsync(s => s.STU_ID == id);
            if (Student == null) return NotFound();
            return Page();
        }
    }
}
