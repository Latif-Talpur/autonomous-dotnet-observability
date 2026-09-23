using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Transport.Http
{
    /// <summary>
    /// File-based durable spool.  Each pending event is a separate JSON file so that
    /// individual deliveries can be atomically committed by deleting the file.
    /// </summary>
    internal sealed class LocalSpoolStore
    {
        private readonly string _directory;
        private readonly long _maxSizeBytes;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public LocalSpoolStore(string directory, long maxSizeBytes)
        {
            _directory = directory;
            _maxSizeBytes = maxSizeBytes;
            Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Writes the envelope to a new spool file.
        /// Returns false and drops the event when the spool size limit has been reached.
        /// </summary>
        public bool TryWrite(ErrorEnvelope envelope)
        {
            if (GetTotalSize() >= _maxSizeBytes) return false;

            // Ticks-prefixed names sort lexicographically by arrival time (oldest-first replay).
            var name = $"{DateTime.UtcNow.Ticks:D20}-{SanitiseName(envelope.EventId)}.json";
            var tmp = Path.Combine(_directory, name + ".tmp");
            var dest = Path.Combine(_directory, name);
            try
            {
                var entry = new SpoolEntry { Envelope = envelope };
                File.WriteAllText(tmp, JsonSerializer.Serialize(entry, _json));
                File.Move(tmp, dest);   // rename is atomic on most file systems
                return true;
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Enumerates all spool entries, oldest-first.
        /// Corrupt files are skipped silently.
        /// </summary>
        public IEnumerable<(string Path, SpoolEntry Entry)> ReadAll()
        {
            if (!Directory.Exists(_directory)) yield break;

            var files = Directory.GetFiles(_directory, "*.json")
                                 .OrderBy(f => f, StringComparer.Ordinal);

            foreach (var file in files)
            {
                SpoolEntry? entry = null;
                try
                {
                    var text = File.ReadAllText(file);
                    entry = JsonSerializer.Deserialize<SpoolEntry>(text, _json);
                }
                catch { }

                if (entry?.Envelope != null)
                    yield return (file, entry);
            }
        }

        /// <summary>Removes a successfully delivered spool file.</summary>
        public void Delete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private long GetTotalSize()
        {
            if (!Directory.Exists(_directory)) return 0;
            try
            {
                return new DirectoryInfo(_directory)
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        private static string SanitiseName(string name)
        {
            // Strip any characters that are not safe in a file name.
            var chars = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => Array.IndexOf(chars, c) >= 0 ? '_' : c).ToArray());
        }
    }
}
