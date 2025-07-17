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
        // Remove [Key] attribute since views typically don't have primary keys
        public int StudentId { get; set; }
        
        public int TestId { get; set; }
        
        public string Unit { get; set; } = string.Empty;
        
        [Column("test_name")]
        public string TestName { get; set; } = string.Empty;
        
        public string Subject { get; set; } = string.Empty;
        
        [Column("localstudentid")]
        public string LocalStudentId { get; set; } = string.Empty;
        
        public string FirstName { get; set; } = string.Empty;
        
        public string LastName { get; set; } = string.Empty;
        
        public decimal? Results { get; set; }
        
        public string Proficiency { get; set; } = string.Empty;
        
        public string Quadrant { get; set; } = string.Empty;
        
        public string SectionNumber { get; set; } = string.Empty;
        
        public string CourseNumber { get; set; } = string.Empty;
        
        public string CourseTitle { get; set; } = string.Empty;
        
        public string DepartmentName { get; set; } = string.Empty;
        
        public string TeacherFirstName { get; set; } = string.Empty;
        
        public string TeacherLastName { get; set; } = string.Empty;
        
        public int TeacherId { get; set; }
        
        public string IsPrimaryTeacherFlag { get; set; } = string.Empty;
        
        // Computed properties for easier use in UI
        [NotMapped]
        public string StudentFullName => $"{LastName}, {FirstName}";
        
        [NotMapped]
        public string TeacherFullName => $"{TeacherLastName}, {TeacherFirstName}";
        
        [NotMapped]
        public bool IsPrimaryTeacher => IsPrimaryTeacherFlag?.ToLower() == "true";
        
        [NotMapped]
        public string FormattedScore => Results?.ToString("F1") ?? "-";
        
        [NotMapped]
        public string SubjectBadgeClass => Subject switch
        {
            "ELA" => "bg-primary",
            "Math" => "bg-success",
            _ => "bg-secondary"
        };
        
        [NotMapped]
        public string ProficiencyBadgeClass => Proficiency switch
        {
            "Proficient" or "Advanced" => "bg-success",
            "Approaching" or "Developing" => "bg-warning text-dark",
            "Below" or "Beginning" => "bg-danger",
            _ => "bg-secondary"
        };
        
        [NotMapped]
        public string QuadrantBadgeClass => Quadrant switch
        {
            "Challenge" => "bg-primary",
            "Benchmark" => "bg-success",
            "Strategic" => "bg-warning text-dark",
            "Intensive" => "bg-danger",
            _ => "bg-secondary"
        };
    }
}