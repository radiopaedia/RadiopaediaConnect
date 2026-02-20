using System.Net.Http.Json;
using System.Text.Json;
using RadiopaediaConnect.Data;

namespace RadiopaediaConnect.Services
{
    public interface IOAuthService
    {
        Task<string> GetValidAccessTokenAsync(string username);
    }

    public class OAuthService : IOAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly UserRepository _userRepo;
        private readonly SettingsService _settingsService;

        public OAuthService(HttpClient httpClient, UserRepository userRepo, SettingsService settingsService)
        {
            _httpClient = httpClient;
            _userRepo = userRepo;
            _settingsService = settingsService;
        }

        public async Task<string> GetValidAccessTokenAsync(string username)
        {
            var user = await _userRepo.GetUserAsync(username);
            if (user == null) throw new UnauthorizedAccessException($"User {username} not found in database.");

            if (user.TokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return user.AccessToken;
            }

            var (clientId, clientSecret) = await _settingsService.GetRadiopaediaCredentialsAsync();

            var formData = new Dictionary<string, string>
            {
                { "client_id", clientId ?? "" },
                { "client_secret", clientSecret ?? "" },
                { "grant_type", "refresh_token" },
                { "refresh_token", user.RefreshToken }
            };

            var response = await _httpClient.PostAsync("https://radiopaedia.org/oauth/token", new FormUrlEncodedContent(formData));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                await _userRepo.LogEventAsync("TokenRefreshFailed", username, $"Status: {response.StatusCode} | Error: {errorContent}", "Error");
                throw new Exception("Failed to refresh Radiopaedia token.");
            }

            var tokenResp = await response.Content.ReadFromJsonAsync<JsonElement>();

            var newAccess = tokenResp.GetProperty("access_token").GetString();
            var newRefresh = tokenResp.GetProperty("refresh_token").GetString();
            var expiresIn = tokenResp.GetProperty("expires_in").GetInt32();

            user.AccessToken = newAccess;
            user.RefreshToken = newRefresh;
            user.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            user.LastUpdatedUtc = DateTime.UtcNow;

            await _userRepo.UpsertTokensAsync(user);
            await _userRepo.LogEventAsync("TokenRefresh", username, "Successfully refreshed token");

            return newAccess;
        }
    }
}