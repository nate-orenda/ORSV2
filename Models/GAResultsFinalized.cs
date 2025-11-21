using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    [Table("GAResultsFinalized")]
    public class GAResultsFinalized
    {
        [Key]
        public int FinalizedResultId { get; set; }
        
        public int ProtocolId { get; set; }
        public int OriginalResultId { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
        public int SchoolYear { get; set; }
        public int CP { get; set; }
        
        public string LocalStudentId { get; set; } = string.Empty;
        public string LocalSchoolId { get; set; } = string.Empty;
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public int Grade { get; set; }
        public string? Gender { get; set; }
        public int? GradYear { get; set; }
        public decimal? Age { get; set; }
        public string? RaceEthnicity { get; set; }
        
        public bool? SWD { get; set; }
        public bool? SED { get; set; }
        public string? LF { get; set; }
        public string? ELPACLevel { get; set; }
        public decimal? YrsInProgram { get; set; }
        
        public string? Counselor { get; set; }
        public string? CounselorName { get; set; }
        
        public bool? Foster { get; set; }
        public bool? Migrant { get; set; }
        public bool? Homeless { get; set; }
        
        public decimal? CreditsCompleted { get; set; }
        public double? CurrentGPA { get; set; }
        public double? CumulativeGPA { get; set; }
        public bool? FAFSA { get; set; }
        public bool? CollegeApplication { get; set; }
        
        public bool? AGGrades { get; set; }
        public bool? AGSchedule { get; set; }
        public bool? OnTrack { get; set; }
        public bool? GPA { get; set; }
        public bool? AssessmentsELA { get; set; }
        public bool? AssessmentsMath { get; set; }
        public bool? Grades { get; set; }
        public bool? Referrals { get; set; }
        public bool? Attendance { get; set; }
        public bool? Affiliation { get; set; }
        
        public string? Quadrant { get; set; }
        public bool? OnTarget { get; set; }
        
        public string? UpdatedBy { get; set; }
        public DateTime FinalizedAt { get; set; } = DateTime.UtcNow;
        public string? RowHash { get; set; }
        
        // Foreign keys
        [ForeignKey("ProtocolId")]
        public virtual GAProtocol? Protocol { get; set; }
    }
}