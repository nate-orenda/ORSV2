using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnPostLogin()
    {
        return Challenge(new AuthenticationProperties { RedirectUri = "/" }, "Google");
    }

    public IActionResult OnPostLogout()
    {
        return SignOut(new AuthenticationProperties { RedirectUri = "/" },
            IdentityConstants.ApplicationScheme);
    }
}

