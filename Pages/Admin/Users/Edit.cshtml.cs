// File: ORSV2/Pages/Admin/Users/Edit.cshtml.cs

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
    [Authorize(Roles = "OrendaAdmin,DistrictAdmin")]
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

        public class EditInputModel
        {
            public string Id { get; set; } = "";
            public string? Email { get; set; }
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public int? DistrictId { get; set; }
            public List<int> SchoolIds { get; set; } = new();
            public List<string> Roles { get; set; } = new();
            public int? StaffId { get; set; }
            
            // New property to bind to the unlock checkbox
            public bool UnlockUser { get; set; }
        }

        [BindProperty]
        public EditInputModel Input { get; set; } = new();
        
        // New property to control UI visibility
        public bool IsLockedOut { get; set; }

        public SelectList Districts { get; set; } = null!;
        public List<SelectListItem> AllRoles { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            // Check if the user is currently locked out
            IsLockedOut = await _userManager.IsLockedOutAsync(user);

            Input = new EditInputModel
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DistrictId = user.DistrictId,
                SchoolIds = user.UserSchools.Select(us => us.SchoolId).ToList(),
                Roles = (await _userManager.GetRolesAsync(user)).ToList(),
                StaffId = user.StaffId
            };

            Districts = new SelectList(await _context.Districts.ToListAsync(), "Id", "Name");
            AllRoles = await _roleManager.Roles
                .Select(r => new SelectListItem { Value = r.Name!, Text = r.Name! })
                .ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.Id == Input.Id);

            if (user == null) return NotFound();

            // === Add User Unlock Logic ===
            if (Input.UnlockUser)
            {
                var unlockResult = await _userManager.SetLockoutEndDateAsync(user, null);
                if (!unlockResult.Succeeded)
                {
                    // Handle potential errors if unlocking fails
                    ModelState.AddModelError(string.Empty, "Error: Could not unlock user account.");
                    return Page();
                }
            }

            user.Email = Input.Email;
            user.UserName = Input.Email; // Keep username in sync with email
            user.FirstName = Input.FirstName;
            user.LastName = Input.LastName;
            user.DistrictId = Input.DistrictId;

            await _userManager.UpdateAsync(user);

            _context.UserSchools.RemoveRange(user.UserSchools);
            foreach (var sid in Input.SchoolIds.Distinct())
            {
                _context.UserSchools.Add(new UserSchool { UserId = user.Id, SchoolId = sid });
            }

            var existingRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, existingRoles);
            await _userManager.AddToRolesAsync(user, Input.Roles);

            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }

        public async Task<JsonResult> OnGetSchoolsAsync(int districtId)
        {
            var schools = await _context.Schools
                .Where(s => s.DistrictId == districtId && !s.Inactive)
                .Select(s => new { id = s.Id, name = s.Name })
                .ToListAsync();

            return new JsonResult(schools);
        }
    }
}