using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    public class GAMatrix
    {
        [Key]
        public int MatrixId { get; set; }

        public int? DistrictId { get; set; }
        public int? SchoolId { get; set; }

        public int DistrictKey { get; set; }
        public int SchoolKey { get; set; }

        public int SchoolYear { get; set; }
        public int Grade { get; set; }
        public int CP { get; set; }

        public string LCAPPriority { get; set; } = string.Empty;

        public string Indicator { get; set; } = string.Empty;
        public string IndicatorDescription { get; set; } = string.Empty;

        public double? DefaultValue { get; set; }
        public string ReadableValue { get; set; } = string.Empty;

        public DateTime DateCreated { get; set; }
    }
}
