using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Services;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;

namespace RadiopaediaConnect.Extensions
{
    public static class AuthExtensions
    {
        public static IServiceCollection AddRadiopaediaAuthentication(this IServiceCollection services)
        {
            services.AddHttpClient("Radiopaedia")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });

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
            .AddScheme<OAuthOptions, RadiopaediaOAuthHandler>("Radiopaedia", "Radiopaedia", options =>
            {
                // Credentials are populated at runtime by RadiopaediaOAuthPostConfigure
                options.ClientId = "placeholder";
                options.ClientSecret = "placeholder";
                options.CallbackPath = "/signin-radiopaedia";

                options.UsePkce = false;

                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.None;
                options.CorrelationCookie.IsEssential = true;
                options.CorrelationCookie.Expiration = TimeSpan.FromMinutes(30);

                options.AuthorizationEndpoint = "https://radiopaedia.org/oauth/authorize";
                options.TokenEndpoint = "https://radiopaedia.org/oauth/token";
                options.UserInformationEndpoint = "https://radiopaedia.org/api/v1/users/current";

                options.SaveTokens = true;
                options.ClaimsIssuer = "Radiopaedia";

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

            // Register the shared credentials cache (singleton so it survives across requests)
            services.AddSingleton<OAuthCredentialsCache>();

            // Register the PostConfigure handler that injects DB credentials at runtime
            services.AddSingleton<IPostConfigureOptions<OAuthOptions>, RadiopaediaOAuthPostConfigure>();

            return services;
        }
    }

    public class OAuthCredentialsCache
    {
        private string? _clientId;
        private string? _clientSecret;
        private bool _loaded;
        private readonly object _lock = new();

        private readonly SettingsRepository _repository;
        private readonly ILogger<OAuthCredentialsCache> _logger;

        public OAuthCredentialsCache(SettingsRepository repository, ILogger<OAuthCredentialsCache> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public (string? ClientId, string? ClientSecret) Get()
        {
            lock (_lock)
            {
                if (!_loaded)
                    Load();

                return (_clientId, _clientSecret);
            }
        }

        public void Refresh()
        {
            lock (_lock)
            {
                _loaded = false;
                Load();
                _logger.LogInformation("[OAuth] Credentials cache refreshed.");
            }
        }

        private void Load()
        {
            try
            {
                var settings = _repository.GetLocalSettings();
                _clientId = settings.RadiopaediaClientId;
                _clientSecret = settings.RadiopaediaClientSecret;
                _loaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[OAuth] Could not load credentials from DB: {ex.Message}");
            }
        }
    }

    public class RadiopaediaOAuthPostConfigure : IPostConfigureOptions<OAuthOptions>
    {
        private readonly OAuthCredentialsCache _cache;

        public RadiopaediaOAuthPostConfigure(OAuthCredentialsCache cache)
        {
            _cache = cache;
        }

        public void PostConfigure(string? name, OAuthOptions options)
        {
            if (name != "Radiopaedia") return;

            var (clientId, clientSecret) = _cache.Get();

            if (!string.IsNullOrEmpty(clientId))
                options.ClientId = clientId;

            if (!string.IsNullOrEmpty(clientSecret))
                options.ClientSecret = clientSecret;
        }
    }
}