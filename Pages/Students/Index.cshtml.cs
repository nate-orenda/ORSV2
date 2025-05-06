using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Students
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [BindProperty(SupportsGet = true)]
        public bool ShowSWD { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ShowSED { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ShowEth { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ShowRace { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ShowLang { get; set; }


        public List<STU> Students { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();


        public async Task<IActionResult> OnGetAsync(int districtId, int schoolId)
        {
            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user);

            // ✅ Fetch district and school names from the DB
            var district = await _context.Districts.FirstOrDefaultAsync(d => d.Id == districtId && !d.Inactive);
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.Id == schoolId && !s.Inactive);

            if (district == null || school == null)
                return NotFound();

            var districtName = district.Name;
            var schoolName = school.Name;

            // ✅ Then build breadcrumbs
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Districts", Url = Url.Page("/Districts/Index") },
                new BreadcrumbItem { Title = districtName, Url = Url.Page("/Schools/Index", new { districtId }) },
                new BreadcrumbItem { Title = schoolName } // current page
            };

            // 🔐 Access control
            if (!roles.Contains("OrendaAdmin") && !roles.Contains("OrendaManager"))
            {
                if (user.DistrictId.HasValue && user.DistrictId.Value != districtId)
                    return Forbid();

                var userSchoolIds = await _context.UserSchools
                    .Where(us => us.UserId == user.Id)
                    .Select(us => us.SchoolId)
                    .ToListAsync();

                if (!userSchoolIds.Contains(schoolId))
                    return Forbid();
            }

            // ✅ Load students
            Students = await _context.STU
                .Where(s => s.DistrictID == districtId && s.SchoolID == schoolId && !s.Inactive)
                .OrderBy(s => s.LastName)
                .ThenBy(s => s.FirstName)
                .ToListAsync();

            return Page();
        }


    }
}
