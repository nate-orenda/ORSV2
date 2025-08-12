using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class TargetGroupModel : GABasePageModel
    {
        public TargetGroupModel(ApplicationDbContext context) : base(context) { }

        public Models.TargetGroup? CurrentGroup { get; set; }
        public List<GAResults> Students { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
        public string SchoolName { get; set; } = "";
        public string GroupName { get; set; } = "";

        // Re-use the existing helper function from Students.cshtml
        public bool? GetIndicatorValue(GAResults student, string indicatorName)
        {
            return indicatorName switch
            {
                "OnTrack" => student.OnTrack,
                "GPA" => student.GPA,
                "AGGrades" => student.AGGrades,
                "AGSchedule" => student.AGSchedule,
                "Affiliation" => student.Affiliation,
                "FAFSA" => student.FAFSA,
                "CollegeApplication" => student.CollegeApplication,
                "Attendance" => student.Attendance,
                "Referrals" => student.Referrals,
                "Grades" => student.Grades,
                "ELA" => student.AssessmentsELA,
                "Math" => student.AssessmentsMath,
                _ => null
            };
        }

        public async Task<IActionResult> OnGetAsync(int id)
{
    CurrentGroup = await _context.TargetGroups
        .Include(tg => tg.School)
        .ThenInclude(s => s.District)
        .AsNoTracking()
        .FirstOrDefaultAsync(tg => tg.Id == id);

    // This check now satisfies the compiler that CurrentGroup and its School are not null for the rest of the method.
    if (CurrentGroup?.School is null)
    {
        return NotFound();
    }

    if (!await AuthorizeAsync(CurrentGroup.SchoolId))
    {
        return Forbid();
    }

    SchoolName = CurrentGroup.School.Name;
    GroupName = CurrentGroup.Name;
    ViewData["Title"] = $"Target Group - {GroupName}";

    // Fetch the students belonging to this target group
    Students = await _context.TargetGroupStudents
        .Where(tgs => tgs.TargetGroupId == id)
        .Include(tgs => tgs.GAResult)
        .Select(tgs => tgs.GAResult!)
        .AsNoTracking()
        .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
        .ToListAsync();


    // Build breadcrumbs for navigation
    Breadcrumbs = new List<BreadcrumbItem>
    {
        new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
        new BreadcrumbItem { Title = CurrentGroup.School.District?.Name ?? "District", Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = CurrentGroup.School.DistrictId }) },
        new BreadcrumbItem { Title = SchoolName, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = CurrentGroup.SchoolId }) },
        new BreadcrumbItem { Title = GroupName }
    };

    return Page();
}
    }
}