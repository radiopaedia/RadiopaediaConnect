using FellowOakDicom;
using FellowOakDicom.Imaging;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// Raised when the frames of a multiframe instance cannot be lifted out individually, so
    /// the caller can fall back to the PNG pipeline instead of failing the whole upload.
    /// </summary>
    public class FrameSplitNotSupportedException : Exception
    {
        public FrameSplitNotSupportedException(string message) : base(message) { }
    }

    /// <summary>
    /// Explodes multiframe DICOM instances into one single-frame instance per frame.
    ///
    /// Radiopaedia's stack upload counts one uploaded file as one image in the stack and does
    /// not expand multiframe files server-side, so a cine run sent as a single instance shows
    /// only its first frame. Biplane DSA is where this bites hardest: the series is two
    /// multiframe files, so a run of dozens of frames arrives as two pictures.
    ///
    /// Frames are lifted out without decoding: the stored bytes of the frame are copied
    /// straight into the new instance and the source transfer syntax is kept. The pixels stay
    /// bit-identical and no codec has to be installed for the split to work.
    /// </summary>
    public static class DicomFrameSplitter
    {
        /// <summary>
        /// Transfer syntaxes that hold a video stream rather than independently stored frames.
        /// A single frame cannot be pulled out of these without re-encoding the whole run.
        /// </summary>
        private static readonly HashSet<string> VideoTransferSyntaxes = new(StringComparer.Ordinal)
        {
            DicomTransferSyntax.MPEG2.UID.UID,
            DicomTransferSyntax.MPEG2MainProfileHighLevel.UID.UID,
            DicomTransferSyntax.MPEG4AVCH264HighProfileLevel41.UID.UID,
            DicomTransferSyntax.MPEG4AVCH264BDCompatibleHighProfileLevel41.UID.UID,
            DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For2DVideo.UID.UID,
            DicomTransferSyntax.MPEG4AVCH264HighProfileLevel42For3DVideo.UID.UID,
            DicomTransferSyntax.MPEG4AVCH264StereoHighProfileLevel42.UID.UID,
            DicomTransferSyntax.HEVCH265MainProfileLevel51.UID.UID,
            DicomTransferSyntax.HEVCH265Main10ProfileLevel51.UID.UID
        };

        /// <summary>
        /// Writes each of <paramref name="frames"/> to <paramref name="outputDir"/> as its own
        /// single-frame instance. InstanceNumber is rewritten to the position in the list, so
        /// the stack keeps the order the user selected even when the frames came from several
        /// source instances. Returns the paths written, in that same order.
        /// </summary>
        /// <exception cref="FrameSplitNotSupportedException">
        /// A source instance stores its frames in a way that cannot be indexed.
        /// </exception>
        public static async Task<List<string>> StageFramesAsync(
            IReadOnlyList<DicomFrameExpander.ExpandedFrame> frames, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            // A part usually spans one or two source files, so keeping them open across the
            // whole run avoids re-parsing the header for every frame.
            var sources = new Dictionary<string, DicomFile>(StringComparer.OrdinalIgnoreCase);
            var written = new List<string>(frames.Count);

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];

                if (!sources.TryGetValue(frame.FilePath, out var source))
                {
                    source = await DicomFile.OpenAsync(frame.FilePath);
                    EnsureSplittable(source);
                    sources[frame.FilePath] = source;
                }

                var single = ExtractFrame(source, frame.FrameIndex, instanceNumber: i + 1);
                var path = Path.Combine(outputDir, $"{i + 1:D5}.dcm");
                await single.SaveAsync(path);
                written.Add(path);
            }

            return written;
        }

        /// <summary>
        /// Builds a standalone single-frame instance from one frame of <paramref name="source"/>.
        /// Every element except the pixel data is carried over unchanged; anonymisation still
        /// runs afterwards and decides what actually survives to the upload.
        /// </summary>
        public static DicomFile ExtractFrame(DicomFile source, int frameIndex, int instanceNumber)
        {
            var sourcePixels = DicomPixelData.Create(source.Dataset);
            var frameData = sourcePixels.GetFrame(frameIndex);

            // The source transfer syntax has to be carried to the new dataset before the pixel
            // data is written, because it decides whether the frame is stored encapsulated or
            // native. Validation is off: the values were already accepted once on the way in.
            var dataset = new DicomDataset(source.Dataset.InternalTransferSyntax).NotValidated();
            foreach (var item in source.Dataset)
            {
                if (item.Tag == DicomTag.PixelData) continue;
                dataset.Add(item);
            }

            // Each frame is now its own instance, so it needs its own identity and its own
            // place in the stack. The UID is derived from the source UID and the frame index
            // rather than generated, so re-running an upload produces the same instances
            // instead of a second copy of every frame.
            var sourceSopUid = source.Dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, string.Empty);
            if (string.IsNullOrWhiteSpace(sourceSopUid)) sourceSopUid = DicomUID.Generate().UID;

            var frameUid = DicomUidMap.HashedUid($"{sourceSopUid}::frame{frameIndex}");

            dataset.AddOrUpdate(DicomTag.SOPInstanceUID, frameUid);
            dataset.AddOrUpdate(DicomTag.InstanceNumber, instanceNumber);

            ApplyFrameTiming(dataset, frameIndex);

            var targetPixels = DicomPixelData.Create(dataset, newPixelData: true);
            targetPixels.AddFrame(frameData);
            dataset.AddOrUpdate(DicomTag.NumberOfFrames, 1);

            var file = new DicomFile(dataset);
            file.FileMetaInfo.TransferSyntax = source.Dataset.InternalTransferSyntax;
            file.FileMetaInfo.MediaStorageSOPInstanceUID = DicomUID.Parse(frameUid);
            return file;
        }

        /// <summary>
        /// Fixes up the cine timing attributes for an instance that now holds one frame.
        /// (0018,1065) FrameTimeVector describes a whole run and its multiplicity would no
        /// longer match, so it is collapsed to (0018,1063) FrameTime and (0028,0009)
        /// FrameIncrementPointer is repointed at that. If no frame time can be worked out the
        /// pointer is dropped rather than left naming an attribute that is not there.
        /// </summary>
        private static void ApplyFrameTiming(DicomDataset dataset, int frameIndex)
        {
            if (dataset.Contains(DicomTag.FrameTimeVector))
            {
                if (!dataset.Contains(DicomTag.FrameTime) &&
                    dataset.TryGetValues<decimal>(DicomTag.FrameTimeVector, out var vector))
                {
                    // The first entry of the vector is normally 0 (the run's origin), so fall
                    // back to the first real interval when this frame's own entry is empty.
                    decimal frameTime = frameIndex < vector.Length && vector[frameIndex] > 0
                        ? vector[frameIndex]
                        : vector.FirstOrDefault(v => v > 0);

                    if (frameTime > 0) dataset.AddOrUpdate(DicomTag.FrameTime, frameTime);
                }

                dataset.Remove(DicomTag.FrameTimeVector);
            }

            if (dataset.Contains(DicomTag.FrameTime))
                dataset.AddOrUpdate(new DicomAttributeTag(DicomTag.FrameIncrementPointer, DicomTag.FrameTime));
            else
                dataset.Remove(DicomTag.FrameIncrementPointer);
        }

        /// <summary>
        /// Throws when the frames of <paramref name="file"/> cannot be addressed individually.
        /// </summary>
        private static void EnsureSplittable(DicomFile file)
        {
            var syntax = file.Dataset.InternalTransferSyntax;

            if (VideoTransferSyntaxes.Contains(syntax.UID.UID))
                throw new FrameSplitNotSupportedException(
                    $"transfer syntax {syntax.UID.Name} carries a video stream, single frames cannot be lifted out of it");

            // Native pixel data is a flat buffer, so any frame is a simple offset into it.
            if (!syntax.IsEncapsulated) return;

            var fragments = file.Dataset.GetDicomItem<DicomFragmentSequence>(DicomTag.PixelData);
            if (fragments == null)
                throw new FrameSplitNotSupportedException("encapsulated pixel data could not be read");

            int frameCount = file.Dataset.GetValueOrDefault(DicomTag.NumberOfFrames, 0, 1);

            // A frame is found either through the basic offset table or, when there is none, by
            // assuming one fragment per frame. Anything else cannot be indexed safely.
            if (fragments.OffsetTable.Count >= frameCount) return;
            if (fragments.Fragments.Count == frameCount) return;

            throw new FrameSplitNotSupportedException(
                $"{frameCount} frame(s) are stored in {fragments.Fragments.Count} fragment(s) with no basic offset table");
        }
    }
}
