using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class WrapUpModel : ProtocolSectionBaseModel
{
    public WrapUpModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 9; // WrapUp section

    [BindProperty]
    public string WrapUpResponse { get; set; } = string.Empty;

    [BindProperty]
    public DateTime? NextSessionDate { get; set; }

    [BindProperty]
    public bool FinalizeProtocol { get; set; }

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public string NextScheduledSession { get; set; } = string.Empty;
    public GACheckpointSchedule? Schedule { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        LoadWrapUpData();
        await LoadScheduleInfoAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        // Validate required fields
        if (string.IsNullOrWhiteSpace(WrapUpResponse))
        {
            ModelState.AddModelError(nameof(WrapUpResponse), "Reflection feedback is required.");
            LoadWrapUpData();
            await LoadScheduleInfoAsync();
            return Page();
        }

        try
        {
            // Save the wrap-up response to section responses
            await SaveSectionResponseAsync(9, WrapUpResponse);

            // Update protocol fields if values are provided
            // Need to reload the protocol to get fresh data
            var protocol = await _context.GAProtocols
                .FirstOrDefaultAsync(p => p.Id == ProtocolId);

            if (protocol == null) 
            {
                TempData["Error"] = "Protocol not found.";
                return RedirectToPage("/GuidanceAlignment/Protocols", new { schoolId = School?.Id });
            }

            var protocolNeedsUpdate = false;

            // Update NextProtocolDate if provided
            if (NextSessionDate.HasValue)
            {
                protocol.NextProtocolDate = NextSessionDate.Value;
                protocolNeedsUpdate = true;
            }

            // Update IsFinalized status if checkbox is checked
            if (FinalizeProtocol)
            {
                protocol.IsFinalized = true;
                protocolNeedsUpdate = true;
            }

            if (protocolNeedsUpdate)
            {
                protocol.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            // Set success message based on finalization status
            if (FinalizeProtocol)
            {
                TempData["Success"] = "Protocol completed and finalized successfully!";
            }
            else
            {
                TempData["Success"] = "Wrap Up section saved successfully!";
            }

            // Return to the main protocols page for the school
            return RedirectToPage("/GuidanceAlignment/Protocols", new { schoolId = School?.Id });
        }
        catch (Exception)
        {
            // Log the exception (you might want to use ILogger here)
            TempData["Error"] = "An error occurred while saving. Please try again.";
            
            // Reload data and return to page
            LoadWrapUpData();
            await LoadScheduleInfoAsync();
            return Page();
        }
    }

    private void LoadWrapUpData()
    {
        if (Protocol?.SectionResponses == null) return;

        // Load existing wrap-up response
        var wrapUpResponse = Protocol.SectionResponses
            .FirstOrDefault(r => r.SectionNumber == 9);

        if (wrapUpResponse != null)
        {
            WrapUpResponse = wrapUpResponse.ResponseText ?? string.Empty;
            LastUpdated = wrapUpResponse.UpdatedAt.ToString("MMM dd, yyyy 'at' h:mm tt");
            LastUpdatedBy = wrapUpResponse.UpdatedBy ?? "Unknown";
        }

        // Load protocol-level data
        if (Protocol != null)
        {
            NextSessionDate = Protocol.NextProtocolDate;
            FinalizeProtocol = Protocol.IsFinalized;
        }
    }

    private async Task LoadScheduleInfoAsync()
    {
        if (School == null) return;

        // Load the checkpoint schedule for this school
        Schedule = await _context.GACheckpointSchedule
            .FirstOrDefaultAsync(s => s.DistrictId == School.DistrictId && s.SchoolId == School.Id);

        if (Schedule != null && Protocol != null)
        {
            // Determine next scheduled session based on current checkpoint
            var nextCheckpointDate = GetNextCheckpointDate(Protocol.CP);
            if (nextCheckpointDate.HasValue)
            {
                NextScheduledSession = $"Next session scheduled for {nextCheckpointDate.Value:MMM dd, yyyy} (Checkpoint {Protocol.CP + 1})";
            }
            else
            {
                NextScheduledSession = "No future checkpoints scheduled for this school year.";
            }
        }
    }

    private DateTime? GetNextCheckpointDate(int currentCP)
    {
        if (Schedule == null) return null;

        return currentCP switch
        {
            1 => Schedule.Checkpoint2Date,
            2 => Schedule.Checkpoint3Date,
            3 => Schedule.Checkpoint4Date,
            4 => Schedule.Checkpoint5Date,
            _ => null // No more checkpoints after CP5
        };
    }
}