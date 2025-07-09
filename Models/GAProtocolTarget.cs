namespace ORSV2.Models
{
    public class GAProtocolTarget
    {
        public int Id { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int SchoolYear { get; set; }
        public int GradeLevel { get; set; }

        public string TargetName { get; set; } = string.Empty;
        public decimal TargetValue { get; set; }
        public string? TargetType { get; set; }

        public DateTime UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }
}