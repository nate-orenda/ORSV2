using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    [Table("assessments")]
    public class Assessment
    {
        [Key]
        [Column("test_id")]
        [MaxLength(255)]
        public string TestId { get; set; } = string.Empty;

        [Required]
        [Column("districtid")]
        public int DistrictId { get; set; }

        [Column("test_name")]
        public string? TestName { get; set; }

        [Required]
        [Column("unit")]
        public int Unit { get; set; }

        [Required]
        [Column("standards")]
        public string Standards { get; set; } = string.Empty;

        // Navigation properties
        [ForeignKey("DistrictId")]
        public District? District { get; set; }
    }

    // DTO for dropdowns and form binding
    public class AssessmentDropdownDto
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class Form1ViewModel
    {
        public int? SelectedDistrictId { get; set; }
        public int? SelectedUnit { get; set; }
        public string? SelectedAssessment { get; set; }

        public List<District> AvailableDistricts { get; set; } = new();
        public List<AssessmentDropdownDto> AvailableUnits { get; set; } = new();
        public List<AssessmentDropdownDto> AvailableAssessments { get; set; } = new();
    }
}