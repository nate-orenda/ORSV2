using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Text.Json;

namespace ORSV2.Pages.CurriculumAlignment
{
    [Authorize]
    public class Form1Model : PageModel
    {
        private readonly ApplicationDbContext _context;

        public Form1Model(ApplicationDbContext context)
        {
            _context = context;
        }

        // Filter Properties
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

        // Dropdown Data
        public List<SelectListItem> Districts { get; set; } = new();
        public List<SelectListItem> Cycles { get; set; } = new();
        public List<SelectListItem> TestNames { get; set; } = new();
        public List<SelectListItem> Schools { get; set; } = new();
        public List<SelectListItem> Teachers { get; set; } = new();

        // Results Data
        public List<StudentResult> StudentResults { get; set; } = new();
        public int TotalCount { get; set; }

        // Summary Data for UI
        public Dictionary<string, int> SubjectCounts { get; set; } = new();
        public Dictionary<string, int> QuadrantCounts { get; set; } = new();
        public Dictionary<string, int> ProficiencyCounts { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Authorization check
            if (!await HasAccessToDistrictAsync(SelectedDistrictId))
            {
                SelectedDistrictId = 0; // Reset if no access
            }
            
            await LoadDistrictsAsync();
            
            // Load initial data if we have selections
            if (SelectedDistrictId > 0)
            {
                await LoadCyclesAsync();
                await LoadTestNamesAsync();
                await LoadSchoolsAsync();
                
                if (SelectedSchoolId.HasValue)
                {
                    await LoadTeachersAsync();
                }
                
                if (!string.IsNullOrEmpty(SelectedCycle) && !string.IsNullOrEmpty(SelectedTestName))
                {
                    await LoadStudentResultsAsync();
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Authorization check
            if (!await HasAccessToDistrictAsync(SelectedDistrictId))
            {
                return Forbid();
            }
            
            await LoadDistrictsAsync();
            
            if (SelectedDistrictId > 0)
            {
                await LoadCyclesAsync();
                await LoadTestNamesAsync();
                await LoadSchoolsAsync();
                
                if (SelectedSchoolId.HasValue)
                {
                    await LoadTeachersAsync();
                }
                
                if (!string.IsNullOrEmpty(SelectedCycle) && !string.IsNullOrEmpty(SelectedTestName))
                {
                    await LoadStudentResultsAsync();
                }
            }
            
            return Page();
        }

        private async Task LoadDistrictsAsync()
        {
            IQueryable<District> query = _context.Districts.Where(d => !d.Inactive);
            
            // Apply authorization filters
            if (User.IsInRole("OrendaAdmin"))
            {
                // Optimized approach: First check if view has any data at all with timeout protection
                try
                {
                    var hasViewData = await _context.VwStudentResultsClasses
                        .Take(1) // Only check if ANY record exists
                        .AnyAsync();
                    
                    if (hasViewData)
                    {
                        // Get sample of student IDs from view (limit to prevent timeout)
                        var sampleStudentIds = await _context.VwStudentResultsClasses
                            .Select(v => v.StudentId)
                            .Distinct()
                            .Take(1000) // Limit to first 1000 for performance
                            .ToListAsync();
                        
                        if (sampleStudentIds.Any())
                        {
                            // Find districts that have these students
                            var districtIdsWithData = await _context.STU
                                .Where(s => sampleStudentIds.Contains(s.StuId))
                                .Select(s => s.DistrictID)
                                .Distinct()
                                .ToListAsync();
                            
                            if (districtIdsWithData.Any())
                            {
                                query = query.Where(d => districtIdsWithData.Contains(d.Id));
                            }
                            else
                            {
                                // Fallback: Show all districts for OrendaAdmin
                                // The specific filtering will happen in subsequent dropdowns
                            }
                        }
                    }
                    else
                    {
                        // No assessment data, show no districts
                        query = query.Where(d => false);
                    }
                }
                catch (Exception ex)
                {
                    // If query times out or fails, fallback to showing all districts for OrendaAdmin
                    // Log the error but don't crash the page
                    Console.WriteLine($"District loading fallback for OrendaAdmin: {ex.Message}");
                }
            }
            else if (User.IsInRole("DistrictAdmin") || User.IsInRole("SchoolAdmin"))
            {
                // Get user's district access
                var userDistrictId = User.FindFirst("DistrictId")?.Value;
                if (int.TryParse(userDistrictId, out int districtId))
                {
                    query = query.Where(d => d.Id == districtId);
                    
                    // For non-admin users, skip the expensive data check
                    // They can only see their district anyway, and we'll validate data in subsequent calls
                }
                else
                {
                    query = query.Where(d => false); // No access if no district claim
                }
            }
            else
            {
                query = query.Where(d => false); // No access for other roles
            }
            
            var districts = await query
                .OrderBy(d => d.Name)
                .Select(d => new SelectListItem 
                { 
                    Value = d.Id.ToString(), 
                    Text = d.Name,
                    Selected = d.Id == SelectedDistrictId
                })
                .ToListAsync();
            
            Districts = new List<SelectListItem> 
            { 
                new SelectListItem { Value = "", Text = "Select District..." } 
            };
            Districts.AddRange(districts);
        }

        private async Task LoadCyclesAsync()
        {
            if (SelectedDistrictId <= 0) return;
            
            // Get student IDs for this district
            var studentIdsInDistrict = await _context.STU
                .Where(s => s.DistrictID == SelectedDistrictId)
                .Select(s => s.StuId)
                .ToListAsync();
            
            if (!studentIdsInDistrict.Any()) return;
            
            // Get cycles for these students
            var cycles = await _context.VwStudentResultsClasses
                .Where(v => studentIdsInDistrict.Contains(v.StudentId))
                .Select(v => v.Unit)
                .Distinct()
                .Where(unit => !string.IsNullOrEmpty(unit))
                .OrderBy(c => c)
                .ToListAsync();
            
            Cycles = new List<SelectListItem> 
            { 
                new SelectListItem { Value = "", Text = "Select Cycle/Unit..." } 
            };
            
            foreach (var cycle in cycles)
            {
                Cycles.Add(new SelectListItem 
                { 
                    Value = cycle, 
                    Text = cycle,
                    Selected = cycle == SelectedCycle
                });
            }
        }

        private async Task LoadTestNamesAsync()
        {
            if (SelectedDistrictId <= 0) return;
            
            var query = from v in _context.VwStudentResultsClasses
                       join s in _context.STU on v.StudentId equals s.StuId
                       where s.DistrictID == SelectedDistrictId
                       select v;
            
            if (!string.IsNullOrEmpty(SelectedCycle))
                query = query.Where(v => v.Unit == SelectedCycle);
            
            var testNames = await query
                .Select(v => v.TestName)
                .Distinct()
                .Where(testName => !string.IsNullOrEmpty(testName))
                .OrderBy(t => t)
                .ToListAsync();
            
            TestNames = new List<SelectListItem> 
            { 
                new SelectListItem { Value = "", Text = "Select Test Name..." } 
            };
            
            foreach (var testName in testNames)
            {
                TestNames.Add(new SelectListItem 
                { 
                    Value = testName, 
                    Text = testName,
                    Selected = testName == SelectedTestName
                });
            }
        }

        private async Task LoadSchoolsAsync()
        {
            var schools = await _context.Schools
                .Where(s => s.DistrictId == SelectedDistrictId && !s.Inactive)
                .OrderBy(s => s.Name)
                .Select(s => new SelectListItem 
                { 
                    Value = s.Id.ToString(), 
                    Text = s.Name,
                    Selected = s.Id == SelectedSchoolId
                })
                .ToListAsync();
            
            Schools = new List<SelectListItem> 
            { 
                new SelectListItem { Value = "", Text = "All Schools" } 
            };
            Schools.AddRange(schools);
        }

        private async Task LoadTeachersAsync()
        {
            // Join with STU to get district/school information
            var query = from v in _context.VwStudentResultsClasses
                       join s in _context.STU on v.StudentId equals s.StuId
                       where s.DistrictID == SelectedDistrictId
                       select new { View = v, Student = s };
            
            if (!string.IsNullOrEmpty(SelectedCycle))
                query = query.Where(x => x.View.Unit == SelectedCycle);
            
            if (!string.IsNullOrEmpty(SelectedTestName))
                query = query.Where(x => x.View.TestName == SelectedTestName);
            
            if (SelectedSchoolId.HasValue)
                query = query.Where(x => x.Student.SchoolID == SelectedSchoolId.Value);
            
            var teachers = await query
                .Select(x => new { x.View.TeacherId, x.View.TeacherFirstName, x.View.TeacherLastName })
                .Distinct()
                .Where(t => !string.IsNullOrEmpty(t.TeacherLastName) && !string.IsNullOrEmpty(t.TeacherFirstName))
                .OrderBy(t => t.TeacherLastName)
                .ThenBy(t => t.TeacherFirstName)
                .ToListAsync();
            
            Teachers = new List<SelectListItem> 
            { 
                new SelectListItem { Value = "", Text = "All Teachers" } 
            };
            
            foreach (var teacher in teachers)
            {
                Teachers.Add(new SelectListItem 
                { 
                    Value = teacher.TeacherId.ToString(), 
                    Text = $"{teacher.TeacherLastName}, {teacher.TeacherFirstName}",
                    Selected = teacher.TeacherId == SelectedTeacherId
                });
            }
        }

        private async Task LoadStudentResultsAsync()
        {
            var query = from v in _context.VwStudentResultsClasses
                       join s in _context.STU on v.StudentId equals s.StuId
                       where s.DistrictID == SelectedDistrictId
                       select new { View = v, Student = s };
            
            if (!string.IsNullOrEmpty(SelectedCycle))
                query = query.Where(x => x.View.Unit == SelectedCycle);
            
            if (!string.IsNullOrEmpty(SelectedTestName))
                query = query.Where(x => x.View.TestName == SelectedTestName);
            
            if (SelectedSchoolId.HasValue)
                query = query.Where(x => x.Student.SchoolID == SelectedSchoolId.Value);
            
            if (SelectedTeacherId.HasValue)
                query = query.Where(x => x.View.TeacherId == SelectedTeacherId.Value);
            
            var results = await query
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
                    IsPrimaryTeacher = !string.IsNullOrEmpty(x.View.IsPrimaryTeacherFlag) && 
                                     x.View.IsPrimaryTeacherFlag.ToLower() == "true"
                })
                .OrderBy(r => r.LastName)
                .ThenBy(r => r.FirstName)
                .ToListAsync();
            
            StudentResults = results;
            TotalCount = results.Count;
            
            // Calculate summary statistics
            SubjectCounts = results
                .Where(r => !string.IsNullOrEmpty(r.Subject))
                .GroupBy(r => r.Subject)
                .ToDictionary(g => g.Key, g => g.Count());
            
            QuadrantCounts = results
                .Where(r => !string.IsNullOrEmpty(r.Quadrant))
                .GroupBy(r => r.Quadrant)
                .ToDictionary(g => g.Key, g => g.Count());
            
            ProficiencyCounts = results
                .Where(r => !string.IsNullOrEmpty(r.Proficiency))
                .GroupBy(r => r.Proficiency)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        // Authorization helper method
        private async Task<bool> HasAccessToDistrictAsync(int districtId)
        {
            if (districtId <= 0) return true; // Allow if no district selected
            
            // OrendaAdmins have access to all districts
            if (User.IsInRole("OrendaAdmin")) return true;
            
            // Get user's district from claims
            var userDistrictId = User.FindFirst("DistrictId")?.Value;
            if (int.TryParse(userDistrictId, out int userDistrict))
            {
                return userDistrict == districtId;
            }
            
            return false;
        }

        // API endpoints for cascading dropdowns
        public async Task<IActionResult> OnGetCyclesAsync(int districtId)
        {
            // Authorization check
            if (!await HasAccessToDistrictAsync(districtId))
            {
                return Forbid();
            }
            
            try
            {
                // Optimized query with limits
                var studentIdsInDistrict = await _context.STU
                    .Where(s => s.DistrictID == districtId)
                    .Select(s => s.StuId)
                    .Take(5000) // Limit for performance
                    .ToListAsync();
                
                if (!studentIdsInDistrict.Any())
                {
                    return new JsonResult(new List<string>());
                }
                
                var cycles = await _context.VwStudentResultsClasses
                    .Where(v => studentIdsInDistrict.Contains(v.StudentId))
                    .Select(v => v.Unit)
                    .Distinct()
                    .Where(unit => !string.IsNullOrEmpty(unit))
                    .OrderBy(c => c)
                    .ToListAsync();
                
                return new JsonResult(cycles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cycles API error: {ex.Message}");
                return new JsonResult(new List<string>());
            }
        }

        public async Task<IActionResult> OnGetTestNamesAsync(int districtId, string cycle)
        {
            // Authorization check
            if (!await HasAccessToDistrictAsync(districtId))
            {
                return Forbid();
            }
            
            try
            {
                // Optimized query with limits
                var studentIdsInDistrict = await _context.STU
                    .Where(s => s.DistrictID == districtId)
                    .Select(s => s.StuId)
                    .Take(5000) // Limit for performance
                    .ToListAsync();
                
                if (!studentIdsInDistrict.Any())
                {
                    return new JsonResult(new List<string>());
                }
                
                var query = _context.VwStudentResultsClasses
                    .Where(v => studentIdsInDistrict.Contains(v.StudentId));
                
                if (!string.IsNullOrEmpty(cycle))
                    query = query.Where(v => v.Unit == cycle);
                
                var testNames = await query
                    .Select(v => v.TestName)
                    .Distinct()
                    .Where(testName => !string.IsNullOrEmpty(testName))
                    .OrderBy(t => t)
                    .ToListAsync();
                
                return new JsonResult(testNames);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test names API error: {ex.Message}");
                return new JsonResult(new List<string>());
            }
        }

        public async Task<IActionResult> OnGetTeachersAsync(int districtId, string cycle, string testName, int? schoolId)
        {
            // Authorization check
            if (!await HasAccessToDistrictAsync(districtId))
            {
                return Forbid();
            }
            
            try
            {
                // Optimized query with limits
                var studentIdsInDistrict = await _context.STU
                    .Where(s => s.DistrictID == districtId)
                    .Select(s => s.StuId)
                    .Take(5000) // Limit for performance
                    .ToListAsync();
                
                if (!studentIdsInDistrict.Any())
                {
                    return new JsonResult(new List<object>());
                }
                
                var query = _context.VwStudentResultsClasses
                    .Where(v => studentIdsInDistrict.Contains(v.StudentId));
                
                if (!string.IsNullOrEmpty(cycle))
                    query = query.Where(v => v.Unit == cycle);
                
                if (!string.IsNullOrEmpty(testName))
                    query = query.Where(v => v.TestName == testName);
                
                if (schoolId.HasValue)
                {
                    // Filter by school through student records
                    var schoolStudentIds = await _context.STU
                        .Where(s => s.SchoolID == schoolId.Value && studentIdsInDistrict.Contains(s.StuId))
                        .Select(s => s.StuId)
                        .ToListAsync();
                    
                    query = query.Where(v => schoolStudentIds.Contains(v.StudentId));
                }
                
                var teachers = await query
                    .Select(v => new { 
                        id = v.TeacherId, 
                        name = $"{v.TeacherLastName}, {v.TeacherFirstName}" 
                    })
                    .Distinct()
                    .Where(t => !string.IsNullOrEmpty(t.name) && t.name != ", ")
                    .OrderBy(t => t.name)
                    .Take(100) // Limit teacher results
                    .ToListAsync();
                
                return new JsonResult(teachers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Teachers API error: {ex.Message}");
                return new JsonResult(new List<object>());
            }
        }
    }

    // Model for student results - keep this exactly as you had it
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