using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    public class GAProtocolSectionResponse
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("Protocol")]
        public int ProtocolId { get; set; }
        public GAProtocol Protocol { get; set; } = null!;

        public int SectionNumber { get; set; } // 1–9
        [MaxLength(100)]
        public string SectionTitle { get; set; } = string.Empty;

        public string? ResponseText { get; set; }

        public DateTime UpdatedAt { get; set; }
        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
    }
}
