using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    /// <summary>
    /// Anonymous by design: login must be reachable pre-auth, and me/quota check
    /// User.Identity themselves so the frontend gets a clean 401 to render the login page.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [AllowAnonymous]
    public class AuthController : ControllerBase
    {
        private readonly RadiopaediaApiClient _apiClient;

        public AuthController(RadiopaediaApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        [HttpGet("login")]
        public IActionResult Login(string returnUrl = "/")
        {
            return Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, "Radiopaedia");
        }

        [HttpGet("me")]
        public IActionResult GetMe()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }

            return Ok(new
            {
                username = User.FindFirst("urn:radiopaedia:username")?.Value,
                isAuthenticated = true
            });
        }

        [HttpGet("quota")]
        public async Task<IActionResult> GetQuota()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }

            var username = User.FindFirst("urn:radiopaedia:username")?.Value;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized("User session invalid.");
            }

            try
            {
                var quota = await _apiClient.GetUserQuotaAsync(username);
                if (quota == null)
                {
                    return Ok(new { current = 0, maximum = 0 });
                }

                return Ok(new { current = quota.Current, maximum = quota.Maximum });
            }
            catch
            {
                // Return default values if quota fetch fails
                return Ok(new { current = 0, maximum = 0 });
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return Ok(new { message = "Logged out successfully" });
        }
    }
}