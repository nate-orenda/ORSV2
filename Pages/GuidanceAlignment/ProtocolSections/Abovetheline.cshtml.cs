// Pages/GuidanceAlignment/Protocols/Abovetheline.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.GuidanceAlignment.ProtocolSections;

public class AbovethelineModel : GABasePageModel
{
    public AbovethelineModel(ApplicationDbContext context) : base(context) { }

    [BindProperty(SupportsGet = true)]
    public int ProtocolId { get; set; }

    public GAProtocol? Protocol { get; set; }
    public School? School { get; set; }
    public List<AboveLineChartPoint> ChartData { get; set; } = new();
    public List<AboveLineSummaryRow> Table_AllGrades { get; set; } = new();
    public List<AboveLineSummaryRow> Table_6_8 { get; set; } = new();
    public List<AboveLineSummaryRow> Table_9_10 { get; set; } = new();
    public List<AboveLineSummaryRow> Table_11_12 { get; set; } = new();
    [BindProperty(SupportsGet = true)]
    public int? CompareCP { get; set; }

    public List<AboveLineSummaryRow> PreviousCPRows { get; set; } = new();


    public class AboveLineChartPoint
    {
        public string CPLabel { get; set; } = string.Empty;
        public decimal AllGrades { get; set; }
        public decimal Gr6_8 { get; set; }
        public decimal Gr9_10 { get; set; }
        public decimal Gr11_12 { get; set; }
    }

    public class AboveLineSummaryRow
    {
        public string GradeLabel { get; set; } = string.Empty;
        public decimal TargetPct { get; set; }
        public decimal CheckpointPct { get; set; }
        public decimal Difference => CheckpointPct - TargetPct;
        public int Enrollment { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authorized = await AuthorizeAsync();
        if (!authorized) return Forbid();

        Protocol = await _context.GAProtocols.FindAsync(ProtocolId);
        if (Protocol == null) return NotFound();

        School = await _context.Schools.Include(s => s.District).FirstOrDefaultAsync(s => s.Id == Protocol.SchoolId);
        if (School == null) return NotFound();

        var cp = Protocol.CP;
        var schoolYear = Protocol.SchoolYear;

        var results = await _context.GAResults
            .Where(r => r.SchoolId == School.Id && r.DistrictId == School.DistrictId && r.SchoolYear == schoolYear && r.CP == cp)
            .ToListAsync();

        ChartData = new List<AboveLineChartPoint>
        {
            new AboveLineChartPoint
            {
                CPLabel = $"CP {cp}",
                AllGrades = SafePct(results.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge"), results.Count),
                Gr6_8 = SafePct(results.Count(r => r.Grade is >= 6 and <= 8 && (r.Quadrant == "Benchmark" || r.Quadrant == "Challenge")),
                                results.Count(r => r.Grade is >= 6 and <= 8)),
                Gr9_10 = SafePct(results.Count(r => r.Grade is 9 or 10 && (r.Quadrant == "Benchmark" || r.Quadrant == "Challenge")),
                                 results.Count(r => r.Grade is 9 or 10)),
                Gr11_12 = SafePct(results.Count(r => r.Grade is 11 or 12 && (r.Quadrant == "Benchmark" || r.Quadrant == "Challenge")),
                                  results.Count(r => r.Grade is 11 or 12))
            }
        };

        var targets = await _context.GAProtocolTargets
            .Where(t => t.SchoolId == School.Id && t.DistrictId == School.DistrictId && t.SchoolYear == schoolYear && t.TargetType == "AboveLine")
            .ToDictionaryAsync(t => t.GradeLevel, t => t.TargetValue);

        var summaryRows = results
            .GroupBy(r => r.Grade)
            .Select(g =>
            {
                var grade = g.Key;
                var total = g.Count();
                var above = g.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge");
                return new AboveLineSummaryRow
                {
                    GradeLabel = grade == 0 ? "K" : grade.ToString(),
                    TargetPct = targets.ContainsKey(grade) ? targets[grade] : 0,
                    CheckpointPct = SafePct(above, total),
                    Enrollment = total
                };
            }).ToList();

        Table_AllGrades = new List<AboveLineSummaryRow> {
            new AboveLineSummaryRow {
                GradeLabel = "All",
                TargetPct = summaryRows.Any() ? Math.Round(summaryRows.Average(r => r.TargetPct), 1) : 0,
                CheckpointPct = SafePct(results.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge"), results.Count),
                Enrollment = results.Count
            }
        };

        Table_6_8 = summaryRows.Where(r => new[] { "6", "7", "8" }.Contains(r.GradeLabel)).ToList();
        Table_9_10 = summaryRows.Where(r => new[] { "9", "10" }.Contains(r.GradeLabel)).ToList();
        Table_11_12 = summaryRows.Where(r => new[] { "11", "12" }.Contains(r.GradeLabel)).ToList();

        if (CompareCP.HasValue && CompareCP.Value >= 1)
        {
            var priorResults = await _context.GAResults
                .Where(r => r.SchoolId == School.Id
                    && r.DistrictId == School.DistrictId
                    && r.SchoolYear == schoolYear
                    && r.CP == CompareCP.Value)
                .ToListAsync();

            PreviousCPRows = priorResults
                .GroupBy(r => r.Grade)
                .Select(g =>
                {
                    var grade = g.Key;
                    var total = g.Count();
                    var above = g.Count(r => r.Quadrant == "Benchmark" || r.Quadrant == "Challenge");

                    return new AboveLineSummaryRow
                    {
                        GradeLabel = grade == 0 ? "K" : grade.ToString(),
                        TargetPct = 0, // optional or skip
                        CheckpointPct = SafePct(above, total),
                        Enrollment = total
                    };
                }).ToList();
        }


        return Page();
    }

    private static decimal SafePct(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round((decimal)numerator / denominator * 100, 1);
}
