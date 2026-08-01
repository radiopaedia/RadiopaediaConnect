using FellowOakDicom;

namespace RadiopaediaConnect.Services
{
    /// <summary>
    /// Shared helper for expanding multiframe DICOM files into individual frame entries.
    /// Used by both CornerstoneController (metadata endpoint) and CaseProcessorService (upload pipeline)
    /// to guarantee identical frame ordering.
    ///
    /// Also owns sub-series detection: some PACS store several independent multiframe
    /// acquisitions under one SeriesInstanceUID (biplane angio being the common case —
    /// plane A and plane B share a series and are told apart by ImageType / DetectorID).
    /// Uploading those as one series stitches unrelated runs into a single stack, so the
    /// picker splits them and the pipeline treats each group as its own series.
    /// </summary>
    public static class DicomFrameExpander
    {
        public class FileInfo
        {
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int InstanceNumber { get; set; }
            public int NumberOfFrames { get; set; }
            public string SopInstanceUid { get; set; } = string.Empty;

            /// <summary>(0008,0008) ImageType. For Siemens biplane the 3rd value is "BIPLANE A"/"BIPLANE B".</summary>
            public string[] ImageType { get; set; } = Array.Empty<string>();

            /// <summary>(0018,700A) DetectorID — one value per physical detector/plane.</summary>
            public string DetectorId { get; set; } = string.Empty;
        }

        public class ExpandedFrame
        {
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int FrameIndex { get; set; }
        }

        /// <summary>
        /// One independently-uploadable group of instances within a single SeriesInstanceUID.
        /// </summary>
        public class SubSeries
        {
            /// <summary>Stable identifier for this group. Used for storage paths and to seed the
            /// anonymised SeriesInstanceUID so Radiopaedia keeps the groups apart.</summary>
            public string Key { get; set; } = string.Empty;

            /// <summary>Human-readable suffix shown in the picker, e.g. "BIPLANE A".</summary>
            public string Label { get; set; } = string.Empty;

            public List<string> SopInstanceUids { get; set; } = new();
            public List<string> FileNames { get; set; } = new();
            public int FrameCount { get; set; }
            public bool HasMultiframe { get; set; }
        }

        /// <summary>Key used for the group holding all single-frame instances of a split series.</summary>
        public const string SingleFrameGroupKey = "singleframe";

        /// <summary>
        /// Scans DICOM files and extracts the metadata needed for frame expansion and
        /// sub-series detection. Files are returned sorted by InstanceNumber.
        /// </summary>
        public static async Task<List<FileInfo>> ScanFilesAsync(string[] filePaths)
        {
            var fileInfos = new List<FileInfo>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var dicomFile = await DicomFile.OpenAsync(filePath, FileReadOption.SkipLargeTags);
                    var dataset = dicomFile.Dataset;

                    fileInfos.Add(new FileInfo
                    {
                        FileName = Path.GetFileName(filePath),
                        FilePath = filePath,
                        InstanceNumber = dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0),
                        NumberOfFrames = dataset.GetValueOrDefault(DicomTag.NumberOfFrames, 0, 1),
                        SopInstanceUid = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty),
                        ImageType = dataset.TryGetValues<string>(DicomTag.ImageType, out var it)
                            ? it
                            : Array.Empty<string>(),
                        DetectorId = dataset.GetSingleValueOrDefault(DicomTag.DetectorID, string.Empty).Trim()
                    });
                }
                catch
                {
                    // Skip files that can't be read (non-image DICOM objects, corrupt files)
                }
            }

            return fileInfos.OrderBy(f => f.InstanceNumber).ToList();
        }

        /// <summary>
        /// Expands file infos into a flat list of (file, frameIndex) entries.
        /// Single-frame files produce one entry; multiframe files produce N entries.
        /// Order: sorted by InstanceNumber, then FrameIndex within each file.
        /// </summary>
        public static List<ExpandedFrame> ExpandFrames(List<FileInfo> fileInfos)
        {
            var expanded = new List<ExpandedFrame>();

            foreach (var file in fileInfos)
            {
                for (int f = 0; f < file.NumberOfFrames; f++)
                {
                    expanded.Add(new ExpandedFrame
                    {
                        FileName = file.FileName,
                        FilePath = file.FilePath,
                        FrameIndex = f
                    });
                }
            }

            return expanded;
        }

        /// <summary>
        /// Returns true when a series holds more than one multiframe instance — i.e. several
        /// independent acquisitions ("panes") stored under one SeriesInstanceUID.
        /// </summary>
        public static bool CanSplit(List<FileInfo> fileInfos) =>
            fileInfos.Count(f => f.NumberOfFrames > 1) > 1;

        /// <summary>
        /// Splits a series into independently-uploadable groups: one per multiframe instance,
        /// plus a single combined group for any single-frame instances. Returns an empty list
        /// when the series holds at most one multiframe instance (nothing to split).
        /// </summary>
        public static List<SubSeries> BuildSubSeries(List<FileInfo> fileInfos)
        {
            if (!CanSplit(fileInfos)) return new List<SubSeries>();

            var multiframe = fileInfos.Where(f => f.NumberOfFrames > 1).ToList();
            var singleFrame = fileInfos.Where(f => f.NumberOfFrames <= 1).ToList();

            var labeller = BuildLabeller(multiframe);

            var groups = multiframe
                .Select(f => new SubSeries
                {
                    Key = f.SopInstanceUid,
                    Label = labeller(f),
                    SopInstanceUids = new List<string> { f.SopInstanceUid },
                    FileNames = new List<string> { f.FileName },
                    FrameCount = f.NumberOfFrames,
                    HasMultiframe = true
                })
                .ToList();

            if (singleFrame.Count > 0)
            {
                groups.Add(new SubSeries
                {
                    Key = SingleFrameGroupKey,
                    Label = "Single-frame images",
                    SopInstanceUids = singleFrame.Select(f => f.SopInstanceUid).ToList(),
                    FileNames = singleFrame.Select(f => f.FileName).ToList(),
                    FrameCount = singleFrame.Count,
                    HasMultiframe = false
                });
            }

            return groups;
        }

        /// <summary>
        /// Picks the most meaningful label source for a set of instances. ImageType's 3rd value
        /// is preferred (Siemens writes "BIPLANE A"/"BIPLANE B" there), then DetectorID, then
        /// the instance number as a last resort. A source is only used when it is present on
        /// every instance and unique across them — otherwise it cannot tell the panes apart.
        /// </summary>
        private static Func<FileInfo, string> BuildLabeller(List<FileInfo> files)
        {
            if (IsDistinctAndPresent(files, ImageTypeLabel))
                return ImageTypeLabel;

            if (IsDistinctAndPresent(files, f => f.DetectorId))
                return f => $"Detector {f.DetectorId}";

            return f => $"Image {f.InstanceNumber}";
        }

        private static string ImageTypeLabel(FileInfo f) =>
            f.ImageType.Length >= 3 ? f.ImageType[2].Trim() : string.Empty;

        private static bool IsDistinctAndPresent(List<FileInfo> files, Func<FileInfo, string> selector)
        {
            var values = files.Select(selector).ToList();
            if (values.Any(string.IsNullOrWhiteSpace)) return false;
            return values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == files.Count;
        }

        /// <summary>
        /// Re-expresses a frame window that was chosen against the whole series so it applies to
        /// one part of it. The window the user picked counts positions in the flattened frame list
        /// across every instance, so splitting the series renumbers those positions.
        ///
        /// Returns null when the window selects nothing from this part.
        /// </summary>
        /// <remarks>
        /// A multiframe part occupies a contiguous run of the flattened list, so intersecting it
        /// with the user's arithmetic selection always yields another arithmetic sequence and the
        /// mapping is exact. The combined single-frame part can be scattered, and there the
        /// selection may not be uniform — the window is then widened to the enclosing contiguous
        /// range so frames are added rather than silently dropped.
        /// </remarks>
        public static (int Start, int End, int Step)? MapWindowToSubSeries(
            List<FileInfo> allFiles, SubSeries sub, int start, int end, int step)
        {
            var expanded = ExpandFrames(allFiles);
            if (step < 1) step = 1;

            var wanted = new HashSet<int>();
            for (int i = Math.Max(0, start - 1); i <= end - 1 && i < expanded.Count; i += step)
                wanted.Add(i);

            var subFiles = new HashSet<string>(sub.FileNames, StringComparer.Ordinal);

            // Walk the flattened list, tracking each frame's position within this part
            var local = new List<int>();
            int rank = 0;
            for (int g = 0; g < expanded.Count; g++)
            {
                if (!subFiles.Contains(expanded[g].FileName)) continue;
                if (wanted.Contains(g)) local.Add(rank);
                rank++;
            }

            if (local.Count == 0) return null;

            int localStep = local.Count > 1 ? local[1] - local[0] : 1;
            for (int i = 1; i < local.Count; i++)
            {
                if (local[i] - local[i - 1] != localStep)
                {
                    localStep = 1;   // not uniform — widen to the enclosing range
                    break;
                }
            }

            return (local[0] + 1, local[^1] + 1, localStep);
        }

        /// <summary>
        /// Narrows a scanned file list to the instances belonging to one sub-series.
        /// An empty <paramref name="sopInstanceUids"/> means "the whole series" — the
        /// behaviour for every series that was never split.
        /// </summary>
        public static List<FileInfo> FilterToSubSeries(
            List<FileInfo> fileInfos, IReadOnlyCollection<string>? sopInstanceUids)
        {
            if (sopInstanceUids == null || sopInstanceUids.Count == 0) return fileInfos;

            var wanted = new HashSet<string>(sopInstanceUids, StringComparer.Ordinal);
            return fileInfos.Where(f => wanted.Contains(f.SopInstanceUid)).ToList();
        }
    }
}
