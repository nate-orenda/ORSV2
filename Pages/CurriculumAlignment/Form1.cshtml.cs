using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        #region Page Properties
        [BindProperty(SupportsGet = true)]
        public int? SelectedDistrictId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedUnit { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTestId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedSchoolId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTeacherId { get; set; }

        public SelectList Districts { get; set; } = new(new List<object>());
        public SelectList Units { get; set; } = new(new List<object>());
        public SelectList TestNames { get; set; } = new(new List<object>());
        public SelectList Schools { get; set; } = new(new List<object>());
        public SelectList Teachers { get; set; } = new(new List<object>());
        #endregion

        public async Task OnGetAsync()
        {
            var userDistrictIds = await GetUserDistrictIdsAsync();
            
            var districts = await _context.VwStudentResultsClasses
                .Where(v => userDistrictIds.Contains(v.DistrictId))
                .Select(v => new { v.DistrictId })
                .Distinct()
                .Join(_context.Districts, v => v.DistrictId, d => d.Id, (v, d) => new { d.Id, d.Name })
                .OrderBy(d => d.Name)
                .ToListAsync();
            
            Districts = new SelectList(districts, "Id", "Name", SelectedDistrictId);
        }
        
        #region AJAX Handlers
        // ... OnGetUnitsAsync, OnGetTestNamesAsync, etc. are unchanged ...
        public async Task<JsonResult> OnGetUnitsAsync(int districtId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var units = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId)
                .Select(v => v.Unit).Distinct().OrderBy(u => u)
                .Select(u => new { value = u, text = "Unit " + u })
                .ToListAsync();

            return new JsonResult(units);
        }

        public async Task<JsonResult> OnGetTestNamesAsync(int districtId, int unit)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var tests = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId && v.Unit == unit)
                .Select(v => new { value = v.TestId, text = v.TestName }).Distinct()
                .OrderBy(t => t.text)
                .ToListAsync();

            return new JsonResult(tests);
        }

        public async Task<JsonResult> OnGetSchoolsAsync(int districtId, string testId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };

            var accessibleSchoolIds = await GetUserSchoolIdsAsync();

            var schools = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId && v.TestId == testId && accessibleSchoolIds.Contains(v.SchoolId))
                .Join(_context.Schools, v => v.SchoolId, s => s.Id, (v, s) => new { s.Id, s.Name })
                .Distinct()
                .OrderBy(s => s.Name)
                .Select(s => new { value = s.Id, text = s.Name })
                .ToListAsync();
            
            return new JsonResult(schools);
        }

        public async Task<JsonResult> OnGetTeachersAsync(int districtId, string testId, int schoolId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new List<object>()) { StatusCode = 403 };
            if (!await HasSchoolAccessAsync(schoolId)) return new JsonResult(new List<object>()) { StatusCode = 403 };
            
            var teachers = await _context.VwStudentResultsClasses
                .Where(v => v.DistrictId == districtId && v.TestId == testId && v.SchoolId == schoolId && !string.IsNullOrEmpty(v.TeacherId))
                .Select(v => new { Id = v.TeacherId, Name = v.TeacherLastName + ", " + v.TeacherFirstName })
                .Distinct()
                .OrderBy(t => t.Name)
                .Select(t => new { value = t.Id, text = t.Name })
                .ToListAsync();
            
            return new JsonResult(teachers);
        }
        
        public async Task<JsonResult> OnGetStudentResultsAsync(int districtId, string testId, int? schoolId, string? teacherId)
        {
            if (!await HasDistrictAccessAsync(districtId)) return new JsonResult(new { }) { StatusCode = 403 };
            if (schoolId.HasValue && !await HasSchoolAccessAsync(schoolId.Value))
            {
                 return new JsonResult(new { }) { StatusCode = 403 };
            }

            var query = _context.VwStudentResultsClasses.AsNoTracking()
                .Where(v => v.DistrictId == districtId && v.TestId == testId);

            if (schoolId.HasValue) query = query.Where(v => v.SchoolId == schoolId.Value);
            if (!string.IsNullOrEmpty(teacherId)) query = query.Where(v => v.TeacherId == teacherId);

            var studentData = await query.ToListAsync();
            if (!studentData.Any()) return new JsonResult(new StudentResultTableViewModel());

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var uniqueStandardIds = studentData
                .Where(r => !string.IsNullOrWhiteSpace(r.Results))
                .SelectMany(r => JsonSerializer.Deserialize<List<StandardResult>>(r.Results, options) ?? new List<StandardResult>())
                .Select(sr => sr.StandardId)
                .Distinct()
                .ToList();

            var standardsDict = await _context.Standards
                .Where(s => uniqueStandardIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s);

            var tableViewModel = new StudentResultTableViewModel();
            tableViewModel.Headers = standardsDict.Values
                .OrderBy(s => s.HumanCodingScheme)
                .Select(s => new StandardHeader
                {
                    HumanCodingScheme = s.HumanCodingScheme,
                    FullStatement = s.FullStatement
                }).ToList();
            
            var standardIdToCodeMap = standardsDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.HumanCodingScheme);

            var studentRows = studentData
                .GroupBy(s => s.StudentId)
                .Select(g => g.First())
                .Select(s => {
                    var results = JsonSerializer.Deserialize<List<StandardResult>>(s.Results, options) ?? new List<StandardResult>();
                    var scores = new Dictionary<string, int>();
                    foreach(var result in results)
                    {
                        if (standardIdToCodeMap.TryGetValue(result.StandardId, out var code))
                        {
                            scores[code] = result.Score;
                        }
                    }
                    return new StudentResultRow
                    {
                        // TeacherName removed from here
                        StudentName = s.StudentFullName,
                        LocalStudentId = s.LocalStudentId,
                        StandardScores = scores,
                        TotalStandardsPassed = results.Count(r => r.Proficient),
                        _TeacherNameInternal = s.TeacherFullName // Temp property for grouping
                    };
                })
                .ToList();

            // Group by teacher
            tableViewModel.TeacherGroups = studentRows
                .GroupBy(row => row._TeacherNameInternal)
                .Select(g => new TeacherGroup
                {
                    TeacherName = g.Key,
                    StudentRows = g.OrderBy(s => s.StudentName).ToList()
                })
                .OrderBy(tg => tg.TeacherName)
                .ToList();


            return new JsonResult(tableViewModel);
        }
        #endregion

        #region Authorization Helpers
        // ... Unchanged ...
        private async Task<List<int>> GetUserDistrictIdsAsync()
        {
            if (User.IsInRole("OrendaAdmin"))
            {
                return await _context.Districts.Select(d => d.Id).ToListAsync();
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return new List<int>();
            
            var userDistrictId = await _context.Users
                .Where(u => u.Id == userId && u.DistrictId.HasValue)
                .Select(u => u.DistrictId.Value).FirstOrDefaultAsync();

            return userDistrictId > 0 ? new List<int> { userDistrictId } : new List<int>();
        }

        private async Task<bool> HasDistrictAccessAsync(int districtId)
        {
            var userDistrictIds = await GetUserDistrictIdsAsync();
            return userDistrictIds.Contains(districtId);
        }

        private async Task<List<int>> GetUserSchoolIdsAsync()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return new List<int>();
            
            if (User.IsInRole("OrendaAdmin") || User.IsInRole("DistrictAdmin"))
            {
                var userDistrictIds = await GetUserDistrictIdsAsync();
                return await _context.Schools
                    .Where(s => userDistrictIds.Contains(s.DistrictId))
                    .Select(s => s.Id).ToListAsync();
            }
            
            return await _context.UserSchools
                .Where(us => us.UserId == userId)
                .Select(us => us.SchoolId).ToListAsync();
        }

        private async Task<bool> HasSchoolAccessAsync(int schoolId)
        {
            var accessibleSchoolIds = await GetUserSchoolIdsAsync();
            return accessibleSchoolIds.Contains(schoolId);
        }
        #endregion
    }

    #region View Models
    public class StudentResultTableViewModel
    {
        public List<StandardHeader> Headers { get; set; } = new();
        public List<TeacherGroup> TeacherGroups { get; set; } = new(); // <-- CHANGED
    }

    // NEW Class for Grouping
    public class TeacherGroup
    {
        public string TeacherName { get; set; } = string.Empty;
        public List<StudentResultRow> StudentRows { get; set; } = new();
    }

    public class StandardHeader
    {
        public string HumanCodingScheme { get; set; } = string.Empty;
        public string FullStatement { get; set; } = string.Empty;
    }

    public class StudentResultRow
    {
        public string StudentName { get; set; } = string.Empty;
        public string LocalStudentId { get; set; } = string.Empty;
        public int TotalStandardsPassed { get; set; }
        public Dictionary<string, int> StandardScores { get; set; } = new();
        
        [JsonIgnore] // Internal property, not sent to client
        public string _TeacherNameInternal { get; set; } = string.Empty;
    }
    
    public class StandardResult
    {
        [JsonPropertyName("standard_id")]
        public string StandardId { get; set; } = string.Empty;
        [JsonPropertyName("score")]
        public int Score { get; set; }
        [JsonPropertyName("proficient")]
        public bool Proficient { get; set; }
    }
    #endregion
}