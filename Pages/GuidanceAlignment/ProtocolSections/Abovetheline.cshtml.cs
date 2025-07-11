using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class AbovethelineModel : ProtocolSectionBaseModel
{
    public AbovethelineModel(ApplicationDbContext context) : base(context) { }

    public override int CurrentSection => 3; // Above the Line section

    [BindProperty(SupportsGet = true)]
    public int? CompareCP { get; set; }

    [BindProperty]
    public string? AboveLineComment { get; set; }

    public List<TableGroup> TableGroups { get; set; } = new();

    public class AboveLineSummaryRow
    {
        public string GradeLabel { get; set; } = string.Empty;
        public decimal TargetPct { get; set; }
        public decimal CheckpointPct { get; set; }
        public decimal? PreviousPct { get; set; }
        public decimal Difference => CheckpointPct - TargetPct;
        public decimal? Change => PreviousPct.HasValue ? CheckpointPct - PreviousPct.Value : null;
        public int Enrollment { get; set; }
    }

    public class TableGroup
    {
        public string Title { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public List<AboveLineSummaryRow> Rows { get; set; } = new();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await LoadAboveLineDataAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveCommentAsync()
    {
        var result = await LoadProtocolDataAsync();
        if (result.GetType() != typeof(PageResult)) return result;

        await SaveSectionResponseAsync(3, AboveLineComment ?? string.Empty);
        return RedirectToPage(new { protocolId = ProtocolId, compareCP = CompareCP });
    }

    private async Task LoadAboveLineDataAsync()
    {
        if (Protocol == null || School == null) return;
        
        var cp = Protocol.CP;
        var schoolYear = Protocol.SchoolYear;

        // Load comment
        AboveLineComment = Responses.GetValueOrDefault(3, string.Empty);

        // Load current checkpoint results
        var currentResults = await _context.GAResults
            .Where(r => r.SchoolId == School.Id && 
                       r.DistrictId == School.DistrictId && 
                       r.SchoolYear == schoolYear && 
                       r.CP == cp)
            .ToListAsync();

        // Load comparison results if requested
        var compareResults = new List<GAResults>();
        if (CompareCP.HasValue && CompareCP.Value < cp)
        {
            compareResults = await _context.GAResults
                .Where(r => r.SchoolId == School.Id && 
                           r.DistrictId == School.DistrictId && 
                           r.SchoolYear == schoolYear && 
                           r.CP == CompareCP.Value)
                .ToListAsync();
        }

        // Load targets
        var targets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == School.Id && 
                       t.DistrictId == School.DistrictId && 
                       t.SchoolYear == schoolYear && 
                       t.TargetType == "AboveLine")
            .ToDictionaryAsync(t => t.GradeLevel, t => t.TargetValue);

        // Build table data
        TableGroups = BuildTableGroups(currentResults, compareResults, targets);
    }

    private List<TableGroup> BuildTableGroups(List<GAResults> currentResults, List<GAResults> compareResults, Dictionary<int, decimal> targets)
    {
        var summaryRows = BuildSummaryRows(currentResults, compareResults, targets);

        return new List<TableGroup>
        {
            new TableGroup
            {
                Title = "Schoolwide (All Grades)",
                Color = "#FF6447",
                Rows = new List<AboveLineSummaryRow>
                {
                    new AboveLineSummaryRow
                    {
                        GradeLabel = "All",
                        TargetPct = summaryRows.Any() ? summaryRows.Average(r => r.TargetPct) : 0,
                        CheckpointPct = CalculateAboveLinePct(currentResults),
                        PreviousPct = compareResults.Any() ? CalculateAboveLinePct(compareResults) : null,
                        Enrollment = currentResults.Count
                    }
                }
            },
            new TableGroup
            {
                Title = "Predictive Indicators Stage (Grades 6–8)",
                Color = "#38B54B",
                Rows = summaryRows.Where(r => IsInGradeRange(r.GradeLabel, 6, 8)).ToList()
            },
            new TableGroup
            {
                Title = "Predictive Indicators Stage (Grades 9–10)",
                Color = "#38B54B",
                Rows = summaryRows.Where(r => IsInGradeRange(r.GradeLabel, 9, 10)).ToList()
            },
            new TableGroup
            {
                Title = "End of Journey Indicators of Success (Grades 11–12)",
                Color = "#29E2F0",
                Rows = summaryRows.Where(r => IsInGradeRange(r.GradeLabel, 11, 12)).ToList()
            }
        };
    }

    private List<AboveLineSummaryRow> BuildSummaryRows(List<GAResults> currentResults, List<GAResults> compareResults, Dictionary<int, decimal> targets)
    {
        return currentResults
            .GroupBy(r => r.Grade)
            .Select(g =>
            {
                var grade = g.Key;
                var gradeLabel = grade == 0 ? "K" : grade.ToString();
                var currentGradeResults = g.ToList();
                var compareGradeResults = compareResults.Where(r => r.Grade == grade).ToList();

                return new AboveLineSummaryRow
                {
                    GradeLabel = gradeLabel,
                    TargetPct = targets.GetValueOrDefault(grade, 0),
                    CheckpointPct = CalculateAboveLinePct(currentGradeResults),
                    PreviousPct = compareGradeResults.Any() ? CalculateAboveLinePct(compareGradeResults) : null,
                    Enrollment = currentGradeResults.Count
                };
            })
            .OrderBy(r => r.GradeLabel == "K" ? 0 : int.Parse(r.GradeLabel))
            .ToList();
    }

    private static decimal CalculateAboveLinePct(IEnumerable<GAResults> results)
    {
        var resultList = results.ToList();
        if (!resultList.Any()) return 0;

        var aboveLineCount = resultList.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge");
        return Math.Round((decimal)aboveLineCount / resultList.Count * 100, 1);
    }

    private static bool IsInGradeRange(string gradeLabel, int minGrade, int maxGrade)
    {
        if (gradeLabel == "K") return minGrade == 0;
        if (int.TryParse(gradeLabel, out int grade))
        {
            return grade >= minGrade && grade <= maxGrade;
        }
        return false;
    }
}