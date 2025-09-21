// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ORSV2.Data;
using ORSV2.Models;
using System.Text.Json;
using Microsoft.Extensions.Options;


namespace ORSV2.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;
        private readonly string _notificationEmail;
        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext context,
            string notificationEmail)
        {
            _userManager   = userManager;
            _userStore     = userStore;
            _emailStore    = GetEmailStore();
            _signInManager = signInManager;
            _logger        = logger;
            _emailSender   = emailSender;
            _context       = context;
            _notificationEmail = notificationEmail;
        }


        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }

            [Required, StringLength(50)]
            [Display(Name = "First name")]
            public string FirstName { get; set; }

            [Required, StringLength(50)]
            [Display(Name = "Last name")]
            public string LastName { get; set; }
        }

        private class SchoolAccessEntry
        {
            public int SchoolCode { get; set; }
            public bool ReadOnlyAccess { get; set; }
            public bool CommunicationGroup { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = CreateUser();
                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

                var staff = await _context.Staff.FirstOrDefaultAsync(s => s.EmailAddress == Input.Email);
                if (staff != null && staff.Inactive != true)
                {
                    user.FirstName = staff.FirstName;
                    user.LastName = staff.LastName;
                    user.DistrictId = staff.DistrictId;
                    user.StaffId = staff.StaffId;

                    // Add Primary School
                    var primarySchool = await _context.Schools.FirstOrDefaultAsync(s => s.LocalSchoolId.ToString() == staff.PrimarySchool && s.DistrictId == staff.DistrictId);
                    if (primarySchool != null && !user.UserSchools.Any(us => us.SchoolId == primarySchool.Id))
                    {
                        user.UserSchools.Add(new UserSchool { SchoolId = primarySchool.Id, User = user });
                    }

                    // Add from SchoolAccess
                    if (!string.IsNullOrWhiteSpace(staff.SchoolAccess))
                    {
                        try
                        {
                            var accessList = JsonSerializer.Deserialize<List<SchoolAccessEntry>>(staff.SchoolAccess);
                            if (accessList != null)
                            {
                                foreach (var entry in accessList)
                                {
                                    var school = await _context.Schools.FirstOrDefaultAsync(s => s.LocalSchoolId == entry.SchoolCode.ToString() && s.DistrictId == staff.DistrictId);
                                    if (school != null && !user.UserSchools.Any(us => us.SchoolId == school.Id))
                                    {
                                        user.UserSchools.Add(new UserSchool { SchoolId = school.Id, User = user });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to parse SchoolAccess for {Email}: {Error}", staff.EmailAddress, ex.Message);
                        }
                    }
                }
                else
                {
                    user.FirstName = Input.FirstName?.Trim();
                    user.LastName  = Input.LastName?.Trim();
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                }

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
                    
                    // --- Admin notification (single mailbox) ---
                    if (!string.IsNullOrWhiteSpace(_notificationEmail))
                    {
                        // Keep PII concise and useful for provisioning
                        var matchedStaff = user.StaffId.HasValue ? "matched" : "not matched";
                        var locked       = user.LockoutEnd.HasValue
                            ? $"Locked (until {user.LockoutEnd:yyyy-MM-dd})"
                            : "Unlocked";

                        // Admin page link (prefer Page() when available; fall back to Content)
                        var adminUrl =
                            Url.Page("/Admin/Users", pageHandler: null, values: new { search = user.Email }, protocol: Request.Scheme)
                            ?? Url.Content("~/Admin/Users?search=" + user.Email);

                        var adminBody = $@"
                    <h3>New ORSV2 Registration</h3>
                    <p><strong>Email:</strong> {HtmlEncoder.Default.Encode(user.Email)}</p>
                    <p><strong>Name:</strong> {HtmlEncoder.Default.Encode(user.FirstName)} {HtmlEncoder.Default.Encode(user.LastName)}</p>
                    <p><strong>DistrictId:</strong> {user.DistrictId?.ToString() ?? "—"}</p>
                    <p><strong>StaffId:</strong> {user.StaffId?.ToString() ?? "—"} ({matchedStaff})</p>
                    <p><strong>Status:</strong> {locked}</p>
                    <p><a href=""{HtmlEncoder.Default.Encode(adminUrl)}"">Open user admin</a></p>";

                        try
                        {
                            await _emailSender.SendEmailAsync(
                                _notificationEmail,
                                "New ORSV2 registration",
                                adminBody);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed sending new user notification to {Admin}", _notificationEmail);
                        }
                    }
                    // --- end admin notification ---


                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. Ensure it has a parameterless constructor.");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
