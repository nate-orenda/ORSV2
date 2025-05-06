using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ORSV2.Models
{
    public class District
    {
        public int Id { get; set; } // Changed from Guid to int

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(14)]
        public string? CDSCode { get; set; }

        [MaxLength(255)]
        public string? SISApiKey { get; set; }

        [MaxLength(255)]
        public string? SISApiSecret { get; set; }

        [MaxLength(255)]
        [Display(Name = "SIS Base URL")]
        public string? SISBaseUrl { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public bool Inactive { get; set; } = false;
        public DateTime DateCreated { get; set; }
        public DateTime DateUpdated { get; set; }

        public ICollection<School> Schools { get; set; } = new List<School>();
        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    }

}