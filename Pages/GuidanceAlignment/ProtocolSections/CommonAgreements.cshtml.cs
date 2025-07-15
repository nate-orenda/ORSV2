using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class CommonAgreementsModel : ProtocolSectionBaseModel
{
    public CommonAgreementsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 7; // Common Agreements section

    [BindProperty]
    public Dictionary<string, string> CommonAgreementResponses { get; set; } = new();

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

    // New properties for the Action Plan
    public List<GAProtocolActionPlanItem> ActionPlanItems { get; set; } = new();

    [BindProperty]
    public GAProtocolActionPlanItem NewActionPlanItem { get; set; } = new GAProtocolActionPlanItem { DueDate = DateTime.Today };

    public SelectList SchoolUsers { get; set; } = new SelectList(new List<object>());
    // Define the focus areas and their associated questions
    public static readonly Dictionary<string, FocusArea> FocusAreas = new()
    {
        {
            "Indicators",
            new FocusArea
            {
                Title = "Indicators",
                Icon = "bi-graph-up",
                Color = "primary",
                Questions = new Dictionary<string, string>
                {
                    { "FocusIndicators", "What INDICATOR(s) will be the focus for the next checkpoint? Why?" },
                    { "IndicatorStrategies", "What guidance strategies will be implemented to improve this INDICATOR? Make Common Agreements." }
                }
            }
        },
        {
            "Quadrants",
            new FocusArea
            {
                Title = "Quadrants",
                Icon = "bi-grid-3x3",
                Color = "success",
                Questions = new Dictionary<string, string>
                {
                    { "FocusQuadrants", "Which QUADRANT(s) will be the focus for the next checkpoint? Why?" },
                    { "QuadrantStrategies", "What guidance strategies will be implemented to improve this Quadrant? Make Common Agreements." }
                }
            }
        },
        {
            "StudentGroups",
            new FocusArea
            {
                Title = "Student Groups",
                Icon = "bi-people",
                Color = "info",
                Questions = new Dictionary<string, string>
                {
                    { "FocusStudentGroups", "Which STUDENT GROUP(s) will be the focus for the next checkpoint? Why?" },
                    { "StudentGroupStrategies", "What guidance strategies will be implemented to improve this Student Group? Make Common Agreements." }
                }
            }
        }
    };

    public class FocusArea
    {
        public string Title { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public Dictionary<string, string> Questions { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        LoadResponses();
        await LoadActionPlanItemsAsync();
        await LoadSchoolUsersAsync(); // Load users for the dropdown
        return Page();
    }
    public async Task<IActionResult> OnPostSaveAllAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveAllResponsesAsync();
        
        TempData["Success"] = "Common Agreements saved successfully!";
        return RedirectToPage(new { protocolId = ProtocolId });
    }

    private void LoadResponses()
    {
        if (Protocol == null) return;

        // Load existing responses for section 7 (exactly like Trends does for section 6)
        var existingResponses = Protocol.SectionResponses?
            .Where(r => r.SectionNumber == 7)
            .ToDictionary(r => r.SectionTitle, r => r.ResponseText ?? string.Empty) ?? new Dictionary<string, string>();

        // Initialize CommonAgreementResponses with existing data
        CommonAgreementResponses = new Dictionary<string, string>();

        // Initialize all possible response keys
        foreach (var focusArea in FocusAreas)
        {
            foreach (var question in focusArea.Value.Questions)
            {
                var responseKey = $"{focusArea.Key}_{question.Key}";
                CommonAgreementResponses[responseKey] = existingResponses.GetValueOrDefault(responseKey, string.Empty);
            }
        }

        // Set last updated info from any section 7 response
        var lastResponse = Protocol?.SectionResponses?
            .Where(r => r.SectionNumber == 7)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        if (lastResponse != null)
        {
            LastUpdated = lastResponse.UpdatedAt.ToString("MMM dd, yyyy 'at' h:mm tt");
            LastUpdatedBy = lastResponse.UpdatedBy ?? "Unknown";
        }
    }
    public async Task<IActionResult> OnPostAddActionPlanItemAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        if (ModelState.IsValid)
        {
            NewActionPlanItem.ProtocolId = ProtocolId;
            NewActionPlanItem.UpdatedBy = User.Identity?.Name ?? "Unknown";
            NewActionPlanItem.UpdatedAt = DateTime.UtcNow;

            _context.GAProtocolActionPlanItems.Add(NewActionPlanItem);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Action Plan item added successfully!";
        }
        else
        {
            // If the model state is invalid, we need to reload the page data
            TempData["ErrorMessage"] = "Please correct the errors and try again.";
            LoadResponses();
            await LoadActionPlanItemsAsync();
            await LoadSchoolUsersAsync();
            return Page();
        }

        return RedirectToPage(new { protocolId = ProtocolId });
    }

    private async Task SaveAllResponsesAsync()
    {
        if (Protocol == null) return;

        var now = DateTime.UtcNow;
        var user = User?.Identity?.Name ?? "Unknown";

        // Get existing section 7 responses (like Trends does for section 6)
        var existingResponses = await _context.GAProtocolSectionResponses
            .Where(r => r.ProtocolId == Protocol.Id && r.SectionNumber == 7)
            .ToListAsync();

        foreach (var kvp in CommonAgreementResponses)
        {
            var sectionTitle = kvp.Key;
            var responseText = kvp.Value?.Trim() ?? string.Empty;

            var existingResponse = existingResponses.FirstOrDefault(r => r.SectionTitle == sectionTitle);

            if (existingResponse != null)
            {
                // Update existing response
                existingResponse.ResponseText = responseText;
                existingResponse.UpdatedAt = now;
                existingResponse.UpdatedBy = user;
            }
            else if (!string.IsNullOrWhiteSpace(responseText))
            {
                // Create new response only if there's content (exactly like Trends)
                _context.GAProtocolSectionResponses.Add(new GAProtocolSectionResponse
                {
                    ProtocolId = Protocol.Id,
                    SectionNumber = 7,
                    SectionTitle = sectionTitle,
                    ResponseText = responseText,
                    UpdatedAt = now,
                    UpdatedBy = user
                });
            }
        }

        await _context.SaveChangesAsync();
    }
    private async Task LoadActionPlanItemsAsync()
    {
        if (Protocol == null) return;
        ActionPlanItems = await _context.GAProtocolActionPlanItems
            .Include(a => a.TeamMember) // Eager load the user details
            .Where(a => a.ProtocolId == Protocol.Id)
            .OrderBy(a => a.DueDate)
            .ToListAsync();
    }

    private async Task LoadSchoolUsersAsync()
    {
        if (School?.DistrictId == null)
        {
            SchoolUsers = new SelectList(new List<object>());
            return;
        }

        // --- Define Roles for Each Access Level ---
        var schoolLevelRoles = new[] { "Counselor", "Teacher", "School Admin" };
        var districtLevelRoles = new[] { "DistrictAdmin" };
        var globalRoles = new[] { "OrendaManager", "OrendaUser", "OrendaAdmin" };

        // --- Build Queries for Each User Group ---

        // 1. Get users assigned specifically to this school
        var schoolUsersQuery = _context.Users
            .Where(u => u.UserSchools.Any(us => us.SchoolId == School.Id) &&
                        _context.UserRoles.Any(ur => ur.UserId == u.Id &&
                                                    _context.Roles.Any(r => r.Id == ur.RoleId &&
                                                                            schoolLevelRoles.Contains(r.Name))));

        // 2. Get District Admins for this school's district
        var districtUsersQuery = _context.Users
            .Where(u => u.DistrictId == School.DistrictId &&
                        _context.UserRoles.Any(ur => ur.UserId == u.Id &&
                                                    _context.Roles.Any(r => r.Id == ur.RoleId &&
                                                                            districtLevelRoles.Contains(r.Name))));

        // 3. Get users with global access
        var globalUsersQuery = _context.Users
            .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id &&
                                                    _context.Roles.Any(r => r.Id == ur.RoleId &&
                                                                        globalRoles.Contains(r.Name))));

        // --- Combine, Order, and Finalize the List ---
        var finalUserList = await schoolUsersQuery
            .Union(districtUsersQuery)
            .Union(globalUsersQuery)
            .Distinct()
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                Id = u.Id,
                // Create a display string with both name and email
                DisplayName = $"{u.FirstName} {u.LastName} ({u.Email})"
            })
            .ToListAsync();

        SchoolUsers = new SelectList(finalUserList, "Id", "DisplayName");
    }
}