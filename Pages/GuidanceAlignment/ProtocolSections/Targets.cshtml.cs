// Pages/GuidanceAlignment/ProtocolSections/Targets.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class TargetsModel : GABasePageModel
{
    public TargetsModel(ApplicationDbContext context) : base(context) { }

    [BindProperty(SupportsGet = true)] public int ProtocolId { get; set; }

    public GAProtocol? Protocol { get; set; }
    public School? School { get; set; }
    public List<GAProtocolTarget> GradeLevelTargets { get; set; } = new();
    public List<int> AvailableGradeLevels { get; set; } = new();

    [BindProperty] public Dictionary<int, decimal> UpdatedTargets { get; set; } = new();
    [BindProperty] public GAProtocolTarget? NewTarget { get; set; }

    public async Task<IActionResult> OnGetPartialAsync(int protocolId)
    {
        Protocol = await _context.GAProtocols
            .Include(p => p.SectionResponses)
            .FirstOrDefaultAsync(p => p.Id == protocolId);

        if (Protocol == null)
            return NotFound();

        School = await _context.Schools
            .Include(s => s.District)
            .FirstOrDefaultAsync(s => s.Id == Protocol.SchoolId);

        if (School == null)
            return NotFound();

        GradeLevelTargets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == Protocol.SchoolId
                    && t.DistrictId == Protocol.DistrictId
                    && t.SchoolYear == Protocol.SchoolYear
                    && t.TargetType == "AboveLine")
            .OrderBy(t => t.GradeLevel)
            .ToListAsync();

        var usedGrades = GradeLevelTargets.Select(t => t.GradeLevel).ToHashSet();
        AvailableGradeLevels = Enumerable.Range(0, 13).Where(g => !usedGrades.Contains(g)).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostPartialAsync(int protocolId)
    {
        Protocol = await _context.GAProtocols
            .Include(p => p.SectionResponses)
            .FirstOrDefaultAsync(p => p.Id == protocolId);

        if (Protocol == null)
            return NotFound();

        School = await _context.Schools
            .Include(s => s.District)
            .FirstOrDefaultAsync(s => s.Id == Protocol.SchoolId);

        if (School == null)
            return NotFound();

        var now = DateTime.UtcNow;
        var user = User.Identity?.Name ?? "Unknown";

        if (UpdatedTargets != null && UpdatedTargets.Any())
        {
            var targetIds = UpdatedTargets.Keys.ToList();
            var targetsToUpdate = await _context.GAProtocolTargets
                .Where(t => targetIds.Contains(t.Id))
                .ToListAsync();

            foreach (var t in targetsToUpdate)
            {
                if (UpdatedTargets.TryGetValue(t.Id, out var newValue))
                {
                    t.TargetValue = newValue;
                    t.UpdatedAt = now;
                    t.UpdatedBy = user;
                }
            }
            await _context.SaveChangesAsync();
        }

        if (Request.Form.ContainsKey("insertButton"))
        {
            if (NewTarget != null && NewTarget.TargetValue > 0 && NewTarget.TargetValue <= 100)
            {
                NewTarget.TargetName = "Above the Line";
                NewTarget.TargetType = "AboveLine";
                NewTarget.SchoolId = Protocol.SchoolId;
                NewTarget.DistrictId = Protocol.DistrictId;
                NewTarget.SchoolYear = Protocol.SchoolYear;
                NewTarget.UpdatedAt = now;
                NewTarget.UpdatedBy = user;

                _context.GAProtocolTargets.Add(NewTarget);
                await _context.SaveChangesAsync();
            }
        }

        GradeLevelTargets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == Protocol.SchoolId
                    && t.DistrictId == Protocol.DistrictId
                    && t.SchoolYear == Protocol.SchoolYear
                    && t.TargetType == "AboveLine")
            .OrderBy(t => t.GradeLevel)
            .ToListAsync();

        var usedGrades = GradeLevelTargets.Select(t => t.GradeLevel).ToHashSet();
        AvailableGradeLevels = Enumerable.Range(0, 13).Where(g => !usedGrades.Contains(g)).ToList();

        return Page(); // re-render the partial
    }



    public async Task<IActionResult> OnGetAsync()
    {
        Protocol = await _context.GAProtocols.FirstOrDefaultAsync(p => p.Id == ProtocolId);
        if (Protocol == null) return NotFound();

        School = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == Protocol.SchoolId);
        if (School == null || School.District == null) return NotFound();

        GradeLevelTargets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == Protocol.SchoolId &&
                        t.DistrictId == Protocol.DistrictId &&
                        t.SchoolYear == Protocol.SchoolYear &&
                        t.TargetType == "AboveLine")
            .OrderBy(t => t.GradeLevel)
            .ToListAsync();

        var usedGrades = GradeLevelTargets.Select(t => t.GradeLevel).ToHashSet();
        AvailableGradeLevels = Enumerable.Range(0, 13).Where(g => !usedGrades.Contains(g)).ToList();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var protocol = await _context.GAProtocols.FirstOrDefaultAsync(p => p.Id == ProtocolId);
        if (protocol == null) return NotFound();

        var now = DateTime.UtcNow;
        var user = User.Identity?.Name ?? "Unknown";

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

        if (Request.Form.ContainsKey("insertButton"))
        {
            if (NewTarget != null && NewTarget.TargetValue > 0 && NewTarget.TargetValue <= 100)
            {
                NewTarget.TargetName = "Above the Line";
                NewTarget.TargetType = "AboveLine";
                NewTarget.SchoolId = protocol.SchoolId;
                NewTarget.DistrictId = protocol.DistrictId;
                NewTarget.SchoolYear = protocol.SchoolYear;
                NewTarget.UpdatedAt = now;
                NewTarget.UpdatedBy = user;

                _context.GAProtocolTargets.Add(NewTarget);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Target inserted!";
                return RedirectToPage(new { ProtocolId });
            }
        }

        if (!ModelState.IsValid)
        {
            GradeLevelTargets = await _context.GAProtocolTargets
                .Where(t => t.SchoolId == protocol.SchoolId &&
                            t.DistrictId == protocol.DistrictId &&
                            t.SchoolYear == protocol.SchoolYear)
                .OrderBy(t => t.GradeLevel)
                .ThenBy(t => t.TargetName)
                .ToListAsync();
            return Page();
        }

        await _context.SaveChangesAsync();
        TempData["Success"] = "Targets saved!";
        return RedirectToPage(new { ProtocolId });
    }
}