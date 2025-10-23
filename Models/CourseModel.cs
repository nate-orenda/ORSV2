namespace ORSV2.Models
{
    public class CourseViewModel
    {
        public required string CourseNumber { get; set; }
        public required string Title { get; set; }
        public required string DepartmentCode { get; set; }
        public required string Validation { get; set; } // CSU_Rule_ValidationLevelCode
        public required string AG { get; set; } // CSU_SubjectAreaCode
        public required string Elective { get; set; } // UC_Rule_CanBeAnElective
        public decimal? CreditDefault { get; set; }
        public required string InactiveStatusCode { get; set; }
        public DateTime? DateUpdated { get; set; }
    }
}