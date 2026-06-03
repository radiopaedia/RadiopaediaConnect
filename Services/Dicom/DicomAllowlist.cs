using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FellowOakDicom;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// The set of DICOM tags copied verbatim during anonymisation, loaded at runtime from an
    /// external JSON file (<c>Config/dicom-allowlist.json</c>) rather than being hard-coded.
    ///
    /// Design notes:
    ///   • Tags are selected by their <b>hex (group,element)</b> — the hex is the identity. The
    ///     fo-dicom <c>alias</c> and <c>description</c> in the JSON are documentation only. This
    ///     removes the entire class of "wrong constant alias" bugs (e.g. ContrastBolusRoute vs
    ///     ContrastBolusIngredient) because selection never depends on a C# constant name.
    ///   • At load time each entry's declared alias is cross-checked against fo-dicom's own
    ///     dictionary keyword; a mismatch is logged as a warning (drift guard).
    ///   • Fail-closed: if the file is missing or invalid we throw. We must never silently fall
    ///     back to "keep nothing" (broken images) or "keep everything" (PHI leak).
    ///
    /// The JSON mirrors the "keep" actions of radiopaedia/dicom-anonymiser Policies.ts. UID
    /// regeneration and type-2 PHI emptying remain in <see cref="DicomAnonymizer"/> (they are
    /// behaviour, not a flat tag list).
    /// </summary>
    /// <summary>One entry from the allowlist JSON. <paramref name="Tag"/> is 8 hex digits (e.g. "00181048").</summary>
    public sealed record AllowlistEntry(string Tag, string? Alias, string? Description);

    public sealed class DicomAllowlist
    {
        /// <summary>Fast lookup set used during anonymisation.</summary>
        public IReadOnlySet<DicomTag> KeepTags { get; }

        /// <summary>The parsed entries in file (hex) order — used to drive the UI so there is a single source of truth.</summary>
        public IReadOnlyList<AllowlistEntry> Entries { get; }

        private DicomAllowlist(IReadOnlySet<DicomTag> keepTags, IReadOnlyList<AllowlistEntry> entries)
        {
            KeepTags = keepTags;
            Entries = entries;
        }

        /// <summary>
        /// Resolved path of the allowlist JSON. Overridable via <c>RCONNECT_ALLOWLIST_PATH</c>;
        /// otherwise <c>Config/dicom-allowlist.json</c> next to the running assembly (which, in
        /// the Docker image, is <c>/app/Config/dicom-allowlist.json</c>).
        /// </summary>
        public static string DefaultPath =>
            Environment.GetEnvironmentVariable("RCONNECT_ALLOWLIST_PATH") is { Length: > 0 } overridePath
                ? overridePath
                : Path.Combine(AppContext.BaseDirectory, "Config", "dicom-allowlist.json");

        private sealed class PolicyFile
        {
            [JsonPropertyName("keep")] public List<KeepEntry> Keep { get; set; } = new();
        }

        private sealed class KeepEntry
        {
            [JsonPropertyName("tag")] public string Tag { get; set; } = "";
            [JsonPropertyName("alias")] public string? Alias { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
        }

        /// <summary>
        /// Loads and validates the allowlist. Throws on a missing file, malformed JSON, or any
        /// unparseable tag — anonymisation must not proceed without an explicit, valid keep-list.
        /// </summary>
        public static DicomAllowlist Load(string path, ILogger logger)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"DICOM anonymiser allowlist not found at '{path}'. Refusing to anonymise without " +
                    "an explicit keep-list (fail-closed). Check the file is bundled into the container.",
                    path);

            PolicyFile? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PolicyFile>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"DICOM allowlist '{path}' is not valid JSON: {ex.Message}", ex);
            }

            if (parsed?.Keep is not { Count: > 0 })
                throw new InvalidDataException($"DICOM allowlist '{path}' contains no 'keep' entries.");

            var tags = new HashSet<DicomTag>();
            var entries = new List<AllowlistEntry>(parsed.Keep.Count);
            int aliasMismatches = 0;

            foreach (var entry in parsed.Keep)
            {
                if (string.IsNullOrWhiteSpace(entry.Tag)) continue;

                var tag = ParseTag(entry.Tag); // throws on malformed hex — fail-closed
                tags.Add(tag);
                entries.Add(new AllowlistEntry(entry.Tag.Trim().ToUpperInvariant(), entry.Alias, entry.Description));

                // Drift guard: warn if the declared alias disagrees with fo-dicom's dictionary
                // keyword. Selection still uses the hex, so a mismatch is informational only —
                // but it surfaces exactly the kind of slip that the JSON is meant to prevent.
                if (!string.IsNullOrWhiteSpace(entry.Alias))
                {
                    var keyword = DicomDictionary.Default[tag]?.Keyword;
                    if (!string.IsNullOrEmpty(keyword) &&
                        !keyword.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                        !keyword.Equals(entry.Alias, StringComparison.OrdinalIgnoreCase))
                    {
                        aliasMismatches++;
                        logger.LogWarning(
                            "[Anon] Allowlist alias/hex mismatch for {Tag}: JSON alias '{Alias}' but " +
                            "fo-dicom keyword is '{Keyword}'. Selection uses the hex; verify the entry.",
                            entry.Tag, entry.Alias, keyword);
                    }
                }
            }

            logger.LogInformation(
                "[Anon] Loaded {Count} keep-tag(s) from allowlist {Path} ({Mismatches} alias mismatch(es))",
                tags.Count, path, aliasMismatches);

            return new DicomAllowlist(tags, entries);
        }

        /// <summary>
        /// Parses a tag written as 8 hex digits, optionally with a comma/parens/spaces
        /// (e.g. "00181040", "0018,1040", "(0018,1040)").
        /// </summary>
        private static DicomTag ParseTag(string raw)
        {
            var s = raw.Trim()
                       .Replace("(", "").Replace(")", "")
                       .Replace(",", "").Replace(" ", "");
            if (s.Length != 8)
                throw new FormatException(
                    $"Invalid DICOM tag '{raw}' in allowlist — expected 8 hex digits like '00181040' or '0018,1040'.");

            ushort group = ushort.Parse(s.AsSpan(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            ushort element = ushort.Parse(s.AsSpan(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new DicomTag(group, element);
        }
    }
}
