// Services/DistrictFocusService.cs
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;
using System.Text.Json;

namespace ORSV2.Services
{
    public interface IDistrictFocusService
    {
        Task<int?> GetFocusDistrictIdAsync(string userId, bool isOrendaUser);
        Task SetFocusDistrictIdAsync(string userId, int districtId);
        Task<List<District>> GetAvailableDistrictsAsync(string userId, bool isOrendaUser);
        Task<bool> ValidateFocusDistrictAsync(string userId, int districtId, bool isOrendaUser);
    }

    public class DistrictFocusService : IDistrictFocusService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DistrictFocusService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<int?> GetFocusDistrictIdAsync(string userId, bool isOrendaUser)
        {
            if (!isOrendaUser)
            {
                // Non-Orenda users: return their assigned district
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                return user?.DistrictId;
            }

            // Orenda users: check session first, then user preference
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session != null)
            {
                var sessionFocus = session.GetString($"FocusDistrict_{userId}");
                if (int.TryParse(sessionFocus, out var sessionDistrictId))
                {
                    // Validate the district still exists and is active
                    var isValid = await _context.Districts
                        .AnyAsync(d => d.Id == sessionDistrictId && !d.Inactive);
                    if (isValid) return sessionDistrictId;
                }
            }

            // Fallback: get the first available district
            var firstDistrict = await _context.Districts
                .Where(d => !d.Inactive)
                .OrderBy(d => d.Name)
                .FirstOrDefaultAsync();

            return firstDistrict?.Id;
        }

        public Task SetFocusDistrictIdAsync(string userId, int districtId)
        {
            // Store in session for immediate use
            var session = _httpContextAccessor.HttpContext?.Session;
            session?.SetString($"FocusDistrict_{userId}", districtId.ToString());

            // Optional: Also store in database for persistence across sessions
            // This would require a UserPreferences table or similar
            
            return Task.CompletedTask;
        }

        public async Task<List<District>> GetAvailableDistrictsAsync(string userId, bool isOrendaUser)
        {
            if (isOrendaUser)
            {
                // Orenda users see all districts
                return await _context.Districts
                    .Where(d => !d.Inactive)
                    .OrderBy(d => d.Name)
                    .ToListAsync();
            }
            else
            {
                // Non-Orenda users see only their assigned district
                var user = await _context.Users
                    .Include(u => u.District)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user?.District != null && !user.District.Inactive)
                {
                    return new List<District> { user.District };
                }
                return new List<District>();
            }
        }

        public async Task<bool> ValidateFocusDistrictAsync(string userId, int districtId, bool isOrendaUser)
        {
            if (isOrendaUser)
            {
                // Orenda users can focus on any active district
                return await _context.Districts.AnyAsync(d => d.Id == districtId && !d.Inactive);
            }
            else
            {
                // Non-Orenda users can only focus on their assigned district
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                return user?.DistrictId == districtId;
            }
        }
    }
}