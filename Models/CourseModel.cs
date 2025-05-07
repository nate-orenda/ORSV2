public class CourseViewModel
{
    public string CourseNumber { get; set; }
    public string Title { get; set; }
    public string DepartmentCode { get; set; }
    public string Validation { get; set; } // CSU_Rule_ValidationLevelCode
    public string AG { get; set; } // CSU_SubjectAreaCode
    public string Elective { get; set; } // UC_Rule_CanBeAnElective
    public decimal? CreditDefault { get; set; }
    public string InactiveStatusCode { get; set; }
    public DateTime? DateUpdated { get; set; }
}
