using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Models.ViewModels;

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
            public bool GA { get; set; }
            public bool CA { get; set; }
        }

        public List<GACheckpointScheduleViewModel> CheckpointSchedules { get; set; } = new();

        public sealed class CalendarItem
        {
            public DateTime Date { get; set; }
            public string Label { get; set; } = string.Empty;
            public int SchoolId { get; set; }
            public int DistrictId { get; set; }
            public string DistrictName { get; set; } = string.Empty;   // ⬅️ NEW
            public string Cp { get; set; } = string.Empty;
        }

        public List<CalendarItem> CalendarItems { get; set; } = new();

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
                    .Where(s => !s.Inactive && s.enabled);
            }
            else if (roles.Contains("DistrictAdmin") && user.DistrictId.HasValue)
            {
                var districtId = user.DistrictId.Value;
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && s.DistrictId == districtId);
            }
            else
            {
                var userSchoolIds = user.UserSchools
                    .Where(us => us.School != null && !us.School.Inactive && us.School!.enabled)
                    .Select(us => us.SchoolId)
                    .Distinct()
                    .ToList();

                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && userSchoolIds.Contains(s.Id));
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

            // ===== Enrollment =====
            var enrollBySchool = await _context.STU
                .Where(stu => schoolIds.Contains(stu.SchoolID))
                .GroupBy(stu => stu.SchoolID)
                .Select(g => new { SchoolId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SchoolId, x => x.Count);

            // ===== GAResults aggregates =====
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
                        GAResultsLastUpdated = gaAgg.TryGetValue(s.Id, out var agg2) ? agg2.LastUpdated : null,
                        GA = s.GA,
                        CA = s.CA
                    }).ToList()
                })
                .OrderBy(b => b.DistrictName)
                .ToList();

            // ===== Checkpoint schedule rows =====
            var scheduleRows = await _context.GACheckpointSchedule
                .Where(x => schoolIds.Contains(x.SchoolId))
                .Join(_context.Schools, g => g.SchoolId, s => s.Id,
                    (g, s) => new GACheckpointScheduleViewModel
                    {
                        ScheduleId = g.ScheduleId,
                        DistrictId = g.DistrictId,
                        SchoolId = g.SchoolId,
                        SchoolName = s.Name,
                        Checkpoint1Date = g.Checkpoint1Date,
                        Checkpoint2Date = g.Checkpoint2Date,
                        Checkpoint3Date = g.Checkpoint3Date,
                        Checkpoint4Date = g.Checkpoint4Date,
                        Checkpoint5Date = g.Checkpoint5Date
                    })
                .AsNoTracking()
                .ToListAsync();

            CheckpointSchedules = scheduleRows;

            var districtNameBySchoolId = schools.ToDictionary(s => s.Id, s => s.District!.Name);

            // ===== Build calendar items (stable dates) =====
            CalendarItems = CheckpointSchedules
            .SelectMany(r => new[]
            {
                (Date:r.Checkpoint1Date, Cp:"CP1"),
                (Date:r.Checkpoint2Date, Cp:"CP2"),
                (Date:r.Checkpoint3Date, Cp:"CP3"),
                (Date:r.Checkpoint4Date, Cp:"CP4"),
                (Date:r.Checkpoint5Date, Cp:"CP5"),
            }
            .Where(t => t.Date.HasValue)
            .Select(t => new CalendarItem
            {
                Date = t.Date!.Value.Date,
                Label = $"{t.Cp} – {r.SchoolName}",
                SchoolId = r.SchoolId,
                DistrictId = r.DistrictId,
                DistrictName = districtNameBySchoolId.TryGetValue(r.SchoolId, out var dn) ? dn : "",
                Cp = t.Cp
            }))
            .OrderBy(x => x.Date)
            .ToList();

            return Page();
        }
    }
}
