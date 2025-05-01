using Microsoft.AspNetCore.Identity;

namespace ORSV2.Models
{
    public class ApplicationUser : IdentityUser
    {
        public required string DisplayName { get; set; }
        public Guid? DistrictId { get; set; }
        public Guid? SchoolId { get; set; }

        public District? District { get; set; }
        public School? School { get; set; }
    }
}
