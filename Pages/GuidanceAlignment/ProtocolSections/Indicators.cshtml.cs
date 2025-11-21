using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class IndicatorsModel : ProtocolSectionBaseModel
{
    public IndicatorsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 5; // Indicators section

    [BindProperty(SupportsGet = true)]
    public string DemographicFilter { get; set; } = "All";

    [BindProperty]
    public string IndicatorsResponse { get; set; } = string.Empty;

    public List<IndicatorTableGroup> TableGroups { get; set; } = new();
    public List<(string Value, string Label)> DemographicOptions { get; set; } = new()
    {
        ("All", "All Students"),
        ("EL", "English Learners"),
        ("SWD", "Students w/ Disabilities"),
        ("RFEP", "RFEP"),
        ("Black", "Black or African American"),
        ("Hispanic", "Hispanic or Latino"),
        ("SED", "Socio-Economically Disadvantaged")
    };

    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

    public class IndicatorTableGroup
    {
        public string Title { get; set; } = string.Empty;
        public string GradeRange { get; set; } = string.Empty;
        public List<int> Grades { get; set; } = new();
        public List<IndicatorRow> Rows { get; set; } = new();
    }

    public class IndicatorRow
    {
        public string IndicatorName { get; set; } = string.Empty;
        public Dictionary<int, GradeMetrics> GradeData { get; set; } = new();
    }

    public class GradeMetrics
    {
        public decimal PercentMet { get; set; }
        public int CountMet { get; set; }
        public int TotalCount { get; set; }
        public bool IsAvailable { get; set; } = true; // For indicators not available at certain grades
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await LoadIndicatorDataAsync();
        LoadResponseData();
        return Page();
    }

    public async Task<IActionResult> OnGetTablesAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await LoadIndicatorDataAsync();
        
        return Partial("_IndicatorTables", this);
    }

    public async Task<IActionResult> OnPostSaveResponseAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveSectionResponseAsync(5, IndicatorsResponse);
        
        TempData["Success"] = "Indicators section saved successfully!";
        return RedirectToPage(new { protocolId = ProtocolId, demographicFilter = DemographicFilter });
    }

    private async Task LoadIndicatorDataAsync()
    {
        if (Protocol == null || School == null) return;

        var cp = Protocol.CP;
        var districtId = School.DistrictId;
        var schoolId = School.Id;

        // Load all finalized results for this school/checkpoint/year with demographic filtering
        var allResults = await GetFilteredFinalizedResultsAsync(schoolId, districtId, cp);

        // Get indicators for grades 6-12
        var indicatorsQuery = _context.GAQuadrantIndicators
            .Where(i => i.CP == cp && 
                       i.IsEnabled == true &&
                       i.Grade >= 6 && i.Grade <= 12 &&
                       (i.SchoolId == null || i.SchoolId == schoolId) &&
                       (i.DistrictId == null || i.DistrictId == districtId))
            .GroupBy(i => new { i.Grade, i.IndicatorName })
            .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First());

        var indicators = await indicatorsQuery.ToListAsync();

        // Group results by grade for easier lookup
        var resultsByGrade = allResults.GroupBy(r => r.Grade).ToDictionary(g => g.Key, g => g.ToList());

        // Determine which grade ranges have both indicators AND enrollment data
        var gradesWithData = resultsByGrade.Keys.Where(grade => resultsByGrade[grade].Any()).ToList();
        var hasMiddleSchoolData = gradesWithData.Any(g => g >= 6 && g <= 8) && 
                                 indicators.Any(i => i.Grade >= 6 && i.Grade <= 8);
        var hasHighSchool9_10Data = gradesWithData.Any(g => g >= 9 && g <= 10) && 
                                   indicators.Any(i => i.Grade >= 9 && i.Grade <= 10);
        var hasHighSchool11_12Data = gradesWithData.Any(g => g >= 11 && g <= 12) && 
                                    indicators.Any(i => i.Grade >= 11 && i.Grade <= 12);

        TableGroups = new List<IndicatorTableGroup>();

        // Add Middle School table if grades 6-8 have both indicators and enrollment
        if (hasMiddleSchoolData)
        {
            var middleSchoolGrades = gradesWithData.Where(g => g >= 6 && g <= 8).OrderBy(g => g).ToList();
            if (middleSchoolGrades.Any())
            {
                var table = BuildIndicatorTable("Middle School", "Grades 6–8", middleSchoolGrades, indicators, resultsByGrade, cp);
                if (table.Rows.Any(r => r.GradeData.Values.Any(gd => gd.IsAvailable && gd.TotalCount > 0)))
                {
                    TableGroups.Add(table);
                }
            }
        }

        // Add High School tables only if we have enrollment data
        if (hasHighSchool9_10Data)
        {
            var grades9_10 = gradesWithData.Where(g => g >= 9 && g <= 10).OrderBy(g => g).ToList();
            if (grades9_10.Any())
            {
                var table = BuildIndicatorTable("Predictive Indicators Stage", "Grades 9–10", grades9_10, indicators, resultsByGrade, cp);
                if (table.Rows.Any(r => r.GradeData.Values.Any(gd => gd.IsAvailable && gd.TotalCount > 0)))
                {
                    TableGroups.Add(table);
                }
            }
        }

        if (hasHighSchool11_12Data)
        {
            var grades11_12 = gradesWithData.Where(g => g >= 11 && g <= 12).OrderBy(g => g).ToList();
            if (grades11_12.Any())
            {
                var table = BuildIndicatorTable("End of Journey Indicators", "Grades 11–12", grades11_12, indicators, resultsByGrade, cp);
                if (table.Rows.Any(r => r.GradeData.Values.Any(gd => gd.IsAvailable && gd.TotalCount > 0)))
                {
                    TableGroups.Add(table);
                }
            }
        }
    }

    private async Task<List<GAResultsFinalized>> GetFilteredFinalizedResultsAsync(int schoolId, int districtId, int cp)
    {
        var query = _context.GAResultsFinalized
            .Where(r => r.SchoolId == schoolId && 
                       r.DistrictId == districtId && 
                       r.SchoolYear == Protocol!.SchoolYear &&
                       r.CP == cp &&
                       r.ProtocolId == ProtocolId);

        // Apply demographic filter
        query = DemographicFilter switch
        {
            "EL" => query.Where(r => r.LF == "EL"),
            "SWD" => query.Where(r => r.SWD == true),
            "RFEP" => query.Where(r => r.LF == "RFEP"),
            "Black" => query.Where(r => r.RaceEthnicity == "Black or African American"),
            "Hispanic" => query.Where(r => r.RaceEthnicity == "Hispanic or Latino"),
            "SED" => query.Where(r => r.SED == true),
            _ => query // "All" - no additional filter
        };

        return await query.ToListAsync();
    }

    private IndicatorTableGroup BuildIndicatorTable(
        string title, 
        string gradeRange, 
        List<int> grades,
        IEnumerable<GAQuadrantIndicators> allIndicators,
        Dictionary<int, List<GAResultsFinalized>> resultsByGrade,
        int cp)
    {
        var group = new IndicatorTableGroup
        {
            Title = title,
            GradeRange = gradeRange,
            Grades = grades,
            Rows = new List<IndicatorRow>()
        };

        // Get all unique indicators for these grades
        var uniqueIndicators = allIndicators
            .Where(i => grades.Contains(i.Grade))
            .Select(i => i.IndicatorName)
            .Distinct()
            .OrderBy(name => name)
            .ToList();

        foreach (var indicatorName in uniqueIndicators)
        {
            var row = new IndicatorRow
            {
                IndicatorName = indicatorName,
                GradeData = new Dictionary<int, GradeMetrics>()
            };

            foreach (var grade in grades)
            {
                // Check if this indicator exists for this grade
                var indicatorExists = allIndicators.Any(i => i.Grade == grade && i.IndicatorName == indicatorName);
                
                if (indicatorExists && resultsByGrade.TryGetValue(grade, out var gradeResults))
                {
                    var (percentMet, countMet, totalCount) = CalculateIndicatorMetrics(indicatorName, gradeResults);
                    row.GradeData[grade] = new GradeMetrics
                    {
                        PercentMet = percentMet,
                        CountMet = countMet,
                        TotalCount = totalCount,
                        IsAvailable = true
                    };
                }
                else if (indicatorExists)
                {
                    // Indicator exists but no students in this grade
                    row.GradeData[grade] = new GradeMetrics
                    {
                        PercentMet = 0,
                        CountMet = 0,
                        TotalCount = 0,
                        IsAvailable = true
                    };
                }
                else
                {
                    // Indicator not available for this grade (e.g., FAFSA for grade 11)
                    row.GradeData[grade] = new GradeMetrics
                    {
                        IsAvailable = false
                    };
                }
            }

            group.Rows.Add(row);
        }

        return group;
    }

    private (decimal PercentMet, int CountMet, int TotalCount) CalculateIndicatorMetrics(string indicatorName, List<GAResultsFinalized> results)
    {
        if (!results.Any())
            return (0, 0, 0);

        int countMet = indicatorName switch
        {
            "OnTrack" => results.Count(r => r.OnTrack == true),
            "GPA" => results.Count(r => r.GPA == true),
            "AGGrades" => results.Count(r => r.AGGrades == true),
            "AGSchedule" => results.Count(r => r.AGSchedule == true),
            "Affiliation" => results.Count(r => r.Affiliation == true),
            "FAFSA" => results.Count(r => r.FAFSA == true),
            "CollegeApplication" => results.Count(r => r.CollegeApplication == true),
            "Attendance" => results.Count(r => r.Attendance == true),
            "Referrals" => results.Count(r => r.Referrals == true),
            "Grades" => results.Count(r => r.Grades == true),
            "ELA" => results.Count(r => r.AssessmentsELA == true),
            "Math" => results.Count(r => r.AssessmentsMath == true),
            _ => 0
        };

        int totalCount = results.Count;
        decimal percentMet = totalCount > 0 ? Math.Round((decimal)countMet / totalCount * 100, 1) : 0;

        return (percentMet, countMet, totalCount);
    }

    private static bool Is12thGradeOnlyIndicator(string indicatorName)
    {
        // These indicators are typically only available for 12th grade
        return indicatorName is "FAFSA" or "CollegeApplication";
    }

    private void LoadResponseData()
    {
        // Load the existing response for this section
        IndicatorsResponse = Responses.GetValueOrDefault(5, string.Empty);

        // Load last updated info
        var sectionResponse = Protocol?.SectionResponses?.FirstOrDefault(r => r.SectionNumber == 5);
        if (sectionResponse != null)
        {
            LastUpdated = sectionResponse.UpdatedAt.ToString("MM/dd/yyyy hh:mm tt");
            LastUpdatedBy = sectionResponse.UpdatedBy ?? "Unknown";
        }
    }
}