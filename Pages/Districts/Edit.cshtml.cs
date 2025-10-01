using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{
    [Authorize(Roles = "OrendaAdmin")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public EditModel(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [BindProperty]
        public District District { get; set; } = new District();
        
        [BindProperty]
        public IFormFile? UploadedLogo { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var district = await _context.Districts.FindAsync(id);
            if (district == null)
            {
                return NotFound();
            }

            District = district;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var districtToUpdate = await _context.Districts.FindAsync(District.Id);
            if (districtToUpdate == null)
            {
                return NotFound();
            }
            
            // Handle new logo upload
            if (UploadedLogo != null)
            {
                 if (UploadedLogo.Length > 5 * 1024 * 1024) // 5 MB limit
                {
                    ModelState.AddModelError("UploadedLogo", "The file size cannot exceed 5MB.");
                    return Page();
                }

                // Delete the old file if it exists
                if (!string.IsNullOrEmpty(districtToUpdate.LogoImagePath))
                {
                    var oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, districtToUpdate.LogoImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                // Save the new file
                var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "district-logos");
                Directory.CreateDirectory(uploadsFolder);
                var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(UploadedLogo.FileName);
                var newFilePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(newFilePath, FileMode.Create))
                {
                    await UploadedLogo.CopyToAsync(fileStream);
                }
                districtToUpdate.LogoImagePath = Path.Combine("/uploads/district-logos", uniqueFileName).Replace('\\', '/');
            }


            // Only update API fields if new values were provided
            if (!string.IsNullOrWhiteSpace(District.SISApiKey))
            {
                districtToUpdate.SISApiKey = District.SISApiKey;
            }

            if (!string.IsNullOrWhiteSpace(District.SISApiSecret))
            {
                districtToUpdate.SISApiSecret = District.SISApiSecret;
            }

            // Always update basic fields
            districtToUpdate.Name = District.Name;
            districtToUpdate.CDSCode = District.CDSCode;
            districtToUpdate.SISBaseUrl = District.SISBaseUrl;
            districtToUpdate.Notes = District.Notes;

            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}