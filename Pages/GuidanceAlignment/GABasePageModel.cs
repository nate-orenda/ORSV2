using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public abstract class GABasePageModel : PageModel
    {
        protected readonly ApplicationDbContext _context;
        public ApplicationUser? CurrentUser { get; private set; }
        public List<int> AllowedSchoolIds { get; private set; } = new();
        public List<int> AllowedDistrictIds { get; private set; } = new();

        public GABasePageModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> AuthorizeAsync(int? requestedSchoolId = null)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return false;

            CurrentUser = await _context.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);

            if (CurrentUser == null)
                return false;

            // Check role access
            if (User.IsInRole("Teacher") || !User.IsInRole("OrendaAdmin") &&
                !User.IsInRole("OrendaManager") && !User.IsInRole("OrendaUser") &&
                !User.IsInRole("DistrictAdmin") && !User.IsInRole("SchoolAdmin") &&
                !User.IsInRole("Counselor"))
            {
                return false;
            }

            if (User.IsInRole("OrendaAdmin") || User.IsInRole("OrendaManager") || User.IsInRole("OrendaUser"))
            {
                AllowedDistrictIds = await _context.Districts.Select(d => d.Id).ToListAsync();
                AllowedSchoolIds = await _context.Schools.Select(s => s.Id).ToListAsync();
            }
            else if (User.IsInRole("DistrictAdmin") && CurrentUser.DistrictId.HasValue)
            {
                AllowedDistrictIds.Add(CurrentUser.DistrictId.Value);
                AllowedSchoolIds = await _context.Schools
                    .Where(s => s.DistrictId == CurrentUser.DistrictId)
                    .Select(s => s.Id)
                    .ToListAsync();
            }
            else if (User.IsInRole("SchoolAdmin") || User.IsInRole("Counselor"))
            {
                AllowedSchoolIds = CurrentUser.UserSchools.Select(us => us.SchoolId).ToList();
                AllowedDistrictIds = await _context.Schools
                    .Where(s => AllowedSchoolIds.Contains(s.Id))
                    .Select(s => s.DistrictId)
                    .Distinct()
                    .ToListAsync();
            }

            // If a school is specified, restrict access
            if (requestedSchoolId.HasValue && !AllowedSchoolIds.Contains(requestedSchoolId.Value))
                return false;

            return true;
        }
    }
}