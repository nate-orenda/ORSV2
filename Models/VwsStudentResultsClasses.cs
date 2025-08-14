using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    // Maps to SQL view: dbo.vw_student_results_classes
    [Table("vw_student_results_classes")]
    public class VwStudentResultsClasses
    {
        [Column("DistrictId")]
        public int DistrictId { get; set; }                      // int, NOT NULL

        [Column("SchoolId")]
        public int? SchoolId { get; set; }                       // int, NULL

        [Column("studentid")]
        public int StudentId { get; set; }                       // int, NOT NULL

        [Column("test_id")]
        public string TestId { get; set; } = string.Empty;       // nvarchar(510), NOT NULL

        [Column("unit")]
        public int? Unit { get; set; }                           // int, NULL

        [Column("test_name")]
        public string TestName { get; set; } = string.Empty;     // nvarchar(510), NOT NULL

        [Column("Subject")]
        public string Subject { get; set; } = string.Empty;      // nvarchar(64),  NOT NULL

        [Column("localstudentid")]
        public int? LocalStudentId { get; set; }                 // int, NULL  (note: was string earlier)

        [Column("FirstName")]
        public string? FirstName { get; set; }                   // nvarchar(200), NULL

        [Column("LastName")]
        public string? LastName { get; set; }                    // nvarchar(200), NULL

        [Column("standard_id")]
        public string? StandardId { get; set; }                  // nvarchar(128), NULL

        [Column("human_coding_scheme")]
        public string HumanCodingScheme { get; set; } = string.Empty; // nvarchar(128), NOT NULL

        [Column("results")]
        public decimal? Results { get; set; }                    // decimal(9,2), NULL  (points)

        [Column("max_points")]
        public decimal? MaxPoints { get; set; }                  // decimal(9,2), NULL

        [Column("proficiency")]
        public int? Proficiency { get; set; }                    // int, NULL

        [Column("quadrant")]
        public int Quadrant { get; set; }                        // int, NOT NULL

        [Column("SectionNumber")]
        public string? SectionNumber { get; set; }               // nvarchar(40), NULL

        [Column("Period")]
        public string? Period { get; set; }                      // nvarchar(20), NULL

        [Column("CourseNumber")]
        public string? CourseNumber { get; set; }                // nvarchar(40), NULL

        [Column("CourseTitle")]
        public string? CourseTitle { get; set; }                 // nvarchar(510), NULL

        [Column("DepartmentName")]
        public string? DepartmentName { get; set; }              // nvarchar(510), NULL

        [Column("TeacherFirstName")]
        public string? TeacherFirstName { get; set; }            // nvarchar(200), NULL

        [Column("TeacherLastName")]
        public string? TeacherLastName { get; set; }             // nvarchar(200), NULL

        [Column("TeacherId")]
        public string? TeacherId { get; set; }                   // nvarchar(100), NULL

        [Column("IsPrimaryTeacherFlag")]
        public string? IsPrimaryTeacherFlag { get; set; }        // nvarchar(max), NULL

        [Column("AspNetUserId")]
        public string? AspNetUserId { get; set; }                // nvarchar(900), NULL

        // Convenience computed props (safe for nulls)
        [NotMapped]
        public string StudentFullName => $"{LastName ?? ""}, {FirstName ?? ""}".Trim(' ', ',');

        [NotMapped]
        public string TeacherFullName => $"{TeacherLastName ?? ""}, {TeacherFirstName ?? ""}".Trim(' ', ',');

        [NotMapped]
        public bool IsPrimaryTeacher => (IsPrimaryTeacherFlag ?? "").Equals("true", System.StringComparison.OrdinalIgnoreCase);
    }
}
