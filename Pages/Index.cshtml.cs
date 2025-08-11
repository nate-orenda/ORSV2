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

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Greeting
        public string UserName { get; set; } = string.Empty;

        // View model for the cards
        public List<DistrictBlock> DistrictBlocks { get; set; } = new();

        public class DistrictBlock
        {
            public int DistrictId { get; set; }
            public string DistrictName { get; set; } = string.Empty;
            public List<SchoolCard> Schools { get; set; } = new();
        }

        public class SchoolCard
        {
            public int SchoolId { get; set; }
            public string SchoolName { get; set; } = string.Empty;
            public int Enrollment { get; set; }
            public int? CurrentCP { get; set; }
            public DateTime? GAResultsLastUpdated { get; set; }
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

            // ===== Determine scope =====
            IQueryable<School> scopedSchools;

            if (roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser"))
            {
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive);
            }
            else if (roles.Contains("DistrictAdmin") && user.DistrictId.HasValue)
            {
                var districtId = user.DistrictId.Value;
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.DistrictId == districtId);
            }
            else
            {
                // School-level roles
                var userSchoolIds = user.UserSchools
                    .Where(us => us.School != null && !us.School.Inactive)
                    .Select(us => us.SchoolId)
                    .Distinct()
                    .ToList();

                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && userSchoolIds.Contains(s.Id));
            }

            var schools = await scopedSchools
                .OrderBy(s => s.District!.Name)
                .ThenBy(s => s.Name)
                .ToListAsync();

            if (schools.Count == 0)
            {
                DistrictBlocks = new();
                return Page();
            }

            var schoolIds = schools.Select(s => s.Id).ToList();

            // ===== Enrollment (from STU/Students) =====
            // Using STU because that’s what your current page uses.
            var enrollBySchool = await _context.STU
                .Where(stu => schoolIds.Contains(stu.SchoolID))
                .GroupBy(stu => stu.SchoolID)
                .Select(g => new { SchoolId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SchoolId, x => x.Count);

            // ===== GAResults aggregates (Current CP, Last Updated) =====
            // Assumes GAResults has DistrictId, SchoolId, CP (int), and UpdatedAt (DateTime?) fields.
            var gaAgg = await _context.GAResults
                .Where(r => schoolIds.Contains(r.SchoolId))
                .GroupBy(r => r.SchoolId)
                .Select(g => new
                {
                    SchoolId = g.Key,
                    CurrentCP = (int?)g.Max(r => r.CP),
                    LastUpdated = g.Max(r => r.DateLastUpdated)
                })
                .ToDictionaryAsync(x => x.SchoolId, x => new { x.CurrentCP, x.LastUpdated });

            // ===== Build blocks =====
            DistrictBlocks = schools
                .GroupBy(s => new { s.DistrictId, s.District!.Name })
                .Select(g => new DistrictBlock
                {
                    DistrictId = g.Key.DistrictId,
                    DistrictName = g.Key.Name,
                    Schools = g.Select(s => new SchoolCard
                    {
                        SchoolId = s.Id,
                        SchoolName = s.Name,
                        Enrollment = enrollBySchool.TryGetValue(s.Id, out var cnt) ? cnt : 0,
                        CurrentCP = gaAgg.TryGetValue(s.Id, out var agg) ? agg.CurrentCP : null,
                        GAResultsLastUpdated = gaAgg.TryGetValue(s.Id, out var agg2) ? agg2.LastUpdated : null
                    }).ToList()
                })
                .OrderBy(b => b.DistrictName)
                .ToList();

            return Page();
        }
    }
}
