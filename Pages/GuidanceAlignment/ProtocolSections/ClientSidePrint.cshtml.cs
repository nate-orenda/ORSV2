using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections
{
    public class ClientSidePrintModel : ProtocolSectionBaseModel
    {
        public ClientSidePrintModel(ApplicationDbContext context) : base(context) { }

        public override int CurrentSection => 0; // Special case for print

        public async Task<IActionResult> OnGetAsync()
        {
            // Just load the basic protocol data - JavaScript will do the rest
            var result = await LoadProtocolDataAsync();
            if (result.GetType() != typeof(PageResult)) return result;

            return Page();
        }
    }
}