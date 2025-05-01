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
            var currentUser = await _userManager.GetUserAsync(User);

            var allUsers = await _context.Users
                .Include(u => u.District)
                .Include(u => u.School)
                .ToListAsync();

            var filtered = allUsers;

            if (User.IsInRole("DistrictAdmin"))
            {
                filtered = allUsers.Where(u =>
                    u.DistrictId == currentUser.DistrictId
                ).ToList();
            }
            else if (User.IsInRole("SchoolAdmin"))
            {
                filtered = allUsers.Where(u =>
                    u.SchoolId == currentUser.SchoolId
                ).ToList();
            }

            Users = new List<UserDisplay>();
            foreach (var user in filtered)
            {
                var roles = await _userManager.GetRolesAsync(user);
                Users.Add(new UserDisplay
                {
                    Id = user.Id,
                    Email = user.Email,
                    Roles = roles.ToList(),
                    DistrictName = user.District?.Name ?? "",
                    SchoolName = user.School?.Name ?? ""
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
