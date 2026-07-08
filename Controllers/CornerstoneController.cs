using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;
using Microsoft.AspNetCore.Mvc;
using RadiopaediaConnect.Data;
using RadiopaediaConnect.Services;

namespace RadiopaediaConnect.Controllers
{
    [ApiController]
    [Route("api/cornerstone")]
    public class CornerstoneController : ControllerBase
    {
        private readonly DicomRepository _repository;

        public CornerstoneController(DicomRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Returns sorted wadouri: URLs for legacy WADO image loader (kept for backward compatibility).
        /// </summary>
        [HttpGet("series/{seriesUid}")]
        public async Task<IActionResult> GetSeriesFiles(string seriesUid)
        {
            var series = await _repository.GetSeriesAsync(seriesUid);
            if (series == null || !series.IsRetrieved)
                return NotFound("Series not found or not yet retrieved.");

            if (!Directory.Exists(series.StoragePath))
                return NotFound("DICOM directory not found on disk.");

            var files = Directory.GetFiles(series.StoragePath, "*.dcm");
            var dicomList = new List<DicomSortableItem>();

            foreach (var file in files)
            {
                try
                {
                    var dicomFile = await DicomFile.OpenAsync(file, FileReadOption.SkipLargeTags);
                    var dataset = dicomFile.Dataset;

                    int numberOfFrames = dataset.GetValueOrDefault(DicomTag.NumberOfFrames, 0, 1);
                    string fileName = Path.GetFileName(file);

                    for (int i = 0; i < numberOfFrames; i++)
                    {
                        double sliceLocation = CalculateSliceLocation(dataset);

                        dicomList.Add(new DicomSortableItem
                        {
                            FileName = fileName,
                            FrameIndex = i,
                            Distance = sliceLocation,
                            InstanceNumber = dataset.GetValueOrDefault(DicomTag.InstanceNumber, 0, 0)
                        });
                    }
                }
                catch
                {
                }
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}/api/cornerstone/image";

            var sortedUrls = dicomList
                .OrderBy(x => x.InstanceNumber)
                .ThenBy(x => x.FrameIndex)
                .Select(x => $"wadouri:{baseUrl}?seriesUid={seriesUid}&filename={x.FileName}&frame={x.FrameIndex}")
                .ToList();

            return Ok(sortedUrls);
        }

        /// <summary>
        /// Returns structured metadata including multiframe info and expanded frame list.
        /// Used by the custom csdicom: image loader.
        /// </summary>
        [HttpGet("series/{seriesUid}/metadata")]
        public async Task<IActionResult> GetSeriesMetadata(string seriesUid)
        {
            var series = await _repository.GetSeriesAsync(seriesUid);
            if (series == null || !series.IsRetrieved)
                return NotFound("Series not found or not yet retrieved.");

            if (!Directory.Exists(series.StoragePath))
                return NotFound("DICOM directory not found on disk.");

            var dicomFiles = Directory.GetFiles(series.StoragePath, "*.dcm");
            var fileInfos = await DicomFrameExpander.ScanFilesAsync(dicomFiles);
            var expandedFrames = DicomFrameExpander.ExpandFrames(fileInfos);

            return Ok(new
            {
                seriesUid,
                totalFrameCount = expandedFrames.Count,
                hasMultiframe = fileInfos.Any(f => f.NumberOfFrames > 1),
                files = fileInfos.Select(f => new
                {
                    f.FileName,
                    f.InstanceNumber,
                    f.NumberOfFrames,
                    isMultiframe = f.NumberOfFrames > 1
                }),
                expandedFrames = expandedFrames.Select(f => new
                {
                    f.FileName,
                    f.FrameIndex
                })
            });
        }

        /// <summary>
        /// Serves raw DICOM file bytes with NO transcoding, NO frame extraction.
        /// The client handles all parsing and decoding.
        /// </summary>
        [HttpGet("raw")]
        public async Task<IActionResult> GetRawDicomFile([FromQuery] string seriesUid, [FromQuery] string filename)
        {
            var series = await _repository.GetSeriesAsync(seriesUid);
            if (series == null) return NotFound("Series not found");

            var filePath = ResolveSeriesFile(series.StoragePath, filename);
            if (filePath == null) return NotFound("File not found");

            return PhysicalFile(filePath, "application/octet-stream", filename);
        }

        /// <summary>
        /// Legacy endpoint: serves transcoded DICOM files for the WADO image loader.
        /// Kept for backward compatibility.
        /// </summary>
        [HttpGet("image")]
        public async Task<IActionResult> GetDicomFile([FromQuery] string seriesUid, [FromQuery] string filename, [FromQuery] int frame = 0)
        {
            var series = await _repository.GetSeriesAsync(seriesUid);
            if (series == null) return NotFound("Series not found");

            var filePath = ResolveSeriesFile(series.StoragePath, filename);
            if (filePath == null) return NotFound("File not found");

            try
            {
                var dicomFile = await DicomFile.OpenAsync(filePath);
                var finalFile = dicomFile;
                int totalFrames = dicomFile.Dataset.GetValueOrDefault(DicomTag.NumberOfFrames, 0, 1);

                if (totalFrames > 1)
                {
                    finalFile = ExtractSingleFrame(dicomFile, frame);
                }

                var uncompressedFile = finalFile.Clone(DicomTransferSyntax.ExplicitVRLittleEndian);

                var ms = new MemoryStream();
                await uncompressedFile.SaveAsync(ms);
                ms.Position = 0;

                return File(ms, "application/dicom", filename);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {filename}: {ex.Message}");
                return PhysicalFile(filePath, "application/dicom", filename);
            }
        }

        /// <summary>
        /// Resolves a user-supplied filename strictly to a .dcm file directly inside the
        /// series storage folder. Returns null for traversal attempts ("..", path separators,
        /// absolute paths) or missing files.
        /// </summary>
        private static string? ResolveSeriesFile(string storagePath, string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;
            if (filename != Path.GetFileName(filename)) return null;
            if (!filename.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase)) return null;

            var fullPath = Path.GetFullPath(Path.Combine(storagePath, filename));
            if (!fullPath.StartsWith(Path.GetFullPath(storagePath), StringComparison.OrdinalIgnoreCase))
                return null;

            return System.IO.File.Exists(fullPath) ? fullPath : null;
        }

        private DicomFile ExtractSingleFrame(DicomFile sourceFile, int frameIndex)
        {
            var sourceDataset = sourceFile.Dataset;
            var sourcePixelData = DicomPixelData.Create(sourceDataset);

            if (frameIndex < 0 || frameIndex >= sourcePixelData.NumberOfFrames)
            {
                throw new ArgumentOutOfRangeException(nameof(frameIndex), $"Frame {frameIndex} out of range.");
            }

            var newDataset = sourceDataset.Clone();
            newDataset.AddOrUpdate(DicomTag.NumberOfFrames, (ushort)1);

            var newPixelData = DicomPixelData.Create(newDataset, true);
            var frameBuffer = sourcePixelData.GetFrame(frameIndex);
            newPixelData.AddFrame(frameBuffer);

            return new DicomFile(newDataset);
        }

        private double CalculateSliceLocation(DicomDataset dataset)
        {
            if (dataset.Contains(DicomTag.ImagePositionPatient) &&
                dataset.Contains(DicomTag.ImageOrientationPatient))
            {
                var ipp = dataset.GetValues<double>(DicomTag.ImagePositionPatient);
                var iop = dataset.GetValues<double>(DicomTag.ImageOrientationPatient);

                double nx = iop[1] * iop[5] - iop[2] * iop[4];
                double ny = iop[2] * iop[3] - iop[0] * iop[5];
                double nz = iop[0] * iop[4] - iop[1] * iop[3];

                return (ipp[0] * nx) + (ipp[1] * ny) + (ipp[2] * nz);
            }

            return dataset.GetValueOrDefault(DicomTag.SliceLocation, 0, 0.0);
        }

        private class DicomSortableItem
        {
            public string FileName { get; set; }
            public int FrameIndex { get; set; }
            public double Distance { get; set; }
            public int InstanceNumber { get; set; }
        }
    }
}
