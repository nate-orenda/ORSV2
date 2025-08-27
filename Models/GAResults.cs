using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class GAResults
    {
        [Key]
        public int ResultId { get; set; }

        public int DistrictId { get; set; }
        public int SchoolId { get; set; }

        [Required]
        [MaxLength(10)]
        public string LocalSchoolId { get; set; } = string.Empty;

        public int StudentId { get; set; }

        [MaxLength(20)]
        public string LocalStudentId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? LastName { get; set; }

        [MaxLength(100)]
        public string? FirstName { get; set; }

        public int Grade { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; }

        public int? GradYear { get; set; }

        public decimal? Age { get; set; }

        [MaxLength(100)]
        public string? RaceEthnicity { get; set; }

        public bool SWD { get; set; }
        public bool SED { get; set; }

        [MaxLength(50)]
        public string? LF { get; set; }

        [MaxLength(10)]
        public string? ELPACLevel { get; set; }

        public decimal? YrsInProgram { get; set; }

        [MaxLength(100)]
        public string? Counselor { get; set; }

        public bool Foster { get; set; }
        public bool Migrant { get; set; }
        public bool Homeless { get; set; }

        public int CP { get; set; }  // Checkpoint (e.g., 1–5)
        public int SchoolYear { get; set; }

        public DateTime DateInserted { get; set; }
        public DateTime? DateLastUpdated { get; set; }

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }

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

        [MaxLength(10)]
        public string? Quadrant { get; set; }

        [MaxLength(255)]
        public string? RowHash { get; set; }

        [MaxLength(100)]
        public string? CounselorName { get; set; }
        public double? CurrentGPA { get; set; }
        public double? CumulativeGPA { get; set; }
        public decimal? CreditsCompleted { get; set; }

        // Navigation
        public School? School { get; set; }
        public District? District { get; set; }
    }
}
