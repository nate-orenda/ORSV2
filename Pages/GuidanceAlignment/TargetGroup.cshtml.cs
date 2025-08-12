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

            if (CurrentGroup?.School is null) return NotFound();
            if (!await AuthorizeAsync(CurrentGroup.SchoolId)) return Forbid();

            SchoolName = CurrentGroup.School.Name;
            GroupName  = CurrentGroup.Name;
            ViewData["Title"] = $"Target Group - {GroupName}";

            // figure out current checkpoint + school year for this school
            var today     = DateTime.Today;
            var schedule  = await _context.GACheckpointSchedule
                                .AsNoTracking()
                                .FirstOrDefaultAsync(s => s.SchoolId == CurrentGroup.SchoolId);
            int cp        = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);
            int schoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;

            // Join: TargetGroupStudents (StudentId) -> STU (StuId) -> GAResults (by LocalStudentID + SchoolID)
            Students = await (
                from tgs in _context.TargetGroupStudents.AsNoTracking()
                join stu in _context.STU.AsNoTracking()
                    on tgs.StudentId equals stu.StuId
                join r in _context.GAResults.AsNoTracking()
                    on stu.StuId equals r.StudentId   // <-- join by StuId ↔ StudentId
                where tgs.TargetGroupId == id
                    && r.CP == cp
                    && r.SchoolYear == schoolYear
                select r
            )
            .OrderBy(r => r.LastName).ThenBy(r => r.FirstName)
            .ToListAsync();

            // Breadcrumbs
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