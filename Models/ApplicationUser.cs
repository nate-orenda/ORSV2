using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ORSV2.Models
{
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(150)]
        public string? FirstName { get; set; }

        [MaxLength(150)]
        public string? LastName { get; set; }
        public Guid? DistrictId { get; set; }
        public District? District { get; set; }
        public ICollection<UserSchool> UserSchools { get; set; } = new List<UserSchool>();
        public School? School { get; set; }
    }

    public class UserSchool
    {
        public string UserId { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;

        public Guid SchoolId { get; set; }
        public School School { get; set; } = null!;
    }

}
