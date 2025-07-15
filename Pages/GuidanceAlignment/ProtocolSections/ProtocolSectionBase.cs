using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    // Abstract property that each section must implement to identify itself
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

        // Load section responses into dictionary
        foreach (var title in SectionTitles)
        {
            var response = Protocol.SectionResponses?.FirstOrDefault(r => r.SectionNumber == title.Key);
            Responses[title.Key] = response?.ResponseText ?? string.Empty;
        }

        // Build breadcrumbs
        Breadcrumbs = new List<BreadcrumbItem>
        {
            new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
            new BreadcrumbItem { Title = School.District.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
            new BreadcrumbItem { Title = School.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = "Protocols", Url = Url.Page("/GuidanceAlignment/Protocols", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = $"Edit Protocol – CP {Protocol.CP}" }
        };

        // Set ViewData for the layout
        ViewData["ProtocolId"] = ProtocolId;
        ViewData["Protocol"] = Protocol;
        ViewData["School"] = School;
        ViewData["CurrentSection"] = CurrentSection;
        ViewData["SectionTitles"] = SectionTitles;
        ViewData["Responses"] = Responses;
        ViewData["Breadcrumbs"] = Breadcrumbs;

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

        var now = DateTime.UtcNow;
        var user = User.Identity?.Name ?? "Unknown";

        var response = protocol.SectionResponses
            .FirstOrDefault(r => r.SectionNumber == sectionNumber);

        if (response != null)
        {
            response.ResponseText = content?.Trim();
            response.UpdatedAt = now;
            response.UpdatedBy = user;
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