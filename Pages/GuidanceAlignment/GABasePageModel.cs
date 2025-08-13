using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Linq.Expressions;

namespace ORSV2.Pages.GuidanceAlignment
{
    public abstract class GABasePageModel : PageModel
    {
        protected readonly ApplicationDbContext _context;
        public ApplicationUser? CurrentUser { get; private set; }
        public List<int> AllowedSchoolIds { get; private set; } = new();
        public List<int> AllowedDistrictIds { get; private set; } = new();

        // NEW: which service must be enabled for visibility on this page?
        protected enum Service { None, GA, CA }
        protected virtual Service RequiredService => Service.GA; // GA pages default

        private Expression<Func<School, bool>> ServiceFilter =>
            RequiredService switch
            {
                Service.GA => s => s.GA,   // School.GA flag in your model
                Service.CA => s => s.CA,   // School.CA flag in your model
                _           => s => true
            };

        public GABasePageModel(ApplicationDbContext context) => _context = context;

        public async Task<bool> AuthorizeAsync(int? requestedSchoolId = null)
        {
            if (!User.Identity?.IsAuthenticated ?? true) return false;

            CurrentUser = await _context.Users
                .Include(u => u.UserSchools)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            if (CurrentUser is null) return false;

            // role gate (unchanged)
            if (User.IsInRole("Teacher") || !User.IsInRole("OrendaAdmin") &&
                !User.IsInRole("OrendaManager") && !User.IsInRole("OrendaUser") &&
                !User.IsInRole("DistrictAdmin") && !User.IsInRole("SchoolAdmin") &&
                !User.IsInRole("Counselor"))
            {
                return false;
            }

            // Build a scoped school query that ALWAYS enforces: active + enabled + required service
            IQueryable<School> scoped = _context.Schools
                .AsNoTracking()
                .Where(s => !s.Inactive && s.enabled)  // your existing active flag on School
                .Where(ServiceFilter);                 // ← the GA/CA switch happens here

            if (User.IsInRole("OrendaAdmin") || User.IsInRole("OrendaManager") || User.IsInRole("OrendaUser"))
            {
                // no further narrowing
            }
            else if (User.IsInRole("DistrictAdmin") && CurrentUser.DistrictId.HasValue)
            {
                var did = CurrentUser.DistrictId.Value;
                scoped = scoped.Where(s => s.DistrictId == did);
            }
            else if (User.IsInRole("SchoolAdmin") || User.IsInRole("Counselor"))
            {
                var userSchoolIds = CurrentUser.UserSchools.Select(us => us.SchoolId).ToList();
                scoped = scoped.Where(s => userSchoolIds.Contains(s.Id));
            }

            // Materialize the allowed IDs once, service-filtered
            AllowedSchoolIds   = await scoped.Select(s => s.Id).ToListAsync();
            AllowedDistrictIds = await scoped.Select(s => s.DistrictId).Distinct().ToListAsync();

            // If a specific school was requested, it must be in the allowed set
            if (requestedSchoolId.HasValue && !AllowedSchoolIds.Contains(requestedSchoolId.Value))
                return false;

            return true;
        }
    }
}
