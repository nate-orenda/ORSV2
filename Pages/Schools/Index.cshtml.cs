using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using Microsoft.AspNetCore.Identity;

namespace ORSV2.Pages.Schools
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager,DistrictAdmin,SchoolAdmin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public int DistrictId { get; set; }
        public List<School> Schools { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int districtId)
        {
            DistrictId = districtId;

            // ✅ Fetch district and school names from the DB
            var district = await _context.Districts.FirstOrDefaultAsync(d => d.Id == districtId);
            
            if (district == null)
                return NotFound();

            var districtName = district.Name;

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Districts", Url = Url.Page("/Districts/Index") },
                new BreadcrumbItem { Title = district.Name } // current page
            };


            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .ThenInclude(us => us.School)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            if (user == null) return Forbid();

            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager"))
            {
                Schools = await _context.Schools
                    .Where(s => s.DistrictId == districtId && !s.Inactive)
                    .ToListAsync();
            }
            else if (roles.Contains("DistrictAdmin"))
            {
                if (user.DistrictId != districtId)
                {
                    return Forbid();
                }

                Schools = await _context.Schools
                    .Where(s => s.DistrictId == districtId && !s.Inactive)
                    .ToListAsync();
            }
            else if (roles.Contains("SchoolAdmin"))
            {
                var schoolIds = user.UserSchools.Select(us => us.SchoolId).ToList();

                Schools = await _context.Schools
                    .Where(s => s.DistrictId == districtId && schoolIds.Contains(s.Id) && !s.Inactive)
                    .ToListAsync();

                if (!Schools.Any())
                {
                    return Forbid();
                }
            }
            else
            {
                return Forbid();
            }

            return Page();
        }

    }

}
