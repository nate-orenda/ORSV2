using System.ComponentModel.DataAnnotations.Schema; // Add this using statement

namespace ORSV2.Models
{
    public class TargetGroupStudent
    {
        public int TargetGroupId { get; set; }
        public int GAResultId { get; set; }

        public TargetGroup? TargetGroup { get; set; }
        public GAResults? GAResult { get; set; }
    }
}