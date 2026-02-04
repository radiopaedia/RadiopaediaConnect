using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Data;

namespace RadiopaediaConnect.Services
{
    public class RadiopaediaApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IOAuthService _authService;
        private readonly ILogger<RadiopaediaApiClient> _logger;
        private readonly DicomRepository _repository;

        public RadiopaediaApiClient(
            HttpClient httpClient,
            IOAuthService authService,
            DicomRepository repository,
            ILogger<RadiopaediaApiClient> logger)
        {
            _httpClient = httpClient;
            _authService = authService;
            _repository = repository;
            _logger = logger;

            _httpClient.BaseAddress = new Uri("https://radiopaedia.org/api/v1/");
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public async Task<string> CreateCaseAsync(DraftCase draft, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            int? systemId = draft.System > 0 ? draft.System : null;
            int? certaintyId = draft.DiagnosticCertainty > 0 ? draft.DiagnosticCertainty : null;

            string? gender = null;
            if (!string.IsNullOrWhiteSpace(draft.Sex))
            {
                gender = char.ToUpper(draft.Sex[0]) + draft.Sex.Substring(1).ToLower();
            }

            var payload = new
            {
                title = draft.Title,
                presentation = draft.Presentation,
                system_id = systemId,
                diagnostic_certainty_id = certaintyId,
                age = draft.Age,
                gender = gender,
                body = draft.CaseDiscussion
            };

            var json = JsonSerializer.Serialize(payload);
            _logger.LogInformation($"[API-DEBUG] Sending Payload: {json}");

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation($"[API] Creating Case '{draft.Title}' for user {username}...");
            var response = await _httpClient.PostAsync("cases", content);
            var respString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"[API] Create Case Failed: {response.StatusCode} - {respString}");
                throw new HttpRequestException($"Radiopaedia API Error: {response.ReasonPhrase}");
            }

            using var doc = JsonDocument.Parse(respString);
            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.ToString();
            }
            throw new Exception("API response did not contain a Case ID.");
        }

        public async Task<string> CreateStudyAsync(string radiopaediaCaseId, SubmitCaseStudyDto studyDto, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                modality = studyDto.Modality,
                findings = studyDto.Findings,
                study_date = DateTime.UtcNow.ToString("yyyy-MM-dd")
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation($"[API] Creating Study ({studyDto.Modality}) for Case {radiopaediaCaseId}...");

            var response = await _httpClient.PostAsync($"cases/{radiopaediaCaseId}/studies", content);
            var respString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"[API] Create Study Failed: {respString}");
                throw new HttpRequestException($"Radiopaedia API Error: {response.ReasonPhrase}");
            }

            using var doc = JsonDocument.Parse(respString);
            if (doc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.ToString();
            }
            throw new Exception("API response did not contain a Study ID.");
        }

        public async Task UploadStudyZipAsync(string caseId, string studyId, string zipFilePath, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (!File.Exists(zipFilePath)) throw new FileNotFoundException($"Zip file not found: {zipFilePath}");

            var fileName = Path.GetFileName(zipFilePath);
            _logger.LogInformation($"[API] Uploading ZIP '{fileName}' to Case {caseId} / Study {studyId}...");

            using var fileStream = File.OpenRead(zipFilePath);
            using var content = new StreamContent(fileStream);

            content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = fileName
            };

            var response = await _httpClient.PostAsync($"cases/{caseId}/studies/{studyId}/images", content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError($"[API] Upload Failed: {err}");
                throw new Exception($"Zip Upload Failed: {response.StatusCode}");
            }

            _logger.LogInformation("[API] Zip Upload Successful!");
        }

        public async Task MarkUploadFinishedAsync(string caseId, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync($"cases/{caseId}/mark_upload_finished", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"[API] Failed to mark upload finished for Case {caseId}. Status: {response.StatusCode}");
            }
            else
            {
                _logger.LogInformation($"[API] Case {caseId} marked as 'Upload Finished'.");
            }
        }

        /// <summary>
        /// Gets the current user's draft case quota from Radiopaedia API
        /// </summary>
        public async Task<UserQuotaDto?> GetUserQuotaAsync(string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            _logger.LogInformation($"[API] Fetching user quota for {username}...");

            var response = await _httpClient.GetAsync("users/current");
            var respString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"[API] Get User Quota Failed: {response.StatusCode} - {respString}");
                return null;
            }

            using var doc = JsonDocument.Parse(respString);
            var root = doc.RootElement;

            // Parse the quotas object from the user response
            if (root.TryGetProperty("quotas", out var quotas))
            {
                return new UserQuotaDto
                {
                    Current = quotas.TryGetProperty("draft_case_count", out var current) ? current.GetInt32() : 0,
                    Maximum = quotas.TryGetProperty("allowed_draft_cases", out var maximum) ? maximum.GetInt32() : 0
                };
            }

            return null;
        }
    }

    /// <summary>
    /// DTO for user quota information
    /// </summary>
    public class UserQuotaDto
    {
        public int Current { get; set; }
        public int Maximum { get; set; }
    }
}