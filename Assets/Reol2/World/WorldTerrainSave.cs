using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Debug = UnityEngine.Debug;
using EditPages = System.Collections.Generic.Dictionary<UnityEngine.Vector3Int,
    System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, Reol2.World.ContinentField.SampleEdit>>;

namespace Reol2.World
{
    internal sealed class WorldTerrainSave : IDisposable
    {
        private const int Magic = 0x45543252; // R2TE
        private const int FormatVersion = 1;
        private const int DigestBytes = 32;
        private const float SaveInterval = 5f;
        private const int Side = ContinentField.CellCount;
        private const int SamplesPerPage = Side * Side * Side;
        private readonly ContinentField field;
        private readonly long seed;
        private readonly int regionSize;
        private FileStream sessionLock;
        private Task<long> writing;
        private long savingRevision, savedRevision;
        private float nextSave;
        private bool keepBackup;
        private bool writeFailed;
        private bool quitBlocked;
        private bool quitWithoutSaving;
        private string status;

        internal string FilePath { get; private set; }
        internal bool CanEdit => sessionLock != null;
        internal bool HasPendingEdits => field.EditRevision != savedRevision;
        internal string Status
        {
            get
            {
                if (quitBlocked)
                    return "Terrain save failed. Close again to retry, or press F9 to exit with the last save";
                return CanEdit && writing == null && HasPendingEdits && !writeFailed
                    ? "Terrain edits waiting to save" : status;
            }
        }

        internal WorldTerrainSave(ContinentField field, string directory, long seed, int regionSize)
        {
            this.field = field;
            this.seed = seed;
            this.regionSize = regionSize;
            try
            {
                string name = FormattableString.Invariant($"terrain-{seed}-{regionSize}.r2terrain");
                FilePath = Path.Combine(Path.GetFullPath(directory), name);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                sessionLock = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                var timer = Stopwatch.StartNew();
                EditPages pages = ReadWithBackup();
                if (pages == null)
                    return;
                field.RestoreEditedPages(pages);
                status = keepBackup ? "Terrain edits restored from backup" : "Terrain edits loaded";
                Debug.Log(FormattableString.Invariant(
                    $"Terrain save | loaded {pages.Count} pages, {field.EditedSamples} samples | {timer.Elapsed.TotalMilliseconds:F3} ms | {FilePath}"));
            }
            catch (Exception error) when (IsFileError(error) || error is IncompatibleSaveException)
            {
                status = error switch
                {
                    IncompatibleSaveException => "Terrain save belongs to a different world, generator or format",
                    InvalidDataException => "Terrain save is damaged and no valid backup could be loaded",
                    _ => "Terrain save unavailable. Check disk access or close the other game window"
                };
                sessionLock?.Dispose();
                sessionLock = null;
                Debug.LogWarning($"Terrain save | {status} | {FilePath} | {error.Message}");
            }
        }

        internal void Tick(float time)
        {
            if (!CanEdit || quitWithoutSaving)
                return;
            if (writing?.IsCompleted == true)
                FinishWrite();
            if (writing != null || !HasPendingEdits || time < nextSave)
                return;
            nextSave = time + SaveInterval;
            BeginWrite();
        }

        private void BeginWrite()
        {
            EditPages snapshot = field.CaptureEditSnapshot();
            savingRevision = field.EditRevision;
            bool protectBackup = keepBackup;
            status = "Saving terrain edits…";
            writing = Task.Run(() => WriteSnapshot(snapshot, protectBackup));
        }

        private void FinishWrite()
        {
            try
            {
                long bytes = writing.GetAwaiter().GetResult();
                savedRevision = savingRevision;
                keepBackup = false;
                writeFailed = false;
                if (!HasPendingEdits)
                    quitBlocked = false;
                status = "Terrain edits saved";
                Debug.Log(FormattableString.Invariant($"Terrain save | committed {bytes} bytes | {FilePath}"));
            }
            catch (Exception error) when (IsFileError(error))
            {
                writeFailed = true;
                status = "Terrain save failed. Check disk space and write access";
                Debug.LogWarning($"Terrain save | {status} | {FilePath} | {error.Message}");
            }
            finally
            {
                writing = null;
                field.ReleaseEditSnapshot();
            }
        }

        internal bool Flush()
        {
            if (!CanEdit)
                return false;
            if (writing != null)
                FinishWrite();
            if (HasPendingEdits)
            {
                BeginWrite();
                FinishWrite();
            }
            return !HasPendingEdits;
        }

        internal bool TrySaveBeforeQuit()
        {
            if (quitWithoutSaving || !HasPendingEdits)
                return true;
            quitBlocked = !Flush();
            return !quitBlocked;
        }

        internal bool ConfirmQuitWithoutSaving()
        {
            if (!quitBlocked || !HasPendingEdits)
                return false;
            quitWithoutSaving = true;
            return true;
        }

        public void Dispose()
        {
            try
            {
                if (quitWithoutSaving)
                {
                    // Keep the file lock until a write that started earlier has finished.
                    if (writing != null)
                        FinishWrite();
                }
                else if (CanEdit)
                    Flush();
            }
            finally
            {
                sessionLock?.Dispose();
                sessionLock = null;
            }
        }

        private EditPages ReadWithBackup()
        {
            InvalidDataException corruptPrimary = null;
            try
            {
                EditPages primary = ReadIfPresent(FilePath);
                if (primary != null)
                    return primary;
            }
            catch (InvalidDataException error)
            {
                corruptPrimary = error;
            }

            EditPages backup = ReadIfPresent(FilePath + ".bak");
            if (backup != null)
            {
                keepBackup = true;
                return backup;
            }
            if (corruptPrimary != null)
                throw corruptPrimary;
            return null;
        }

        private EditPages ReadIfPresent(string path)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
                byte[] expected = reader.ReadBytes(DigestBytes);
                using var sha = SHA256.Create();
                if (expected.Length != DigestBytes || !expected.SequenceEqual(sha.ComputeHash(file)))
                    throw new InvalidDataException("Terrain save checksum mismatch.");
                file.Position = DigestBytes;
                if (reader.ReadInt32() != Magic)
                    throw new InvalidDataException("Terrain save header is invalid.");
                if (reader.ReadInt32() != FormatVersion || reader.ReadInt32() != WorldDefinition.GeneratorVersion
                    || reader.ReadInt64() != seed || reader.ReadInt32() != regionSize)
                    throw new IncompatibleSaveException();
                using var zip = new DeflateStream(file, CompressionMode.Decompress, leaveOpen: true);
                using var data = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);
                var pages = new EditPages();
                int pageCount = data.ReadInt32();
                if (pageCount < 0)
                    throw new InvalidDataException("Terrain save page count is invalid.");
                for (int i = 0; i < pageCount; i++)
                {
                    Vector3Int page = ReadVector(data);
                    if (!ValidPage(page))
                        throw new InvalidDataException("Terrain save page is outside the world.");
                    var samples = ReadPage(data, page * Side);
                    if (!pages.TryAdd(page, samples))
                        throw new InvalidDataException("Terrain save contains a repeated page.");
                }
                if (zip.ReadByte() != -1)
                    throw new InvalidDataException("Terrain save has extra data.");
                return pages;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (EndOfStreamException error)
            {
                throw new InvalidDataException("Terrain save is incomplete.", error);
            }
        }

        private static Dictionary<Vector3Int, ContinentField.SampleEdit> ReadPage(BinaryReader reader, Vector3Int origin)
        {
            int count = reader.ReadUInt16();
            if (count == 0 || count > SamplesPerPage)
                throw new InvalidDataException("Terrain save sample count is invalid.");
            var page = new Dictionary<Vector3Int, ContinentField.SampleEdit>(count);
            for (int i = 0; i < count; i++)
            {
                int index = reader.ReadUInt16();
                float density = reader.ReadSingle();
                byte flags = reader.ReadByte();
                Vector3Int sample = origin + new Vector3Int(index % Side, index / Side % Side, index / (Side * Side));
                if (index >= SamplesPerPage || WorldDefinition.ClampSample(sample) != sample || !float.IsFinite(density)
                    || flags > 2 || flags != 0 && density >= 0f)
                    throw new InvalidDataException("Terrain save contains an invalid sample.");
                Vector3Int? source = flags == 1 ? ReadVector(reader) : null;
                if (source.HasValue && !WorldDefinition.Contains(new WorldPosition(source.Value.x, source.Value.y, source.Value.z)))
                    throw new InvalidDataException("Terrain save contains an invalid placement cell.");
                if (!page.TryAdd(sample, new ContinentField.SampleEdit(density, source, flags == 2)))
                    throw new InvalidDataException("Terrain save contains a repeated sample.");
            }
            return page;
        }

        private long WriteSnapshot(EditPages snapshot, bool protectBackup)
        {
            string temporary = FilePath + ".tmp";
            long length;
            try
            {
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    file.Position = DigestBytes;
                    using (var header = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
                    {
                        header.Write(Magic);
                        header.Write(FormatVersion);
                        header.Write(WorldDefinition.GeneratorVersion);
                        header.Write(seed);
                        header.Write(regionSize);
                    }
                    using (var zip = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true))
                    using (var writer = new BinaryWriter(zip))
                    {
                        writer.Write(snapshot.Count);
                        foreach (var entry in snapshot)
                            WritePage(writer, entry.Key, entry.Value);
                    }
                    file.Position = DigestBytes;
                    using var sha = SHA256.Create();
                    byte[] digest = sha.ComputeHash(file);
                    file.Position = 0;
                    file.Write(digest, 0, digest.Length);
                    file.Flush(flushToDisk: true);
                    length = file.Length;
                }
                if (File.Exists(FilePath))
                    File.Replace(temporary, FilePath, protectBackup ? null : FilePath + ".bak");
                else
                    File.Move(temporary, FilePath);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return length;
        }

        private static void WritePage(BinaryWriter writer, Vector3Int key, Dictionary<Vector3Int, ContinentField.SampleEdit> page)
        {
            WriteVector(writer, key);
            writer.Write((ushort)page.Count);
            Vector3Int origin = key * Side;
            foreach (var entry in page)
            {
                Vector3Int local = entry.Key - origin;
                writer.Write((ushort)(local.x + Side * (local.y + Side * local.z)));
                ContinentField.SampleEdit edit = entry.Value;
                writer.Write(edit.Density);
                writer.Write((byte)(edit.Source.HasValue ? 1 : edit.SculptedFill ? 2 : 0));
                if (edit.Source is Vector3Int source)
                    WriteVector(writer, source);
            }
        }

        private static Vector3Int ReadVector(BinaryReader reader) =>
            new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

        private static void WriteVector(BinaryWriter writer, Vector3Int value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        // The positive world face has a shared sample, even though it has no playable page of its own.
        private static bool ValidPage(Vector3Int key)
        {
            const int edge = WorldDefinition.Extent / ContinentField.ChunkSize;
            return key.x >= -edge && key.x <= edge && key.z >= -edge && key.z <= edge
                && key.y >= WorldDefinition.MinY / ContinentField.ChunkSize && key.y <= WorldDefinition.MaxY / ContinentField.ChunkSize;
        }

        private static bool IsFileError(Exception error) => error is IOException or InvalidDataException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException or CryptographicException;

        private sealed class IncompatibleSaveException : Exception { }
    }
}
