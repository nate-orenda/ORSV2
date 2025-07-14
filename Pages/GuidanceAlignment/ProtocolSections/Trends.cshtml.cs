using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class TrendsModel : ProtocolSectionBaseModel
{
    public TrendsModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 6; // Trends section

    // Form binding properties
    [BindProperty]
    public Dictionary<string, string> TrendResponses { get; set; } = new();

    // Display properties
    public string LastUpdated { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;

    // Data for context - organized by demographic groups
    public IndicatorTableGroup? PredictiveGrades6_8 { get; set; }
    public IndicatorTableGroup? PredictiveGrades9_10 { get; set; }
    public IndicatorTableGroup? EndOfJourneyGrades { get; set; }
    public IndicatorTableGroup? EnglishLearnerData { get; set; }
    public IndicatorTableGroup? RFEPData { get; set; }
    public IndicatorTableGroup? AfricanAmericanData { get; set; }
    public IndicatorTableGroup? StudentsWithDisabilitiesData { get; set; }

    // Define the trend questions and their keys
    public static readonly Dictionary<string, string> TrendQuestions = new()
    {
        ["GradeLevelPredictive"] = "What trends do you notice in the predictive grades (6-10)?",
        ["GradeLevelPredictiveWhy"] = "Why do you think these predictive grade trends are occurring?",
        ["GradeLevelEndJourney"] = "What trends do you notice in the end of journey grades (11-12)?",
        ["GradeLevelEndJourneyWhy"] = "Why do you think these end of journey trends are occurring?",
        ["EnglishLearner"] = "What trends do you notice for English Learners?",
        ["EnglishLearnerWhy"] = "Why do you think these English Learner trends are occurring?",
        ["RFEP"] = "What trends do you notice for RFEP students?",
        ["RFEPWhy"] = "Why do you think these RFEP trends are occurring?",
        ["AfricanAmerican"] = "What trends do you notice for African American students?",
        ["AfricanAmericanWhy"] = "Why do you think these African American trends are occurring?",
        ["StudentsWithDisabilities"] = "What trends do you notice for Students with Disabilities?",
        ["StudentsWithDisabilitiesWhy"] = "Why do you think these Students with Disabilities trends are occurring?"
    };

    public class IndicatorTableGroup
    {
        public string Title { get; set; } = string.Empty;
        public string GradeRange { get; set; } = string.Empty;
        public List<int> Grades { get; set; } = new();
        public List<IndicatorRow> Rows { get; set; } = new();
        public int TotalStudents { get; set; } // Fixed: Total for this specific group only
        public Dictionary<string, int> QuadrantCounts { get; set; } = new(); // Added: Quadrant breakdown
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
        public bool IsAvailable { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await LoadTrendsDataAsync();
        LoadExistingResponses();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveTrendsResponsesAsync();
        
        TempData["Success"] = "Trends analysis saved successfully!";
        return RedirectToPage(new { protocolId = ProtocolId });
    }

    private async Task LoadTrendsDataAsync()
    {
        if (Protocol == null || School == null) return;

        var cp = Protocol.CP;
        var schoolYear = Protocol.SchoolYear;
        var districtId = School.DistrictId;
        var schoolId = School.Id;

        // Load all GAResults for this school/checkpoint/year
        var allResults = await _context.GAResults
            .Where(r => r.SchoolId == schoolId && 
                       r.DistrictId == districtId && 
                       r.SchoolYear == schoolYear && 
                       r.CP == cp)
            .ToListAsync();

        // Get all indicators for grades 6-12 from the most specific scope available
        var indicators = await _context.GAQuadrantIndicators
            .Where(i => i.CP == cp && 
                       i.IsEnabled == true &&
                       i.Grade >= 6 && i.Grade <= 12 &&
                       (i.SchoolId == null || i.SchoolId == schoolId) &&
                       (i.DistrictId == null || i.DistrictId == districtId))
            .GroupBy(i => new { i.Grade, i.IndicatorName })
            .Select(g => g.OrderByDescending(i => i.SchoolId != null ? 3 : i.DistrictId != null ? 2 : 1).First())
            .ToListAsync();

        // Build targeted data sections for each question group
        BuildPredictiveGradesSections(allResults, indicators, cp);
        BuildEndOfJourneySections(allResults, indicators, cp);
        BuildDemographicSections(allResults, indicators, cp);
    }

    private void BuildPredictiveGradesSections(List<GAResults> allResults, List<GAQuadrantIndicators> indicators, int cp)
    {
        // Predictive Grades 6-8 (Middle School)
        var grades6_8 = allResults.Where(r => r.Grade >= 6 && r.Grade <= 8).ToList();
        if (grades6_8.Any())
        {
            var grades = grades6_8.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            PredictiveGrades6_8 = BuildIndicatorTable("Middle School Predictive Indicators", "Grades 6–8", grades, indicators, grades6_8, cp);
        }

        // Predictive Grades 9-10 (High School)
        var grades9_10 = allResults.Where(r => r.Grade >= 9 && r.Grade <= 10).ToList();
        if (grades9_10.Any())
        {
            var grades = grades9_10.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            PredictiveGrades9_10 = BuildIndicatorTable("High School Predictive Indicators", "Grades 9–10", grades, indicators, grades9_10, cp);
        }
    }

    private void BuildEndOfJourneySections(List<GAResults> allResults, List<GAQuadrantIndicators> indicators, int cp)
    {
        // End of Journey Grades 11-12
        var grades11_12 = allResults.Where(r => r.Grade >= 11 && r.Grade <= 12).ToList();
        if (grades11_12.Any())
        {
            var grades = grades11_12.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            EndOfJourneyGrades = BuildIndicatorTable("End of Journey Indicators", "Grades 11–12", grades, indicators, grades11_12, cp);
        }
    }

    private void BuildDemographicSections(List<GAResults> allResults, List<GAQuadrantIndicators> indicators, int cp)
    {
        // English Learners (LF = 'EL') - Filter by school for total count
        var englishLearners = allResults.Where(r => r.LF == "EL" && r.SchoolId == School!.Id).ToList();
        if (englishLearners.Any())
        {
            var grades = englishLearners.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            EnglishLearnerData = BuildIndicatorTable("English Learner Performance", $"Grades {grades.Min()}–{grades.Max()}", grades, indicators, englishLearners, cp);
        }

        // RFEP Students (LF = 'RFEP')
        var rfepStudents = allResults.Where(r => r.LF == "RFEP").ToList();
        if (rfepStudents.Any())
        {
            var grades = rfepStudents.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            RFEPData = BuildIndicatorTable("RFEP Student Performance", $"Grades {grades.Min()}–{grades.Max()}", grades, indicators, rfepStudents, cp);
        }

        // African American Students
        var africanAmericanStudents = allResults.Where(r => r.RaceEthnicity == "Black or African American").ToList();
        if (africanAmericanStudents.Any())
        {
            var grades = africanAmericanStudents.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            AfricanAmericanData = BuildIndicatorTable("African American Student Performance", $"Grades {grades.Min()}–{grades.Max()}", grades, indicators, africanAmericanStudents, cp);
        }

        // Students with Disabilities (SWD = true)
        var studentsWithDisabilities = allResults.Where(r => r.SWD == true).ToList();
        if (studentsWithDisabilities.Any())
        {
            var grades = studentsWithDisabilities.Select(r => r.Grade).Distinct().OrderBy(g => g).ToList();
            StudentsWithDisabilitiesData = BuildIndicatorTable("Students with Disabilities Performance", $"Grades {grades.Min()}–{grades.Max()}", grades, indicators, studentsWithDisabilities, cp);
        }
    }

    private IndicatorTableGroup BuildIndicatorTable(
        string title, 
        string gradeRange, 
        List<int> grades,
        IEnumerable<GAQuadrantIndicators> allIndicators,
        List<GAResults> filteredResults,
        int cp)
    {
        var group = new IndicatorTableGroup
        {
            Title = title,
            GradeRange = gradeRange,
            Grades = grades,
            Rows = new List<IndicatorRow>(),
            TotalStudents = filteredResults.Count, // Fixed: Only count students in this specific group
            QuadrantCounts = filteredResults
                .Where(r => !string.IsNullOrEmpty(r.Quadrant))
                .GroupBy(r => r.Quadrant!)
                .ToDictionary(g => g.Key, g => g.Count()) // Added: Quadrant breakdown
        };

        // Group filtered results by grade
        var resultsByGrade = filteredResults.GroupBy(r => r.Grade).ToDictionary(g => g.Key, g => g.ToList());

        // Get unique indicators for these grades
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
                    row.GradeData[grade] = new GradeMetrics { IsAvailable = false };
                }
            }

            group.Rows.Add(row);
        }

        return group;
    }

    private (decimal PercentMet, int CountMet, int TotalCount) CalculateIndicatorMetrics(string indicatorName, List<GAResults> results)
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

    private void LoadExistingResponses()
    {
        // Load existing trend responses from section 6
        var existingResponses = Protocol?.SectionResponses?
            .Where(r => r.SectionNumber == 6)
            .ToDictionary(r => r.SectionTitle, r => r.ResponseText ?? string.Empty) ?? new Dictionary<string, string>();

        // Initialize TrendResponses with existing data
        TrendResponses = new Dictionary<string, string>();
        foreach (var kvp in TrendQuestions)
        {
            TrendResponses[kvp.Key] = existingResponses.GetValueOrDefault(kvp.Key, string.Empty);
        }

        // Set last updated info from any section 6 response
        var lastResponse = Protocol?.SectionResponses?
            .Where(r => r.SectionNumber == 6)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        if (lastResponse != null)
        {
            LastUpdated = lastResponse.UpdatedAt.ToString("MMM dd, yyyy 'at' h:mm tt");
            LastUpdatedBy = lastResponse.UpdatedBy ?? "Unknown";
        }
    }

    private async Task SaveTrendsResponsesAsync()
    {
        if (Protocol == null) return;

        var now = DateTime.UtcNow;
        var user = User?.Identity?.Name ?? "Unknown";

        // Get existing section 6 responses
        var existingResponses = await _context.GAProtocolSectionResponses
            .Where(r => r.ProtocolId == Protocol.Id && r.SectionNumber == 6)
            .ToListAsync();

        foreach (var kvp in TrendQuestions)
        {
            var key = kvp.Key;
            var responseText = TrendResponses.GetValueOrDefault(key, string.Empty)?.Trim();

            var existingResponse = existingResponses.FirstOrDefault(r => r.SectionTitle == key);

            if (existingResponse != null)
            {
                // Update existing response
                existingResponse.ResponseText = responseText;
                existingResponse.UpdatedAt = now;
                existingResponse.UpdatedBy = user;
            }
            else if (!string.IsNullOrWhiteSpace(responseText))
            {
                // Create new response only if there's content
                _context.GAProtocolSectionResponses.Add(new GAProtocolSectionResponse
                {
                    ProtocolId = Protocol.Id,
                    SectionNumber = 6,
                    SectionTitle = key,
                    ResponseText = responseText,
                    UpdatedAt = now,
                    UpdatedBy = user
                });
            }
        }

        await _context.SaveChangesAsync();
    }
}