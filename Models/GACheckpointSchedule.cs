using ORSV2.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
public class GACheckpointSchedule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // Aeries/Python sets this
    public int ScheduleId { get; set; }
    public int DistrictId { get; set; }
    public int SchoolId { get; set; }

    public DateTime? Checkpoint1Date { get; set; }
    public DateTime? Checkpoint2Date { get; set; }
    public DateTime? Checkpoint3Date { get; set; }
    public DateTime? Checkpoint4Date { get; set; }
    public DateTime? Checkpoint5Date { get; set; }

    public School School { get; set; } // Navigation
}
