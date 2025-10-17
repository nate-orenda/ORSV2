using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using Microsoft.AspNetCore.Mvc;

namespace ORSV2.Pages.Admin.Users
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin,SchoolAdmin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public string? CurrentUserId { get; set; }
        public List<UserDisplay> Users { get; set; } = new();
        public List<DistrictDropdown> AvailableDistricts { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DistrictFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? LockedFilter { get; set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task OnGetAsync()
        {
            var currentUser = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            CurrentUserId = currentUser?.Id;

            // Load available districts for filter dropdown
            if (User.IsInRole("OrendaAdmin"))
            {
                AvailableDistricts = await _context.Districts
                    .OrderBy(d => d.Name)
                    .Select(d => new DistrictDropdown { Id = d.Id.ToString(), Name = d.Name })
                    .ToListAsync();
            }

            // Build base query
            var query = _context.Users
                .Include(u => u.District)
                .Include(u => u.UserSchools)
                    .ThenInclude(us => us.School)
                .AsQueryable();

            // Role-based filtering
            if (User.IsInRole("DistrictAdmin"))
            {
                query = query.Where(u => u.DistrictId == currentUser!.DistrictId);
            }
            else if (User.IsInRole("SchoolAdmin"))
            {
                var schoolIds = currentUser!.UserSchools.Select(us => us.SchoolId).ToList();
                query = query.Where(u => u.UserSchools.Any(us => schoolIds.Contains(us.SchoolId)));
            }

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                query = query.Where(u => u.Email!.Contains(SearchTerm) || u.UserName!.Contains(SearchTerm));
            }

            // District filter (OrendaAdmin only)
            if (!string.IsNullOrEmpty(DistrictFilter) && User.IsInRole("OrendaAdmin"))
            {
                if (int.TryParse(DistrictFilter, out var districtId))
                {
                    query = query.Where(u => u.DistrictId == districtId);
                }
            }

            // Locked status filter
            var now = DateTimeOffset.UtcNow;
            if (!string.IsNullOrEmpty(LockedFilter))
            {
                if (LockedFilter == "locked")
                {
                    query = query.Where(u => u.LockoutEnd > now);
                }
                else if (LockedFilter == "active")
                {
                    query = query.Where(u => u.LockoutEnd == null || u.LockoutEnd <= now);
                }
            }

            // Execute query
            var filteredUsers = await query.OrderBy(u => u.Email).ToListAsync();

            // Map to display model with roles
            foreach (var user in filteredUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow;

                Users.Add(new UserDisplay
                {
                    Id = user.Id,
                    Email = user.Email ?? "",
                    Roles = roles.ToList(),
                    DistrictName = user.District?.Name ?? "",
                    SchoolName = string.Join(", ", user.UserSchools.Select(us => us.School.Name)),
                    IsLocked = isLocked
                });
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            if (!User.IsInRole("OrendaAdmin"))
                return Forbid();

            var currentUserId = _userManager.GetUserId(User);
            if (id == currentUserId)
            {
                StatusMessage = "❌ You cannot delete your own account.";
                return RedirectToPage();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
                StatusMessage = $"✅ User '{user.Email}' was successfully deleted.";
            }
            else
            {
                StatusMessage = "⚠️ User not found.";
            }

            return RedirectToPage();
        }

        public class UserDisplay
        {
            public string Id { get; set; } = "";
            public string Email { get; set; } = "";
            public List<string> Roles { get; set; } = new();
            public string DistrictName { get; set; } = "";
            public string SchoolName { get; set; } = "";
            public bool IsLocked { get; set; }
        }

        public class DistrictDropdown
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
        }
    }
}