using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ORSV2.Models
{
    public class Courses
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // Aeries/Python sets this
        public int Id { get; set; }
        public int DistrictId { get; set; }
        public string CourseNumber { get; set; }
        public string Title { get; set; }
        public string DepartmentCode { get; set; }
        public string CSU_Rule_ValidationLevelCode { get; set; }
        public string CSU_SubjectAreaCode { get; set; }
        public string UC_Rule_CanBeAnElective { get; set; }
        public decimal? CreditDefault { get; set; }
        public string InactiveStatusCode { get; set; }
        public DateTime? DateUpdated { get; set; }
    }
}