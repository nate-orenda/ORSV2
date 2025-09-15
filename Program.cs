using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Services;

var builder = WebApplication.CreateBuilder(args);

// Get connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

// Connect to SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Configure SMTP settings
builder.Services.Configure<SmtpSettings>(options =>
{
    options.Host = builder.Configuration["SMTP-HOST"] ?? "";
    options.Port = int.TryParse(builder.Configuration["SMTP-PORT"], out var port) ? port : 587;
    options.Username = builder.Configuration["SMTP-EMAIL"] ?? "";
    options.Password = builder.Configuration["SMTP-PASSWORD"] ?? "";
    options.EnableSsl = true;
});

// Register email service
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Register District Focus Service
builder.Services.AddScoped<IDistrictFocusService, DistrictFocusService>();

// Add session support for district focus
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// Add HttpContextAccessor for session access in services
builder.Services.AddHttpContextAccessor();

// --- CORRECTED IDENTITY CONFIGURATION ---
// Set up Identity with roles and custom claims factory
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddRoles<ApplicationRole>(); // Ensure roles are registered

// Register your custom factory AFTER setting up Identity
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, CustomClaimsPrincipalFactory>();
// --- END IDENTITY CONFIGURATION ---

// Add Razor Pages
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Login");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/Register");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/AccessDenied");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/ResetPassword");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/ConfirmEmail");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/RegisterConfirmation");
    options.Conventions.AllowAnonymousToAreaPage("Identity", "/Account/ResendEmailConfirmation");
    options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
});

// Configure application cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.LogoutPath = "/Identity/Account/Logout";
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
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
    options.AddPolicy("CanViewCurriculumForms", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole(
            "OrendaAdmin",
            "OrendaManager",
            "OrendaUser",
            "DistrictAdmin",
            "SchoolAdmin",
            "Teacher"
        );
    });
});

builder.Services.Configure<FunctionEndpointsOptions>(
    builder.Configuration.GetSection("FunctionEndpoints"));
    
builder.Services.AddHttpClient("ImportsClient")
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(10); // extend to 10 minutes
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

// Add this to your Program.cs before app.Run()

// API endpoint to get current user's focus district
// Add this to your Program.cs before app.Run()

// API endpoint to get current user's focus district and available districts
app.MapGet("/api/user/focus-district", async (
    HttpContext context,
    IDistrictFocusService districtFocusService,
    UserManager<ApplicationUser> userManager) =>
{
    if (!context.User.Identity?.IsAuthenticated ?? true)
        return Results.Unauthorized();

    var user = await userManager.FindByNameAsync(context.User.Identity!.Name!);
    if (user == null) return Results.Unauthorized();

    var roles = await userManager.GetRolesAsync(user);
    var isOrendaUser = roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser");

    try
    {
        var focusDistrictId = await districtFocusService.GetFocusDistrictIdAsync(user.Id, isOrendaUser);
        var availableDistricts = await districtFocusService.GetAvailableDistrictsAsync(user.Id, isOrendaUser);
        
        string? focusDistrictName = null;
        if (focusDistrictId.HasValue)
        {
            var focusDistrict = availableDistricts.FirstOrDefault(d => d.Id == focusDistrictId.Value);
            focusDistrictName = focusDistrict?.Name;
        }
        
        return Results.Ok(new 
        { 
            focusDistrictId = focusDistrictId,
            focusDistrictName = focusDistrictName,
            availableDistricts = availableDistricts.Select(d => new { d.Id, d.Name }).ToList()
        });
    }
    catch
    {
        return Results.Ok(new 
        { 
            focusDistrictId = (int?)null, 
            focusDistrictName = (string?)null,
            availableDistricts = new List<object>()
        });
    }
}).RequireAuthorization();

app.UseStatusCodePagesWithReExecute("/StatusCode", "?code={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Use session before authentication
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.Run();