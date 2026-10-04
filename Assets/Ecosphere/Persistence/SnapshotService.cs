// Ecosphere — stage 07: snapshot ring, save slots, world files and rewind.
//
// Files on disk (per platform save folder):
//   slot_01.ecoworld … slot_08.ecoworld   manual save slots
//   ring_00.ecoworld … ring_23.ecoworld   hot snapshot ring (autosave + rewind slider)
//   <world>.ecoworld                      exported/shareable world file
//   <world>.png                           optional orbit thumbnail (written by the UI)
//
// Container format (see Docs/ux.md §Saves):
//   "ECOSWRLD1" | int32 headerBytes | utf8 JSON header | payload
// The header is plain JSON (payloadBytes/payloadHash live inside it) so a world file can be
// listed, inspected and diffed without decompressing anything; the payload is GZip by
// default and its checksum is verified on load.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Persistence
{
    /// <summary>One entry of a save-slot or ring listing.</summary>
    public struct SnapshotSlotInfo
    {
        public string FileName;
        public string FullPath;
        public int SlotIndex;      // -1 for exports, ring index for ring files
        public bool IsRing;
        public bool Exists;
        public long Bytes;
        public long ModifiedUtcTicks;
        public SnapshotHeader Header;

        public string Describe()
        {
            if (!Exists) return "empty";
            return Header.Describe() + " • " + (Bytes / 1024L) + " KB";
        }
    }

    /// <summary>Rewind plan: which ring entry a request resolved to.</summary>
    public struct RewindTarget
    {
        public int RingIndex;
        public bool Found;
        public SnapshotHeader Header;
    }

    public sealed class SnapshotIoResult
    {
        public bool Success;
        public string Error;
        public string Path;
        public long Bytes;
        public ulong StateHash;
        public double Milliseconds;

        public static SnapshotIoResult Fail(string error) => new SnapshotIoResult { Success = false, Error = error };
    }

    /// <summary>
    /// Owns every snapshot on disk plus the hot ring used by rewind. Pure file I/O and
    /// bookkeeping: it never mutates the simulation, callers decide what to do with results.
    /// </summary>
    public sealed class SnapshotService
    {
        /// <summary>Ring length; matches SaveNames.DefaultRingSize unless overridden.</summary>
        public int RingSize { get; set; } = SaveNames.DefaultRingSize;

        /// <summary>Directory holding slots and the ring (per-user, per-platform).</summary>
        public string SaveDirectory { get; private set; }

        /// <summary>Directory for exported world files (Documents/Ecosphere on desktop).</summary>
        public string ExportDirectory { get; private set; }

        /// <summary>Directory for screenshots (photo mode).</summary>
        public string PhotoDirectory { get; private set; }

        public WorldStateCodec Codec { get; } = new WorldStateCodec();

        public SnapshotService(string saveDirectory, string exportDirectory, string photoDirectory)
        {
            SaveDirectory = saveDirectory;
            ExportDirectory = exportDirectory;
            PhotoDirectory = photoDirectory;
            EnsureDirectories();
        }

        /// <summary>Recommended layout for the current platform (no Unity API: caller passes paths).</summary>
        public static SnapshotService CreateDefault(string persistentDataPath, string picturesPath)
        {
            string save = Path.Combine(persistentDataPath, "saves");
            string export = Path.Combine(picturesPath, "Ecosphere");
            return new SnapshotService(save, export, Path.Combine(picturesPath, "Ecosphere"));
        }

        public void EnsureDirectories()
        {
            try
            {
                if (!string.IsNullOrEmpty(SaveDirectory)) Directory.CreateDirectory(SaveDirectory);
                if (!string.IsNullOrEmpty(ExportDirectory)) Directory.CreateDirectory(ExportDirectory);
                if (!string.IsNullOrEmpty(PhotoDirectory)) Directory.CreateDirectory(PhotoDirectory);
            }
            catch (Exception)
            {
                // A read-only or sandboxed folder must not crash the game; saves will report
                // their own failure with a readable reason instead.
            }
        }

        // ── Write / read ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Writes a snapshot atomically: temp file first, then replace. A crash mid-save
        /// therefore never destroys the previous good file, and the checksum in the header
        /// catches a truncated write.
        /// </summary>
        public SnapshotIoResult Write(World world, string path, SnapshotHeader header, bool compress = true)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string temp = SaveNames.TempFileName(path);
            string payloadPath = temp + ".payload";
            try
            {
                // Pass 1: serialize the payload on its own so its hash and size are known
                // before the header is written (the header stores both).
                WorldSnapshotStats stats;
                using (var payloadFile = File.Create(payloadPath))
                using (var payloadStream = compress
                    ? new GZipStream(payloadFile, CompressionLevel.Fastest, leaveOpen: true)
                    : payloadFile)
                {
                    stats = Codec.Write(world, payloadStream);
                }

                header.Flags = compress
                    ? (header.Flags | SnapshotFlags.Compressed)
                    : (header.Flags & ~SnapshotFlags.Compressed);
                header.FormatVersion = SnapshotHeader.CurrentFormatVersion;
                header.PayloadBytes = stats.PayloadBytes;
                header.PayloadHash = (uint)(stats.StateHash ^ (stats.StateHash >> 32));
                header.EntityCount = stats.Entities;
                header.SavedAtUtcTicks = DateTime.UtcNow.Ticks;

                byte[] jsonBytes = Encoding.UTF8.GetBytes(header.ToJson());

                // Pass 2: container = magic | jsonLength | json | payload.
                using (var final = File.Create(temp))
                {
                    byte[] magic = Encoding.ASCII.GetBytes(SnapshotHeader.Magic);
                    final.Write(magic, 0, magic.Length);
                    byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
                    final.Write(lengthBytes, 0, lengthBytes.Length);
                    final.Write(jsonBytes, 0, jsonBytes.Length);
                    using var payloadFile = File.OpenRead(payloadPath);
                    payloadFile.CopyTo(final);
                }
                File.Delete(payloadPath);

                // Replace only after the new file is complete: a crash mid-save keeps the
                // previous good snapshot, and the checksum catches a truncated one.
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);

                stopwatch.Stop();
                return new SnapshotIoResult
                {
                    Success = true,
                    Path = path,
                    Bytes = new FileInfo(path).Length,
                    StateHash = stats.StateHash,
                    Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                };
            }
            catch (Exception exception)
            {
                TryDelete(temp);
                TryDelete(payloadPath);
                return SnapshotIoResult.Fail(exception.Message);
            }
        }

        /// <summary>Reads a header without touching the payload (fast slot listing).</summary>
        public static bool TryReadHeader(string path, out SnapshotHeader header, out string error)
        {
            header = default;
            error = null;
            try
            {
                using var file = File.OpenRead(path);
                using var reader = new BinaryReader(file, Encoding.UTF8);
                byte[] magic = reader.ReadBytes(SnapshotHeader.Magic.Length);
                if (Encoding.ASCII.GetString(magic) != SnapshotHeader.Magic)
                {
                    error = "not an Ecosphere world file";
                    return false;
                }
                int jsonLength = reader.ReadInt32();
                if (jsonLength <= 0 || jsonLength > 1 << 20)
                {
                    error = "corrupt header length";
                    return false;
                }
                string json = Encoding.UTF8.GetString(reader.ReadBytes(jsonLength));
                return SnapshotHeader.TryParse(json, out header, out error);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Loads a snapshot into the world. Validates the checksum before installing state and
        /// reports a mismatch instead of loading a corrupt world.
        /// </summary>
        public SnapshotIoResult Load(World world, string path, bool verifyChecksum = true)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            if (!File.Exists(path)) return SnapshotIoResult.Fail("file not found: " + path);
            if (!TryReadHeader(path, out SnapshotHeader header, out string headerError))
                return SnapshotIoResult.Fail(headerError);

            try
            {
                using var file = File.OpenRead(path);
                using var reader = new BinaryReader(file, Encoding.UTF8);
                reader.ReadBytes(SnapshotHeader.Magic.Length);
                int jsonLength = reader.ReadInt32();
                reader.ReadBytes(jsonLength);

                Stream payload = header.IsCompressed
                    ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: true)
                    : file;

                bool ok = Codec.Read(world, payload, out string error, out WorldSnapshotStats stats);
                if (!ok) return SnapshotIoResult.Fail(error);

                uint hash = (uint)(stats.StateHash ^ (stats.StateHash >> 32));
                if (verifyChecksum && header.PayloadHash != 0u && hash != header.PayloadHash)
                {
                    return SnapshotIoResult.Fail("checksum mismatch (expected " + header.PayloadHash + ", got " + hash + ")");
                }

                stopwatch.Stop();
                return new SnapshotIoResult
                {
                    Success = true,
                    Path = path,
                    Bytes = new FileInfo(path).Length,
                    StateHash = stats.StateHash,
                    Milliseconds = stopwatch.Elapsed.TotalMilliseconds,
                };
            }
            catch (Exception exception)
            {
                return SnapshotIoResult.Fail(exception.Message);
            }
        }

        // ── Ring + slots ───────────────────────────────────────────────────────────────

        /// <summary>Writes the next ring file (autosave/rewind history).</summary>
        public SnapshotIoResult WriteRing(World world, int index, SnapshotHeader header)
        {
            header.Flags |= SnapshotFlags.AutoSnapshot | SnapshotFlags.RewindCheckpoint;
            string name = SaveNames.RingFileName(index, RingSize);
            return Write(world, Path.Combine(SaveDirectory, name), header);
        }

        public SnapshotIoResult WriteSlot(World world, int slot, SnapshotHeader header)
        {
            header.Flags &= ~SnapshotFlags.AutoSnapshot;
            string name = SaveNames.SlotFileName(slot);
            return Write(world, Path.Combine(SaveDirectory, name), header);
        }

        public SnapshotIoResult LoadSlot(World world, int slot)
        {
            return Load(world, Path.Combine(SaveDirectory, SaveNames.SlotFileName(slot)));
        }

        /// <summary>Lists every ring entry ordered by file index (oldest wrap order is resolved by date).</summary>
        public List<SnapshotSlotInfo> ListRing()
        {
            var list = new List<SnapshotSlotInfo>(RingSize);
            for (int i = 0; i < RingSize; i++)
            {
                string path = Path.Combine(SaveDirectory, SaveNames.RingFileName(i, RingSize));
                list.Add(Describe(path, i, isRing: true));
            }
            list.Sort(CompareByDate);
            return list;
        }

        public List<SnapshotSlotInfo> ListSlots()
        {
            var list = new List<SnapshotSlotInfo>(SaveNames.SlotCount);
            for (int i = 0; i < SaveNames.SlotCount; i++)
            {
                string path = Path.Combine(SaveDirectory, SaveNames.SlotFileName(i));
                list.Add(Describe(path, i, isRing: false));
            }
            return list;
        }

        /// <summary>Lists exported world files, newest first.</summary>
        public List<SnapshotSlotInfo> ListExports()
        {
            var list = new List<SnapshotSlotInfo>();
            try
            {
                if (!Directory.Exists(ExportDirectory)) return list;
                string[] files = Directory.GetFiles(ExportDirectory, "*" + SaveNames.WorldExtension);
                for (int i = 0; i < files.Length; i++) list.Add(Describe(files[i], -1, isRing: false));
            }
            catch (Exception)
            {
                // Ignore: an unreadable folder simply lists nothing.
            }
            list.Sort(CompareByDate);
            return list;
        }

        private static SnapshotSlotInfo Describe(string path, int index, bool isRing)
        {
            var info = new SnapshotSlotInfo { FileName = Path.GetFileName(path), FullPath = path, SlotIndex = index, IsRing = isRing };
            try
            {
                if (!File.Exists(path)) return info;
                var file = new FileInfo(path);
                info.Exists = true;
                info.Bytes = file.Length;
                info.ModifiedUtcTicks = file.LastWriteTimeUtc.Ticks;
                if (TryReadHeader(path, out SnapshotHeader header, out _)) info.Header = header;
            }
            catch (Exception)
            {
                info.Exists = false;
            }
            return info;
        }

        private static int CompareByDate(SnapshotSlotInfo a, SnapshotSlotInfo b)
        {
            if (!a.Exists && !b.Exists) return a.SlotIndex.CompareTo(b.SlotIndex);
            if (!a.Exists) return 1;
            if (!b.Exists) return -1;
            return b.Header.TotalTicks.CompareTo(a.Header.TotalTicks);
        }

        /// <summary>
        /// Chooses the ring entry closest to a requested tick (the rewind slider's job).
        /// </summary>
        public RewindTarget FindRewindTarget(ulong targetTick)
        {
            List<SnapshotSlotInfo> ring = ListRing();
            var target = new RewindTarget { Found = false, RingIndex = -1 };
            ulong bestDistance = ulong.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                if (!ring[i].Exists) continue;
                ulong tick = ring[i].Header.TotalTicks;
                if (tick > targetTick) continue; // never rewind forward
                ulong distance = targetTick - tick;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                target.Found = true;
                target.RingIndex = ring[i].SlotIndex;
                target.Header = ring[i].Header;
            }
            return target;
        }

        /// <summary>Deletes one ring entry (used when the ring is trimmed).</summary>
        public bool DeleteRingEntry(int index)
        {
            try
            {
                string path = Path.Combine(SaveDirectory, SaveNames.RingFileName(index, RingSize));
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ── Share ──────────────────────────────────────────────────────────────────────

        /// <summary>Copies the current save (slot or ring entry) into the export folder.</summary>
        public SnapshotIoResult Export(string sourcePath, string worldName)
        {
            try
            {
                EnsureDirectories();
                string target = Path.Combine(ExportDirectory, SaveNames.WorldFileName(worldName));
                int suffix = 1;
                while (File.Exists(target))
                {
                    string baseName = SaveNames.Sanitize(worldName, SaveNames.MaxNameLength - 3) + "-" + suffix;
                    target = Path.Combine(ExportDirectory, SaveNames.WorldFileName(baseName));
                    suffix++;
                }
                File.Copy(sourcePath, target, overwrite: false);
                return new SnapshotIoResult
                {
                    Success = true,
                    Path = target,
                    Bytes = new FileInfo(target).Length,
                };
            }
            catch (Exception exception)
            {
                return SnapshotIoResult.Fail(exception.Message);
            }
        }

        /// <summary>Imports a shared world file into the first free slot.</summary>
        public SnapshotIoResult Import(string worldFile, out int slot)
        {
            slot = -1;
            try
            {
                if (!File.Exists(worldFile)) return SnapshotIoResult.Fail("file not found");
                if (!TryReadHeader(worldFile, out SnapshotHeader header, out string error))
                    return SnapshotIoResult.Fail(error);

                for (int i = 0; i < SaveNames.SlotCount; i++)
                {
                    string path = Path.Combine(SaveDirectory, SaveNames.SlotFileName(i));
                    if (File.Exists(path)) continue;
                    slot = i;
                    File.Copy(worldFile, path, overwrite: false);
                    return new SnapshotIoResult
                    {
                        Success = true,
                        Path = path,
                        Bytes = new FileInfo(path).Length,
                    };
                }

                // No free slot: overwrite the oldest by date.
                List<SnapshotSlotInfo> slots = ListSlots();
                int oldest = 0;
                for (int i = 1; i < slots.Count; i++)
                {
                    if (slots[i].ModifiedUtcTicks < slots[oldest].ModifiedUtcTicks) oldest = i;
                }
                slot = oldest;
                string overwrite = Path.Combine(SaveDirectory, SaveNames.SlotFileName(oldest));
                File.Copy(worldFile, overwrite, overwrite: true);
                return new SnapshotIoResult { Success = true, Path = overwrite };
            }
            catch (Exception exception)
            {
                return SnapshotIoResult.Fail(exception.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // best effort
            }
        }
    }

    /// <summary>
    /// Autosave policy: writes a ring snapshot every N game days, keeps the ring bounded and
    /// exposes the "time since last snapshot" the HUD shows next to the rewind slider.
    /// </summary>
    public sealed class AutosavePolicy
    {
        /// <summary>Days between automatic snapshots (0 disables autosave).</summary>
        public float IntervalDays { get; set; } = 1f;

        public bool Enabled => IntervalDays > 0f;

        public int RingCursor { get; private set; }
        public ulong LastSnapshotDay { get; private set; } = ulong.MaxValue;
        public int SnapshotCount { get; private set; }

        public bool ShouldSnapshot(ulong absoluteDay)
        {
            if (!Enabled) return false;
            if (LastSnapshotDay == ulong.MaxValue)
            {
                LastSnapshotDay = absoluteDay;
                return false;
            }
            float elapsed = absoluteDay - LastSnapshotDay;
            return elapsed >= IntervalDays;
        }

        /// <summary>Advances the ring cursor after a successful write.</summary>
        public void RecordSnapshot(ulong absoluteDay, int ringSize)
        {
            LastSnapshotDay = absoluteDay;
            SnapshotCount++;
            int size = ringSize < 1 ? 1 : ringSize;
            RingCursor = (RingCursor + 1) % size;
        }

        /// <summary>Marks a snapshot as taken without writing one (used when the user saves).</summary>
        public void ResetTimer(ulong absoluteDay) => LastSnapshotDay = absoluteDay;

        public void SetIntervalDays(float days)
        {
            IntervalDays = days < 0f ? 0f : days;
        }

        /// <summary>Ring entries in age order for the rewind slider (oldest first).</summary>
        public static List<SnapshotSlotInfo> SliderEntries(List<SnapshotSlotInfo> ring)
        {
            var result = new List<SnapshotSlotInfo>();
            if (ring == null) return result;
            for (int i = 0; i < ring.Count; i++)
            {
                if (ring[i].Exists) result.Add(ring[i]);
            }
            result.Sort((a, b) => a.Header.TotalTicks.CompareTo(b.Header.TotalTicks));
            return result;
        }
    }
}
