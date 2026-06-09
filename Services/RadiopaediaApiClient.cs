using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

            _logger.LogInformation("[API] Creating Case '{Title}' for user {Username}...", draft.Title, username);
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync("cases", content);
            sw.Stop();
            var respString = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[API] POST cases completed in {Ms}ms -> {Status}", sw.ElapsedMilliseconds, (int)response.StatusCode);

            if ((int)response.StatusCode == 429)
                _logger.LogWarning("[API] Rate limited (429) by Radiopaedia on CreateCase");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[API] Create Case Failed: {StatusCode} - {Response}", response.StatusCode, respString);
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

            _logger.LogInformation("[API] Creating Study ({Modality}) for Case {CaseId}...", studyDto.Modality, radiopaediaCaseId);

            var sw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync($"cases/{radiopaediaCaseId}/studies", content);
            sw.Stop();
            var respString = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("[API] POST studies completed in {Ms}ms -> {Status}", sw.ElapsedMilliseconds, (int)response.StatusCode);

            if ((int)response.StatusCode == 429)
                _logger.LogWarning("[API] Rate limited (429) by Radiopaedia on CreateStudy");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[API] Create Study Failed: {Response}", respString);
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

            var sw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync($"cases/{caseId}/studies/{studyId}/images", content);
            sw.Stop();
            _logger.LogInformation("[API] POST images completed in {Ms}ms -> {Status}", sw.ElapsedMilliseconds, (int)response.StatusCode);

            if ((int)response.StatusCode == 429)
                _logger.LogWarning("[API] Rate limited (429) by Radiopaedia on UploadZip");

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("[API] Upload Failed: {Error}", err);
                throw new Exception($"Zip Upload Failed: {response.StatusCode}");
            }

            _logger.LogInformation("[API] Zip upload successful for Case {CaseId} / Study {StudyId}", caseId, studyId);
        }

        /// <summary>
        /// Uploads a set of anonymized DICOM files to Radiopaedia via the S3 large-file pipeline.
        /// See: https://radiopaedia.org/api-documentation#upload-large-file
        ///
        /// Per-file pipeline (hash → presigned URL → PUT) to keep the S3 URL fresh:
        ///
        /// For each file:
        ///   Step 1 — POST /direct_s3_uploads  { sha256: [hash] }
        ///             → { id, url, status? }
        ///             Files with status "already_uploaded" skip the PUT.
        ///   Step 2 — PUT presigned S3 URL  (binary DICOM, Content-Type: application/dicom)
        ///
        /// Step 3 — POST /image_preparation/{caseId}/studies/{studyId}/series
        ///           { image_format, series: { root_index }, stack_upload: { uploaded_data: [id…] } }
        ///           Attaches all uploaded files to the study as a single series.
        /// </summary>
        public async Task UploadDicomSeriesAsync(
            string caseId, string studyId, IReadOnlyList<string> dicomFilePaths, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            _logger.LogInformation("[API] S3 upload: {Count} DICOM file(s) → Case {CaseId} / Study {StudyId}",
                dicomFilePaths.Count, caseId, studyId);

            // Use a separate HttpClient — S3 is a different host and must not receive
            // the Radiopaedia Authorization header.
            using var s3Client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            // ── Step 1: Hash all files and request presigned URLs in one batch call ────────
            // Presigned URLs are valid for 900s. With 4 concurrent PUTs the total upload time
            // for large series is well within that window.
            var sw = Stopwatch.StartNew();

            var hashes = new List<string>(dicomFilePaths.Count);
            foreach (var filePath in dicomFilePaths)
            {
                await using var fs = File.OpenRead(filePath);
                var hashBytes = await SHA256.HashDataAsync(fs);
                hashes.Add(Convert.ToHexString(hashBytes).ToLowerInvariant());
            }

            var initPayload = new { sha256 = hashes.ToArray() };
            var initContent = new StringContent(
                JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");

            sw.Restart();
            // Note: /direct_s3_uploads is at root, not under /api/v1/ — use absolute URL.
            var initResponse = await _httpClient.PostAsync(
                "https://radiopaedia.org/direct_s3_uploads", initContent);
            sw.Stop();

            var initBody = await initResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("[API] direct_s3_uploads ({Count} files) {Ms}ms → {Status}",
                dicomFilePaths.Count, sw.ElapsedMilliseconds, (int)initResponse.StatusCode);

            if (!initResponse.IsSuccessStatusCode)
            {
                _logger.LogError("[API] direct_s3_uploads failed: {Body}", initBody);
                throw new Exception($"S3 upload URL request failed: {initResponse.StatusCode}");
            }

            using var initDoc = JsonDocument.Parse(initBody);
            var uploadsArray = initDoc.RootElement.GetProperty("uploads");

            if (uploadsArray.GetArrayLength() != dicomFilePaths.Count)
                throw new Exception(
                    $"direct_s3_uploads returned {uploadsArray.GetArrayLength()} entries for {dicomFilePaths.Count} files");

            // ── Step 2: PUT files concurrently (max 4 in flight) ─────────────────────────
            // Results are keyed by index to preserve order for the attach step.
            const int MaxConcurrency = 4;
            using var semaphore = new SemaphoreSlim(MaxConcurrency);

            var uploadedIds = new long[dicomFilePaths.Count];
            var putTasks = new List<Task>(dicomFilePaths.Count);
            // Capture uploads array as cloned elements before the JsonDocument is disposed.
            var uploadEntries = Enumerable.Range(0, dicomFilePaths.Count)
                .Select(i => uploadsArray[i].Clone())
                .ToList();

            for (int i = 0; i < dicomFilePaths.Count; i++)
            {
                var idx = i;
                var filePath = dicomFilePaths[idx];
                var fileName = Path.GetFileName(filePath);
                var upload = uploadEntries[idx];
                var uploadId = upload.GetProperty("id").GetInt64();
                uploadedIds[idx] = uploadId;

                bool alreadyUploaded =
                    upload.TryGetProperty("status", out var statusEl) &&
                    statusEl.GetString() == "already_uploaded";

                if (alreadyUploaded)
                {
                    _logger.LogInformation("[API] {File} already on S3 (id={Id}), skipping PUT", fileName, uploadId);
                    continue;
                }

                var presignedUrl = upload.GetProperty("url").GetString()!;

                putTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await using var fileStream = File.OpenRead(filePath);
                        using var fileContent = new StreamContent(fileStream);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/dicom");

                        var putSw = Stopwatch.StartNew();
                        var s3Response = await s3Client.PutAsync(presignedUrl, fileContent);
                        putSw.Stop();

                        _logger.LogInformation("[API] S3 PUT {File} (id={Id}) → {Status} ({Ms}ms)",
                            fileName, uploadId, (int)s3Response.StatusCode, putSw.ElapsedMilliseconds);

                        if (!s3Response.IsSuccessStatusCode)
                        {
                            var s3Err = await s3Response.Content.ReadAsStringAsync();
                            _logger.LogError("[API] S3 PUT failed for {File}: {Body}", fileName, s3Err);
                            throw new Exception($"S3 PUT failed for {fileName}: {s3Response.StatusCode}");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(putTasks);
            var uploadedIds_ordered = uploadedIds.ToList();

            // ── Step 3: Attach all uploaded files to the study as a series ───────────────
            // root_index is 0-based: the API docs show root_index:1 for a 3-file upload
            // (middle frame). Math.Max(1,…) was wrong — for a single file the only valid
            // index is 0, and sending 1 causes the viewer to reference a non-existent frame.
            int rootIndex = uploadedIds_ordered.Count > 1 ? uploadedIds_ordered.Count / 2 : 0;

            var attachPayload = new
            {
                image_format = "application/dicom",
                series = new { root_index = rootIndex },
                stack_upload = new { uploaded_data = uploadedIds_ordered.ToArray() }
            };

            var attachJson = JsonSerializer.Serialize(attachPayload);
            _logger.LogInformation("[API] image_preparation payload: {Json}", attachJson);

            var attachContent = new StringContent(attachJson, Encoding.UTF8, "application/json");

            sw.Restart();
            // Note: /image_preparation is also at root, not under /api/v1/.
            var attachUrl = $"https://radiopaedia.org/image_preparation/{caseId}/studies/{studyId}/series";
            _logger.LogInformation("[API] POST {Url}", attachUrl);
            var attachResponse = await _httpClient.PostAsync(attachUrl, attachContent);
            sw.Stop();

            var attachBody = await attachResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("[API] image_preparation/series {Ms}ms → {Status} | body: {Body}",
                sw.ElapsedMilliseconds, (int)attachResponse.StatusCode, attachBody);

            if (!attachResponse.IsSuccessStatusCode)
            {
                _logger.LogError("[API] Attach to study failed: {Body}", attachBody);
                throw new Exception($"Attach DICOM series to study failed: {attachResponse.StatusCode}");
            }

            _logger.LogInformation(
                "[API] {Count} DICOM file(s) attached to Case {CaseId} / Study {StudyId}",
                uploadedIds_ordered.Count, caseId, studyId);
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

        public async Task<JsonElement?> GetCaseOriginalsAsync(string radiopaediaCaseId, string username)
        {
            var token = await _authService.GetValidAccessTokenAsync(username);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.GetAsync($"cases/{radiopaediaCaseId}/originals");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[API] GetCaseOriginals failed for case {CaseId}: {Status} {Body}",
                    radiopaediaCaseId, (int)response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

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

    public class UserQuotaDto
    {
        public int Current { get; set; }
        public int Maximum { get; set; }
    }
}