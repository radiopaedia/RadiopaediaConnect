using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    public abstract class AdminControllerBase : ControllerBase
    {
        public const string SessionCookieName = "rconnect_admin_session";

        protected bool AuthorizeAdmin(AdminSessionService sessionService)
        {
            var token = Request.Cookies[SessionCookieName];
            return sessionService.ValidateSession(token);
        }

        protected void SetAdminSessionCookie(AdminSessionService sessionService)
        {
            var token = sessionService.CreateSession();
            Response.Cookies.Append(SessionCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddHours(8)
            });
        }

        protected void ClearAdminSessionCookie(AdminSessionService sessionService)
        {
            var token = Request.Cookies[SessionCookieName];
            sessionService.InvalidateSession(token);
            Response.Cookies.Delete(SessionCookieName);
        }
    }
}
