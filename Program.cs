using Azure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
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

// Add HttpContextAccessor for session access in services
builder.Services.AddHttpContextAccessor();

// --- IDENTITY CONFIGURATION ---
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddRoles<ApplicationRole>();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, CustomClaimsPrincipalFactory>();
// --- END IDENTITY CONFIGURATION ---

// Global antiforgery for Razor Pages
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
})
.AddMvcOptions(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Global antiforgery for MVC controllers (since you MapControllers)
builder.Services.AddControllers(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// (Optional) Explicit header name for AJAX
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
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
        c.Timeout = TimeSpan.FromMinutes(10);
    });

builder.Services.AddSingleton<string>(
    _ => builder.Configuration.GetValue<string>("NotificationEmail")
         ?? throw new InvalidOperationException("NotificationEmail is not configured"));

var app = builder.Build();

app.UseMiddleware<DatabaseWakeupMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.MapControllers();

app.UseStatusCodePagesWithReExecute("/StatusCode", "?code={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.Run();
