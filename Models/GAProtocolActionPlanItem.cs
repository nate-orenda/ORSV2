// In a new file: Models/GAProtocolActionPlanItem.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    public class GAProtocolActionPlanItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProtocolId { get; set; }

        [ForeignKey("ProtocolId")]
        public virtual GAProtocol? Protocol { get; set; }

        [Required(ErrorMessage = "Please select a team member.")]
        public string? TeamMemberId { get; set; }

        [ForeignKey("TeamMemberId")]
        public virtual ApplicationUser? TeamMember { get; set; }

        [Required(ErrorMessage = "Please enter a task description.")]
        [StringLength(500)]
        public string Task { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select a due date.")]
        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? UpdatedBy { get; set; }
    }
}