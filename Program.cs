using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Configure Azure Key Vault for production with robust error handling
if (builder.Environment.IsProduction())
{
    try
    {
        Console.WriteLine("Configuring Azure Key Vault...");
        var keyVaultEndpoint = new Uri("https://promotekeys.vault.azure.net/");
        
        // Configure DefaultAzureCredential for App Service Managed Identity
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeEnvironmentCredential = false,
            ExcludeManagedIdentityCredential = false, // This is what we want for App Service
            ExcludeSharedTokenCacheCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeAzureCliCredential = true,
            ExcludeInteractiveBrowserCredential = true
        });

        builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, credential);
        Console.WriteLine("✅ Azure Key Vault configured successfully");
        
        // Test Key Vault access by trying to read a configuration value
        var testValue = builder.Configuration["SMTP-HOST"];
        Console.WriteLine($"✅ Key Vault test - SMTP-HOST: {(string.IsNullOrEmpty(testValue) ? "NOT FOUND" : "Found")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Azure Key Vault configuration failed: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        Console.WriteLine("⚠️  Will attempt to use App Service Configuration as fallback");
        
        // Don't throw - we'll use App Service configuration as fallback
    }
}

// Get connection string with multiple fallback options
string? connectionString = 
    builder.Configuration.GetConnectionString("DefaultConnection") ??
    builder.Configuration["DefaultConnection"] ??
    builder.Configuration["SQLCONNSTR_DefaultConnection"] ??
    builder.Configuration["CUSTOMCONNSTR_DefaultConnection"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("❌ Connection string 'DefaultConnection' not found in any configuration source.");
}

Console.WriteLine("✅ Database connection string found");

// Connect to SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(30);
    }));

// Configure SMTP settings with detailed logging
builder.Services.Configure<SmtpSettings>(options =>
{
    options.Host = builder.Configuration["SMTP-HOST"] ?? "";
    
    var portString = builder.Configuration["SMTP-PORT"] ?? "587";
    options.Port = int.TryParse(portString, out var port) ? port : 587;
    
    options.Username = builder.Configuration["SMTP-EMAIL"] ?? "";
    options.Password = builder.Configuration["SMTP-PASSWORD"] ?? "";
    options.EnableSsl = true;
    
    // Log SMTP configuration status (without sensitive data)
    Console.WriteLine($"SMTP Configuration:");
    Console.WriteLine($"  Host: {(string.IsNullOrEmpty(options.Host) ? "❌ NOT SET" : "✅ " + options.Host)}");
    Console.WriteLine($"  Port: {options.Port}");
    Console.WriteLine($"  Username: {(string.IsNullOrEmpty(options.Username) ? "❌ NOT SET" : "✅ Set")}");
    Console.WriteLine($"  Password: {(string.IsNullOrEmpty(options.Password) ? "❌ NOT SET" : "✅ Set")}");
});

// Register email service
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Set up Identity with roles
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    
    // Lockout settings  
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    
    // User settings
    options.User.RequireUniqueEmail = true;
})
.AddRoles<ApplicationRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

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
    Console.WriteLine("✅ Google authentication configured");
}
else
{
    Console.WriteLine("⚠️  Google authentication not configured (missing client ID or secret)");
}

// Require authentication globally
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Test database connection
try
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var canConnect = await context.Database.CanConnectAsync();
    Console.WriteLine($"Database connection: {(canConnect ? "✅ SUCCESS" : "❌ FAILED")}");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Database connection test failed: {ex.Message}");
}

Console.WriteLine($"🚀 Application starting in {app.Environment.EnvironmentName} environment");
app.Run();