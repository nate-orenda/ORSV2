using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Admin.Users
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin,SchoolAdmin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public List<UserDisplay> Users { get; set; } = new();

        public async Task OnGetAsync()
        {
            var currentUser = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            var query = _context.Users
                .Include(u => u.District)
                .Include(u => u.UserSchools)
                    .ThenInclude(us => us.School)
                .AsQueryable();

            if (User.IsInRole("DistrictAdmin"))
            {
                query = query.Where(u => u.DistrictId == currentUser!.DistrictId);
            }
            else if (User.IsInRole("SchoolAdmin"))
            {
                var schoolIds = currentUser!.UserSchools.Select(us => us.SchoolId).ToList();
                query = query.Where(u => u.UserSchools.Any(us => schoolIds.Contains(us.SchoolId)));
            }

            var filteredUsers = await query.ToListAsync();

            foreach (var user in filteredUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                Users.Add(new UserDisplay
                {
                    Id = user.Id,
                    Email = user.Email ?? "",
                    Roles = roles.ToList(),
                    DistrictName = user.District?.Name ?? "",
                    SchoolName = string.Join(", ", user.UserSchools.Select(us => us.School.Name))
                });
            }
        }

        public class UserDisplay
        {
            public string Id { get; set; }
            public string Email { get; set; }
            public List<string> Roles { get; set; } = new();
            public string DistrictName { get; set; }
            public string SchoolName { get; set; }
        }
    }
}
