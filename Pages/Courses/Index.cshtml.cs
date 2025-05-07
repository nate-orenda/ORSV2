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
    public int DistrictId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; }

    public List<CourseViewModel> Courses { get; set; } = new();
    public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

    public IActionResult OnGet()
    {
        // ✅ Security check
        if (!UserHasAccessToDistrict(DistrictId))
        {
            return Forbid(); // or RedirectToPage("/AccessDenied");
        }

        var district = _context.Districts
        .FirstOrDefault(d => d.Id == DistrictId && !d.Inactive);

        if (district == null)
            return NotFound();

        Breadcrumbs = new List<BreadcrumbItem>
    {
        new BreadcrumbItem { Title = "Districts", Url = Url.Page("/Districts/Index") },
        new BreadcrumbItem { Title = district.Name } // current page
    };

        var query = _context.Courses
            .Where(c => c.DistrictId == DistrictId);

        if (!string.IsNullOrEmpty(Search))
        {
            query = query.Where(c =>
                c.CourseNumber.Contains(Search) ||
                c.Title.Contains(Search) ||
                c.DepartmentCode.Contains(Search));
        }

        Courses = query
            .OrderBy(c => c.CourseNumber)
            .Select(c => new CourseViewModel
            {
                CourseNumber = c.CourseNumber,
                Title = c.Title,
                DepartmentCode = c.DepartmentCode,
                Validation = c.CSU_Rule_ValidationLevelCode,
                AG = c.CSU_SubjectAreaCode,
                Elective = c.UC_Rule_CanBeAnElective,
                CreditDefault = c.CreditDefault,
                InactiveStatusCode = c.InactiveStatusCode,
                DateUpdated = c.DateUpdated
            })
            .ToList();

        return Page();
    }

    private bool UserHasAccessToDistrict(int districtId)
    {
        // OrendaAdmins get access to all districts
        if (User.IsInRole("OrendaAdmin")) return true;

        // Get the user's district ID(s) from claims or database
        var claim = User.FindFirst("DistrictId");
        if (claim != null && int.TryParse(claim.Value, out var userDistrictId))
        {
            return userDistrictId == districtId;
        }

        return false;
    }
}
