using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class SchoolsModel : GABasePageModel
    {
        public SchoolsModel(ApplicationDbContext context) : base(context) { }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }

        /// <summary>
        /// A view model representing the information to display for each school card.
        /// </summary>
        public class SchoolCardInfo
        {
            public required School School { get; set; }
            public int CurrentCheckpoint { get; set; }
            public DateTime? LastUpdated { get; set; }
        }

        /// <summary>
        /// The list of school card infos to display on the page.
        /// </summary>
        public List<SchoolCardInfo> SchoolCards { get; set; } = new();

        public string DistrictName { get; set; } = "";

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync())
                return Forbid();

            // Get district name
            var district = await _context.Districts
                .AsNoTracking()
                .Where(d => d.Id == DistrictId)
                .FirstOrDefaultAsync();

            DistrictName = district?.Name ?? "Unknown District";

            ViewData["Breadcrumbs"] = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = DistrictName } // current page, no URL
            };

            // Load active schools within this district that the user is allowed to view
            var schools = await _context.Schools
                .AsNoTracking()
                .Where(s => !s.Inactive &&
                            s.DistrictId == DistrictId &&
                            AllowedSchoolIds.Contains(s.Id))
                .OrderBy(s => s.Name)
                .ToListAsync();

            if (!schools.Any())
            {
                SchoolCards = new List<SchoolCardInfo>();
                return Page();
            }

            // Determine today's date once so it is consistent across all calculations
            var today = DateTime.Today;
            var schoolYear = CurrentCheckpointHelper.GetCurrentSchoolYear(today);

            // Preload all checkpoint schedules for the selected schools to avoid N+1 queries
            var schoolIds = schools.Select(s => s.Id).ToList();

            var schedules = await _context.GACheckpointSchedule
                .AsNoTracking()
                .Where(s => s.DistrictId == DistrictId && schoolIds.Contains(s.SchoolId))
                .ToDictionaryAsync(s => s.SchoolId, s => s);

            // Preload GA results grouped by (SchoolId, CP) to obtain the most recent update per checkpoint
            // Filter by District + SchoolYear so we only use current-year results
            var gaResultsAgg = await _context.GAResults
                .AsNoTracking()
                .Where(r =>
                    r.DistrictId == DistrictId &&
                    schoolIds.Contains(r.SchoolId) &&
                    r.SchoolYear == schoolYear)
                .GroupBy(r => new { r.SchoolId, r.CP })
                .Select(g => new
                {
                    g.Key.SchoolId,
                    g.Key.CP,
                    // Use DateLastUpdated when present, otherwise DateInserted
                    LastUpdated = g.Max(x => x.DateLastUpdated ?? x.DateInserted)
                })
                .ToListAsync();

            var lastUpdateDict = gaResultsAgg
                .ToDictionary(a => (SchoolId: a.SchoolId, CP: a.CP), a => a.LastUpdated);

            // Build the list of school card information objects
            SchoolCards = new List<SchoolCardInfo>();

            foreach (var school in schools)
            {
                // Attempt to find a checkpoint schedule for this school
                schedules.TryGetValue(school.Id, out var schedule);

                // Determine the current checkpoint using the helper
                int cp = CurrentCheckpointHelper.GetCurrentCheckpoint(schedule, today);

                DateTime? lastUpdated = null;
                if (cp > 0 &&
                    lastUpdateDict.TryGetValue((school.Id, cp), out var dt))
                {
                    lastUpdated = dt;
                }

                SchoolCards.Add(new SchoolCardInfo
                {
                    School = school,
                    CurrentCheckpoint = cp,
                    LastUpdated = lastUpdated
                });
            }

            return Page();
        }
    }
}
