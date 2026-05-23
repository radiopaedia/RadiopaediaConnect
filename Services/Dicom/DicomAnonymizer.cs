using FellowOakDicom;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// Produces anonymised DICOM files compatible with Radiopaedia's viewer and server-side
    /// re-anonymiser (github.com/radiopaedia/dicom-anonymiser).
    ///
    /// Strategy — mirrors Radiopaedia's open-source policy exactly:
    ///   • Default action is REMOVE — only explicitly approved tags reach the output.
    ///   • A small set of PHI tags that DICOM IOD rules require to be present are written as
    ///     EMPTY strings rather than removed (Radiopaedia calls this "replace").
    ///   • UIDs are replaced with synthetic values kept consistent within a series via DicomUidMap.
    ///   • (0012,0062) Patient Identity Removed = YES and (0012,0063) De-identification Method
    ///     are added to every output file per DICOM PS3.15 Annex E.
    ///
    /// Critical fixes vs. first version:
    ///   • (0008,0005) Specific Character Set — added; required when any string tag is present.
    ///   • (0028,0006) Planar Configuration  — added; required for RGB / colour images (US etc.).
    ///   • (0020,0052) Frame of Reference UID — now in uidMap; type-1 required for CT/MR IODs.
    ///   • PHI type-2 tags (PatientName, StudyDate …) zeroed rather than removed, satisfying
    ///     strict DICOM conformance checks that validators run before serving files to viewers.
    ///
    /// Tag selection informed by:
    ///   - github.com/radiopaedia/dicom-anonymiser  (broadlySafeFieldsPolicy + module policies)
    ///   - DICOM PS3.3 IOD definitions for CT, MR, US, DX, NM, PET
    /// </summary>
    public class DicomAnonymizer
    {
        private readonly ILogger<DicomAnonymizer> _logger;

        // ── UIDs that must NOT be copied verbatim — handled explicitly in AnonymizeFile ─────
        // SOPInstanceUID (00080018) is intentionally absent: Radiopaedia's anonymiser removes
        // it (no policy entry → default "remove") and their server rejects files where it is
        // present in the dataset. It is computed only for file-meta / naming purposes.
        private static readonly HashSet<DicomTag> _uidTagsToReplace = new()
        {
            DicomTag.SOPInstanceUID,      // (0008,0018) — computed but NOT written to dataset
            DicomTag.StudyInstanceUID,    // (0020,000D) consistent/study  → SHA-512 hash
            DicomTag.SeriesInstanceUID,   // (0020,000E) consistent/series → SHA-512 hash
            DicomTag.FrameOfReferenceUID, // (0020,0052) consistent/FoR   → SHA-512 hash
        };

        // ── Tags copied verbatim (non-PHI, safe for display and analysis) ───────────────────
        //    Mirrors Radiopaedia's "keep" actions across all module policies.
        //    Private tags (odd group numbers) are never on this list.
        private static readonly HashSet<DicomTag> _allowedTags = new()
        {
            // ── SOP Common ─────────────────────────────────────────────────────────────────
            DicomTag.SpecificCharacterSet,          // (0008,0005) CRITICAL — required when any string is present
            DicomTag.SOPClassUID,                   // (0008,0016) image type — kept unchanged

            // ── General Series / Study (non-PHI parts) ─────────────────────────────────────
            DicomTag.Modality,                      // (0008,0060)
            DicomTag.SeriesDescription,             // (0008,103E) Radiopaedia explicitly keeps
            DicomTag.SeriesNumber,                  // (0020,0011)
            DicomTag.AcquisitionNumber,             // (0020,0012)
            DicomTag.InstanceNumber,                // (0020,0013) frame ordering

            // ── Image Pixel — core decoding (required for any viewer) ──────────────────────
            DicomTag.SamplesPerPixel,               // (0028,0002)
            DicomTag.PhotometricInterpretation,     // (0028,0004)
            DicomTag.PlanarConfiguration,           // (0028,0006) CRITICAL — required for RGB/colour images
            DicomTag.Rows,                          // (0028,0010)
            DicomTag.Columns,                       // (0028,0011)
            DicomTag.PixelAspectRatio,              // (0028,0034)
            DicomTag.BitsAllocated,                 // (0028,0100)
            DicomTag.BitsStored,                    // (0028,0101)
            DicomTag.HighBit,                       // (0028,0102)
            DicomTag.PixelRepresentation,           // (0028,0103)
            DicomTag.SmallestImagePixelValue,       // (0028,0106)
            DicomTag.LargestImagePixelValue,        // (0028,0107)
            DicomTag.PixelPaddingValue,             // (0028,0120)
            DicomTag.PixelPaddingRangeLimit,        // (0028,0121)
            DicomTag.QualityControlImage,           // (0028,0300)
            DicomTag.BurnedInAnnotation,            // (0028,0301)
            DicomTag.PixelData,                     // (7FE0,0010) the actual image bytes

            // ── Image Plane / spatial geometry ─────────────────────────────────────────────
            DicomTag.ImageOrientationPatient,       // (0020,0037)
            DicomTag.ImagePositionPatient,          // (0020,0032)
            DicomTag.PixelSpacing,                  // (0028,0030)
            DicomTag.SliceThickness,                // (0018,0050)
            DicomTag.SliceLocation,                 // (0020,1041)
            DicomTag.SpacingBetweenSlices,          // (0018,0088)
            DicomTag.ImagerPixelSpacing,            // (0018,1164)
            DicomTag.PatientOrientation,            // (0020,0020)
            DicomTag.Laterality,                    // (0020,0060) L/R — not patient-identifying
            DicomTag.PatientPosition,               // (0018,5100) HFS/HFP/FFS etc. — orientation
            DicomTag.PositionReferenceIndicator,    // (0020,1040)
            DicomTag.ImagesInAcquisition,           // (0020,1002)

            // ── Display / Windowing ────────────────────────────────────────────────────────
            DicomTag.WindowCenter,                  // (0028,1050)
            DicomTag.WindowWidth,                   // (0028,1051)
            DicomTag.WindowCenterWidthExplanation,  // (0028,1055)
            DicomTag.VOILUTFunction,                // (0028,1056)
            DicomTag.RescaleIntercept,              // (0028,1052) CT HU conversion
            DicomTag.RescaleSlope,                  // (0028,1053)
            DicomTag.RescaleType,                   // (0028,1054)
            DicomTag.PixelIntensityRelationship,    // (0028,1040)
            DicomTag.PixelIntensityRelationshipSign,// (0028,1041)
            DicomTag.PresentationLUTShape,          // (2050,0020)

            // ── Palette / LUT (colour-mapped images) ──────────────────────────────────────
            DicomTag.RedPaletteColorLookupTableDescriptor,           // (0028,1101)
            DicomTag.GreenPaletteColorLookupTableDescriptor,         // (0028,1102)
            DicomTag.BluePaletteColorLookupTableDescriptor,          // (0028,1103)
            DicomTag.RedPaletteColorLookupTableData,                 // (0028,1201)
            DicomTag.GreenPaletteColorLookupTableData,               // (0028,1202)
            DicomTag.BluePaletteColorLookupTableData,                // (0028,1203)
            DicomTag.SegmentedRedPaletteColorLookupTableData,        // (0028,1221)
            DicomTag.SegmentedGreenPaletteColorLookupTableData,      // (0028,1222)
            DicomTag.SegmentedBluePaletteColorLookupTableData,       // (0028,1223)

            // ── Multi-frame ───────────────────────────────────────────────────────────────
            DicomTag.NumberOfFrames,                // (0028,0008)
            DicomTag.FrameIncrementPointer,         // (0028,0009)
            DicomTag.FrameTime,                     // (0018,1063) cine
            DicomTag.FrameTimeVector,               // (0018,1065) cine
            DicomTag.RepresentativeFrameNumber,     // (0028,6010)

            // ── Lossy compression ─────────────────────────────────────────────────────────
            DicomTag.LossyImageCompression,         // (0028,2110)
            DicomTag.LossyImageCompressionRatio,    // (0028,2112)
            DicomTag.LossyImageCompressionMethod,   // (0028,2114)

            // ── Patient — non-identifying fields kept by Radiopaedia ──────────────────────
            DicomTag.PatientSex,                    // (0010,0040) kept by Radiopaedia

            // ── Acquisition parameters — all non-PHI scanner settings ─────────────────────
            //    Source: Radiopaedia broadlySafeFieldsPolicy + modality-specific module policies
            DicomTag.ImageType,                     // (0008,0008)
            DicomTag.BodyPartExamined,              // (0018,0015)
            DicomTag.ContrastBolusAgent,            // (0018,0010)
            DicomTag.ContrastBolusRoute,            // (0018,1048)
            DicomTag.ScanOptions,                   // (0018,0022) CT
            DicomTag.ScanningSequence,              // (0018,0020) MR
            DicomTag.SequenceVariant,               // (0018,0021) MR
            DicomTag.MRAcquisitionType,             // (0018,0023) MR
            DicomTag.KVP,                           // (0018,0060) CT tube voltage
            DicomTag.DataCollectionDiameter,        // (0018,0090)
            DicomTag.RepetitionTime,                // (0018,0080) MR TR
            DicomTag.EchoTime,                      // (0018,0081) MR TE
            DicomTag.InversionTime,                 // (0018,0082) MR TI
            DicomTag.NumberOfAverages,              // (0018,0083) MR
            DicomTag.ImagingFrequency,              // (0018,0084) MR
            DicomTag.ImagedNucleus,                 // (0018,0085) MR
            DicomTag.EchoNumbers,                   // (0018,0086) MR
            DicomTag.MagneticFieldStrength,         // (0018,0087) MR
            DicomTag.NumberOfPhaseEncodingSteps,    // (0018,0089)
            DicomTag.EchoTrainLength,               // (0018,0091) MR
            DicomTag.PercentSampling,               // (0018,0093)
            DicomTag.PercentPhaseFieldOfView,       // (0018,0094)
            DicomTag.PixelBandwidth,                // (0018,0095)
            DicomTag.SpatialResolution,             // (0018,1050)
            DicomTag.HeartRate,                     // (0018,1088)
            DicomTag.CardiacNumberOfImages,         // (0018,1090)
            DicomTag.TriggerWindow,                 // (0018,1094)
            DicomTag.ReconstructionDiameter,        // (0018,1100)
            DicomTag.DistanceSourceToDetector,      // (0018,1110)
            DicomTag.DistanceSourceToPatient,       // (0018,1111)
            DicomTag.EstimatedRadiographicMagnificationFactor, // (0018,1114)
            DicomTag.GantryDetectorTilt,            // (0018,1120)
            DicomTag.TableHeight,                   // (0018,1130)
            DicomTag.RotationDirection,             // (0018,1140)
            DicomTag.ExposureTime,                  // (0018,1150)
            DicomTag.XRayTubeCurrent,               // (0018,1151)
            DicomTag.Exposure,                      // (0018,1152)
            DicomTag.AveragePulseWidth,             // (0018,1154)
            DicomTag.FilterType,                    // (0018,1160)
            DicomTag.FocalSpots,                    // (0018,1190)
            DicomTag.FlipAngle,                     // (0018,1314) MR
            DicomTag.VariableFlipAngleFlag,         // (0018,1315) MR
            DicomTag.SAR,                           // (0018,1316) MR specific absorption rate
            DicomTag.ConvolutionKernel,             // (0018,1210) CT
            // CT-specific dose / protocol
            DicomTag.SingleCollimationWidth,        // (0018,9306)
            DicomTag.TotalCollimationWidth,         // (0018,9307)
            DicomTag.TableSpeed,                    // (0018,9309)
            DicomTag.TableFeedPerRotation,          // (0018,9310)
            DicomTag.SpiralPitchFactor,             // (0018,9311)
            DicomTag.ExposureModulationType,        // (0018,9323)
            DicomTag.CTDIvol,                       // (0018,9345)
            // Cardiac / respiratory gating
            DicomTag.CardiacSynchronizationTechnique, // (0018,9037)
            DicomTag.CardiacSignalSource,           // (0018,9085)
            // Radiation dose metrics (no PHI)
            new DicomTag(0x0040, 0x0301),           // (0040,0301) Total Number of Exposures
            DicomTag.HalfValueLayer,                // (0040,0314)
            DicomTag.OrganDose,                     // (0040,0316)
            // Mammography
            DicomTag.BreastImplantPresent,          // (0028,1300)
        };

        // ── Tags replaced with empty values ─────────────────────────────────────────────────
        // DICOM IOD type-2 rules require these tags to exist even if their value is unknown.
        // Removing them entirely violates conformance and causes some validators / viewers to
        // reject the file. We zero them out — exactly what Radiopaedia's "replace" action does.
        private static readonly DicomTag[] _emptyReplaceTags =
        {
            DicomTag.PatientName,             // (0010,0010) PN type-2
            DicomTag.PatientID,               // (0010,0020) LO type-2
            DicomTag.PatientBirthDate,        // (0010,0030) DA type-2
            DicomTag.StudyDate,               // (0008,0020) DA type-2
            DicomTag.StudyTime,               // (0008,0030) TM type-2
            DicomTag.AccessionNumber,         // (0008,0050) SH type-2
            DicomTag.ReferringPhysicianName,  // (0008,0090) PN type-2
            DicomTag.StudyID,                 // (0020,0010) SH type-2
            DicomTag.Manufacturer,            // (0008,0070) LO type-2
        };

        public DicomAnonymizer(ILogger<DicomAnonymizer> logger)
        {
            _logger = logger;
        }

        // ── Public API ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Anonymises all .dcm files in <paramref name="inputDir"/> and writes them to
        /// <paramref name="outputDir"/>. Returns output paths. A shared <paramref name="uidMap"/>
        /// keeps Study/Series/FrameOfReference UIDs consistent across files in a series.
        /// </summary>
        public async Task<List<string>> AnonymizeSeriesAsync(
            string inputDir, string outputDir, DicomUidMap uidMap)
        {
            Directory.CreateDirectory(outputDir);
            var outputPaths = new List<string>();

            var dcmFiles = Directory.GetFiles(inputDir, "*.dcm");
            _logger.LogInformation("[Anon] Anonymising {Count} DICOM file(s) in {Dir}", dcmFiles.Length, inputDir);

            foreach (var filePath in dcmFiles)
            {
                try
                {
                    var source = await DicomFile.OpenAsync(filePath);
                    var anonFile = AnonymizeFile(source, uidMap);

                    // SOPInstanceUID is absent from the dataset (Radiopaedia's validator
                    // rejects it if present). Use the file-meta UID for naming instead.
                    var newSopUid = anonFile.FileMetaInfo.MediaStorageSOPInstanceUID?.UID
                                   ?? Guid.NewGuid().ToString("N");
                    var outPath = Path.Combine(outputDir, $"{newSopUid}.dcm");
                    await anonFile.SaveAsync(outPath);
                    outputPaths.Add(outPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Anon] Skipped {File}: {Msg}", Path.GetFileName(filePath), ex.Message);
                }
            }

            _logger.LogInformation("[Anon] Produced {Count} anonymised file(s) → {Dir}", outputPaths.Count, outputDir);
            return outputPaths;
        }

        /// <summary>
        /// Anonymises a single <see cref="DicomFile"/> in memory and returns the result.
        /// The source file is not modified.
        /// </summary>
        public DicomFile AnonymizeFile(DicomFile source, DicomUidMap uidMap)
        {
            var src = source.Dataset;
            var anon = new DicomDataset();

            // ── Step 1: Copy all allowlisted tags, skipping UIDs (handled below) ──────────
            foreach (var tag in _allowedTags)
            {
                if (_uidTagsToReplace.Contains(tag)) continue;
                if (!src.Contains(tag)) continue;

                try
                {
                    var item = src.GetDicomItem<DicomItem>(tag);
                    if (item != null) anon.Add(item);
                }
                catch
                {
                    // Corrupt/truncated value — skip silently.
                }
            }

            // ── Step 2: Write zeroed-out PHI tags (type-2 — must be present, even if empty) ─
            foreach (var tag in _emptyReplaceTags)
                anon.AddOrUpdate(tag, string.Empty);

            // ── Step 3: Replace UIDs with consistent synthetic values ─────────────────────
            var origSopUid    = src.GetSingleValueOrDefault(DicomTag.SOPInstanceUID,    DicomUID.Generate().UID);
            var origStudyUid  = src.GetSingleValueOrDefault(DicomTag.StudyInstanceUID,  DicomUID.Generate().UID);
            var origSeriesUid = src.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, DicomUID.Generate().UID);
            var origFoRUid    = src.GetSingleValueOrDefault(DicomTag.FrameOfReferenceUID, string.Empty);
            var origSopClass  = src.GetSingleValueOrDefault(DicomTag.SOPClassUID,
                                    DicomUID.SecondaryCaptureImageStorage.UID);

            var hashedSopUid = DicomUidMap.HashedUid(origSopUid);

            // Radiopaedia's anonymiser (Anon.ts) has no explicit policy for SOPInstanceUID
            // (00080018) — their default "remove" action strips it from the dataset entirely.
            // Their server-side validator rejects any file where 00080018 is present, even if
            // the value is a correctly-formatted Radiopaedia-prefix UID.
            //
            // fo-dicom's DicomFile(DicomDataset) constructor reads 00080018 from the dataset
            // to auto-build its own file meta, so we must add it temporarily — then remove it
            // immediately after construction so it is absent when the file is written to disk.
            // The Dataset reference inside DicomFile IS the same anon object, so Remove() here
            // takes effect before SaveAsync() writes the file.
            anon.AddOrUpdate(DicomTag.SOPInstanceUID,    hashedSopUid);       // ← temp, removed below
            anon.AddOrUpdate(DicomTag.StudyInstanceUID,  uidMap.GetOrCreate(origStudyUid));
            anon.AddOrUpdate(DicomTag.SeriesInstanceUID, uidMap.GetOrCreate(origSeriesUid));
            anon.AddOrUpdate(DicomTag.SOPClassUID,       origSopClass);

            // Frame of Reference UID: keep consistent across slices so MPR/overlay works.
            if (!string.IsNullOrEmpty(origFoRUid))
                anon.AddOrUpdate(DicomTag.FrameOfReferenceUID, uidMap.GetOrCreate(origFoRUid));

            // ── Step 4: Flag the file as anonymised (DICOM PS3.15 Annex E) ───────────────
            anon.AddOrUpdate(DicomTag.PatientIdentityRemoved, "YES");
            anon.AddOrUpdate(DicomTag.DeidentificationMethod,
                "Radiopaedia Connect - allowlist; UIDs replaced; PHI zeroed");

            // ── Step 5: Rebuild file meta from scratch (source meta can carry PHI) ────────
            var anonFile = new DicomFile(anon);                               // reads 00080018 here
            anonFile.FileMetaInfo.TransferSyntax             = source.FileMetaInfo.TransferSyntax;
            anonFile.FileMetaInfo.MediaStorageSOPClassUID    = DicomUID.Parse(origSopClass);
            anonFile.FileMetaInfo.MediaStorageSOPInstanceUID = DicomUID.Parse(hashedSopUid);

            // Strip 00080018 from the dataset now that file meta is set.
            // Save() writes Dataset and FileMetaInfo separately — the tag will not appear
            // in the saved file's dataset portion, which is what Radiopaedia's validator checks.
            anon.Remove(DicomTag.SOPInstanceUID);

            return anonFile;
        }
    }
}
