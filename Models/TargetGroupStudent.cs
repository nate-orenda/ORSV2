using System.ComponentModel.DataAnnotations.Schema; // Add this using statement

namespace ORSV2.Models
{
    public class TargetGroupStudent
    {
        public int TargetGroupId { get; set; }
        public int StudentId { get; set; }   // <-- use StuId

        public TargetGroup? TargetGroup { get; set; }
        public STU? Student { get; set; }    // nav to Students table (STU entity)
    }

}