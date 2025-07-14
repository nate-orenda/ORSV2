using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ORSV2.Models
{
    public class Courses
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // Aeries/Python sets this
        public int Id { get; set; }
        
        public int DistrictId { get; set; }
        
        // Required fields - these should always have values
        [Required]
        [MaxLength(50)]
        public required string CourseNumber { get; set; }
        
        [Required]
        [MaxLength(200)]
        public required string Title { get; set; }
        
        // Optional fields - these can be null in the database
        [MaxLength(50)]
        public string? DepartmentCode { get; set; }
        
        [MaxLength(10)]
        public string? CSU_Rule_ValidationLevelCode { get; set; }
        
        [MaxLength(10)]
        public string? CSU_SubjectAreaCode { get; set; }
        
        [MaxLength(10)]
        public string? UC_SubjectAreaCode { get; set; }
        
        [MaxLength(10)]
        public string? UC_Rule_CanBeAnElective { get; set; }
        
        [Precision(5, 2)]
        public decimal? CreditDefault { get; set; }
        
        [MaxLength(10)]
        public string? InactiveStatusCode { get; set; }
        
        public DateTime? DateUpdated { get; set; }
    }
}