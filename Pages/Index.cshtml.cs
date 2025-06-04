using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        public string ScopeLabel { get; set; } = "District";
        public Dictionary<string, List<string>> DistrictSchools { get; set; } = new();

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public string UserName { get; set; } = string.Empty;
        public List<EnrollmentSummary> EnrollmentSummaries { get; set; } = new();
        public string ChartLabels { get; set; } = string.Empty;
        public string ChartData { get; set; } = string.Empty;

        public class EnrollmentSummary
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .ThenInclude(us => us.School)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            if (user == null)
                return RedirectToPage("/Account/Login", new { area = "Identity" });

            UserName = user.FirstName ?? user.UserName ?? "User";
            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser"))
            {
                ScopeLabel = "District";

                var districts = await _context.Districts.ToDictionaryAsync(d => d.Id, d => d.Name);
                var schools = await _context.Schools.ToListAsync();

                DistrictSchools = schools
                    .GroupBy(s => districts.ContainsKey(s.DistrictId) ? districts[s.DistrictId] : "Unknown")
                    .ToDictionary(g => g.Key, g => g.Select(s => s.Name).OrderBy(n => n).ToList());

                var districtNames = await _context.Districts
                    .ToDictionaryAsync(d => d.Id, d => d.Name);

                var data = await _context.STU
                    .GroupBy(s => s.DistrictID)
                    .Select(g => new
                    {
                        DistrictId = g.Key,
                        Count = g.Count()
                    })
                    .ToListAsync();

                EnrollmentSummaries = data
                    .Select(g => new EnrollmentSummary
                    {
                        Name = districtNames.ContainsKey(g.DistrictId) ? districtNames[g.DistrictId] : "Unknown",
                        Count = g.Count
                    })
                    .ToList();
            }
            else if (roles.Contains("DistrictAdmin"))
            {
                ScopeLabel = "District";

                var schoolNames = await _context.Schools
                    .ToDictionaryAsync(s => s.Id, s => s.Name);

                var data = await _context.STU
                    .Where(s => s.DistrictID == user.DistrictId)
                    .GroupBy(s => s.SchoolID)
                    .Select(g => new
                    {
                        SchoolId = g.Key,
                        Count = g.Count()
                    })
                    .ToListAsync();

                EnrollmentSummaries = data
                    .Select(g => new EnrollmentSummary
                    {
                        Name = schoolNames.ContainsKey(g.SchoolId) ? schoolNames[g.SchoolId] : "Unknown",
                        Count = g.Count
                    })
                    .ToList();
            }
            else
            {
                var schoolIds = user.UserSchools.Select(us => us.SchoolId).ToList();
                ScopeLabel = "School";

                var schoolNames = await _context.Schools
                    .Where(s => schoolIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.Name);

                var data = await _context.STU
                    .Where(s => schoolIds.Contains(s.SchoolID))
                    .GroupBy(s => s.SchoolID)
                    .Select(g => new
                    {
                        SchoolId = g.Key,
                        Count = g.Count()
                    })
                    .ToListAsync();

                EnrollmentSummaries = data
                    .Select(g => new EnrollmentSummary
                    {
                        Name = schoolNames.ContainsKey(g.SchoolId) ? schoolNames[g.SchoolId] : "Unknown",
                        Count = g.Count
                    })
                    .ToList();
            }

            ChartLabels = string.Join(",", EnrollmentSummaries.Select(e => $"\"{e.Name}\""));
            ChartData = string.Join(",", EnrollmentSummaries.Select(e => e.Count));

            return Page();
        }
    }
}
