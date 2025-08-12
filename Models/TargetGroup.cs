using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class TargetGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SchoolId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Note { get; set; }
        public ICollection<TargetGroupStudent> TargetGroupStudents { get; set; } = new List<TargetGroupStudent>();
        public School? School { get; set; }
    }

}