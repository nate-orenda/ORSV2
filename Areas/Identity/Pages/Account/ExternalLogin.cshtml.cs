// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using ORSV2.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using ORSV2.Data;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ORSV2.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ExternalLoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;
        private readonly string _notificationEmail;
        public ExternalLoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            IEmailSender emailSender,
            ApplicationDbContext context,
            string notificationEmail)  // Add this parameter
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _emailSender = emailSender;
            _context = context;
            _notificationEmail = notificationEmail;  // Add this assignment
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ProviderDisplayName { get; set; }
        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [StringLength(50)]
            [Display(Name = "First name")]
            public string FirstName { get; set; }

            [StringLength(50)]
            [Display(Name = "Last name")]
            public string LastName { get; set; }
        }

        private class SchoolAccessEntry
        {
            public int SchoolCode { get; set; }
            public bool ReadOnlyAccess { get; set; }
            public bool CommunicationGroup { get; set; }
        }

        public IActionResult OnGet() => RedirectToPage("./Login");

        public IActionResult OnPost(string provider, string returnUrl = null)
        {
            var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        public async Task<IActionResult> OnGetCallbackAsync(string returnUrl = null, string remoteError = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");

            if (remoteError != null)
            {
                ErrorMessage = $"Error from external provider: {remoteError}";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            TempData["LoginProvider"] = info.LoginProvider;
            TempData["ProviderKey"] = info.ProviderKey;
            TempData["ProviderDisplayName"] = info.ProviderDisplayName;

            if (info == null)
            {
                ErrorMessage = "Error loading external login information.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

            var result = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider,
                info.ProviderKey,
                isPersistent: false,
                bypassTwoFactor: true);

            if (result.Succeeded)
            {
                return LocalRedirect(returnUrl);
            }

            if (result.IsLockedOut)
            {
                return RedirectToPage("./Lockout");
            }

            ReturnUrl = returnUrl;
            ProviderDisplayName = info.ProviderDisplayName;

            if (info.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
            {
                Input = new InputModel
                {
                    Email = info.Principal.FindFirstValue(ClaimTypes.Email)
                };
                var given = info.Principal.FindFirstValue(ClaimTypes.GivenName)
                ?? info.Principal.FindFirstValue("given_name");
                var family = info.Principal.FindFirstValue(ClaimTypes.Surname)
                            ?? info.Principal.FindFirstValue("family_name");

                if (string.IsNullOrWhiteSpace(given) || string.IsNullOrWhiteSpace(family))
                {
                    var full = info.Principal.FindFirstValue(ClaimTypes.Name);
                    if (!string.IsNullOrWhiteSpace(full))
                    {
                        var parts = full.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 1) given = parts[0];
                        else if (parts.Length > 1)
                        {
                            family = parts[^1];
                            given = string.Join(' ', parts[..^1]);
                        }
                    }
                }

                Input.FirstName = given;
                Input.LastName  = family;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostConfirmationAsync(string returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("~/");
            
            // Retrieve the info we stored in TempData
            var loginProvider = TempData["LoginProvider"] as string;
            var providerKey = TempData["ProviderKey"] as string;
            var providerDisplayName = TempData["ProviderDisplayName"] as string;

            if (loginProvider == null || providerKey == null)
            {
                ErrorMessage = "Session expired. Please try signing in with Google again.";
                return RedirectToPage("./Login");
            }

            // Get the external login info again
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                ErrorMessage = "Error loading external login information during confirmation.";
                return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
            }

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

                    var primarySchool = await _context.Schools.FirstOrDefaultAsync(s =>
                        s.LocalSchoolId.ToString() == staff.PrimarySchool && s.DistrictId == staff.DistrictId);

                    if (primarySchool != null)
                    {
                        user.UserSchools.Add(new UserSchool { SchoolId = primarySchool.Id, User = user });
                    }

                    if (!string.IsNullOrWhiteSpace(staff.SchoolAccess))
                    {
                        try
                        {
                            var accessList = JsonSerializer.Deserialize<List<SchoolAccessEntry>>(staff.SchoolAccess);
                            if (accessList != null)
                            {
                                foreach (var entry in accessList)
                                {
                                    var school = await _context.Schools.FirstOrDefaultAsync(s =>
                                        s.LocalSchoolId == entry.SchoolCode.ToString() && s.DistrictId == staff.DistrictId);

                                    if (school != null && !user.UserSchools.Any(us => us.SchoolId == school.Id))
                                    {
                                        user.UserSchools.Add(new UserSchool { SchoolId = school.Id, User = user });
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    user.FirstName = Input.FirstName?.Trim();
                    user.LastName  = Input.LastName?.Trim();
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                }

                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    var loginResult = await _userManager.AddLoginAsync(user, info);
                    if (loginResult.Succeeded)
                    {
                        var userId = await _userManager.GetUserIdAsync(user);
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page(
                            "/Account/ConfirmEmail",
                            pageHandler: null,
                            values: new { area = "Identity", userId, code },
                            Request.Scheme);

                        // Send confirmation email
                        await _emailSender.SendEmailAsync(Input.Email, "Confirm your email",
                            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                        // --- Admin notification (multiple mailboxes) ---
                        if (!string.IsNullOrWhiteSpace(_notificationEmail))
                        {
                            var matchedStaff = user.StaffId.HasValue ? "matched" : "not matched";
                            var locked = user.LockoutEnd.HasValue
                                ? $"Locked (until {user.LockoutEnd:yyyy-MM-dd})"
                                : "Unlocked";

                            var adminUrl =
                                Url.Page("/Admin/Users", pageHandler: null, values: new { search = user.Email }, protocol: Request.Scheme)
                                ?? Url.Content("~/Admin/Users?search=" + user.Email);

                            var adminBody = $@"
                                        <h3>New ORSV2 Registration (Google Login)</h3>
                                        <p><strong>Email:</strong> {HtmlEncoder.Default.Encode(user.Email)}</p>
                                        <p><strong>Name:</strong> {HtmlEncoder.Default.Encode(user.FirstName)} {HtmlEncoder.Default.Encode(user.LastName)}</p>
                                        <p><strong>DistrictId:</strong> {user.DistrictId?.ToString() ?? "—"}</p>
                                        <p><strong>StaffId:</strong> {user.StaffId?.ToString() ?? "—"} ({matchedStaff})</p>
                                        <p><strong>Status:</strong> {locked}</p>
                                        <p><strong>Provider:</strong> {HtmlEncoder.Default.Encode(info.LoginProvider)}</p>
                                        <p><a href=""{adminUrl}"">Open user admin</a></p>";

                            var notificationEmails = _notificationEmail
                                .Split(',')
                                .Select(email => email.Trim())
                                .Where(email => !string.IsNullOrWhiteSpace(email))
                                .ToList();

                            foreach (var email in notificationEmails)
                            {
                                try
                                {
                                    await _emailSender.SendEmailAsync(
                                        email,
                                        "New ORSV2 registration (Google)",
                                        adminBody);
                                }
                                catch (Exception)
                                {
                                    // Log but don't fail the registration
                                    // Add logging here if you have ILogger injected
                                }
                            }
                        }
                        // --- end admin notification ---

                        if (_userManager.Options.SignIn.RequireConfirmedAccount)
                        {
                            return RedirectToPage("./RegisterConfirmation", new { Email = Input.Email });
                        }

                        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);
                        return LocalRedirect(returnUrl);
                    }
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            ProviderDisplayName = info.ProviderDisplayName;
            ReturnUrl = returnUrl;
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