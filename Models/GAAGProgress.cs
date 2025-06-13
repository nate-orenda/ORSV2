using System;
using System.ComponentModel.DataAnnotations;

namespace ORSV2.Models
{
    public class GAAGProgress
    {
        [Key]
        public int Id { get; set; }

        public int DistrictId { get; set; }
        public int SchoolId { get; set; }
        public int StudentId { get; set; }
        public int CP { get; set; }
        public int SchoolYear { get; set; }

        public decimal? CreditsEarned_ELA { get; set; }
        public decimal? CreditsEarned_MATH { get; set; }
        public decimal? CreditsEarned_SCI { get; set; }
        public decimal? CreditsEarned_FL { get; set; }
        public decimal? CreditsEarned_VA { get; set; }
        public decimal? CreditsEarned_HIS { get; set; }
        public decimal? CreditsEarned_PREP { get; set; }

        public decimal? CreditsScheduled_ELA { get; set; }
        public decimal? CreditsScheduled_MATH { get; set; }
        public decimal? CreditsScheduled_SCI { get; set; }
        public decimal? CreditsScheduled_FL { get; set; }
        public decimal? CreditsScheduled_VA { get; set; }
        public decimal? CreditsScheduled_HIS { get; set; }
        public decimal? CreditsScheduled_PREP { get; set; }

        public decimal? TargetEarned_ELA { get; set; }
        public decimal? TargetEarned_MATH { get; set; }
        public decimal? TargetEarned_SCI { get; set; }
        public decimal? TargetEarned_FL { get; set; }
        public decimal? TargetEarned_VA { get; set; }
        public decimal? TargetEarned_HIS { get; set; }
        public decimal? TargetEarned_PREP { get; set; }

        public decimal? TargetScheduled_ELA { get; set; }
        public decimal? TargetScheduled_MATH { get; set; }
        public decimal? TargetScheduled_SCI { get; set; }
        public decimal? TargetScheduled_FL { get; set; }
        public decimal? TargetScheduled_VA { get; set; }
        public decimal? TargetScheduled_HIS { get; set; }
        public decimal? TargetScheduled_PREP { get; set; }

        public bool? MetAllGradeBenchmarks { get; set; }
        public bool? MetAllScheduleBenchmarks { get; set; }

        public DateTime DateCalculated { get; set; }
    }
}
