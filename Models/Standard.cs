using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ORSV2.Models
{
    [Table("standards")] // Or the exact name of your standards table
    public class Standard
    {
        [Key]
        [Column("id")]
        public string Id { get; set; } = string.Empty;

        [Column("human_coding_scheme")]
        public string HumanCodingScheme { get; set; } = string.Empty;

        [Column("full_statement")]
        public string FullStatement { get; set; } = string.Empty;

        // Add other columns if needed, like last_change_date_time or education_level
    }
}