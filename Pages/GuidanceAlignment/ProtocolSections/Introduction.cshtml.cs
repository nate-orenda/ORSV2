using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class IntroductionModel : ProtocolSectionBaseModel
{
    public IntroductionModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 1; // Introduction section

    // Accept both 'id' and 'protocolId' for compatibility
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Use Id if ProtocolId is not set (for backward compatibility)
        if (ProtocolId == 0 && Id > 0)
        {
            ProtocolId = Id;
        }

        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        return Page();
    }
}