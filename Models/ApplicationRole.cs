using Microsoft.AspNetCore.Identity;

namespace ORSV2.Models
{
    public class ApplicationRole : IdentityRole
    {
        public string? Description { get; set; }
    }
}