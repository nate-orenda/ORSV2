using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class School
    {
        public int Id { get; set; } // Changed from Guid to int

        [Required]
        public int DistrictId { get; set; } // Changed from Guid to int

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(5)]
        public string? LocalSchoolId { get; set; }

        [MaxLength(20)]
        public string? SchoolType { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        [MaxLength(14)]
        public string? CDSCode { get; set; }
        public bool enabled { get; set; } = false;
        public bool Inactive { get; set; } = false;
        public DateTime DateCreated { get; set; }
        public DateTime DateUpdated { get; set; }
        public short? LowGrade { get; set; }
        public short? HighGrade { get; set; }
        public District? District { get; set; }
        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    }

}
