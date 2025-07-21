using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    /// <summary>
    /// Entity model for the vw_student_results_classes SQL view
    /// Maps student assessment results with their current class enrollments and teachers
    /// </summary>
    [Table("vw_student_results_classes")]
    public class VwStudentResultsClasses
    {
        [Column("DistrictId")]
        public int DistrictId { get; set; }
        [Column("SchoolId")]
        public int SchoolId { get; set; }
        [Column("studentid")]
        public int StudentId { get; set; }
        
        [Column("test_id")]
        public string TestId { get; set; } = string.Empty; // Changed to string

        [Column("unit")]
        public int Unit { get; set; }
        
        [Column("test_name")]
        public string TestName { get; set; } = string.Empty;
        
        [Column("Subject")]
        public string Subject { get; set; } = string.Empty;
        
        [Column("localstudentid")]
        public string LocalStudentId { get; set; } = string.Empty;

        [Column("FirstName")]
        public string FirstName { get; set; } = string.Empty;

        [Column("LastName")]
        public string LastName { get; set; } = string.Empty;
        
        [Column("results")]
        public string Results { get; set; } = string.Empty; // Changed to string

        [Column("proficiency")]
        public int? Proficiency { get; set; } // Changed to int?

        [Column("quadrant")]
        public byte? Quadrant { get; set; } // Changed to byte? for tinyint
        
        [Column("SectionNumber")]
        public string SectionNumber { get; set; } = string.Empty;
        [Column("Period")]
        public string Period { get; set; } = string.Empty;

        [Column("CourseNumber")]
        public string CourseNumber { get; set; } = string.Empty;

        [Column("CourseTitle")]
        public string CourseTitle { get; set; } = string.Empty;
        
        [Column("DepartmentName")]
        public string DepartmentName { get; set; } = string.Empty;
        
        [Column("TeacherFirstName")]
        public string TeacherFirstName { get; set; } = string.Empty;

        [Column("TeacherLastName")]
        public string TeacherLastName { get; set; } = string.Empty;

        [Column("TeacherId")]
        public string TeacherId { get; set; } = string.Empty;

        [Column("IsPrimaryTeacherFlag")]
        public string IsPrimaryTeacherFlag { get; set; } = string.Empty;
        
        // Computed properties for easier use in UI
        [NotMapped]
        public string StudentFullName => $"{LastName}, {FirstName}";
        
        [NotMapped]
        public string TeacherFullName => $"{TeacherLastName}, {TeacherFirstName}";
        
        [NotMapped]
        public bool IsPrimaryTeacher => IsPrimaryTeacherFlag?.ToLower() == "true";
    }
}