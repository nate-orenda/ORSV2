using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public CreateModel(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [BindProperty]
        public District District { get; set; } = new District();

        [BindProperty]
        public IFormFile? UploadedLogo { get; set; }

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            if (UploadedLogo != null)
            {
                // Optional: Add file validation (size, type)
                if (UploadedLogo.Length > 5 * 1024 * 1024) // 5 MB limit
                {
                    ModelState.AddModelError("UploadedLogo", "The file size cannot exceed 5MB.");
                    return Page();
                }

                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "district-logos");
                Directory.CreateDirectory(uploadsFolder); // Ensure the directory exists

                var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(UploadedLogo.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await UploadedLogo.CopyToAsync(fileStream);
                }
                
                // Store the relative path
                District.LogoImagePath = Path.Combine("/uploads/district-logos", uniqueFileName).Replace('\\', '/');
            }

            _context.Districts.Add(District);
            await _context.SaveChangesAsync();

            return RedirectToPage("Index");
        }
    }
}