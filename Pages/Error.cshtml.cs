using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ORSV2.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    // Only populated when the viewer is an OrendaAdmin
    public string? ExceptionMessage { get; private set; }
    public string? StackTrace { get; private set; }
    public string? ExceptionPath { get; private set; }

    private readonly ILogger<ErrorModel> _logger;
    public ErrorModel(ILogger<ErrorModel> logger) => _logger = logger;

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        // Always log the exception (if present), regardless of who is viewing.
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error != null)
        {
            _logger.LogError(feature.Error,
                "Unhandled exception at path {Path}. RequestId: {RequestId}",
                feature.Path, RequestId);

            // Only reveal details to admins
            if (User.IsInRole("OrendaAdmin"))
            {
                ExceptionMessage = feature.Error.Message;
                StackTrace = feature.Error.StackTrace;
                ExceptionPath = feature.Path;
            }
        }
    }
}
