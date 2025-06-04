using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;


namespace ORSV2.Pages.GuidanceAlignment
{
    public class GAProfileCardModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public GAProfileCardModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public GAResults Student { get; set; }

        public List<IndicatorSummary> StudentIndicators { get; set; } = new();

        public class IndicatorSummary
        {
            public string Name { get; set; } = string.Empty;
            public bool Met { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            Student = await _context.GAResults.FirstOrDefaultAsync(s => s.ResultId == id);
            if (Student == null) return NotFound();

            var school = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == Student.SchoolId);
            if (school == null) return NotFound();

            var schedule = await _context.GACheckpointSchedule.FirstOrDefaultAsync(s => s.SchoolId == Student.SchoolId);
            int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, DateTime.Today);
            int schoolYear = DateTime.Today.Month >= 8 ? DateTime.Today.Year + 1 : DateTime.Today.Year;

            var indicators = await _context.GAQuadrantIndicators
                .Where(i => i.Grade == Student.Grade && i.CP == cp && i.IsEnabled == true &&
                       (i.SchoolId == null || i.SchoolId == Student.SchoolId) &&
                       (i.DistrictId == null || i.DistrictId == school.DistrictId))
                .GroupBy(i => i.IndicatorName)
                .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
                .ToListAsync();

            foreach (var ind in indicators)
            {
                bool met = ind.IndicatorName switch
                {
                    "OnTrack" => Student.OnTrack == true,
                    "GPA" => Student.GPA == true,
                    "AGGrades" => Student.AGGrades == true,
                    "AGSchedule" => Student.AGSchedule == true,
                    "Affiliation" => Student.Affiliation == true,
                    "FAFSA" => Student.FAFSA == true,
                    "CollegeApplication" => Student.CollegeApplication == true,
                    "Attendance" => Student.Attendance == true,
                    _ => false
                };

                StudentIndicators.Add(new IndicatorSummary
                {
                    Name = ind.IndicatorName,
                    Met = met
                });
            }

            return Page();
        }

    }
}