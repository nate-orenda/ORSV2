using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class IndexModel : SecureReportPageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<DistrictBlock> DistrictBlocks { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

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
            public bool CA { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Initialize user data scope from claims
            InitializeUserDataScope();

            // ===== Determine scope using claims - only CA-enabled schools =====
            IQueryable<School> scopedSchools;

            if (IsOrendaUser)
            {
                // Orenda users see all CA-enabled schools
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && s.CA);
            }
            else if (IsDistrictAdmin && UserDistrictId.HasValue)
            {
                // District admins see only their district's CA schools
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && s.CA && s.DistrictId == UserDistrictId.Value);
            }
            else if ((IsSchoolAdmin || IsTeacher) && UserSchoolIds.Any())
            {
                // School admins and teachers see only their assigned CA schools
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && s.CA && UserSchoolIds.Contains(s.Id));
            }
            else
            {
                // User has no valid assignments - return empty
                DistrictBlocks = new();
                Breadcrumbs = new List<BreadcrumbItem>
                {
                    new BreadcrumbItem { Title = "Dashboard", Url = Url.Page("/Index") },
                    new BreadcrumbItem { Title = "Curriculum Alignment" }
                };
                return Page();
            }

            var schools = await scopedSchools
                .OrderBy(s => s.District!.Name)
                .ThenBy(s => s.Name)
                .ToListAsync();

            if (schools.Count == 0)
            {
                DistrictBlocks = new();
                Breadcrumbs = new List<BreadcrumbItem>
                {
                    new BreadcrumbItem { Title = "Dashboard", Url = Url.Page("/Index") },
                    new BreadcrumbItem { Title = "Curriculum Alignment" }
                };
                return Page();
            }

            var schoolIds = schools.Select(s => s.Id).ToList();

            // ===== Enrollment =====
            var enrollBySchool = await _context.STU
                .Where(stu => schoolIds.Contains(stu.SchoolID))
                .GroupBy(stu => stu.SchoolID)
                .Select(g => new { SchoolId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SchoolId, x => x.Count);

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
                        CA = s.CA
                    }).ToList()
                })
                .OrderBy(b => b.DistrictName)
                .ToList();

            // Set up breadcrumbs
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Dashboard", Url = Url.Page("/Index") },
                new BreadcrumbItem { Title = "Curriculum Alignment" }
            };

            return Page();
        }
    }
}