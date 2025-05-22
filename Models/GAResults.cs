// Models/GAResults.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        public bool? SWD { get; set; }
        public bool? SED { get; set; }

        [MaxLength(50)]
        public string? LF { get; set; }

        [MaxLength(10)]
        public string? ELPACLevel { get; set; }

        public decimal? YrsInProgram { get; set; }

        [MaxLength(100)]
        public string? Counselor { get; set; }

        public bool? Foster { get; set; }
        public bool? Migrant { get; set; }
        public bool? Homeless { get; set; }

        public int CP { get; set; }
        public int SchoolYear { get; set; }

        public School School { get; set; } // Navigation
        public District District { get; set; }
    }
}
