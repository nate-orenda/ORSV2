using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ORSV2.Models
{
    [Table("ReportingPeriods")]
    [PrimaryKey(nameof(DistrictId), nameof(SchoolId), nameof(MarkingPeriod))]
    public class ReportingPeriods
    {
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int MarkingPeriod { get; set; }

        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }

        public DateTime? BeginDT { get; set; }
        public DateTime? EndDT { get; set; }

        public bool? IsCurrent { get; set; }

        public DateTime DTS { get; set; }
    }
}