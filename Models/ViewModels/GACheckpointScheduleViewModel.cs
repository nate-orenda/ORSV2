namespace ORSV2.Models.ViewModels
{
    public class GACheckpointScheduleViewModel
    {
        public int ScheduleId { get; set; }
        public int DistrictId { get; set; }
        public int SchoolId { get; set; }

        public string SchoolName { get; set; } = string.Empty;

        public DateTime? Checkpoint1Date { get; set; }
        public DateTime? Checkpoint2Date { get; set; }
        public DateTime? Checkpoint3Date { get; set; }
        public DateTime? Checkpoint4Date { get; set; }
        public DateTime? Checkpoint5Date { get; set; }
    }
}