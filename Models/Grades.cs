using System;
using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class Grades
    {
        [Key]
        public int GradeId { get; set; }

        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }

        public required string LocalStudentID { get; set; }
        public required string LocalSchoolId { get; set; }

        public required string CN { get; set; }     // Course Number
        public required string TN { get; set; }     // Teacher Number

        public required string SchoolYear { get; set; }
        public required string Term { get; set; }
        public required string GradeLevel { get; set; }
        public required string Mark { get; set; }
        public required string Type { get; set; }

        public decimal? CR { get; set; }   // Credits Attempted
        public decimal? CC { get; set; }   // Credits Completed
    }
}