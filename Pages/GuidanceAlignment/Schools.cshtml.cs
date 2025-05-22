using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class SchoolsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SchoolsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }

        public List<School> Schools { get; set; } = new();

        public async Task OnGetAsync()
        {
            Schools = await _context.Schools
                .Where(s => s.DistrictId == DistrictId && !s.Inactive)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }
    }
}