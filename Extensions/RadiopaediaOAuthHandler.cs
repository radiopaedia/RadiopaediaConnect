using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RadiopaediaConnect.Extensions
{
    public class RadiopaediaOAuthHandler : OAuthHandler<OAuthOptions>
    {
        private readonly IHttpClientFactory? _httpClientFactory;

        public RadiopaediaOAuthHandler(
            IOptionsMonitor<OAuthOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock,
            IHttpClientFactory? httpClientFactory = null)
            : base(options, logger, encoder, clock)
        {
            _httpClientFactory = httpClientFactory;
        }

        protected override Task InitializeHandlerAsync()
        {
            if (string.IsNullOrEmpty(Options.ClientId))
                throw new InvalidOperationException("OAuth ClientId is not configured");
            if (string.IsNullOrEmpty(Options.ClientSecret))
                throw new InvalidOperationException("OAuth ClientSecret is not configured");
            if (string.IsNullOrEmpty(Options.AuthorizationEndpoint))
                throw new InvalidOperationException("OAuth AuthorizationEndpoint is not configured");
            if (string.IsNullOrEmpty(Options.TokenEndpoint))
                throw new InvalidOperationException("OAuth TokenEndpoint is not configured");

            Logger.LogInformation($"[OAuth] Handler initialized - ClientId: {Options.ClientId?.Substring(0, 8)}...");

            return base.InitializeHandlerAsync();
        }

        protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (string.IsNullOrEmpty(properties.RedirectUri))
            {
                properties.RedirectUri = OriginalPathBase + OriginalPath + Request.QueryString;
            }

            var redirectUri = BuildRedirectUri(Options.CallbackPath);

            var queryParams = new Dictionary<string, string?>
            {
                { "client_id", Options.ClientId },
                { "response_type", "code" },
                { "redirect_uri", redirectUri }
            };

            if (Options.Scope.Any())
            {
                queryParams["scope"] = string.Join(" ", Options.Scope);
            }

            var authorizationUrl = QueryHelpers.AddQueryString(Options.AuthorizationEndpoint, queryParams);

            Logger.LogInformation($"[OAuth] Redirecting to authorization endpoint (without state): {authorizationUrl}");

            Response.Redirect(authorizationUrl);
        }

        protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
        {
            Logger.LogInformation("[OAuth] Exchanging authorization code for access token");

            var httpClient = _httpClientFactory?.CreateClient("Radiopaedia") ?? new HttpClient();

            try
            {
                var tokenRequestParameters = new Dictionary<string, string>
                {
                    { "client_id", Options.ClientId! },
                    { "client_secret", Options.ClientSecret! },
                    { "code", context.Code },
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", context.RedirectUri }
                };

                var requestContent = new FormUrlEncodedContent(tokenRequestParameters);
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint)
                {
                    Content = requestContent
                };
                requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                Logger.LogInformation($"[OAuth] Posting to token endpoint: {Options.TokenEndpoint}");

                var response = await httpClient.SendAsync(requestMessage);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError($"[OAuth] Token endpoint returned error: {response.StatusCode} - {responseContent}");
                    return OAuthTokenResponse.Failed(new Exception($"Token exchange failed: {response.StatusCode}"));
                }

                Logger.LogInformation("[OAuth] Token endpoint returned success");

                var payload = JsonDocument.Parse(responseContent);

                return OAuthTokenResponse.Success(payload);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[OAuth] Exception during token exchange");
                return OAuthTokenResponse.Failed(ex);
            }
            finally
            {
                if (_httpClientFactory == null)
                {
                    httpClient.Dispose();
                }
            }
        }

        protected override async Task<AuthenticationTicket> CreateTicketAsync(
            ClaimsIdentity identity,
            AuthenticationProperties properties,
            OAuthTokenResponse tokens)
        {
            Logger.LogInformation("[OAuth] Creating authentication ticket");

            var httpClient = _httpClientFactory?.CreateClient("Radiopaedia") ?? new HttpClient();

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                request.Headers.UserAgent.ParseAdd("RadiopaediaConnect/1.0");

                Logger.LogInformation($"[OAuth] Fetching user info from: {Options.UserInformationEndpoint}");

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Logger.LogError($"[OAuth] User info request failed: {response.StatusCode} - {errorContent}");
                    throw new Exception($"Failed to retrieve user information: {response.StatusCode}");
                }

                var userContent = await response.Content.ReadAsStringAsync();
                var user = JsonDocument.Parse(userContent);

                Logger.LogInformation("[OAuth] User info retrieved successfully");

                var context = new OAuthCreatingTicketContext(
                    new ClaimsPrincipal(identity),
                    properties,
                    Context,
                    Scheme,
                    Options,
                    httpClient,
                    tokens,
                    user.RootElement);

                context.RunClaimActions();

                await Options.Events.CreatingTicket(context);

                Logger.LogInformation($"[OAuth] Ticket created with {identity.Claims.Count()} claims");

                return new AuthenticationTicket(context.Principal!, context.Properties, Scheme.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[OAuth] Failed to create ticket");
                throw;
            }
            finally
            {
                if (_httpClientFactory == null)
                {
                    httpClient.Dispose();
                }
            }
        }

        protected override async Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
        {
            var query = Request.Query;
            var code = query["code"].ToString();
            var error = query["error"].ToString();

            if (!string.IsNullOrEmpty(error))
            {
                Logger.LogError($"[OAuth] Provider returned error: {error}");
                return HandleRequestResult.Fail($"OAuth error: {error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                Logger.LogError("[OAuth] Authorization code not found in callback");
                return HandleRequestResult.Fail("Authorization code not found.");
            }

            Logger.LogInformation($"[OAuth] Received authorization code, exchanging for tokens...");

            var redirectUri = BuildRedirectUri(Options.CallbackPath);

            OAuthTokenResponse tokens;
            try
            {
                tokens = await ExchangeCodeAsync(new OAuthCodeExchangeContext(
                    new AuthenticationProperties(),
                    code,
                    redirectUri));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[OAuth] Token exchange threw exception");
                return HandleRequestResult.Fail(ex);
            }

            if (tokens.Error != null)
            {
                Logger.LogError($"[OAuth] Token exchange failed: {tokens.Error}");
                return HandleRequestResult.Fail(new Exception($"OAuth token error: {tokens.Error}"));
            }

            if (string.IsNullOrEmpty(tokens.AccessToken))
            {
                Logger.LogError("[OAuth] Access token is null or empty");
                return HandleRequestResult.Fail("Failed to retrieve access token.");
            }

            Logger.LogInformation("[OAuth] Token exchange successful, creating authentication ticket");

            var identity = new ClaimsIdentity(ClaimsIssuer);
            var properties = new AuthenticationProperties();

            AuthenticationTicket ticket;
            try
            {
                ticket = await CreateTicketAsync(identity, properties, tokens);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[OAuth] Failed to create ticket");
                return HandleRequestResult.Fail(ex);
            }

            if (ticket != null)
            {
                Logger.LogInformation("[OAuth] Authentication ticket created successfully");
                return HandleRequestResult.Success(ticket);
            }
            else
            {
                Logger.LogError("[OAuth] Failed to create authentication ticket");
                return HandleRequestResult.Fail("Failed to create authentication ticket.");
            }
        }
    }
}