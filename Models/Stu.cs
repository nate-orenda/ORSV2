using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    [Keyless]
    public class STU
    {
        public Guid STU_ID { get; set; }
        public Guid DistrictID { get; set; }
        public Guid SchoolID { get; set; }

        public string? LocalDistrictCode { get; set; }
        public string? LocalSchoolCode { get; set; }
        public string LocalStudentID { get; set; } = default!;

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MiddleName { get; set; }

        public string? Grade { get; set; }
        public string? Gender { get; set; }

        public DateTime? BirthDate { get; set; }
        public int? GradYear { get; set; }

        public bool? Ethnicity { get; set; }
        public string? RaceCode { get; set; }
        public string? LanguageFluency { get; set; }

        public DateTime? USSchoolEnterDate { get; set; }
        public bool? SWD { get; set; }

        [Precision(5, 2)] // ✅ Matches decimal(5,2) in SQL
        public decimal? CreditsCompleted { get; set; }

        public double? CurrentGPA { get; set; }
        public double? CumulativeGPA { get; set; }

        public string? Counselor { get; set; }

        public bool? FAFSA { get; set; }
        public bool? SED { get; set; }

        public string? Affiliation { get; set; }
        public string? CollegeApplication { get; set; }

        public bool? Inactive { get; set; }

        public DateTime? DateUpdated { get; set; }
        public DateTime? DateCreated { get; set; }
    }
}
