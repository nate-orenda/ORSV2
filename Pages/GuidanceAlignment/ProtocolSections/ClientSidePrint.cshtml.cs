using Microsoft.AspNetCore.Mvc;
using ORSV2.Data;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections
{
    public class ClientSidePrintModel : ProtocolSectionBaseModel
    {
        public ClientSidePrintModel(ApplicationDbContext context) : base(context) { }

        // Let finalized protocols be *viewed* (printed), not edited
        protected override bool AllowReadOnlyWhenFinalized => true;

        public override int CurrentSection => 0; // Special case for print

        public async Task<IActionResult> OnGetAsync()
        {
            // ProtocolId is bound from query (?protocolId=...) via the base [BindProperty(SupportsGet = true)]
            return await LoadProtocolDataAsync(); // will return Page() even when finalized
        }
    }
}
