using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class ProtocolsModel : GABasePageModel
    {
        public ProtocolsModel(ApplicationDbContext context) : base(context) { }

        public List<GAProtocol> Protocols { get; set; } = new();
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        [BindProperty]
        public int CP { get; set; }
        [BindProperty]
        public int SchoolId { get; set; }
        [BindProperty]
        public int DistrictId { get; set; }
        [BindProperty]
        public int SchoolYear { get; set; }

        public int CurrentCP { get; set; }
        public List<(int CP, string Label)> BuildableCheckpoints { get; set; } = new();
        public School? School { get; set; }
        public District? District { get; set; }

        public async Task<IActionResult> OnGetAsync(int? districtId = null, int? schoolId = null)
        {
            var authorized = await AuthorizeAsync(schoolId);
            if (!authorized)
                return Forbid();

            IQueryable<GAProtocol> query = _context.GAProtocols
                .Include(p => p.School);

            if (schoolId.HasValue)
            {
                query = query.Where(p => p.SchoolId == schoolId.Value);
                School = await _context.Schools
                    .Include(s => s.District)
                    .FirstOrDefaultAsync(s => s.Id == schoolId.Value);
            }
            else if (districtId.HasValue)
            {
                query = query.Where(p =>
                    p.DistrictId == districtId.Value &&
                    AllowedSchoolIds.Contains(p.SchoolId));
            }
            else
            {
                query = query.Where(p => AllowedSchoolIds.Contains(p.SchoolId));
            }

            Protocols = await query
                .OrderByDescending(p => p.SchoolYear)
                .ThenBy(p => p.CP)
                .ToListAsync();

            // If a single school is selected, load CP and buildable options
            if (School != null)
            {
                var schedule = await _context.GACheckpointSchedule
                    .FirstOrDefaultAsync(s =>
                        s.DistrictId == School.DistrictId &&
                        s.SchoolId == School.Id);

                var today = DateTime.Today;
                CurrentCP = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);

                SchoolYear = today.Month >= 8 ? today.Year + 1 : today.Year;
                DistrictId = School.DistrictId;
                SchoolId = School.Id;

                var existing = Protocols.Select(p => p.CP).ToList();
                BuildableCheckpoints = CurrentCheckpointHelper.GetBuildableCheckpointLabels(schedule, today, existing);
            }

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = School!.District!.Name, Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
                new BreadcrumbItem { Title = School.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = School.Id }) },
                new BreadcrumbItem { Title = "View Protocols" }
            };

            return Page();
        }

        public async Task<IActionResult> OnPostCreateProtocolAsync()
        {
            var exists = await _context.GAProtocols.AnyAsync(p =>
                p.DistrictId == DistrictId &&
                p.SchoolId == SchoolId &&
                p.SchoolYear == SchoolYear &&
                p.CP == CP
            );

            if (exists)
            {
                ModelState.AddModelError(string.Empty, "Protocol already exists for that checkpoint.");
                return RedirectToPage();
            }

            var protocol = new GAProtocol
            {
                DistrictId = DistrictId,
                SchoolId = SchoolId,
                SchoolYear = SchoolYear,
                CP = CP,
                CreatedBy = User.Identity?.Name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.GAProtocols.Add(protocol);
            await _context.SaveChangesAsync();

            return RedirectToPage(new { schoolId = SchoolId });
        }

        public async Task<IActionResult> OnPostFinalizeAsync(int protocolId)
        {
            var ok = await AuthorizeAsync();
            if (!ok) return Forbid();

            var p = await _context.GAProtocols.FirstOrDefaultAsync(x => x.Id == protocolId);
            if (p is null) return NotFound();

            if (!p.IsFinalized)
            {
                p.IsFinalized = true;
                p.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { schoolId = p.SchoolId });
        }

        public async Task<IActionResult> OnPostUnlockAsync(int protocolId)
        {
            var ok = await AuthorizeAsync();
            if (!ok) return Forbid();

            if (!(User.IsInRole("OrendaAdmin") || User.IsInRole("OrendaManager") || User.IsInRole("OrendaUser")))
                return Forbid();

            var p = await _context.GAProtocols.FirstOrDefaultAsync(x => x.Id == protocolId);
            if (p is null) return NotFound();

            if (p.IsFinalized)
            {
                p.IsFinalized = false; // simple unlock
                p.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return RedirectToPage(new { schoolId = p.SchoolId });
        }

    }
}
