namespace ORSV2.Models
{
    public class GAProtocol
    {
        public int Id { get; set; }

        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int SchoolYear { get; set; }
        public int CP { get; set; }
        public List<GAProtocolSectionResponse> SectionResponses { get; set; } = new();

        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsFinalized { get; set; }

        public District? District { get; set; }
        public School? School { get; set; }
    }
}
