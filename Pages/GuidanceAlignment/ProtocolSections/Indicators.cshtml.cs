using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class IndicatorsModel : ProtocolSectionBaseModel
{
    public IndicatorsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 5; // Indicators section

    [BindProperty]
    public string IndicatorsResponse { get; set; } = string.Empty;

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        // Load the existing response for this section
        IndicatorsResponse = Responses.GetValueOrDefault(5, string.Empty);

        // Load last updated info
        var sectionResponse = Protocol?.SectionResponses?.FirstOrDefault(r => r.SectionNumber == 5);
        if (sectionResponse != null)
        {
            LastUpdated = sectionResponse.UpdatedAt.ToString("MM/dd/yyyy hh:mm tt");
            LastUpdatedBy = sectionResponse.UpdatedBy ?? "Unknown";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveResponseAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveSectionResponseAsync(5, IndicatorsResponse);
        
        TempData["Success"] = "Indicators section saved successfully!";
        return RedirectToPage(new { protocolId = ProtocolId });
    }
}