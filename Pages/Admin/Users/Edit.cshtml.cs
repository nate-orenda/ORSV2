// Pages/Admin/Users/Edit.cshtml.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.ComponentModel.DataAnnotations.Schema;

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
        public MultiSelectList Schools { get; set; } = null!;
        public List<string> AllRoles { get; set; } = new();

        public class InputModel
        {
            public string Id { get; set; } = "";
            public string Email { get; set; } = "";
            public Guid? DistrictId { get; set; }
            public List<Guid> SelectedSchoolIds { get; set; } = new();
            public List<string> SelectedRoles { get; set; } = new();
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var targetUserRoles = await _userManager.GetRolesAsync(user);

            if (User.IsInRole("DistrictAdmin") && user.DistrictId != currentUser.DistrictId)
                return Forbid();

            if (User.IsInRole("SchoolAdmin")) return Forbid();

            Input = new InputModel
            {
                Id = user.Id,
                Email = user.Email ?? "",
                DistrictId = user.DistrictId,
                SelectedSchoolIds = await _context.UserSchools
                    .Where(us => us.UserId == user.Id)
                    .Select(us => us.SchoolId)
                    .ToListAsync(),
                SelectedRoles = targetUserRoles.ToList()
            };

            Districts = new SelectList(await _context.Districts.ToListAsync(), "Id", "Name");
            Schools = new MultiSelectList(await _context.Schools.ToListAsync(), "Id", "Name");

            var allRoles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
            AllRoles = currentUserRoles.Contains("OrendaAdmin") ? allRoles : allRoles.Where(r => r is "SchoolAdmin" or "Counselor" or "Teacher").ToList();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.FindByIdAsync(Input.Id);
            if (user == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var targetUserRoles = await _userManager.GetRolesAsync(user);

            if (User.IsInRole("DistrictAdmin") && user.DistrictId != currentUser.DistrictId)
                return Forbid();

            if (User.IsInRole("SchoolAdmin")) return Forbid();

            user.DistrictId = Input.DistrictId;
            await _userManager.UpdateAsync(user);

            // Remove all existing school links and re-add
            var existingLinks = _context.UserSchools.Where(us => us.UserId == user.Id);
            _context.UserSchools.RemoveRange(existingLinks);
            foreach (var sid in Input.SelectedSchoolIds.Distinct())
            {
                _context.UserSchools.Add(new UserSchool { UserId = user.Id, SchoolId = sid });
            }
            await _context.SaveChangesAsync();

            var allowedRoles = currentUserRoles.Contains("OrendaAdmin")
                ? await _roleManager.Roles.Select(r => r.Name!).ToListAsync()
                : new List<string> { "SchoolAdmin", "Counselor", "Teacher" };

            Input.SelectedRoles = Input.SelectedRoles.Where(r => allowedRoles.Contains(r)).ToList();

            var toRemove = targetUserRoles.Except(Input.SelectedRoles);
            var toAdd = Input.SelectedRoles.Except(targetUserRoles);

            await _userManager.RemoveFromRolesAsync(user, toRemove);
            await _userManager.AddToRolesAsync(user, toAdd);

            return RedirectToPage("Index");
        }
    }
}
