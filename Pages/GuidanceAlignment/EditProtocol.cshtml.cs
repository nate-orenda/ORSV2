using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class EditProtocolModel : GABasePageModel
    {
        public EditProtocolModel(ApplicationDbContext context) : base(context) { }
        public List<GAProtocolTarget> GradeLevelTargets { get; set; } = new();

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

        [BindProperty]
        public Dictionary<int, decimal> UpdatedTargets { get; set; } = new();

        [BindProperty]
        public List<GAProtocolTarget> NewTargets { get; set; } = new();

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

            GradeLevelTargets = await _context.GAProtocolTargets
                .Where(t => t.SchoolId == Protocol.SchoolId
                        && t.DistrictId == Protocol.DistrictId
                        && t.SchoolYear == Protocol.SchoolYear
                        && t.TargetType == "AboveLine")
                .OrderBy(t => t.GradeLevel)
                .ToListAsync();

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
            if (UpdatedTargets.Any())
            {
                var targetIds = UpdatedTargets.Keys.ToList();
                var targetsToUpdate = await _context.GAProtocolTargets
                    .Where(t => targetIds.Contains(t.Id))
                    .ToListAsync();

                foreach (var t in targetsToUpdate)
                {
                    if (UpdatedTargets.TryGetValue(t.Id, out var newValue))
                    {
                        if (newValue >= 0 && newValue <= 100)
                        {
                            t.TargetValue = newValue;
                            t.UpdatedAt = now;
                            t.UpdatedBy = user;
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, $"Invalid value for {t.TargetName} (Grade {t.GradeLevel}). Must be 0–100.");
                        }
                    }
                }
            }
            if (NewTargets.Any())
            {
                foreach (var t in NewTargets.Where(t => t.TargetValue > 0))
                {
                    t.TargetName = "Above the Line";
                    t.TargetType = "AboveLine";
                    t.UpdatedAt = now;
                    t.UpdatedBy = user;

                    _context.GAProtocolTargets.Add(t);
                }
            }

            if (!ModelState.IsValid)
            {
                // Reload targets for redisplay on error
                GradeLevelTargets = await _context.GAProtocolTargets
                    .Where(t => t.SchoolId == protocol.SchoolId
                            && t.DistrictId == protocol.DistrictId
                            && t.SchoolYear == protocol.SchoolYear)
                    .OrderBy(t => t.GradeLevel)
                    .ThenBy(t => t.TargetName)
                    .ToListAsync();

                return Page();
            }
            await _context.SaveChangesAsync();
            TempData["Success"] = "Protocol saved!";
            return RedirectToPage(new { id = Id });
        }

        public async Task<IActionResult> OnPostSaveSectionAsync(int Section, string Content)
        {
            var authorized = await AuthorizeAsync();
            if (!authorized) return Forbid();

            var protocol = await _context.GAProtocols
                .Include(p => p.SectionResponses)
                .FirstOrDefaultAsync(p => p.Id == Id);

            if (protocol == null) return NotFound();

            var now = DateTime.UtcNow;
            var user = User.Identity?.Name ?? "Unknown";

            var response = protocol.SectionResponses
                .FirstOrDefault(r => r.SectionNumber == Section);

            if (response != null)
            {
                response.ResponseText = Content?.Trim();
                response.UpdatedAt = now;
                response.UpdatedBy = user;
            }
            else
            {
                _context.GAProtocolSectionResponses.Add(new GAProtocolSectionResponse
                {
                    ProtocolId = protocol.Id,
                    SectionNumber = Section,
                    SectionTitle = SectionTitles.GetValueOrDefault(Section) ?? $"Section {Section}",
                    ResponseText = Content?.Trim(),
                    UpdatedAt = now,
                    UpdatedBy = user
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Section {Section} saved.";
            return RedirectToPage(new { id = Id });
        }

    }
}
