using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Models.ViewModels;
using ORSV2.Services;

namespace ORSV2.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IDistrictFocusService _districtFocusService;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IDistrictFocusService districtFocusService)
        {
            _context = context;
            _userManager = userManager;
            _districtFocusService = districtFocusService;
        }

        // User context
        public string UserName { get; set; } = string.Empty;
        public bool IsOrendaUser { get; set; }
        
        // Focus district
        public int? FocusDistrictId { get; set; }
        public string FocusDistrictName { get; set; } = string.Empty;
        public List<District> AvailableDistricts { get; set; } = new();
        public bool ShowDistrictSelector { get; set; }

        // View model for the cards
        public List<DistrictBlock> DistrictBlocks { get; set; } = new();

        public class DistrictBlock
        {
            public int DistrictId { get; set; }
            public string DistrictName { get; set; } = string.Empty;
            public List<SchoolCard> Schools { get; set; } = new();
            public bool IsFocused { get; set; }
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

        public async Task<IActionResult> OnGetAsync(int? setFocus = null)
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                    .ThenInclude(us => us.School)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            if (user == null)
                return RedirectToPage("/Account/Login", new { area = "Identity" });

            UserName = user.FirstName ?? user.UserName ?? "User";
            var roles = await _userManager.GetRolesAsync(user);
            IsOrendaUser = roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser");

            // Handle focus district setting for Orenda users
            if (setFocus.HasValue && IsOrendaUser)
            {
                if (await _districtFocusService.ValidateFocusDistrictAsync(user.Id, setFocus.Value, IsOrendaUser))
                {
                    await _districtFocusService.SetFocusDistrictIdAsync(user.Id, setFocus.Value);
                }
            }

            // Get available districts and focus district
            AvailableDistricts = await _districtFocusService.GetAvailableDistrictsAsync(user.Id, IsOrendaUser);
            FocusDistrictId = await _districtFocusService.GetFocusDistrictIdAsync(user.Id, IsOrendaUser);
            
            // IMPORTANT FIX: Auto-set focus district for ALL users with single district access
            if (!FocusDistrictId.HasValue && AvailableDistricts.Count == 1)
            {
                var singleDistrict = AvailableDistricts.First();
                await _districtFocusService.SetFocusDistrictIdAsync(user.Id, singleDistrict.Id);
                FocusDistrictId = singleDistrict.Id;
            }
            
            if (FocusDistrictId.HasValue)
            {
                var focusDistrict = AvailableDistricts.FirstOrDefault(d => d.Id == FocusDistrictId.Value);
                FocusDistrictName = focusDistrict?.Name ?? "";
            }

            // Show district selector for Orenda users if no focus is set or on first visit
            ShowDistrictSelector = IsOrendaUser && !FocusDistrictId.HasValue && AvailableDistricts.Count > 1;

            // ===== Determine scope =====
            IQueryable<School> scopedSchools;

            if (IsOrendaUser)
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
                    IsFocused = FocusDistrictId == g.Key.DistrictId,
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
                .OrderByDescending(b => b.IsFocused) // Focus district first
                .ThenBy(b => b.DistrictName)
                .ToList();

            // ===== Calendar logic (show all accessible districts) =====
            var calendarSchoolIds = schoolIds; // Always use all schools the user has access to
            
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
                    Label = $"{t.Cp} — {r.SchoolName}",
                    SchoolId = r.SchoolId,
                    DistrictId = r.DistrictId,
                    DistrictName = districtNameBySchoolId.TryGetValue(r.SchoolId, out var dn) ? dn : "",
                    Cp = t.Cp
                }))
                .OrderBy(x => x.Date)
                .ToList();

            // Add ViewData for the layout to use
            ViewData["FocusDistrictId"] = FocusDistrictId?.ToString() ?? "";
            ViewData["FocusDistrictName"] = FocusDistrictName;
            ViewData["AvailableDistricts"] = System.Text.Json.JsonSerializer.Serialize(
                AvailableDistricts.Select(d => new { Id = d.Id, Name = d.Name }).ToList()
            );
            ViewData["IsOrendaUser"] = IsOrendaUser;

            return Page();
        }

        // AJAX handler for setting focus district
        public async Task<IActionResult> OnPostSetFocusAsync(int districtId)
        {
            var user = await _userManager.FindByNameAsync(User.Identity!.Name!);
            if (user == null) return BadRequest();

            var roles = await _userManager.GetRolesAsync(user);
            var isOrendaUser = roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser");

            if (await _districtFocusService.ValidateFocusDistrictAsync(user.Id, districtId, isOrendaUser))
            {
                await _districtFocusService.SetFocusDistrictIdAsync(user.Id, districtId);
                return new JsonResult(new { success = true });
            }

            return BadRequest(new { success = false, message = "Invalid district" });
        }
    }
}