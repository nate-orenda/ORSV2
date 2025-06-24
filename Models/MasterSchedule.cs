using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class MasterSchedule
    {
        [Key]
        public int ClassID { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public string LocalSchoolId { get; set; } = string.Empty;
        public string? SectionNumber { get; set; }
        public string? ExternalClassId { get; set; }
        public string? Period { get; set; }
        public decimal? Credit { get; set; }
        public string? Semester { get; set; }
        public string? SectionStaffMembers { get; set; }
        public bool Inactive { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateUpdated { get; set; }

        // Navigation (optional)
        public School? School { get; set; }
    }

}
