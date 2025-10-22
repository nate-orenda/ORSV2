using Azure.Storage.Blobs;
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
        private readonly BlobContainerClient _blobContainer;

        public EditModel(ApplicationDbContext context, BlobContainerClient blobContainer)
        {
            _context = context;
            _blobContainer = blobContainer;
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

                // Delete the old blob if it exists
                if (!string.IsNullOrEmpty(districtToUpdate.LogoImagePath))
                {
                    try
                    {
                        var oldBlobName = ExtractBlobNameFromUri(districtToUpdate.LogoImagePath);
                        if (!string.IsNullOrEmpty(oldBlobName))
                        {
                            var oldBlobClient = _blobContainer.GetBlobClient(oldBlobName);
                            await oldBlobClient.DeleteIfExistsAsync();
                        }
                    }
                    catch
                    {
                        // Log error but continue with upload
                    }
                }

                // Upload the new blob
                var blobName = $"district-{District.Id}-{Guid.NewGuid()}_{Path.GetFileName(UploadedLogo.FileName)}";
                var blobClient = _blobContainer.GetBlobClient(blobName);

                using (var stream = UploadedLogo.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                districtToUpdate.LogoImagePath = blobClient.Uri.ToString();
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

        private string? ExtractBlobNameFromUri(string uri)
        {
            try
            {
                var blobUri = new Uri(uri);
                return blobUri.Segments.LastOrDefault()?.Trim('/');
            }
            catch
            {
                return null;
            }
        }
    }
}