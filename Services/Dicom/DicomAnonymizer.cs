using FellowOakDicom;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// Produces anonymised DICOM files compatible with Radiopaedia's viewer and server-side
    /// re-anonymiser (github.com/radiopaedia/dicom-anonymiser).
    ///
    /// Strategy — mirrors Radiopaedia's open-source policy exactly:
    ///   • Default action is REMOVE — only explicitly approved tags reach the output. The approved
    ///     ("keep") tags live in an external file, Config/dicom-allowlist.json, loaded at runtime
    ///     and selected by HEX (group,element) — not by fo-dicom constant name. See DicomAllowlist.
    ///   • A small set of PHI tags that DICOM IOD rules require to be present are written as
    ///     EMPTY strings rather than removed (Radiopaedia calls this "replace").
    ///   • UIDs are replaced with synthetic values kept consistent within a series via DicomUidMap.
    ///   • (0012,0062) Patient Identity Removed = YES and (0012,0063) De-identification Method
    ///     are added to every output file per DICOM PS3.15 Annex E.
    ///
    /// Tag selection informed by:
    ///   - github.com/radiopaedia/dicom-anonymiser  (Policies.ts "keep" actions)
    ///   - DICOM PS3.3 IOD definitions for CT, MR, US, DX, NM, PET
    /// </summary>
    public class DicomAnonymizer
    {
        private readonly ILogger<DicomAnonymizer> _logger;

        // The keep-list (Config/dicom-allowlist.json) is loaded once at startup as a DI singleton
        // and injected here. The same singleton backs the /api/anonymisation/policy endpoint, so
        // the anonymiser and the UI describe exactly the same tags — no second hand-kept copy.
        private readonly DicomAllowlist _allowlist;

        // ── UIDs that must NOT be copied verbatim — handled explicitly in AnonymizeFile ─────
        // SOPInstanceUID (00080018) is intentionally absent from the allowlist: Radiopaedia's
        // anonymiser removes it (no policy entry → default "remove") and their server rejects
        // files where it is present in the dataset. It is computed only for file-meta / naming.
        private static readonly HashSet<DicomTag> _uidTagsToReplace = new()
        {
            DicomTag.SOPInstanceUID,      // (0008,0018) — computed but NOT written to dataset
            DicomTag.StudyInstanceUID,    // (0020,000D) consistent/study  → SHA-512 hash
            DicomTag.SeriesInstanceUID,   // (0020,000E) consistent/series → SHA-512 hash
            DicomTag.FrameOfReferenceUID, // (0020,0052) consistent/FoR   → SHA-512 hash
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

        /// <summary>The type-2 PHI tags written as empty strings. Exposed so the UI policy endpoint
        /// can describe them from the same source the anonymiser uses.</summary>
        public static IReadOnlyList<DicomTag> EmptyReplaceTags => _emptyReplaceTags;

        public DicomAnonymizer(ILogger<DicomAnonymizer> logger, DicomAllowlist allowlist)
        {
            _logger = logger;
            _allowlist = allowlist;
        }

        /// <summary>The set of tags copied verbatim, from the startup-loaded allowlist singleton.</summary>
        private IReadOnlySet<DicomTag> KeepTags => _allowlist.KeepTags;

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

            var keepTags = KeepTags;   // loaded at startup; see DicomAllowlist (fail-closed)

            var dcmFiles = Directory.GetFiles(inputDir, "*.dcm");
            _logger.LogInformation("[Anon] Anonymising {Count} DICOM file(s) in {Dir} using {Tags} keep-tag(s)",
                dcmFiles.Length, inputDir, keepTags.Count);

            // Anonymise every file, capturing InstanceNumber so we can order them.
            var staged = new List<(string TempPath, int InstanceNumber)>();

            foreach (var filePath in dcmFiles)
            {
                try
                {
                    var source = await DicomFile.OpenAsync(filePath);
                    var anonFile = AnonymizeFile(source, uidMap);

                    // InstanceNumber (0020,0013) is preserved by the allowlist; fall back to
                    // the original source value in case the dataset read returns 0.
                    int instanceNumber = anonFile.Dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
                    if (instanceNumber == 0)
                        instanceNumber = source.Dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);

                    // Use a temp name; we'll rename to sequential after sorting.
                    var newSopUid = anonFile.FileMetaInfo.MediaStorageSOPInstanceUID?.UID
                                   ?? Guid.NewGuid().ToString("N");
                    var tempPath = Path.Combine(outputDir, $"{newSopUid}.dcm");
                    await anonFile.SaveAsync(tempPath);
                    staged.Add((tempPath, instanceNumber));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Anon] Skipped {File}: {Msg}", Path.GetFileName(filePath), ex.Message);
                }
            }

            // Sort by InstanceNumber then rename to zero-padded sequential filenames so that
            // the Radiopaedia viewer (which may rely on filename or upload order) shows slices
            // in the correct anatomical sequence.
            staged.Sort((a, b) => a.InstanceNumber.CompareTo(b.InstanceNumber));

            int digits = Math.Max(5, staged.Count.ToString().Length); // e.g. "00001"
            var outputPaths = new List<string>(staged.Count);

            for (int i = 0; i < staged.Count; i++)
            {
                var seqName = (i + 1).ToString().PadLeft(digits, '0') + ".dcm";
                var finalPath = Path.Combine(outputDir, seqName);
                File.Move(staged[i].TempPath, finalPath, overwrite: true);
                outputPaths.Add(finalPath);
            }

            _logger.LogInformation("[Anon] Produced {Count} anonymised file(s) → {Dir} (ordered by InstanceNumber)",
                outputPaths.Count, outputDir);
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
            var keepTags = KeepTags;

            // ── Step 1: Copy all allowlisted tags, skipping UIDs (handled below) ──────────
            foreach (var tag in keepTags)
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
