using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<District> Districts { get; set; } = new();

        public async Task OnGetAsync()
        {
            Districts = await _context.Districts
                .Where(d => !d.Inactive)
                .OrderBy(d => d.Name)
                .ToListAsync();
        }
    }
}