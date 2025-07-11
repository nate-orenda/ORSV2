using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class TargetsModel : ProtocolSectionBaseModel
{
    public TargetsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 2; // Targets section

    public List<GAProtocolTarget> GradeLevelTargets { get; set; } = new();
    public List<int> AvailableGradeLevels { get; set; } = new();

    [BindProperty] public Dictionary<int, decimal> UpdatedTargets { get; set; } = new();
    [BindProperty] public GAProtocolTarget? NewTarget { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await LoadTargetsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveTargetsAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        if (UpdatedTargets != null && UpdatedTargets.Any())
        {
            var now = DateTime.UtcNow;
            var user = User.Identity?.Name ?? "Unknown";
            
            var targetIds = UpdatedTargets.Keys.ToList();
            var targetsToUpdate = await _context.GAProtocolTargets
                .Where(t => targetIds.Contains(t.Id))
                .ToListAsync();

            foreach (var target in targetsToUpdate)
            {
                if (UpdatedTargets.TryGetValue(target.Id, out var newValue))
                {
                    if (newValue >= 0 && newValue <= 100)
                    {
                        target.TargetValue = newValue;
                        target.UpdatedAt = now;
                        target.UpdatedBy = user;
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, $"Target value must be between 0 and 100 for Grade {target.GradeLevel}.");
                    }
                }
            }

            if (ModelState.IsValid)
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = "Targets updated successfully!";
                return RedirectToPage(new { protocolId = ProtocolId });
            }
        }

        await LoadTargetsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddTargetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        if (NewTarget != null && NewTarget.GradeLevel > 0 && NewTarget.TargetValue > 0 && NewTarget.TargetValue <= 100)
        {
            var now = DateTime.UtcNow;
            var user = User.Identity?.Name ?? "Unknown";

            NewTarget.TargetName = "Above the Line";
            NewTarget.TargetType = "AboveLine";
            NewTarget.SchoolId = Protocol!.SchoolId;
            NewTarget.DistrictId = Protocol.DistrictId;
            NewTarget.SchoolYear = Protocol.SchoolYear;
            NewTarget.UpdatedAt = now;
            NewTarget.UpdatedBy = user;

            _context.GAProtocolTargets.Add(NewTarget);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Target added successfully!";
            return RedirectToPage(new { protocolId = ProtocolId });
        }

        ModelState.AddModelError(string.Empty, "Please select a grade and enter a valid target percentage (1-100).");
        await LoadTargetsAsync();
        return Page();
    }

    private async Task LoadTargetsAsync()
    {
        GradeLevelTargets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == Protocol!.SchoolId
                    && t.DistrictId == Protocol.DistrictId
                    && t.SchoolYear == Protocol.SchoolYear
                    && t.TargetType == "AboveLine")
            .OrderBy(t => t.GradeLevel)
            .ToListAsync();

        var usedGrades = GradeLevelTargets.Select(t => t.GradeLevel).ToHashSet();
        AvailableGradeLevels = Enumerable.Range(0, 13).Where(g => !usedGrades.Contains(g)).ToList();
    }
}