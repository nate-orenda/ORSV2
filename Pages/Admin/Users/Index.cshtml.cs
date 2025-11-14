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
        public List<RoleDropdown> AvailableRoles { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? DistrictFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? RoleFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? LockedFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortBy { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortOrder { get; set; } = "asc";

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

            // Load available roles for filter dropdown
            AvailableRoles = await _context.Roles
                .OrderBy(r => r.Name)
                .Select(r => new RoleDropdown { Id = r.Id, Name = r.Name ?? "" })
                .ToListAsync();

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

            // Search filter (now includes first name and last name)
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                query = query.Where(u => 
                    u.Email!.Contains(SearchTerm) || 
                    u.UserName!.Contains(SearchTerm) ||
                    u.FirstName!.Contains(SearchTerm) ||
                    u.LastName!.Contains(SearchTerm));
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
            var filteredUsers = await query.ToListAsync();

            // Efficiently load roles
            var userIds = filteredUsers.Select(u => u.Id).ToHashSet();
            
            var userRoles = await _context.UserRoles
                                    .Where(ur => userIds.Contains(ur.UserId))
                                    .Select(ur => new { ur.UserId, ur.RoleId })
                                    .ToListAsync();

            var allRoles = await _context.Roles
                                    .ToDictionaryAsync(r => r.Id, r => r.Name!);

            // Role filter (apply after loading roles)
            List<string> usersWithRole = new();
            if (!string.IsNullOrEmpty(RoleFilter))
            {
                usersWithRole = userRoles
                    .Where(ur => ur.RoleId == RoleFilter)
                    .Select(ur => ur.UserId)
                    .ToList();
                
                filteredUsers = filteredUsers.Where(u => usersWithRole.Contains(u.Id)).ToList();
            }

            // Map to display model with roles
            foreach (var user in filteredUsers)
            {
                var roleIds = userRoles
                                .Where(ur => ur.UserId == user.Id)
                                .Select(ur => ur.RoleId);

                var roles = roleIds.Select(roleId => allRoles.TryGetValue(roleId, out var name) ? name : "")
                                   .Where(name => !string.IsNullOrEmpty(name))
                                   .ToList();

                var isLocked = user.LockoutEnd.HasValue && user.LockoutEnd.Value.UtcDateTime > DateTime.UtcNow;

                Users.Add(new UserDisplay
                {
                    Id = user.Id,
                    FirstName = user.FirstName ?? "",
                    LastName = user.LastName ?? "",
                    Email = user.Email ?? "",
                    EmailConfirmed = user.EmailConfirmed,
                    Roles = roles,
                    DistrictName = user.District?.Name ?? "",
                    SchoolName = string.Join(", ", user.UserSchools.Select(us => us.School.Name)),
                    IsLocked = isLocked
                });
            }

            // Apply sorting
            Users = SortBy switch
            {
                "FirstName" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.FirstName).ToList() 
                    : Users.OrderBy(u => u.FirstName).ToList(),
                "LastName" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.LastName).ToList() 
                    : Users.OrderBy(u => u.LastName).ToList(),
                "Email" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.Email).ToList() 
                    : Users.OrderBy(u => u.Email).ToList(),
                "Status" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.IsLocked).ToList() 
                    : Users.OrderBy(u => u.IsLocked).ToList(),
                "Confirmed" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.EmailConfirmed).ToList() 
                    : Users.OrderBy(u => u.EmailConfirmed).ToList(),
                "District" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.DistrictName).ToList() 
                    : Users.OrderBy(u => u.DistrictName).ToList(),
                "Schools" => SortOrder == "desc" 
                    ? Users.OrderByDescending(u => u.SchoolName).ToList() 
                    : Users.OrderBy(u => u.SchoolName).ToList(),
                _ => Users.OrderBy(u => u.Email).ToList() // Default sort by email
            };
        }

        public string GetSortIcon(string columnName)
        {
            if (SortBy != columnName)
                return "bi-arrow-down-up"; // Unsorted icon
            
            return SortOrder == "asc" ? "bi-sort-alpha-down" : "bi-sort-alpha-up";
        }

        public string GetNextSortOrder(string columnName)
        {
            if (SortBy != columnName)
                return "asc";
            
            return SortOrder == "asc" ? "desc" : "asc";
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
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string Email { get; set; } = "";
            public bool EmailConfirmed { get; set; }
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

        public class RoleDropdown
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
        }
    }
}