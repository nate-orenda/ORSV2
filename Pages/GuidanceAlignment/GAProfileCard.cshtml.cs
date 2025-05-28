using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class GAProfileCardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public GAProfileCardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public GAResults Student { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Student = await _context.GAResults.FirstOrDefaultAsync(s => s.ResultId == id);
            if (Student == null) return NotFound();
            return Page();
        }
    }
}