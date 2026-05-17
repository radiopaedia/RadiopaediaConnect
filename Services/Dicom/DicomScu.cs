using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using RadiopaediaConnect.Models;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Services.Dicom
{
    public class DicomScu
    {
        private readonly SettingsService _settingsService;
        private readonly ILogger<DicomScu> _logger;

        private readonly string[] _excludedModalities =
        {
            "NULL", "",
            "DOC", "SC", "SR", "PR", "RTSTRUCT", "RTPLAN", "RTDOSE", "RTIMAGE"
        };

        public DicomScu(SettingsService settingsService, ILogger<DicomScu> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        private async Task<DicomSettings> GetSettingsAsync() =>
            await _settingsService.GetDicomSettingsAsync();

        public async Task<List<DicomStudyDto>> FindStudiesAsync(DicomSearchCriteria criteria)
        {
            var results = new List<DicomStudyDto>();
            var nodeName = criteria.RemoteNodeName;
            var settings = await GetSettingsAsync();

            var remoteNode = settings.RemoteNodes.FirstOrDefault(n => n.Name.Equals(nodeName, StringComparison.OrdinalIgnoreCase));
            if (remoteNode == null)
            {
                _logger.LogError($"Remote DICOM node '{nodeName}' not found.");
                return results;
            }

            var callingAe = remoteNode.CallingAe ?? "RCONNECT_SCU";

            var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

            request.Dataset.AddOrUpdate(DicomTag.PatientID, string.IsNullOrEmpty(criteria.PatientId) ? "" : criteria.PatientId);

            if (!string.IsNullOrEmpty(criteria.PatientName))
            {
                var searchName = criteria.PatientName.Contains("*") ? criteria.PatientName : $"*{criteria.PatientName}*";
                request.Dataset.AddOrUpdate(DicomTag.PatientName, searchName);
            }
            else
            {
                request.Dataset.AddOrUpdate(DicomTag.PatientName, "");
            }

            request.Dataset.AddOrUpdate(DicomTag.AccessionNumber, string.IsNullOrEmpty(criteria.AccessionNumber) ? "" : criteria.AccessionNumber);

            if (criteria.DateFrom.HasValue || criteria.DateTo.HasValue)
            {
                var rangeString = $"{(criteria.DateFrom?.ToString("yyyyMMdd") ?? "")}-{(criteria.DateTo?.ToString("yyyyMMdd") ?? "")}";
                request.Dataset.AddOrUpdate(DicomTag.StudyDate, rangeString);
            }
            else
            {
                request.Dataset.AddOrUpdate(DicomTag.StudyDate, "");
            }

            request.Dataset.AddOrUpdate(DicomTag.StudyTime, "");
            request.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, "");
            request.Dataset.AddOrUpdate(DicomTag.StudyDescription, "");
            request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");
            request.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedInstances, "");
            request.Dataset.AddOrUpdate(DicomTag.PatientSex, "");
            request.Dataset.AddOrUpdate(DicomTag.PatientAge, "");
            request.Dataset.AddOrUpdate(DicomTag.PatientBirthDate, "");

            request.OnResponseReceived = (req, response) =>
            {
                if (response.Status == DicomStatus.Success || response.Status == DicomStatus.Pending)
                {
                    if (response.HasDataset)
                    {
                        results.Add(MapToDto(response.Dataset, nodeName));
                    }
                }
            };

            var client = DicomClientFactory.Create(remoteNode.Host, remoteNode.Port, false, callingAe, remoteNode.AeTitle);

            try
            {
                await client.AddRequestAsync(request);
                await client.SendAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[C-FIND] Study search failed on {NodeName}", nodeName);
            }

            _logger.LogInformation("[C-FIND] Study search on {NodeName} returned {Count} result(s)", nodeName, results.Count);
            return results.OrderByDescending(r => r.StudyDate).ToList();
        }

        public async Task<List<DicomSeriesDto>> FindSeriesAsync(string studyInstanceUid, string nodeName)
        {
            var results = new List<DicomSeriesDto>();
            var settings = await GetSettingsAsync();

            var remoteNode = settings.RemoteNodes.FirstOrDefault(n => n.Name.Equals(nodeName, StringComparison.OrdinalIgnoreCase));
            if (remoteNode == null) return results;

            var callingAe = remoteNode.CallingAe ?? "RCONNECT_SCU";
            var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Series);

            request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyInstanceUid);
            request.Dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, "");
            request.Dataset.AddOrUpdate(DicomTag.Modality, "");
            request.Dataset.AddOrUpdate(DicomTag.SeriesDescription, "");
            request.Dataset.AddOrUpdate(DicomTag.SeriesNumber, "");
            request.Dataset.AddOrUpdate(DicomTag.NumberOfSeriesRelatedInstances, "");

            request.OnResponseReceived = (req, response) =>
            {
                if ((response.Status == DicomStatus.Success || response.Status == DicomStatus.Pending) && response.HasDataset)
                {
                    var result = MapToSeriesDto(response.Dataset, studyInstanceUid, nodeName);
                    if (!_excludedModalities.Contains(result.Modality))
                        results.Add(result);
                }
            };

            var client = DicomClientFactory.Create(remoteNode.Host, remoteNode.Port, false, callingAe, remoteNode.AeTitle);

            try
            {
                await client.AddRequestAsync(request);
                await client.SendAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[C-FIND] Series search failed on {NodeName}", nodeName);
            }

            _logger.LogInformation("[C-FIND] Series search for study {StudyUid} on {NodeName} returned {Count} series", studyInstanceUid, nodeName, results.Count);
            return results.OrderBy(r => r.SeriesNumber).ToList();
        }

        private DicomStudyDto MapToDto(DicomDataset ds, string nodeName)
        {
            return new DicomStudyDto
            {
                StudyInstanceUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty),
                PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty),
                PatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty),
                PatientBirthDate = ds.GetSingleValueOrDefault(DicomTag.PatientBirthDate, new DateTime()),
                AccessionNumber = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty),
                StudyDate = ParseDicomDate(ds),
                Modality = GetModalitiesFromDataset(ds),
                StudyDescription = ds.GetSingleValueOrDefault(DicomTag.StudyDescription, string.Empty),
                InstanceCount = ds.GetSingleValueOrDefault(DicomTag.NumberOfStudyRelatedInstances, 0),
                PatientSex = ds.GetSingleValueOrDefault(DicomTag.PatientSex, "O"),
                PatientAge = GetOrCalculateDetailedAge(ds),
                RemoteNodeName = nodeName
            };
        }

        private DicomSeriesDto MapToSeriesDto(DicomDataset ds, string studyUid, string nodeName)
        {
            return new DicomSeriesDto
            {
                StudyInstanceUid = studyUid,
                SeriesInstanceUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty),
                Modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "UNK"),
                SeriesDescription = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, string.Empty),
                SeriesNumber = ds.GetSingleValueOrDefault(DicomTag.SeriesNumber, 0),
                InstanceCount = ds.GetSingleValueOrDefault(DicomTag.NumberOfSeriesRelatedInstances, 0),
                RemoteNodeName = nodeName
            };
        }

        private static string GetModalitiesFromDataset(DicomDataset ds)
        {
            try
            {
                if (ds.Contains(DicomTag.ModalitiesInStudy))
                {
                    var excludedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NULL", "",
                        "DOC", "SC", "SR", "PR", "RTSTRUCT", "RTPLAN", "RTDOSE", "RTIMAGE" };
                    var vals = ds.GetValues<string>(DicomTag.ModalitiesInStudy);
                    return vals == null ? string.Empty : string.Join(",", vals.Where(s => !string.IsNullOrWhiteSpace(s) && !excludedValues.Contains(s)));
                }

                if (ds.Contains(DicomTag.Modality))
                {
                    return ds.GetSingleValueOrDefault(DicomTag.Modality, string.Empty);
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private DateTime? ParseDicomDate(DicomDataset ds)
        {
            if (ds.TryGetSingleValue(DicomTag.StudyDate, out DateTime date))
            {
                if (ds.TryGetSingleValue(DicomTag.StudyTime, out DateTime time)) return date.Date + time.TimeOfDay;
                return date;
            }
            return null;
        }

        private string GetOrCalculateDetailedAge(DicomDataset ds)
        {
            if (ds.TryGetSingleValue(DicomTag.PatientBirthDate, out DateTime dob) && ds.TryGetSingleValue(DicomTag.StudyDate, out DateTime studyDate))
            {
                int months = ((studyDate.Year - dob.Year) * 12) + studyDate.Month - dob.Month;
                if (studyDate.Day < dob.Day) months--;

                if (months < 0) months = 0;

                int y = months / 12;
                int m = months % 12;

                if (y > 0)
                {
                    return m > 0 ? $"{y} years, {m} months" : $"{y} years";
                }
                return $"{m} months";
            }

            if (ds.TryGetString(DicomTag.PatientAge, out var age) && !string.IsNullOrWhiteSpace(age))
            {
                return age;
            }

            return string.Empty;
        }

        public async Task<bool> TriggerCMoveAsync(string studyInstanceUid, string seriesInstanceUid, string remoteNodeName)
        {
            var settings = await GetSettingsAsync();

            var remoteNode = settings.RemoteNodes.FirstOrDefault(n => n.Name.Equals(remoteNodeName, StringComparison.OrdinalIgnoreCase));
            if (remoteNode == null)
            {
                _logger.LogError($"[C-MOVE] Configuration for remote node '{remoteNodeName}' not found.");
                return false;
            }

            var callingAe = remoteNode.CallingAe ?? "RCONNECT_SCU";
            var destinationAe = settings.Scp.AeTitle;

            try
            {
                DicomCMoveRequest request;

                if (string.IsNullOrEmpty(seriesInstanceUid))
                {
                    request = new DicomCMoveRequest(destinationAe, studyInstanceUid);
                    _logger.LogInformation($"[C-MOVE] Requesting STUDY {studyInstanceUid} from {remoteNode.AeTitle} -> {destinationAe}");
                }
                else
                {
                    request = new DicomCMoveRequest(destinationAe, studyInstanceUid, seriesInstanceUid);
                    _logger.LogInformation($"[C-MOVE] Requesting SERIES {seriesInstanceUid} from {remoteNode.AeTitle} -> {destinationAe}");
                }

                request.OnResponseReceived = (req, response) =>
                {
                    if (response.Status.State != DicomState.Pending && response.Status.State != DicomState.Success)
                    {
                        _logger.LogWarning($"[C-MOVE] PACS Status: {response.Status}");
                    }
                };

                var client = DicomClientFactory.Create(remoteNode.Host, remoteNode.Port, false, callingAe, remoteNode.AeTitle);
                await client.AddRequestAsync(request);
                await client.SendAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[C-MOVE] Request Failed.");
                return false;
            }
        }
    }
}