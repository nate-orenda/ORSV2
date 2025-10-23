using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Claims;

[Authorize]
public class CoursesModel : PageModel
{
    private readonly ApplicationDbContext _context;
    public CoursesModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public int DistrictId { get; set; }

    public List<CourseViewModel> Courses { get; set; } = new();
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

    public IActionResult OnGet(int districtId)
    {
        DistrictId = districtId;

        // ✅ Security check
        if (!UserHasAccessToDistrict(DistrictId))
        {
            return Forbid();
        }

        var district = _context.Districts
            .FirstOrDefault(d => d.Id == DistrictId && !d.Inactive);

        if (district == null)
            return NotFound();

        Breadcrumbs = new List<BreadcrumbItem>
        {
            new BreadcrumbItem { Title = "Districts", Url = Url.Page("/Districts/Index") },
            new BreadcrumbItem { Title = district.Name }
        };

        var query = _context.Courses
            .Where(c => c.DistrictId == DistrictId);

        if (!string.IsNullOrEmpty(Search))
        {
            query = query.Where(c =>
                c.CourseNumber.Contains(Search) ||
                c.Title.Contains(Search) ||
                (c.DepartmentCode != null && c.DepartmentCode.Contains(Search)));
        }

        Courses = query
            .OrderBy(c => c.CourseNumber)
            .Select(c => new CourseViewModel
            {
                CourseNumber = c.CourseNumber,
                Title = c.Title,
                DepartmentCode = c.DepartmentCode ?? string.Empty,
                Validation = c.CSU_Rule_ValidationLevelCode ?? string.Empty,
                AG = c.CSU_SubjectAreaCode ?? string.Empty,
                Elective = c.UC_Rule_CanBeAnElective ?? string.Empty,
                CreditDefault = c.CreditDefault,
                InactiveStatusCode = c.InactiveStatusCode ?? string.Empty,
                DateUpdated = c.DateUpdated
            })
            .ToList();

        return Page();
    }

    private bool UserHasAccessToDistrict(int districtId)
    {
        if (User.IsInRole("OrendaAdmin")) return true;

        var claim = User.FindFirst("DistrictId");
        if (claim != null && int.TryParse(claim.Value, out var userDistrictId))
        {
            return userDistrictId == districtId;
        }

        return false;
    }
}