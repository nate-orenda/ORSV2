using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ORSV2.Models;

namespace ORSV2.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            string[] roles = new[] {
                "OrendaAdmin", "OrendaManager", "OrendaUser",
                "DistrictAdmin", "SchoolAdmin", "Counselor", "Teacher"
            };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new ApplicationRole { Name = role });
                }
            }

            var adminEmail = "nate@orendaed.org";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser != null && !await userManager.IsInRoleAsync(adminUser, "OrendaAdmin"))
            {
                await userManager.AddToRoleAsync(adminUser, "OrendaAdmin");
            }

        }
    }
}
