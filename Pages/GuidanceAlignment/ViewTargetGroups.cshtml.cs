using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Utilities;

namespace ORSV2.Pages.GuidanceAlignment
{
    public class ViewTargetGroupsModel : GABasePageModel
    {
        public ViewTargetGroupsModel(ApplicationDbContext context) : base(context) { }

        [BindProperty(SupportsGet = true)]
        public int SchoolId { get; set; }

        public School? School { get; set; }
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();

        public sealed class GroupRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? Note { get; set; }
            public DateTime CreatedAt { get; set; }
            public int StudentCount { get; set; }
        }

        public List<GroupRow> Groups { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!await AuthorizeAsync(SchoolId)) return Forbid();

            School = await _context.Schools
                .Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == SchoolId);
            if (School == null) return NotFound();

            Groups = await _context.TargetGroups
                .Where(g => g.SchoolId == SchoolId)
                .Select(g => new GroupRow
                {
                    Id = g.Id,
                    Name = g.Name,
                    Note = g.Note,
                    CreatedAt = g.CreatedAt,
                    StudentCount = g.TargetGroupStudents.Count
                })
                .OrderBy(g => g.Name)
                .ToListAsync();

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Guidance Alignment", Url = Url.Page("/GuidanceAlignment/Index") },
                new BreadcrumbItem { Title = School.District?.Name ?? "District", Url = Url.Page("/GuidanceAlignment/Schools", new { districtId = School.DistrictId }) },
                new BreadcrumbItem { Title = School.Name, Url = Url.Page("/GuidanceAlignment/Overview", new { schoolId = School.Id }) },
                new BreadcrumbItem { Title = "Target Groups" }
            };

            return Page();
        }

        public sealed class SaveDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string? Note { get; set; }
            public int SchoolId { get; set; }
        }

        public async Task<IActionResult> OnPostSaveAsync([FromBody] SaveDto dto)
        {
            if (!await AuthorizeAsync(dto.SchoolId)) return Forbid();
            if (dto.Id <= 0 || string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Invalid data.");

            var group = await _context.TargetGroups
                .FirstOrDefaultAsync(g => g.Id == dto.Id && g.SchoolId == dto.SchoolId);
            if (group is null) return NotFound();

            group.Name = dto.Name.Trim();
            group.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            await _context.SaveChangesAsync();

            return new JsonResult(new { ok = true });
        }
    }
}
