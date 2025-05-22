using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ORSV2.Pages.Districts
{
    public class CheckpointSchedulesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CheckpointSchedulesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }

        [BindProperty]
        public List<GACheckpointScheduleViewModel> Schedules { get; set; }

        public string DistrictName { get; set; } = "";

        public bool CanEdit => User.IsInRole("OrendaAdmin") || User.IsInRole("DistrictAdmin");

        public async Task<IActionResult> OnGetAsync()
        {
            var district = await _context.Districts.FindAsync(DistrictId);
            if (district == null)
            {
                return NotFound();
            }

            DistrictName = district.Name;

            Schedules = await _context.GACheckpointSchedule
                .Include(s => s.School)
                .Where(s => s.DistrictId == DistrictId)
                .OrderBy(s => s.School.Name)
                .Select(s => new GACheckpointScheduleViewModel
                {
                    ScheduleId = s.ScheduleId,
                    DistrictId = s.DistrictId,
                    SchoolId = s.SchoolId,
                    SchoolName = s.School.Name,
                    Checkpoint1Date = s.Checkpoint1Date,
                    Checkpoint2Date = s.Checkpoint2Date,
                    Checkpoint3Date = s.Checkpoint3Date,
                    Checkpoint4Date = s.Checkpoint4Date,
                    Checkpoint5Date = s.Checkpoint5Date
                })
                .ToListAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!CanEdit)
                return Forbid();

            if (!ModelState.IsValid)
                return Page();

            foreach (var vm in Schedules)
            {
                var entity = await _context.GACheckpointSchedule.FindAsync(vm.ScheduleId);
                if (entity != null)
                {
                    entity.Checkpoint1Date = vm.Checkpoint1Date;
                    entity.Checkpoint2Date = vm.Checkpoint2Date;
                    entity.Checkpoint3Date = vm.Checkpoint3Date;
                    entity.Checkpoint4Date = vm.Checkpoint4Date;
                    entity.Checkpoint5Date = vm.Checkpoint5Date;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Saved"] = true;
            return RedirectToPage(new { DistrictId });
        }
    }
} 