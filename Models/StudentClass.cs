namespace ORSV2.Models
{
    public class StudentClass
    {
        public int Id { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
        public string CourseID { get; set; } = string.Empty;
        public DateTime? DateStarted { get; set; }
        public DateTime? DateEnded { get; set; }
        public string? SchoolCode { get; set; }
        public string? SectionNumber { get; set; }
        public int SequenceNumber { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateUpdated { get; set; }

        // Navigation (optional)
        public School? School { get; set; }
        public STU? Student { get; set; }
    }

}
