using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ORSV2.Data;
using ORSV2.Models;

namespace ORSV2.Pages.Districts
{

    [Authorize(Roles = "OrendaAdmin")]
    public class RunImportsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<FunctionEndpointsOptions> _fnOptions;

        public RunImportsModel(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IOptions<FunctionEndpointsOptions> fnOptions)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _fnOptions = fnOptions;
        }

        [BindProperty(SupportsGet = true)]
        public int DistrictId { get; set; }
        public ORSV2.Models.District? District { get; set; }

        public async Task<IActionResult> OnGetAsync(int districtId)
        {
            DistrictId = districtId;
            District = await _context.Districts.FirstOrDefaultAsync(d => d.Id == districtId);
            if (District is null) return NotFound();
            return Page();
        }

        public async Task<IActionResult> OnPostRunFunctionAsync([FromForm] string job, [FromForm] int districtId)
        {
            if (!_fnOptions.Value.Endpoints.TryGetValue(job, out var endpoint) || string.IsNullOrWhiteSpace(endpoint.Url))
                return new JsonResult(new { ok = false, output = $"Unknown job '{job}'." }) { StatusCode = 400 };

            try
            {
                var client = _httpClientFactory.CreateClient();
                var url = AppendDistrict(endpoint.Url!, endpoint.DistrictQueryName, districtId);
                using var req = new HttpRequestMessage(new HttpMethod(endpoint.Method), url);

                // Use per-endpoint override if set, otherwise use host (_master) key
                var headerName = endpoint.KeyHeaderName ?? _fnOptions.Value.HostKeyHeaderName;
                var headerValue = endpoint.KeyValue ?? _fnOptions.Value.HostKeyValue;

                if (!string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(headerValue))
                {
                    req.Headers.Add(headerName, headerValue);
                }

                var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);
                var text = await resp.Content.ReadAsStringAsync();

                return new JsonResult(new { ok = resp.IsSuccessStatusCode, output = text, status = (int)resp.StatusCode });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { ok = false, output = $"Error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> OnPostRunSpAsync([FromForm] int districtId)
        {
            try
            {
                var param = new SqlParameter("@DistrictId", districtId);
                await _context.Database.ExecuteSqlRawAsync("EXEC sp_UpdateAllGuidanceAlignment @DistrictId", param);
                return new JsonResult(new { ok = true, output = $"sp_UpdateAllGuidanceAlignment completed for DistrictId={districtId}." });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { ok = false, output = $"SP error: {ex.Message}" }) { StatusCode = 500 };
            }
        }

        private static string AppendDistrict(string url, string queryName, int districtId)
        {
            var sep = url.Contains('?') ? "&" : "?";
            return $"{url}{sep}{queryName}={Uri.EscapeDataString(districtId.ToString())}";
        }
    }
}
