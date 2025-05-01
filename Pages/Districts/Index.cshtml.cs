using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Models;
using ORSV2.Data;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<District> Districts { get; set; } = new List<District>();

        public async Task OnGetAsync()
        {
            Districts = await _context.Districts
                .OrderBy(d => d.Name)
                .ToListAsync();
        }
    }
}
