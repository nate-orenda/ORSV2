using System;
using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class ReportCardGrades
    {
        [Key]
        public int GradeId { get; set; }

        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }

        public string? CourseNumber { get; set; }
        public string? Term { get; set; }
        public string? TN { get; set; }  // Teacher Number
        public string? Mark { get; set; }
        public decimal? CreditsEarned { get; set; }

        public DateTime DateCreated { get; set; }
        public string? RowHash { get; set; }
    }
}