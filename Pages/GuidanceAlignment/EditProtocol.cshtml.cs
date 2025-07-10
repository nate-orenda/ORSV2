using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment;

public class EditProtocolModel : GABasePageModel
{
    public EditProtocolModel(ApplicationDbContext context) : base(context) { }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; } // ProtocolId

    public GAProtocol? Protocol { get; set; }
    public School? School { get; set; }
    public District? District { get; set; }
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

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
        if (!authorized) return Forbid();

        Protocol = await _context.GAProtocols
            .Include(p => p.SectionResponses)
            .FirstOrDefaultAsync(p => p.Id == Id);

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

        Breadcrumbs = new List<BreadcrumbItem>
        {
            new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
            new BreadcrumbItem { Title = School!.District!.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
            new BreadcrumbItem { Title = School.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = "Protocols", Url = Url.Page("/GuidanceAlignment/Protocols", new { schoolId = School.Id }) },
            new BreadcrumbItem { Title = $"Edit Protocol – CP {Protocol.CP}" }
        };

        return Page();
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