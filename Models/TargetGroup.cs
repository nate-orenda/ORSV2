using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class TargetGroup
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        // Foreign key to the school this group belongs to
        public int SchoolId { get; set; }
        public School? School { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property for the students in this group
        public ICollection<TargetGroupStudent> TargetGroupStudents { get; set; } = new List<TargetGroupStudent>();
    }
}