using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class Form1Model : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<Form1Model> _logger;

        public Form1Model(ApplicationDbContext context, ILogger<Form1Model> logger)
        {
            _context = context;
            _logger = logger;
        }

        [BindProperty]
        public int SelectedDistrictId { get; set; }
        
        [BindProperty]
        public string SelectedCycle { get; set; } = string.Empty;
        
        [BindProperty]
        public string SelectedTestName { get; set; } = string.Empty;
        
        [BindProperty]
        public int? SelectedSchoolId { get; set; }
        
        [BindProperty]
        public int? SelectedTeacherId { get; set; }

        public SelectList Districts { get; set; } = new SelectList(new List<object>());
        public SelectList Cycles { get; set; } = new SelectList(new List<object>());
        public SelectList TestNames { get; set; } = new SelectList(new List<object>());
        public SelectList Schools { get; set; } = new SelectList(new List<object>());
        public SelectList Teachers { get; set; } = new SelectList(new List<object>());

        public List<StudentResult> StudentResults { get; set; } = new();
        public int TotalResults { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                await LoadDistrictsAsync();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Form1 page");
                ModelState.AddModelError("", "Error loading page data");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                await LoadDistrictsAsync();
                await LoadDataBasedOnSelections();
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Form1 request");
                ModelState.AddModelError("", "Error processing request");
                return Page();
            }
        }

        // AJAX endpoints for cascading dropdowns
        public async Task<IActionResult> OnGetCyclesAsync(int districtId)
        {
            if (!await HasDistrictAccessAsync(districtId))
                return Forbid();

            var cycles = await _context.Assessments
                .Where(a => a.DistrictId == districtId)
                .Select(a => a.Unit)
                .Distinct()
                .OrderBy(u => u)
                .Select(u => new { value = u.ToString(), text = $"Unit {u}" })
                .ToListAsync();

            return new JsonResult(cycles);
        }

        public async Task<IActionResult> OnGetTestNamesAsync(int districtId, string cycle)
        {
            if (!await HasDistrictAccessAsync(districtId))
                return Forbid();

            if (!int.TryParse(cycle, out int unitNumber))
                return new JsonResult(new List<object>());

            var testNames = await _context.Assessments
                .Where(a => a.DistrictId == districtId && a.Unit == unitNumber)
                .Select(a => new { value = a.TestId, text = a.TestName ?? a.TestId })
                .Distinct()
                .OrderBy(t => t.text)
                .ToListAsync();

            return new JsonResult(testNames);
        }

        public async Task<IActionResult> OnGetSchoolsAsync(int districtId, string testId)
        {
            if (!await HasDistrictAccessAsync(districtId))
                return Forbid();

            try
            {
                // Parse testId to int since VwStudentResultsClasses.TestId is int
                if (!int.TryParse(testId, out int testIdInt))
                {
                    return new JsonResult(new List<object>());
                }

                // First filter the view by district and test, then get schools
                var schools = await (
                    from v in _context.VwStudentResultsClasses
                    join s in _context.STU on v.StudentId equals s.StuId
                    join sch in _context.Schools on s.SchoolID equals sch.Id
                    where s.DistrictID == districtId && v.TestId == testIdInt
                    group new { sch.Id, sch.Name } by new { sch.Id, sch.Name } into g
                    select new { 
                        value = g.Key.Id, 
                        text = $"{g.Key.Name} ({g.Count()} students)" 
                    }
                ).OrderBy(s => s.text)
                .ToListAsync();

                return new JsonResult(schools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading schools for district {DistrictId}, test {TestId}", districtId, testId);
                return new JsonResult(new List<object>());
            }
        }

        public async Task<IActionResult> OnGetTeachersAsync(int districtId, string testId, int? schoolId = null)
        {
            if (!await HasDistrictAccessAsync(districtId))
                return Forbid();

            try
            {
                // Parse testId to int since VwStudentResultsClasses.TestId is int
                if (!int.TryParse(testId, out int testIdInt))
                {
                    return new JsonResult(new List<object>());
                }

                // Filter the view by district, test, and optionally school
                var query = from v in _context.VwStudentResultsClasses
                           join s in _context.STU on v.StudentId equals s.StuId
                           where s.DistrictID == districtId && 
                                 v.TestId == testIdInt &&
                                 !string.IsNullOrEmpty(v.TeacherFirstName) &&
                                 !string.IsNullOrEmpty(v.TeacherLastName)
                           select new { v, s };

                // Apply school filter if provided
                if (schoolId.HasValue)
                {
                    query = query.Where(x => x.s.SchoolID == schoolId.Value);
                }

                var teachers = await query
                    .GroupBy(x => new { x.v.TeacherId, x.v.TeacherFirstName, x.v.TeacherLastName })
                    .Select(g => new { 
                        value = g.Key.TeacherId, 
                        text = $"{g.Key.TeacherLastName}, {g.Key.TeacherFirstName} ({g.Count()} students)" 
                    })
                    .OrderBy(t => t.text)
                    .Take(50) // Limit for performance
                    .ToListAsync();

                return new JsonResult(teachers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading teachers for district {DistrictId}, test {TestId}", districtId, testId);
                return new JsonResult(new List<object>());
            }
        }

        public async Task<IActionResult> OnGetStudentResultsAsync(int districtId, string testId, int? schoolId = null, int? teacherId = null)
        {
            if (!await HasDistrictAccessAsync(districtId))
                return Forbid();

            try
            {
                // Parse testId to int since VwStudentResultsClasses.TestId is int
                if (!int.TryParse(testId, out int testIdInt))
                {
                    return new JsonResult(new List<object>());
                }

                var query = from v in _context.VwStudentResultsClasses
                           join s in _context.STU on v.StudentId equals s.StuId
                           where s.DistrictID == districtId && v.TestId == testIdInt
                           select new { View = v, Student = s };

                if (schoolId.HasValue)
                    query = query.Where(x => x.Student.SchoolID == schoolId.Value);

                if (teacherId.HasValue)
                    query = query.Where(x => x.View.TeacherId == teacherId.Value);

                var results = await query
                    .Take(1000) // Limit for performance
                    .Select(x => new
                    {
                        StudentId = x.View.StudentId,
                        LocalStudentId = x.View.LocalStudentId,
                        FirstName = x.View.FirstName,
                        LastName = x.View.LastName,
                        TestId = x.View.TestId,
                        TestName = x.View.TestName,
                        Unit = x.View.Unit,
                        Subject = x.View.Subject,
                        Results = x.View.Results,
                        Proficiency = x.View.Proficiency,
                        Quadrant = x.View.Quadrant,
                        SectionNumber = x.View.SectionNumber,
                        CourseNumber = x.View.CourseNumber,
                        CourseTitle = x.View.CourseTitle,
                        DepartmentName = x.View.DepartmentName,
                        TeacherFirstName = x.View.TeacherFirstName,
                        TeacherLastName = x.View.TeacherLastName,
                        TeacherId = x.View.TeacherId,
                        IsPrimaryTeacher = x.View.IsPrimaryTeacherFlag.ToLower() == "true"
                    })
                    .OrderBy(r => r.LastName)
                    .ThenBy(r => r.FirstName)
                    .ToListAsync();

                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading student results for district {DistrictId}, test {TestId}", districtId, testId);
                return new JsonResult(new List<object>());
            }
        }

        private async Task LoadDistrictsAsync()
        {
            var districtsQuery = _context.Districts.Where(d => !d.Inactive);

            // Apply user access restrictions based on your existing pattern
            if (User.IsInRole("OrendaAdmin") || User.IsInRole("OrendaManager"))
            {
                // OrendaAdmin/Manager can see all districts that have assessment data
                var districtsWithData = await _context.Assessments
                    .Select(a => a.DistrictId)
                    .Distinct()
                    .ToListAsync();
                
                districtsQuery = districtsQuery.Where(d => districtsWithData.Contains(d.Id));
            }
            else if (User.IsInRole("DistrictAdmin") || User.IsInRole("SchoolAdmin"))
            {
                // Get user's assigned districts (direct assignment + school-based)
                var userDistrictIds = await GetUserDistrictIdsAsync();
                if (userDistrictIds.Any())
                {
                    districtsQuery = districtsQuery.Where(d => userDistrictIds.Contains(d.Id));
                }
                else
                {
                    // If no district access, return empty list
                    Districts = new SelectList(new List<District>(), "Id", "Name", SelectedDistrictId);
                    return;
                }
            }
            else
            {
                // Other roles - get their assigned districts
                var userDistrictIds = await GetUserDistrictIdsAsync();
                if (userDistrictIds.Any())
                {
                    districtsQuery = districtsQuery.Where(d => userDistrictIds.Contains(d.Id));
                }
                else
                {
                    // If no district access, return empty list
                    Districts = new SelectList(new List<District>(), "Id", "Name", SelectedDistrictId);
                    return;
                }
            }

            var districts = await districtsQuery
                .OrderBy(d => d.Name)
                .ToListAsync();

            Districts = new SelectList(districts, "Id", "Name", SelectedDistrictId);
        }

        private async Task LoadDataBasedOnSelections()
        {
            if (SelectedDistrictId > 0)
            {
                await LoadCyclesAsync();

                if (!string.IsNullOrEmpty(SelectedCycle))
                {
                    await LoadTestNamesAsync();

                    if (!string.IsNullOrEmpty(SelectedTestName))
                    {
                        await LoadSchoolsAsync();

                        if (SelectedSchoolId.HasValue)
                        {
                            await LoadTeachersAsync();
                        }

                        await LoadStudentResultsAsync();
                    }
                }
            }
        }

        private async Task LoadCyclesAsync()
        {
            var cycles = await _context.Assessments
                .Where(a => a.DistrictId == SelectedDistrictId)
                .Select(a => new { Value = a.Unit.ToString(), Text = $"Unit {a.Unit}" })
                .Distinct()
                .OrderBy(c => c.Value)
                .ToListAsync();

            Cycles = new SelectList(cycles, "Value", "Text", SelectedCycle);
        }

        private async Task LoadTestNamesAsync()
        {
            if (!int.TryParse(SelectedCycle, out int unitNumber)) return;

            var testNames = await _context.Assessments
                .Where(a => a.DistrictId == SelectedDistrictId && a.Unit == unitNumber)
                .Select(a => new { Value = a.TestId, Text = a.TestName ?? a.TestId })
                .Distinct()
                .OrderBy(t => t.Text)
                .ToListAsync();

            TestNames = new SelectList(testNames, "Value", "Text", SelectedTestName);
        }

        private async Task LoadSchoolsAsync()
        {
            var schoolsData = await (
                from v in _context.VwStudentResultsClasses
                join s in _context.STU on v.StudentId equals s.StuId
                join sch in _context.Schools on s.SchoolID equals sch.Id
                where s.DistrictID == SelectedDistrictId && v.TestId.ToString() == SelectedTestName
                group new { sch.Id, sch.Name } by new { sch.Id, sch.Name } into g
                select new { 
                    Id = g.Key.Id, 
                    Name = $"{g.Key.Name} ({g.Count()} students)" 
                }
            ).OrderBy(s => s.Name)
            .ToListAsync();

            var schoolsList = new List<object> { new { Id = (int?)null, Name = "All Schools" } };
            schoolsList.AddRange(schoolsData.Select(s => new { Id = (int?)s.Id, Name = s.Name }));

            Schools = new SelectList(schoolsList, "Id", "Name", SelectedSchoolId);
        }

        private async Task LoadTeachersAsync()
        {
            if (!SelectedSchoolId.HasValue) return;

            var teachersData = await (
                from v in _context.VwStudentResultsClasses
                join s in _context.STU on v.StudentId equals s.StuId
                where s.DistrictID == SelectedDistrictId && 
                      s.SchoolID == SelectedSchoolId.Value && 
                      v.TestId.ToString() == SelectedTestName &&
                      !string.IsNullOrEmpty(v.TeacherFirstName) &&
                      !string.IsNullOrEmpty(v.TeacherLastName)
                group new { v.TeacherId, v.TeacherFirstName, v.TeacherLastName } 
                by new { v.TeacherId, v.TeacherFirstName, v.TeacherLastName } into g
                select new { 
                    Id = g.Key.TeacherId, 
                    Name = $"{g.Key.TeacherLastName}, {g.Key.TeacherFirstName} ({g.Count()} students)" 
                }
            ).OrderBy(t => t.Name)
            .ToListAsync();

            var teachersList = new List<object> { new { Id = (int?)null, Name = "All Teachers" } };
            teachersList.AddRange(teachersData.Select(t => new { Id = (int?)t.Id, Name = t.Name }));

            Teachers = new SelectList(teachersList, "Id", "Name", SelectedTeacherId);
        }

        private async Task LoadStudentResultsAsync()
        {
            // Parse the test ID to int since VwStudentResultsClasses.TestId is int
            if (!int.TryParse(SelectedTestName, out int testIdInt))
                return;

            var query = from v in _context.VwStudentResultsClasses
                       join s in _context.STU on v.StudentId equals s.StuId
                       where s.DistrictID == SelectedDistrictId && v.TestId == testIdInt
                       select new { View = v, Student = s };

            if (SelectedSchoolId.HasValue)
                query = query.Where(x => x.Student.SchoolID == SelectedSchoolId.Value);

            if (SelectedTeacherId.HasValue)
                query = query.Where(x => x.View.TeacherId == SelectedTeacherId.Value);

            // Get total count
            TotalResults = await query.CountAsync();

            // Load results with pagination (limit to 1000 for performance)
            StudentResults = await query
                .Take(1000)
                .Select(x => new StudentResult
                {
                    StudentId = x.View.StudentId,
                    LocalStudentId = x.View.LocalStudentId,
                    FirstName = x.View.FirstName,
                    LastName = x.View.LastName,
                    TestId = x.View.TestId,
                    TestName = x.View.TestName,
                    Unit = x.View.Unit,
                    Subject = x.View.Subject,
                    Results = x.View.Results,
                    Proficiency = x.View.Proficiency,
                    Quadrant = x.View.Quadrant,
                    SectionNumber = x.View.SectionNumber,
                    CourseNumber = x.View.CourseNumber,
                    CourseTitle = x.View.CourseTitle,
                    DepartmentName = x.View.DepartmentName,
                    TeacherFirstName = x.View.TeacherFirstName,
                    TeacherLastName = x.View.TeacherLastName,
                    TeacherId = x.View.TeacherId,
                    IsPrimaryTeacher = x.View.IsPrimaryTeacherFlag.ToLower() == "true"
                })
                .OrderBy(r => r.LastName)
                .ThenBy(r => r.FirstName)
                .ToListAsync();
        }

        private async Task<bool> HasDistrictAccessAsync(int districtId)
        {
            if (User.IsInRole("OrendaAdmin"))
                return true;

            var userDistrictIds = await GetUserDistrictIdsAsync();
            return userDistrictIds.Contains(districtId);
        }

        private async Task<List<int>> GetUserDistrictIdsAsync()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return new List<int>();

            var userDistrictIds = new List<int>();

            // Get the user with their direct district assignment
            var user = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.DistrictId)
                .FirstOrDefaultAsync();

            // Add direct district assignment if exists
            if (user.HasValue)
            {
                userDistrictIds.Add(user.Value);
            }

            // Get districts through school assignments  
            var schoolDistricts = await _context.UserSchools
                .Where(us => us.UserId == userId)
                .Join(_context.Schools, us => us.SchoolId, s => s.Id, (us, s) => s.DistrictId)
                .ToListAsync();

            userDistrictIds.AddRange(schoolDistricts);

            return userDistrictIds.Distinct().ToList();
        }
    }

    // Simple result model
    public class StudentResult
    {
        public int StudentId { get; set; }
        public string LocalStudentId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int TestId { get; set; }
        public string TestName { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public decimal? Results { get; set; }
        public string Proficiency { get; set; } = string.Empty;
        public string Quadrant { get; set; } = string.Empty;
        public string SectionNumber { get; set; } = string.Empty;
        public string CourseNumber { get; set; } = string.Empty;
        public string CourseTitle { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string TeacherFirstName { get; set; } = string.Empty;
        public string TeacherLastName { get; set; } = string.Empty;
        public int TeacherId { get; set; }
        public bool IsPrimaryTeacher { get; set; }
        
        public string FullName => $"{LastName}, {FirstName}";
        public string TeacherFullName => $"{TeacherLastName}, {TeacherFirstName}";
    }
}