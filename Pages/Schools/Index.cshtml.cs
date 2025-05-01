using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Schools
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public Guid DistrictId { get; set; }
        public List<School> Schools { get; set; } = new List<School>();

        public async Task<IActionResult> OnGetAsync(Guid districtId)
        {
            DistrictId = districtId;
            Schools = await _context.Schools
                .Where(s => s.DistrictId == districtId)
                .ToListAsync();
            return Page();
        }
    }
}
