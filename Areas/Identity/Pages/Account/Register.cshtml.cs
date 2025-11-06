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
using Microsoft.Data.SqlClient; // <-- ADDED for SqlParameter
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
        private readonly RoleManager<ApplicationRole> _roleManager;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            ApplicationDbContext context,
            string notificationEmail,
            RoleManager<ApplicationRole> roleManager) // <-- constructor updated
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _context = context;
            _notificationEmail = notificationEmail;
            _roleManager = roleManager; // <-- Added
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

                    // Load all schools for this district ONCE
                    var schoolsForDistrict = await _context.Schools
                        .Where(s => s.DistrictId == staff.DistrictId)
                        .ToListAsync();

                    // Add Primary School - in-memory lookup
                    if (!string.IsNullOrWhiteSpace(staff.PrimarySchool))
                    {
                        var primarySchool = schoolsForDistrict.FirstOrDefault(s =>
                            s.LocalSchoolId.ToString() == staff.PrimarySchool);

                        if (primarySchool != null && !user.UserSchools.Any(us => us.SchoolId == primarySchool.Id))
                        {
                            user.UserSchools.Add(new UserSchool { SchoolId = primarySchool.Id, User = user });
                        }
                    }

                    // Add from SchoolAccess - in-memory lookups
                    /*if (!string.IsNullOrWhiteSpace(staff.SchoolAccess))
                    {
                        try
                        {
                            var accessList = JsonSerializer.Deserialize<List<SchoolAccessEntry>>(staff.SchoolAccess);
                            if (accessList != null)
                            {
                                foreach (var entry in accessList)
                                {
                                    var school = schoolsForDistrict.FirstOrDefault(s =>
                                        s.LocalSchoolId == entry.SchoolCode.ToString());

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
                    }*/
                }
                else
                {
                    user.FirstName = Input.FirstName?.Trim();
                    user.LastName = Input.LastName?.Trim();
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                }

                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    // --- START: Auto Role Assignment ---
                    string assignedRole = "No role set";
                    if (user.StaffId.HasValue && staff != null)
                    {
                        try
                        {
                            string determinedRole = await DetermineUserRoleAsync(staff);
                            if (!string.IsNullOrWhiteSpace(determinedRole))
                            {
                                // Ensure role exists before assigning
                                if (await _roleManager.RoleExistsAsync(determinedRole))
                                {
                                    await _userManager.AddToRoleAsync(user, determinedRole);
                                    assignedRole = determinedRole;
                                    _logger.LogInformation("Automatically assigned role '{Role}' to user {Email}", determinedRole, user.Email);
                                }
                                else
                                {
                                    _logger.LogWarning("Role '{Role}' not found in database. Cannot assign to user {Email}", determinedRole, user.Email);
                                    assignedRole = $"Role '{determinedRole}' not found";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during automatic role assignment for user {Email}", user.Email);
                            assignedRole = "Error during role assignment";
                        }
                    }
                    // --- END: Auto Role Assignment ---


                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                    // --- START: Capture HttpContext-dependent data BEFORE background task ---
                    string scheme = Request.Scheme;
                    string host = Request.Host.ToUriComponent();
                    string encodedCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    string callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = encodedCode, returnUrl = returnUrl },
                        protocol: scheme);
                    // --- END: Capture HttpContext-dependent data ---

                    // --- START: Send Emails in Background ---
                    // Pass the captured data, not the HttpContext-dependent properties
                    _ = SendRegistrationEmailsAsync(user, assignedRole, callbackUrl, scheme, host);
                    // --- END: Send Emails in Background ---


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

        /// <summary>
        /// Generates email content and sends user confirmation and admin notification emails.
        /// This method is intended to be run in the background ("fire-and-forget") to avoid
        /// blocking the registration process.
        /// </summary>
        private async Task SendRegistrationEmailsAsync(ApplicationUser user, string assignedRole, string callbackUrl, string requestScheme, string requestHost)
        {
            try
            {
                var userId = user.Id;

                // Send confirmation email
                await _emailSender.SendEmailAsync(user.Email, "Confirm your email",
                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                // --- Admin notification (multiple mailboxes) ---
                if (!string.IsNullOrWhiteSpace(_notificationEmail))
                {
                    // Keep PII concise and useful for provisioning
                    var matchedStaff = user.StaffId.HasValue ? "matched" : "not matched";
                    var locked = user.LockoutEnd.HasValue
                        ? $"Locked (until {user.LockoutEnd:yyyy-MM-dd})"
                        : "Unlocked";

                    // Use the passed-in scheme and host
                    var adminUrl = $"{requestScheme}://{requestHost}/Admin/Users/Edit?id={userId}";

                    var adminBody = $@"
                            <h3>New ORSV2 Registration</h3>
                            <p><strong>Email:</strong> {HtmlEncoder.Default.Encode(user.Email)}</p>
                            <p><strong>Name:</strong> {HtmlEncoder.Default.Encode(user.FirstName)} {HtmlEncoder.Default.Encode(user.LastName)}</p>
                            <p><strong>DistrictId:</strong> {user.DistrictId?.ToString() ?? "—"}</p>
                            <p><strong>StaffId:</strong> {user.StaffId?.ToString() ?? "—"} ({matchedStaff})</p>
                            <p><strong>Assigned Role:</strong> {HtmlEncoder.Default.Encode(assignedRole)}</p>
                            <p><strong>Status:</strong> {locked}</p>
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
                                "New ORSV2 registration",
                                adminBody);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed sending new user notification to {Admin}", email);
                        }
                    }
                }
                // --- end admin notification ---
            }
            catch (Exception ex)
            {
                // Log the entire email-sending failure
                _logger.LogError(ex, "Background email sending failed for user {Email}", user.Email);
            }
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

        // Helper class for the raw SQL query result
        private class AssignmentQueryResult
        {
            public string JCDescription { get; set; }
            public string NC1Description { get; set; }
        }

        private async Task<string> DetermineUserRoleAsync(Staff staff)
        {
            if (staff == null || staff.DistrictId == 0) return null;

            var potentialRoles = new HashSet<string>();

            // --- Logic 1: Check StaffAssignments ---
            // Use raw SQL with parameters (BEST PRACTICE)
            var staffIdParam = new SqlParameter("@staffId", staff.StaffId);
            var districtIdParam = new SqlParameter("@districtId", staff.DistrictId);

            var assignments = await _context.Database.SqlQuery<AssignmentQueryResult>($@"
                SELECT
                    jc.Description AS JCDescription,
                    nc1.Description AS NC1Description
                FROM
                    StaffAssignments sa
                LEFT JOIN
                    Codes jc ON sa.DistrictID = jc.DistrictId
                           AND jc.SourceTable = 'STJ'
                           AND jc.SourceField = 'JC'
                           AND sa.JobClassificationCode = jc.Code
                LEFT JOIN
                    Codes nc1 ON sa.DistrictID = nc1.DistrictId
                            AND nc1.SourceTable = 'STJ'
                            AND nc1.SourceField = 'NC1'
                            AND sa.NonClassroomBasedJobAssignmentCode1 = nc1.Code
                WHERE
                    sa.StaffID = {staffIdParam} AND sa.DistrictID = {districtIdParam}
            ").ToListAsync();


            // Define role descriptions
            var teacherJCCodes = new[] { "Teacher", "Itinerant Teacher" };
            var adminPupilJCCodes = new[] { "Administrator", "Pupil Services" };
            var districtAdminNC1Codes = new[] {
                "Admin other subject area", "Admin staff development", "Administrator - Program Coordinator",
                "Deputy or associate superintendent (general)", "Superintendent", "Teacher on Special Assignment"
            };
            var schoolAdminNC1Codes = new[] { "Principal", "Vice principal or assoc/asst administrator" };
            var counselorNC1Codes = new[] { "Counselor", "Counselors and Rehabilitation Counselors" };

            if (assignments.Any())
            {
                foreach (var assignment in assignments)
                {
                    if (assignment.JCDescription != null)
                    {
                        if (teacherJCCodes.Contains(assignment.JCDescription))
                        {
                            potentialRoles.Add("Teacher");
                        }
                        else if (adminPupilJCCodes.Contains(assignment.JCDescription))
                        {
                            // Check NC1
                            if (assignment.NC1Description != null)
                            {
                                if (districtAdminNC1Codes.Contains(assignment.NC1Description))
                                    potentialRoles.Add("DistrictAdmin");
                                else if (schoolAdminNC1Codes.Contains(assignment.NC1Description))
                                    potentialRoles.Add("SchoolAdmin");
                                else if (counselorNC1Codes.Contains(assignment.NC1Description))
                                    potentialRoles.Add("Counselor");
                            }
                        }
                    }
                }
            }

            // --- Logic 2: Fallback to Staff.JobTitle ---
            if (!potentialRoles.Any() && !string.IsNullOrWhiteSpace(staff.JobTitle))
            {
                var jobTitle = staff.JobTitle.ToLower().Trim();

                // Check for DistrictAdmin (highest priority)
                // These are high-confidence keywords.
                if (jobTitle.Contains("superintendent") || // Catches "Superintendent", "Deputy Superintendent", "Associate Superintendent"
                    jobTitle.Contains("asst superintendent") || // Catches "Asst Supt"
                    jobTitle.Contains("assistant superintendent") ||
                    jobTitle.Contains("chief business officer") ||
                    jobTitle.Contains("chief academic officer") ||
                    jobTitle.Contains("chief technology officer") ||
                    jobTitle.Contains("chief of staff") ||
                    jobTitle.Contains("executive director") ||
                    jobTitle.Contains("director of") || // e.g., "Director of MOT"
                    jobTitle.Contains("director,") || // e.g., "Director, Nutrition Services"
                    jobTitle.Contains("director iii") || // e.g., "Director III, Lcap"
                    jobTitle.Contains("director iv") ||
                    jobTitle.Contains("director v") ||
                    jobTitle.Equals("director") ||
                    jobTitle.Contains("teacher on special assignment")) // Match NC1 logic
                {
                    potentialRoles.Add("DistrictAdmin");
                }
                // Check for SchoolAdmin
                else if (jobTitle.Contains("principal") || // Catches "Principal", "Vice Principal", "Asst Principal" etc.
                         jobTitle.Contains("asst principal") ||
                         jobTitle.Contains("assistant principal") ||
                         jobTitle.Contains("vice principal"))
                {
                    potentialRoles.Add("SchoolAdmin");
                }
                // Check for Counselor
                else if (jobTitle.Contains("counselor"))
                {
                    potentialRoles.Add("Counselor");
                }
                // Check for Teacher (lowest priority)
                // Exclude "teacher on special assignment" which is handled above.
                else if ((jobTitle.Contains("teacher") && !jobTitle.Contains("special assignment")) ||
                         jobTitle.Contains("instructor"))
                {
                    potentialRoles.Add("Teacher");
                }
            }

            // --- Logic 3: Determine highest priority role ---
            if (potentialRoles.Contains("DistrictAdmin"))
                return "DistrictAdmin";
            if (potentialRoles.Contains("SchoolAdmin"))
                return "SchoolAdmin";
            if (potentialRoles.Contains("Counselor"))
                return "Counselor";
            if (potentialRoles.Contains("Teacher"))
                return "Teacher";

            return null; // No role determined
        }
    }
}

