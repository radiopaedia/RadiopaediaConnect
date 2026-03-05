using FellowOakDicom;

namespace RadiopaediaConnect.Services
{
    /// <summary>
    /// Shared helper for expanding multiframe DICOM files into individual frame entries.
    /// Used by both CornerstoneController (metadata endpoint) and CaseProcessorService (upload pipeline)
    /// to guarantee identical frame ordering.
    /// </summary>
    public static class DicomFrameExpander
    {
        public class FileInfo
        {
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int InstanceNumber { get; set; }
            public int NumberOfFrames { get; set; }
        }

        public class ExpandedFrame
        {
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int FrameIndex { get; set; }
        }

        /// <summary>
        /// Scans DICOM files and extracts InstanceNumber + NumberOfFrames metadata.
        /// Files are returned sorted by InstanceNumber.
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
                        NumberOfFrames = dataset.GetValueOrDefault(DicomTag.NumberOfFrames, 0, 1)
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
    }
}
