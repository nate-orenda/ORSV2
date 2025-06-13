namespace ORSV2.Models
{
    public class StudentAttendance
    {
        public int Id { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
        public int SchoolYear { get; set; }
        public int Absences { get; set; }
        // Add any other fields you need
    }
}
