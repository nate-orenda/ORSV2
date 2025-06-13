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

        public string LocalStudentID { get; set; }
        public string LocalSchoolId { get; set; }

        public string CN { get; set; }     // Course Number
        public string TN { get; set; }     // Teacher Number

        public string SchoolYear { get; set; }
        public string Term { get; set; }
        public string GradeLevel { get; set; }
        public string Mark { get; set; }
        public string Type { get; set; }

        public decimal? CR { get; set; }   // Credits Attempted
        public decimal? CC { get; set; }   // Credits Completed
    }
}
