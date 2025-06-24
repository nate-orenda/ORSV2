using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

public class Staff
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // Aeries/Python sets this
    public int StaffId { get; set; }

    public int DistrictId { get; set; }

    [MaxLength(50)]
    public string LocalStaffID { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MiddleName { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    [MaxLength(10)]
    public string EthnicityCode { get; set; } = string.Empty;

    [MaxLength(10)]
    public string RaceCode1 { get; set; } = string.Empty;

    [MaxLength(150)]
    public string EmailAddress { get; set; } = string.Empty;

    [MaxLength(150)]
    public string JobTitle { get; set; } = string.Empty;

    [MaxLength(32)]
    public string PrimarySchool { get; set; } = string.Empty;
    public string SchoolAccess { get; set; } = string.Empty;

    [NotMapped]
    public List<StaffSchoolAccess> SchoolAccessEntries =>
        string.IsNullOrWhiteSpace(SchoolAccess)
            ? new()
            : JsonSerializer.Deserialize<List<StaffSchoolAccess>>(SchoolAccess) ?? new();

    public decimal? FullTimePercentage { get; set; }

    public decimal? TotalYearsInThisDistrict { get; set; }

    public decimal? TotalYearsOfEduService { get; set; }

    public DateTime? HireDate { get; set; }

    public bool Inactive { get; set; }

    public DateTime? DateCreated { get; set; }

    public DateTime? DateUpdated { get; set; }
}
public class StaffSchoolAccess
{
    public int SchoolCode { get; set; }
    public bool ReadOnlyAccess { get; set; }
    public bool CommunicationGroup { get; set; }
}
