using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FellowOakDicom;

namespace RadiopaediaConnect.Services.Dicom
{
    /// <summary>
    /// Maintains a stable mapping from original DICOM UIDs to Radiopaedia-compatible hashed UIDs.
    ///
    /// Algorithm exactly matches Radiopaedia's open-source anonymiser
    /// (github.com/radiopaedia/dicom-anonymiser — src/Anon.ts, function hashedUid):
    ///
    ///   prefix  = "1.2.826.0.1.3680043.10.341.512."
    ///   digest  = SHA-512( UTF-8 bytes of original UID )
    ///   word0   = first  4 bytes of digest as big-endian signed int32
    ///   word1   = next   4 bytes of digest as big-endian signed int32
    ///   new UID = prefix + |word0| + "." + |word1|
    ///
    /// Using the same deterministic hash means Radiopaedia's server-side validator —
    /// which independently computes hashedUid(originalUid) and expects to see that value
    /// in the uploaded file — will always accept our anonymised files.
    ///
    /// Stability: repeated calls with the same original UID return the same output, keeping
    /// Study / Series / FrameOfReference UIDs consistent across all slices in a series.
    /// Each distinct SOP Instance UID naturally gets its own unique hash.
    /// </summary>
    public class DicomUidMap
    {
        private const string RadiopaediaPrefix = "1.2.826.0.1.3680043.10.341.512.";

        private readonly Dictionary<string, string> _map = new();

        /// <summary>
        /// Returns the Radiopaedia-compatible hashed UID for <paramref name="originalUid"/>.
        /// Results are cached so calls within the same series are O(1) after the first hit.
        /// If the UID is already in Radiopaedia's namespace it is returned unchanged (no double-hash).
        /// </summary>
        public string GetOrCreate(string originalUid)
        {
            if (string.IsNullOrWhiteSpace(originalUid))
                return HashedUid(DicomUID.Generate().UID);   // fallback: hash a fresh random UID

            if (_map.TryGetValue(originalUid, out var cached))
                return cached;

            var newUid = HashedUid(originalUid);
            _map[originalUid] = newUid;
            return newUid;
        }

        /// <summary>
        /// Produces a Radiopaedia-format UID from <paramref name="originalUid"/> using their
        /// SHA-512 hashing algorithm. Safe to call directly when no caching is needed.
        /// </summary>
        public static string HashedUid(string originalUid)
        {
            // Already a Radiopaedia UID — don't re-hash (matches the `startsWith(prefix)` guard
            // in Radiopaedia's hashedUid() function).
            if (originalUid.StartsWith(RadiopaediaPrefix, StringComparison.Ordinal))
                return originalUid;

            var digest = SHA512.HashData(Encoding.UTF8.GetBytes(originalUid));

            // SJCL (Stanford JavaScript Crypto Library) stores SHA-512 output as an array of
            // big-endian signed 32-bit integers.  Radiopaedia takes the first two words.
            var word0 = BinaryPrimitives.ReadInt32BigEndian(digest.AsSpan(0, 4));
            var word1 = BinaryPrimitives.ReadInt32BigEndian(digest.AsSpan(4, 8));

            return RadiopaediaPrefix + Math.Abs(word0) + "." + Math.Abs(word1);
        }
    }
}
