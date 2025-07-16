using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Claims;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize(Roles = "OrendaAdmin,OrendaManager,OrendaUser,DistrictAdmin,SchoolAdmin,Counselor")]
    public class Form1Model : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public Form1Model(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [BindProperty]
        public Form1ViewModel FormData { get; set; } = new();

        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeUserAsync())
                return Forbid();

            await LoadAvailableDistrictsAsync();
            SetupBreadcrumbs();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!await AuthorizeUserAsync())
                return Forbid();

            await LoadAvailableDistrictsAsync();
            
            if (FormData.SelectedDistrictId.HasValue)
            {
                await LoadUnitsAsync(FormData.SelectedDistrictId.Value);
                
                if (FormData.SelectedUnit.HasValue)
                {
                    await LoadAssessmentsAsync(FormData.SelectedDistrictId.Value, FormData.SelectedUnit.Value);
                }
            }

            SetupBreadcrumbs();
            return Page();
        }

        // AJAX endpoints for cascading dropdowns
        public async Task<IActionResult> OnGetUnitsAsync(int districtId)
        {
            if (!await UserHasAccessToDistrictAsync(districtId))
                return Forbid();

            var units = await _context.Set<Assessment>()
                .Where(a => a.DistrictId == districtId)
                .GroupBy(a => a.Unit)
                .Select(g => new AssessmentDropdownDto
                {
                    Value = g.Key.ToString(),
                    Text = $"Unit {g.Key}",
                    Count = g.Count()
                })
                .OrderBy(u => u.Value)
                .ToListAsync();

            return new JsonResult(units);
        }

        public async Task<IActionResult> OnGetAssessmentsAsync(int districtId, int unit)
        {
            if (!await UserHasAccessToDistrictAsync(districtId))
                return Forbid();

            var assessments = await _context.Set<Assessment>()
                .Where(a => a.DistrictId == districtId && a.Unit == unit)
                .Select(a => new AssessmentDropdownDto
                {
                    Value = a.TestId,
                    Text = !string.IsNullOrEmpty(a.TestName) ? a.TestName : a.TestId,
                    Count = 1
                })
                .OrderBy(a => a.Text)
                .ToListAsync();

            return new JsonResult(assessments);
        }

        private async Task<bool> AuthorizeUserAsync()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return false;

            var user = await _userManager.GetUserAsync(User);
            return user != null;
        }

        private async Task<bool> UserHasAccessToDistrictAsync(int districtId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return false;

            var roles = await _userManager.GetRolesAsync(user);

            // OrendaAdmins, OrendaManagers, and OrendaUsers get access to all districts
            if (roles.Any(r => r == "OrendaAdmin" || r == "OrendaManager" || r == "OrendaUser"))
                return true;

            // District and School admins only get access to their district
            if (roles.Any(r => r == "DistrictAdmin" || r == "SchoolAdmin" || r == "Counselor"))
                return user.DistrictId == districtId;

            return false;
        }

        private async Task LoadAvailableDistrictsAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Any(r => r == "OrendaAdmin" || r == "OrendaManager" || r == "OrendaUser"))
            {
                // Load all active districts that have assessments
                FormData.AvailableDistricts = await _context.Districts
                    .Where(d => !d.Inactive && _context.Set<Assessment>().Any(a => a.DistrictId == d.Id))
                    .OrderBy(d => d.Name)
                    .ToListAsync();
            }
            else if (user.DistrictId.HasValue)
            {
                // Load only user's district if it has assessments
                var userDistrict = await _context.Districts
                    .Where(d => d.Id == user.DistrictId.Value && !d.Inactive 
                           && _context.Set<Assessment>().Any(a => a.DistrictId == d.Id))
                    .FirstOrDefaultAsync();

                if (userDistrict != null)
                {
                    FormData.AvailableDistricts = new List<District> { userDistrict };
                }
            }
        }

        private async Task LoadUnitsAsync(int districtId)
        {
            FormData.AvailableUnits = await _context.Set<Assessment>()
                .Where(a => a.DistrictId == districtId)
                .GroupBy(a => a.Unit)
                .Select(g => new AssessmentDropdownDto
                {
                    Value = g.Key.ToString(),
                    Text = $"Unit {g.Key}",
                    Count = g.Count()
                })
                .OrderBy(u => u.Value)
                .ToListAsync();
        }

        private async Task LoadAssessmentsAsync(int districtId, int unit)
        {
            FormData.AvailableAssessments = await _context.Set<Assessment>()
                .Where(a => a.DistrictId == districtId && a.Unit == unit)
                .Select(a => new AssessmentDropdownDto
                {
                    Value = a.TestId,
                    Text = !string.IsNullOrEmpty(a.TestName) ? a.TestName : a.TestId,
                    Count = 1
                })
                .OrderBy(a => a.Text)
                .ToListAsync();
        }

        private void SetupBreadcrumbs()
        {
            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Curriculum Alignment", Url = Url.Page("/CurriculumAlignment/Index") },
                new BreadcrumbItem { Title = "Form 1" }
            };
        }
    }
}