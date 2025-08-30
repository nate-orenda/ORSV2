using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Claims;
using System.Threading.Tasks;

public class CustomClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly ApplicationDbContext _context;

    public CustomClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        ApplicationDbContext context) // Inject your DbContext
        : base(userManager, roleManager, optionsAccessor)
    {
        _context = context;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Find the full user object including related data
        var userWithDetails = await _context.Users
            .Include(u => u.UserSchools)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (userWithDetails != null)
        {
            // Add DistrictId claim if it exists
            if (userWithDetails.DistrictId.HasValue)
            {
                // Use .Value to access the non-nullable integer before converting to a string
                identity.AddClaim(new Claim(CustomClaimTypes.DistrictId, userWithDetails.DistrictId.Value.ToString()));
            }

            // Add StaffId claim if it exists
            if (userWithDetails.StaffId.HasValue)
            {
                // Do the same for StaffId
                identity.AddClaim(new Claim(CustomClaimTypes.StaffId, userWithDetails.StaffId.Value.ToString()));
            }

            // Add a separate SchoolId claim for each assigned school
            foreach (var userSchool in userWithDetails.UserSchools)
            {
                identity.AddClaim(new Claim(CustomClaimTypes.SchoolId, userSchool.SchoolId.ToString()));
            }
        }

        return identity;
    }
}