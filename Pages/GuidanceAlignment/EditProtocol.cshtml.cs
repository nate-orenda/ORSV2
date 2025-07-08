using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class EditProtocolModel : GABasePageModel
    {
        public EditProtocolModel(ApplicationDbContext context) : base(context) { }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; } // ProtocolId

        public GAProtocol? Protocol { get; set; }

        public Dictionary<int, string> SectionTitles { get; set; } = new()
        {
            {1, "Introduction"},
            {2, "Targets"},
            {3, "Above the Line"},
            {4, "Demographics"},
            {5, "Indicators"},
            {6, "Trends"},
            {7, "Common Agreements"},
            {8, "Action Plan"},
            {9, "Wrap Up"}
        };

        [BindProperty]
        public Dictionary<int, string> Responses { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            var authorized = await AuthorizeAsync();
            if (!authorized)
                return Forbid();

            Protocol = await _context.GAProtocols
                .Include(p => p.SectionResponses)
                .FirstOrDefaultAsync(p => p.Id == Id);

            if (Protocol == null)
                return NotFound();

            // Load section responses into dictionary
            foreach (var title in SectionTitles)
            {
                var response = Protocol.SectionResponses?.FirstOrDefault(r => r.SectionNumber == title.Key);
                Responses[title.Key] = response?.ResponseText ?? string.Empty;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var protocol = await _context.GAProtocols
                .Include(p => p.SectionResponses)
                .FirstOrDefaultAsync(p => p.Id == Id);

            if (protocol == null)
                return NotFound();

            var now = DateTime.UtcNow;
            var user = User.Identity?.Name ?? "Unknown";

            foreach (var kvp in Responses)
            {
                var existing = protocol.SectionResponses.FirstOrDefault(r => r.SectionNumber == kvp.Key);
                if (existing != null)
                {
                    existing.ResponseText = kvp.Value;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = user;
                }
                else
                {
                    _context.GAProtocolSectionResponses.Add(new GAProtocolSectionResponse
                    {
                        ProtocolId = protocol.Id,
                        SectionNumber = kvp.Key,
                        SectionTitle = SectionTitles[kvp.Key],
                        ResponseText = kvp.Value,
                        UpdatedAt = now,
                        UpdatedBy = user
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Protocol saved!";
            return RedirectToPage(new { id = Id });
        }
    }
}
