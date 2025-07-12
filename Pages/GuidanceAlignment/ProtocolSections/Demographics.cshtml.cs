using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class DemographicsModel : ProtocolSectionBaseModel
{
    public DemographicsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 4; // Demographics section

    [BindProperty]
    public string DemographicsResponse { get; set; } = string.Empty;

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

    public List<DemographicSummaryRow> DemographicRows { get; set; } = new();

    public class DemographicSummaryRow
    {
        public string GradeLabel { get; set; } = string.Empty;
        public int Grade { get; set; }
        public decimal EnglishLearnersPct { get; set; }
        public decimal RFEPPct { get; set; }
        public decimal BlackAfricanAmericanPct { get; set; }
        public decimal SpecialEducationPct { get; set; }
        public int TotalEnrollment { get; set; }
        public int EnglishLearnersCount { get; set; }
        public int RFEPCount { get; set; }
        public int BlackAfricanAmericanCount { get; set; }
        public int SpecialEducationCount { get; set; }
        
        // Above the line counts
        public int EnglishLearnersAboveLineCount { get; set; }
        public int RFEPAboveLineCount { get; set; }
        public int BlackAfricanAmericanAboveLineCount { get; set; }
        public int SpecialEducationAboveLineCount { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        // Load the existing response for this section
        DemographicsResponse = Responses.GetValueOrDefault(4, string.Empty);

        // Load last updated info
        var sectionResponse = Protocol?.SectionResponses?.FirstOrDefault(r => r.SectionNumber == 4);
        if (sectionResponse != null)
        {
            LastUpdated = sectionResponse.UpdatedAt.ToString("MM/dd/yyyy hh:mm tt");
            LastUpdatedBy = sectionResponse.UpdatedBy ?? "Unknown";
        }

        // Load demographic data
        await LoadDemographicDataAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostSaveResponseAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveSectionResponseAsync(4, DemographicsResponse);
        
        TempData["Success"] = "Demographics section saved successfully!";
        return RedirectToPage(new { protocolId = ProtocolId });
    }

    private async Task LoadDemographicDataAsync()
    {
        if (Protocol == null || School == null) return;

        var cp = Protocol.CP;
        var schoolYear = Protocol.SchoolYear;

        // Load current checkpoint results
        var currentResults = await _context.GAResults
            .Where(r => r.SchoolId == School.Id && 
                       r.DistrictId == School.DistrictId && 
                       r.SchoolYear == schoolYear && 
                       r.CP == cp)
            .ToListAsync();

        // Build demographic summary by grade
        DemographicRows = currentResults
            .GroupBy(r => r.Grade)
            .Select(gradeGroup =>
            {
                var grade = gradeGroup.Key;
                var gradeResults = gradeGroup.ToList();

                // Filter by demographic groups
                var englishLearners = gradeResults.Where(r => r.LF == "EL").ToList();
                var rfepStudents = gradeResults.Where(r => r.LF == "RFEP").ToList();
                var blackAfricanAmerican = gradeResults.Where(r => r.RaceEthnicity == "Black or African American").ToList();
                var specialEducation = gradeResults.Where(r => r.SWD == true).ToList();

                return new DemographicSummaryRow
                {
                    Grade = grade,
                    GradeLabel = grade == 0 ? "K" : grade.ToString(),
                    EnglishLearnersPct = CalculateAboveLinePct(englishLearners),
                    RFEPPct = CalculateAboveLinePct(rfepStudents),
                    BlackAfricanAmericanPct = CalculateAboveLinePct(blackAfricanAmerican),
                    SpecialEducationPct = CalculateAboveLinePct(specialEducation),
                    TotalEnrollment = gradeResults.Count,
                    EnglishLearnersCount = englishLearners.Count,
                    RFEPCount = rfepStudents.Count,
                    BlackAfricanAmericanCount = blackAfricanAmerican.Count,
                    SpecialEducationCount = specialEducation.Count,
                    EnglishLearnersAboveLineCount = englishLearners.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge"),
                    RFEPAboveLineCount = rfepStudents.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge"),
                    BlackAfricanAmericanAboveLineCount = blackAfricanAmerican.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge"),
                    SpecialEducationAboveLineCount = specialEducation.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge")
                };
            })
            .OrderBy(r => r.Grade == 0 ? -1 : r.Grade) // K first, then 1, 2, 3...
            .ToList();
    }

    private static decimal CalculateAboveLinePct(IEnumerable<GAResults> results)
    {
        var resultList = results.ToList();
        if (!resultList.Any()) return 0;

        var aboveLineCount = resultList.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge");
        return Math.Round((decimal)aboveLineCount / resultList.Count * 100, 1);
    }
}