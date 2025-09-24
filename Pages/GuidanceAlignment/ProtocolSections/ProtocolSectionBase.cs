using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public abstract class ProtocolSectionBaseModel : GABasePageModel
{
    protected ProtocolSectionBaseModel(ApplicationDbContext context) : base(context) { }

    [BindProperty(SupportsGet = true)]
    public int ProtocolId { get; set; }

    public GAProtocol? Protocol { get; set; }
    public School? School { get; set; }

    // NEW: expose lock state to views
    public bool IsLocked { get; private set; }

    // If a page wants to allow viewing even when finalized (e.g., Print)
    protected virtual bool AllowReadOnlyWhenFinalized => false;

    public Dictionary<int, string> SectionTitles { get; set; } = new()
    {
        {1, "Introduction"},
        {2, "Targets"},
        {3, "Above the Line"},
        {4, "Demographics"},
        {5, "Indicators"},
        {6, "Trends"},
        {7, "Common Agreements"},
        //{8, "Action Plan"},
        {9, "Wrap Up"}
    };

    public Dictionary<int, string> Responses { get; set; } = new();
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

    public abstract int CurrentSection { get; }

    protected async Task<IActionResult> LoadProtocolDataAsync()
    {
        var authorized = await AuthorizeAsync();
        if (!authorized) return Forbid();

        Protocol = await _context.GAProtocols
            .Include(p => p.SectionResponses)
            .FirstOrDefaultAsync(p => p.Id == ProtocolId);
        if (Protocol == null) return NotFound();

        School = await _context.Schools
            .Include(s => s.District)
            .FirstOrDefaultAsync(s => s.Id == Protocol.SchoolId);
        if (School == null || School.District == null) return NotFound();

        // === Lock state ===
        IsLocked = Protocol.IsFinalized;

        // If a page explicitly allows read-only viewing when finalized, just continue.
        // Otherwise, still continue (render read-only), but add a heads-up.
        if (IsLocked && !AllowReadOnlyWhenFinalized)
        {
            TempData["Alert"] ??= "This protocol is finalized. Editing is disabled until an Orenda user unlocks it.";
        }

        // Load section responses (null-safe)
        var sectionList = Protocol.SectionResponses ?? new List<GAProtocolSectionResponse>();
        foreach (var title in SectionTitles)
        {
            Responses[title.Key] = sectionList.FirstOrDefault(r => r.SectionNumber == title.Key)?.ResponseText ?? string.Empty;
        }

        // Breadcrumbs
        Breadcrumbs = new List<BreadcrumbItem>
        {
            new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
            new BreadcrumbItem { Title = School.District.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
            new BreadcrumbItem { Title = School.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = "Protocols", Url = Url.Page("/GuidanceAlignment/Protocols", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = $"Edit Protocol – CP {Protocol.CP}" }
        };

        // ViewData for layout/partials
        ViewData["ProtocolId"] = ProtocolId;
        ViewData["Protocol"] = Protocol;
        ViewData["School"] = School;
        ViewData["CurrentSection"] = CurrentSection;
        ViewData["SectionTitles"] = SectionTitles;
        ViewData["Responses"] = Responses;
        ViewData["Breadcrumbs"] = Breadcrumbs;
        ViewData["IsLocked"] = IsLocked;

        return Page();
    }

    protected async Task<IActionResult> SaveSectionResponseAsync(int sectionNumber, string content)
    {
        var authorized = await AuthorizeAsync();
        if (!authorized) return Forbid();

        var protocol = await _context.GAProtocols
            .Include(p => p.SectionResponses)
            .FirstOrDefaultAsync(p => p.Id == ProtocolId);
        if (protocol == null) return NotFound();

        // Hard block writes while finalized
        if (protocol.IsFinalized)
        {
            TempData["Alert"] = "This protocol is finalized and cannot be edited. Ask an Orenda user to unlock.";
            return RedirectToPage("/GuidanceAlignment/Protocols", new { schoolId = protocol.SchoolId });
        }

        var now = DateTime.UtcNow;
        var user = User.Identity?.Name ?? "Unknown";

        var existing = (protocol.SectionResponses ??= new List<GAProtocolSectionResponse>())
            .FirstOrDefault(r => r.SectionNumber == sectionNumber);

        if (existing != null)
        {
            existing.ResponseText = content?.Trim();
            existing.UpdatedAt = now;
            existing.UpdatedBy = user;
        }
        else
        {
            _context.GAProtocolSectionResponses.Add(new GAProtocolSectionResponse
            {
                ProtocolId = protocol.Id,
                SectionNumber = sectionNumber,
                SectionTitle = SectionTitles.GetValueOrDefault(sectionNumber) ?? $"Section {sectionNumber}",
                ResponseText = content?.Trim(),
                UpdatedAt = now,
                UpdatedBy = user
            });
        }

        await _context.SaveChangesAsync();
        return RedirectToPage(new { protocolId = ProtocolId });
    }
}
