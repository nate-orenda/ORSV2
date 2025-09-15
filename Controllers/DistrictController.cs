using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using ORSV2.Services;

namespace ORSV2.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DistrictController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IDistrictFocusService _districtFocusService;

        public DistrictController(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager,
            IDistrictFocusService districtFocusService)
        {
            _context = context;
            _userManager = userManager;
            _districtFocusService = districtFocusService;
        }

        [HttpGet("context")]
        public async Task<IActionResult> GetDistrictContext()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                var roles = await _userManager.GetRolesAsync(user);
                var isOrendaUser = roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser");

                // Get available districts based on user permissions
                var availableDistricts = await _districtFocusService.GetAvailableDistrictsAsync(user.Id, isOrendaUser);
                
                // Get current focus district
                var focusDistrictId = await _districtFocusService.GetFocusDistrictIdAsync(user.Id, isOrendaUser);
                
                string focusDistrictName = "";
                if (focusDistrictId.HasValue)
                {
                    var focusDistrict = availableDistricts.FirstOrDefault(d => d.Id == focusDistrictId.Value);
                    focusDistrictName = focusDistrict?.Name ?? "";
                }

                return Ok(new
                {
                    success = true,
                    isOrendaUser = isOrendaUser,
                    focusDistrictId = focusDistrictId,
                    focusDistrictName = focusDistrictName,
                    availableDistricts = availableDistricts.Select(d => new { 
                        Id = d.Id, 
                        Name = d.Name 
                    }).ToList()
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new { success = false, message = "Failed to get district context" });
            }
        }

        [HttpPost("focus")]
        public async Task<IActionResult> SetFocusDistrict([FromBody] SetFocusRequest request)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();

                var roles = await _userManager.GetRolesAsync(user);
                var isOrendaUser = roles.Contains("OrendaAdmin") || roles.Contains("OrendaManager") || roles.Contains("OrendaUser");

                if (await _districtFocusService.ValidateFocusDistrictAsync(user.Id, request.DistrictId, isOrendaUser))
                {
                    await _districtFocusService.SetFocusDistrictIdAsync(user.Id, request.DistrictId);
                    return Ok(new { success = true });
                }

                return BadRequest(new { success = false, message = "Invalid district" });
            }
            catch (Exception)
            {
                return StatusCode(500, new { success = false, message = "Failed to set focus district" });
            }
        }

        public class SetFocusRequest
        {
            public int DistrictId { get; set; }
        }
    }
}