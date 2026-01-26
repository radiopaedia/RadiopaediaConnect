using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
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

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync();
            return Ok(new { message = "Logged out successfully" });
        }
    }
}