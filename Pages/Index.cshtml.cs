using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Models.ViewModels;

namespace ORSV2.Pages
{
    [Authorize]
    public class IndexModel : SecureReportPageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // User context
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
            public string DistrictName { get; set; } = string.Empty;
            public string Cp { get; set; } = string.Empty;
        }

        public List<CalendarItem> CalendarItems { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            // Initialize user data scope from claims
            InitializeUserDataScope();

            // Get user name from Identity
            UserName = User.Identity?.Name?.Split('@')[0] ?? User.Identity?.Name ?? "User";

            // ===== Determine scope using claims (assignment-first) =====
            IQueryable<School> scopedSchools;

            if (IsOrendaUser)
            {
                // Orenda users see all enabled schools
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled);
            }
            else if (UserSchoolIds.Any())
            {
                // Anyone explicitly assigned to school(s) (e.g., Counselor, School Admin, Teacher)
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && UserSchoolIds.Contains(s.Id));
            }
            else if (UserDistrictId.HasValue)
            {
                // Anyone explicitly assigned to a district (e.g., District Counselor, District Admin)
                scopedSchools = _context.Schools
                    .AsNoTracking()
                    .Include(s => s.District)
                    .Where(s => !s.Inactive && s.enabled && s.DistrictId == UserDistrictId.Value);
            }
            else
            {
                // No valid assignments
                DistrictBlocks = new();
                return Page();
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
                .Where(stu => schoolIds.Contains(stu.SchoolID) && !stu.Inactive)
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

            // ===== Calendar logic (show all accessible districts) =====
            var calendarSchoolIds = schoolIds; // Use all schools the user has access to
            
            var scheduleRows = await _context.GACheckpointSchedule
                .Where(x => calendarSchoolIds.Contains(x.SchoolId))
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

            // ===== Build calendar items =====
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