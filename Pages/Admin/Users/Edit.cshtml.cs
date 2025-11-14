// File: ORSV2/Pages/Admin/Users/Edit.cshtml.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Cryptography.X509Certificates; // Note: This seems unused, but I'll leave it if it was in your original.

namespace ORSV2.Pages.Admin.Users
{
    // UPDATED: Added SchoolAdmin to allow them to access the page with limited rights
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

        // NEW: Simple model to hold school data for the view
        public class SchoolModel
        {
            public int Id { get; set; }
            public string? Name { get; set; }
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

        // NEW PROPERTY TO CONTROL DISTRICT DROPDOWN
        public bool CanChangeDistrict { get; set; }

        public SelectList Districts { get; set; } = null!;
        public List<SelectListItem> AllRoles { get; set; } = new();

        // NEW: This property will hold the schools for the user's *current* district on page load
        public List<SchoolModel> InitialSchools { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(string id)
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            // === START NEW LOGIC ===
            // Determine if the current admin can change districts
            CanChangeDistrict = User.IsInRole("OrendaAdmin");
            // === END NEW LOGIC ===

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
            
            // === START MODIFICATION ===
            // Get the filtered list of roles the current admin is allowed to assign
            var allowedRoleNames = await GetAllowedRoleNamesAsync();
            
            // Populate the dropdown with only those allowed roles
            AllRoles = allowedRoleNames
                .Select(r => new SelectListItem { Value = r, Text = r })
                .ToList();
            // === END MODIFICATION ===

            // === START NEW EFFICIENCY LOGIC ===
            // Pre-load the schools for the user's current district to prevent a follow-up AJAX call
            if (user.DistrictId.HasValue)
            {
                InitialSchools = await _context.Schools
                    .Where(s => s.DistrictId == user.DistrictId.Value && !s.Inactive)
                    .Select(s => new SchoolModel { Id = s.Id, Name = s.Name })
                    .ToListAsync();
            }
            // === END NEW EFFICIENCY LOGIC ===

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.Id == Input.Id);

            if (user == null) return NotFound();

            // === START NEW LOGIC: DISTRICT CHANGE SECURITY CHECK ===
            CanChangeDistrict = User.IsInRole("OrendaAdmin");
            if (!CanChangeDistrict && Input.DistrictId != user.DistrictId)
            {
                // A non-OrendaAdmin is trying to change the district. Block it.
                ModelState.AddModelError(string.Empty, 
                    "Error: You do not have permission to change this user's district.");
                
                // Repopulate page data and return
                await RepopulatePageDataAsync(user);
                return Page();
            }
            // === END NEW LOGIC ===

            // === Add User Unlock Logic ===
            if (Input.UnlockUser)
            {
                var unlockResult = await _userManager.SetLockoutEndDateAsync(user, null);
                if (!unlockResult.Succeeded)
                {
                    // Handle potential errors if unlocking fails
                    ModelState.AddModelError(string.Empty, "Error: Could not unlock user account.");

                    // REPOPULATE DATA FOR PAGE RETURN
                    await RepopulatePageDataAsync(user);
                    return Page();
                }
            }

            // === START: ROLE ASSIGNMENT SECURITY CHECK ===
    
            // 1. Get the roles this admin is *allowed* to manage
            var allowedRoleNames = await GetAllowedRoleNamesAsync();
            var allowedRolesSet = new HashSet<string>(allowedRoleNames);

            // 2. Get the roles the admin is *attempting* to assign
            var attemptedRoles = Input.Roles;

            // 3. Find any roles they are trying to assign that are NOT in their allowed list
            var forbiddenRolesAttempted = attemptedRoles.Where(r => !allowedRolesSet.Contains(r)).ToList();

            if (forbiddenRolesAttempted.Any())
            {
                // 4. If they try anything funny, add a model error and stop processing
                ModelState.AddModelError(string.Empty, 
                    $"Error: You do not have permission to assign the following role(s): {string.Join(", ", forbiddenRolesAttempted)}");
                
                // 5. IMPORTANT: We must re-populate the page data before returning
                await RepopulatePageDataAsync(user, allowedRoleNames);
                return Page();
            }
            // === END: ROLE ASSIGNMENT SECURITY CHECK ===

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
            
            // We already know Input.Roles is safe because of the check above
            await _userManager.AddToRolesAsync(user, Input.Roles);

            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }

        /// <summary>
        /// Helper method to repopulate page data (Districts, AllRoles, IsLockedOut)
        /// when returning Page() from OnPostAsync to prevent errors.
        /// </summary>
        private async Task RepopulatePageDataAsync(ApplicationUser user, List<string>? allowedRoleNames = null)
        {
            Districts = new SelectList(await _context.Districts.ToListAsync(), "Id", "Name");
            
            // If we don't already have the list, get it.
            var roles = allowedRoleNames ?? await GetAllowedRoleNamesAsync();
            AllRoles = roles
                .Select(r => new SelectListItem { Value = r, Text = r })
                .ToList();

            IsLockedOut = await _userManager.IsLockedOutAsync(user);

            // === START NEW LOGIC ===
            CanChangeDistrict = User.IsInRole("OrendaAdmin");
            // === END NEW LOGIC ===
        }

        /// <summary>
        /// Gets the list of role names the currently logged-in admin
        /// is allowed to assign.
        /// </summary>
        private async Task<List<string>> GetAllowedRoleNamesAsync()
        {
            // === START OF MODIFICATION ===

            // 1. Get the ApplicationUser object for the currently logged-in admin
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                // Should not happen if [Authorize] is used, but good to check
                return new List<string>(); 
            }

            // 2. Get the admin's roles ONE TIME
            var currentUserRoles = await _userManager.GetRolesAsync(currentUser);
            var currentUserRolesSet = new HashSet<string>(currentUserRoles);

            // === END OF MODIFICATION ===

            // 1. Get all roles that exist in the database (this is fine)
            var allRolesFromDb = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();

            // 2. Apply filtering based on the *current* admin's role
            
            // MODIFIED: Check the local set instead of User.IsInRole()
            if (currentUserRolesSet.Contains("OrendaAdmin"))
            {
                // OrendaAdmins can assign all roles
                return allRolesFromDb;
            }

            // MODIFIED: Check the local set instead of User.IsInRole()
            if (currentUserRolesSet.Contains("DistrictAdmin"))
            {
                // Per your request, DistrictAdmins cannot assign Orenda roles
                var forbiddenOrendaRoles = new HashSet<string> { "OrendaAdmin", "OrendaManager", "OrendaUser" };
                return allRolesFromDb
                    .Where(roleName => !forbiddenOrendaRoles.Contains(roleName))
                    .ToList();
            }

            // MODIFIED: Check the local set instead of User.IsInRole()
            if (currentUserRolesSet.Contains("SchoolAdmin"))
            {
                // Per your request, SchoolAdmins cannot assign Orenda roles or DistrictAdmin
                var forbiddenRoles = new HashSet<string> { "OrendaAdmin", "OrendaManager", "OrendaUser", "DistrictAdmin" };
                return allRolesFromDb
                    .Where(roleName => !forbiddenRoles.Contains(roleName))
                    .ToList();
            }

            // Default: If the user has none of the above roles (but somehow passed [Authorize]),
            // they can assign nothing.
            return new List<string>();
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