namespace ORSV2.Models.ViewModels
{
    public class StudentListViewModel
    {
        public Guid STU_ID { get; set; }
        public string LocalStudentID { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? MiddleName { get; set; }
        public string? Grade { get; set; }
    }
}
