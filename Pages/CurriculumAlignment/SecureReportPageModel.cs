// In SecureReportPageModel.cs
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Models; // For CustomClaimTypes
using System.Security.Claims;

public abstract class SecureReportPageModel : PageModel
{
    public bool IsOrendaUser { get; private set; }
    public bool IsDistrictAdmin { get; private set; }
    public bool IsSchoolAdmin { get; private set; }
    public bool IsTeacher { get; private set; }

    // User's assigned IDs from claims
    public int? UserDistrictId { get; private set; }
    public List<int> UserSchoolIds { get; private set; } = new();
    public int? UserStaffId { get; private set; }

    protected void InitializeUserDataScope()
    {
        // Role checks remain the same
        if (User.IsInRole("OrendaAdmin") || User.IsInRole("OrendaManager") || User.IsInRole("OrendaUser"))
        {
            IsOrendaUser = true;
            return;
        }
        
        IsDistrictAdmin = User.IsInRole("DistrictAdmin");
        IsSchoolAdmin = User.IsInRole("SchoolAdmin");
        IsTeacher = User.IsInRole("Teacher");

        // --- Populate IDs directly from Claims ---
        var districtIdClaim = User.FindFirst(CustomClaimTypes.DistrictId)?.Value;
        if (int.TryParse(districtIdClaim, out var districtId))
        {
            UserDistrictId = districtId;
        }

        // A user might be assigned to multiple schools
        var schoolIdClaims = User.FindAll(CustomClaimTypes.SchoolId);
        foreach (var claim in schoolIdClaims)
        {
            if (int.TryParse(claim.Value, out var schoolId))
            {
                UserSchoolIds.Add(schoolId);
            }
        }
        
        var staffIdClaim = User.FindFirst(CustomClaimTypes.StaffId)?.Value;
        if (int.TryParse(staffIdClaim, out var staffId))
        {
            UserStaffId = staffId;
        }
    }
}