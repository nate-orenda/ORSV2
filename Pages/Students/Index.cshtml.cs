using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Models.ViewModels;

namespace ORSV2.Pages.Students
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<StudentListViewModel> Students { get; set; }

        public async Task OnGetAsync(Guid districtId, Guid schoolId)
        {
            Students = await _context.STU
                .Where(s => s.DistrictID == districtId && s.SchoolID == schoolId && (s.Inactive == null || s.Inactive == false))
                .Select(s => new StudentListViewModel
                {
                    STU_ID = s.STU_ID,
                    LocalStudentID = s.LocalStudentID,
                    FirstName = s.FirstName,
                    LastName = s.LastName,
                    MiddleName = s.MiddleName,
                    Grade = s.Grade
                })
                .OrderBy(s => s.LastName)
                .ThenBy(s => s.FirstName)
                .ToListAsync();
        }
    }
}
