using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class CommonAgreementsModel : ProtocolSectionBaseModel
{
    public CommonAgreementsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 7; // Common Agreements section

    [BindProperty]
    public Dictionary<string, string> CommonAgreementResponses { get; set; } = new();

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

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

        await LoadResponsesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveResponseAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        // Get the parameters from the request
        var focusArea = Request.Form["focusArea"].ToString();
        var questionKey = Request.Form["questionKey"].ToString();
        var response = Request.Form["response"].ToString();

        if (string.IsNullOrEmpty(focusArea) || string.IsNullOrEmpty(questionKey))
        {
            return new JsonResult(new { success = false, message = "Invalid parameters" });
        }

        // Create a unique key for storing the response
        var responseKey = $"{focusArea}_{questionKey}";
        
        try
        {
            await SaveCommonAgreementResponseAsync(responseKey, response?.Trim() ?? string.Empty);
            return new JsonResult(new { success = true, message = "Response saved successfully!" });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Error saving response: {ex.Message}" });
        }
    }

    private async Task LoadResponsesAsync()
    {
        if (Protocol == null) return;

        // Load existing responses for section 7
        var sectionResponses = await _context.GAProtocolSectionResponses
            .Where(r => r.ProtocolId == Protocol.Id && r.SectionNumber == 7)
            .ToListAsync();

        CommonAgreementResponses = new Dictionary<string, string>();

        // Initialize all possible response keys
        foreach (var focusArea in FocusAreas)
        {
            foreach (var question in focusArea.Value.Questions)
            {
                var responseKey = $"{focusArea.Key}_{question.Key}";
                var existingResponse = sectionResponses.FirstOrDefault(r => r.SectionTitle == responseKey);
                CommonAgreementResponses[responseKey] = existingResponse?.ResponseText ?? string.Empty;
            }
        }

        // Set last updated info
        var lastResponse = sectionResponses
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        if (lastResponse != null)
        {
            LastUpdated = lastResponse.UpdatedAt.ToString("MMM dd, yyyy 'at' h:mm tt");
            LastUpdatedBy = lastResponse.UpdatedBy ?? "Unknown";
        }
    }

    private async Task SaveCommonAgreementResponseAsync(string sectionTitle, string content)
    {
        if (Protocol == null) 
        {
            throw new InvalidOperationException("Protocol is null");
        }

        var now = DateTime.UtcNow;
        var user = User?.Identity?.Name ?? "Unknown";

        // Debug logging
        System.Diagnostics.Debug.WriteLine($"Saving: ProtocolId={Protocol.Id}, SectionTitle={sectionTitle}, Content='{content}', User={user}");

        var existingResponse = await _context.GAProtocolSectionResponses
            .FirstOrDefaultAsync(r => r.ProtocolId == Protocol.Id && 
                                     r.SectionNumber == 7 && 
                                     r.SectionTitle == sectionTitle);

        if (existingResponse != null)
        {
            // Update existing response
            System.Diagnostics.Debug.WriteLine($"Updating existing response with ID: {existingResponse.Id}");
            existingResponse.ResponseText = content;
            existingResponse.UpdatedAt = now;
            existingResponse.UpdatedBy = user;
        }
        else
        {
            // Create new response - always create, even if content is empty (like Trends)
            System.Diagnostics.Debug.WriteLine("Creating new response");
            var newResponse = new GAProtocolSectionResponse
            {
                ProtocolId = Protocol.Id,
                SectionNumber = 7,
                SectionTitle = sectionTitle,
                ResponseText = content,
                UpdatedAt = now,
                UpdatedBy = user
            };
            _context.GAProtocolSectionResponses.Add(newResponse);
        }

        var changes = await _context.SaveChangesAsync();
        System.Diagnostics.Debug.WriteLine($"SaveChanges returned: {changes} changes");
    }
}