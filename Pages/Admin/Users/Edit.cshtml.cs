using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Admin.Users
{
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin,SchoolAdmin")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;

        public EditModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public SelectList Districts { get; set; } = null!;
        public SelectList Schools { get; set; } = null!;
        public List<string> AllRoles { get; set; } = new();

        public class InputModel
        {
            public string Id { get; set; } = "";
            public string Email { get; set; } = "";
            public Guid? DistrictId { get; set; }
            public Guid? SchoolId { get; set; }
            public List<string> SelectedRoles { get; set; } = new();
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userManager.Users
                .Include(u => u.District)
                .Include(u => u.School)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var targetUserRoles = await _userManager.GetRolesAsync(user);

            // 🔒 Scope enforcement
            if (User.IsInRole("DistrictAdmin") && user.DistrictId != currentUser.DistrictId)
                return Forbid();

            if (User.IsInRole("SchoolAdmin") && user.SchoolId != currentUser.SchoolId)
                return Forbid();

            // 🔒 Role hierarchy enforcement (read-level)
            bool isTargetOrendaAdmin = targetUserRoles.Contains("OrendaAdmin");
            bool isTargetDistrictAdmin = targetUserRoles.Contains("DistrictAdmin");

            bool isCurrentOrendaAdmin = currentUserRoles.Contains("OrendaAdmin");

            if (!isCurrentOrendaAdmin && isTargetOrendaAdmin)
                return Forbid();

            if (User.IsInRole("SchoolAdmin") && (isTargetDistrictAdmin || isTargetOrendaAdmin))
                return Forbid();

            Input = new InputModel
            {
                Id = user.Id,
                Email = user.Email ?? "",
                DistrictId = user.DistrictId,
                SchoolId = user.SchoolId,
                SelectedRoles = targetUserRoles.ToList()
            };

            Districts = new SelectList(await _context.Districts.ToListAsync(), "Id", "Name");
            Schools = new SelectList(await _context.Schools.ToListAsync(), "Id", "Name");

            var allRoles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();

            if (isCurrentOrendaAdmin)
            {
                AllRoles = allRoles;
            }
            else if (currentUserRoles.Contains("DistrictAdmin"))
            {
                AllRoles = allRoles.Where(r => r is "SchoolAdmin" or "Counselor" or "Teacher").ToList();
            }
            else if (currentUserRoles.Contains("SchoolAdmin"))
            {
                AllRoles = allRoles.Where(r => r is "Counselor" or "Teacher").ToList();
            }
            else
            {
                AllRoles = new();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByIdAsync(Input.Id);
            if (user == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var targetUserRoles = await _userManager.GetRolesAsync(user);

            // 🔒 Scope enforcement
            if (User.IsInRole("DistrictAdmin") && user.DistrictId != currentUser.DistrictId)
                return Forbid();

            if (User.IsInRole("SchoolAdmin") && user.SchoolId != currentUser.SchoolId)
                return Forbid();

            // 🔒 Role hierarchy enforcement (write-level)
            bool isTargetOrendaAdmin = targetUserRoles.Contains("OrendaAdmin");
            bool isTargetDistrictAdmin = targetUserRoles.Contains("DistrictAdmin");

            bool isCurrentOrendaAdmin = currentUserRoles.Contains("OrendaAdmin");
            bool isCurrentDistrictAdmin = currentUserRoles.Contains("DistrictAdmin");

            if (!isCurrentOrendaAdmin && isTargetOrendaAdmin)
                return Forbid();

            if (User.IsInRole("SchoolAdmin") && (isTargetDistrictAdmin || isTargetOrendaAdmin))
                return Forbid();

            // 🔒 Determine allowed assignable roles
            List<string> allowedRoles;
            if (isCurrentOrendaAdmin)
            {
                allowedRoles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
            }
            else if (isCurrentDistrictAdmin)
            {
                allowedRoles = new() { "SchoolAdmin", "Counselor", "Teacher" };
            }
            else if (currentUserRoles.Contains("SchoolAdmin"))
            {
                allowedRoles = new() { "Counselor", "Teacher" };
            }
            else
            {
                allowedRoles = new();
            }

            // 🔒 Prevent self-demotion
            if (user.Id == currentUser.Id)
            {
                var selfRemovedRoles = targetUserRoles.Intersect(allowedRoles).Except(Input.SelectedRoles);
                if (selfRemovedRoles.Any())
                {
                    ModelState.AddModelError("", "You cannot remove your own role.");
                    return Page();
                }
            }

            // ✅ Sanitize and apply role updates
            Input.SelectedRoles = Input.SelectedRoles.Where(r => allowedRoles.Contains(r)).ToList();

            user.DistrictId = Input.DistrictId;
            user.SchoolId = Input.SchoolId;
            await _userManager.UpdateAsync(user);

            var toRemove = targetUserRoles.Except(Input.SelectedRoles);
            var toAdd = Input.SelectedRoles.Except(targetUserRoles);

            await _userManager.RemoveFromRolesAsync(user, toRemove);
            await _userManager.AddToRolesAsync(user, toAdd);

            return RedirectToPage("Index");
        }
    }
}
