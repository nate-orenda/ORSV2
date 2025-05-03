using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Models;
using ORSV2.Data;
using Microsoft.AspNetCore.Identity;


namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager,DistrictAdmin,SchoolAdmin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public IList<District> Districts { get; set; } = new List<District>();

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager"))
            {
                Districts = await _context.Districts.OrderBy(d => d.Name).ToListAsync();
            }
            else if (roles.Contains("DistrictAdmin") && user.DistrictId != null)
            {
                var district = await _context.Districts.FindAsync(user.DistrictId);
                if (district != null)
                {
                    Districts.Add(district);
                }
            }
            else if (roles.Contains("SchoolAdmin") && user.DistrictId != null)
            {
                var district = await _context.Districts.FindAsync(user.DistrictId);
                if (district != null)
                {
                    Districts.Add(district);
                }
            }
        }
    }


}
