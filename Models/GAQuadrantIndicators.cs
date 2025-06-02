using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class GAQuadrantIndicators
    {
        [Key]
        public int Id { get; set; }
        public int? DistrictId { get; set; }
        public int? SchoolId { get; set; }
        public int Grade { get; set; }
        public int CP { get; set; }

        [MaxLength(10)]
        public required string IndicatorName { get; set; }

        public bool? IsEnabled { get; set; }

        [MaxLength(100)]
        public string? Notes { get; set; }

    }
}