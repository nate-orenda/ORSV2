using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.DataReflection
{
    public class FormsModel : SecureReportPageModel
    {
        private readonly ApplicationDbContext _context;

        public FormsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }
        public List<BreadcrumbItem> Breadcrumbs { get; set; } = new();
        public string DistrictName { get; set; } = string.Empty;
        
        // Granular access control properties
        public bool ShowForm3 { get; private set; }
        public bool ShowMeta { get; private set; }
        public bool ShowMega { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            // Initialize user data scope from SecureReportPageModel
            InitializeUserDataScope();

            if (DistrictId == 0)
            {
                return NotFound("A District ID is required.");
            }

            var district = await _context.Districts.FindAsync(DistrictId);

            if (district == null)
            {
                return NotFound($"District with ID {DistrictId} not found.");
            }

            DistrictName = district.Name;

            // Form 1 & 2: Everyone (Teacher, School Admin, District Admin, Orenda)
            // Form 3 & Meta: School Admin, District Admin, Orenda
            // Mega: District Admin, Orenda only
            
            ShowForm3 = IsOrendaUser || IsDistrictAdmin || IsSchoolAdmin;
            ShowMeta = IsOrendaUser || IsDistrictAdmin || IsSchoolAdmin;
            ShowMega = IsOrendaUser || IsDistrictAdmin;

            Breadcrumbs = new List<BreadcrumbItem>
            {
                new BreadcrumbItem { Title = "Data Reflection", Url = Url.Page("/DataReflection/Index") },
                new BreadcrumbItem { Title = $"{DistrictName} - Select Forms" } // current page; no URL
            };

            return Page();
        }
    }
}