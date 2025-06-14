using ORSV2.Data;
using ORSV2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Connect to SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Set up Identity with roles
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
.AddRoles<ApplicationRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// Add Razor Pages and allow anonymous access to Identity pages
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Login");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Register");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/AccessDenied");
    options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
});

// Configure the application cookie minimally
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.LogoutPath = "/Identity/Account/Logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax; // ✅ Critical for external logins
});


// Add Google authentication
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}


// Require authentication globally
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Seed roles and admin user on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await SeedData.Initialize(services);
}

app.Run();
