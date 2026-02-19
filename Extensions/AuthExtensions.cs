using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadiopaediaConnect.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace RadiopaediaConnect.Extensions
{
    public static class AuthExtensions
    {
        public static IServiceCollection AddRadiopaediaAuthentication(this IServiceCollection services, IConfiguration config)
        {
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = "Radiopaedia";
            })
            .AddCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.None;
                options.Cookie.Name = "RadiopaediaConnectSession";
                options.Cookie.Path = "/";
            })
            .AddOAuth("Radiopaedia", "Radiopaedia", options =>
            {
                options.ClientId = config["Radiopaedia:ClientId"];
                options.ClientSecret = config["Radiopaedia:ClientSecret"];
                options.CallbackPath = "/signin-radiopaedia";

                options.UsePkce = false;

                options.AuthorizationEndpoint = "https://radiopaedia.org/oauth/authorize";
                options.TokenEndpoint = "https://radiopaedia.org/oauth/token";
                options.UserInformationEndpoint = "https://radiopaedia.org/api/v1/users/current";

                options.SaveTokens = true;
                options.ClaimsIssuer = "Radiopaedia";

                // Correlation cookie settings for the state/CSRF round-trip
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
                options.CorrelationCookie.IsEssential = true;
                options.CorrelationCookie.Expiration = TimeSpan.FromMinutes(30);

                // Backchannel handler for token exchange and user info requests
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };

                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = async context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogInformation("[OAuth] Creating ticket - callback successful");

                        var tokens = context.TokenResponse;
                        var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                        request.Headers.UserAgent.ParseAdd("RadiopaediaConnect/1.0");

                        var response = await context.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);
                        response.EnsureSuccessStatusCode();

                        var userJson = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

                        string username = "Unknown";
                        if (userJson.TryGetProperty("username", out var u)) username = u.GetString();
                        else if (userJson.TryGetProperty("login", out var l)) username = l.GetString();

                        logger.LogInformation($"[OAuth] User authenticated: {username}");

                        var repo = context.HttpContext.RequestServices.GetRequiredService<UserRepository>();
                        var expiresInSeconds = !string.IsNullOrEmpty(tokens.ExpiresIn) ? Convert.ToDouble(tokens.ExpiresIn) : 3600;

                        await repo.UpsertTokensAsync(new UserToken
                        {
                            Username = username,
                            AccessToken = tokens.AccessToken,
                            RefreshToken = tokens.RefreshToken,
                            TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds),
                            LastUpdatedUtc = DateTime.UtcNow
                        });

                        context.Identity.AddClaim(new Claim(ClaimTypes.Name, username));
                        context.Identity.AddClaim(new Claim("urn:radiopaedia:username", username));
                    }
                };
            });

            return services;
        }
    }
}