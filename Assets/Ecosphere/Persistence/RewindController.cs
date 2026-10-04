// Ecosphere — stage 07: rewind + autosave controller.
//
// The rewind UX promise: "scrub back, watch the world replay to where you were". The cheap
// implementation the brief asks for is: load the newest ring snapshot at or before the
// target tick, then fast-forward with the normal tick path (TimeScrubControl) until the
// target is reached. Because god tools only ever write through next-tick requests and the
// clock is deterministic, the fast-forward reproduces the same world the player saw.
//
// Autosave rides the same ring: one snapshot per configured game day, written from the
// main thread between ticks so it never interleaves with a simulation step.

using System;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Persistence
{
    /// <summary>Owns the ring snapshots, the autosave timer and pending rewind requests.</summary>
    public sealed class RewindController
    {
        public static RewindController Instance { get; private set; }

        private readonly SnapshotService _snapshots;
        private readonly AutosavePolicy _autosave = new AutosavePolicy();

        private bool _rewindPending;
        private int _pendingRingIndex = -1;
        private ulong _pendingTargetTick;
        private ulong _rewindStartTicks;
        private ulong _lastDaySeen;

        /// <summary>Raised after a rewind completes (UI closes the progress affordance).</summary>
        public event Action<ulong> RewindCompleted;

        /// <summary>Raised when a rewind cannot be satisfied (no ring entry, load error).</summary>
        public event Action<string> RewindFailed;

        public RewindController(SnapshotService snapshots)
        {
            _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            Instance = this;
        }

        public SnapshotService Snapshots => _snapshots;
        public AutosavePolicy Autosave => _autosave;
        public bool IsRewinding => _rewindPending;
        public ulong PendingTargetTick => _pendingTargetTick;

        public void ConfigureAutosave(float intervalDays, int ringSize)
        {
            _autosave.SetIntervalDays(intervalDays);
            _snapshots.RingSize = ringSize < 4 ? 4 : ringSize;
        }

        /// <summary>Queues a rewind to an absolute tick (load ring entry + fast-forward).</summary>
        public void RequestRewind(ulong targetTick)
        {
            RewindTarget target = _snapshots.FindRewindTarget(targetTick);
            if (!target.Found)
            {
                RewindFailed?.Invoke("no snapshot at or before tick " + targetTick);
                return;
            }
            _rewindPending = true;
            _pendingRingIndex = target.RingIndex;
            _pendingTargetTick = targetTick;
            _rewindStartTicks = 0UL;
        }

        public void Cancel()
        {
            _rewindPending = false;
            _pendingRingIndex = -1;
        }

        /// <summary>
        /// Per-frame pulse from the product bootstrap: performs a pending rewind, starts the
        /// fast-forward, and (when due) writes an autosave ring entry. Safe to call while the
        /// game is paused — a rewind is itself a load, not a simulation step.
        /// </summary>
        public void Pulse(World world, bool allowAutosave)
        {
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            EntityQuery timeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            if (timeQuery.IsEmpty)
            {
                timeQuery.Dispose();
                return;
            }
            Entity timeEntity = timeQuery.GetSingletonEntity();
            GameTime time = em.GetComponentData<GameTime>(timeEntity);
            timeQuery.Dispose();

            if (_rewindPending)
            {
                PulseRewind(world, em, time);
                return;
            }

            if (allowAutosave && _autosave.Enabled && time.AbsoluteDay != _lastDaySeen)
            {
                _lastDaySeen = time.AbsoluteDay;
                if (_autosave.ShouldSnapshot(time.AbsoluteDay))
                {
                    WriteAutosave(world, em, time);
                }
            }
        }

        private void PulseRewind(World world, EntityManager em, in GameTime time)
        {
            if (_rewindStartTicks == 0UL)
            {
                SnapshotIoResult result = _snapshots.Load(world, RingPath(_pendingRingIndex));
                if (!result.Success)
                {
                    _rewindPending = false;
                    RewindFailed?.Invoke(result.Error);
                    SimLogs.Push(LogCategory.Time, SimLogCodes.SnapshotLoaded, 0UL, 0f, 1f);
                    return;
                }
                SimLogs.Push(LogCategory.Time, SimLogCodes.SnapshotLoaded, result.StateHash);
                _rewindStartTicks = 1UL;
                StartFastForward(em);
                return;
            }

            if (time.TotalTicks >= _pendingTargetTick)
            {
                _rewindPending = false;
                _pendingRingIndex = -1;
                SimLogs.Push(LogCategory.Time, SimLogCodes.RewindCompleted, time.TotalTicks);
                RewindCompleted?.Invoke(time.TotalTicks);
            }
        }

        private void StartFastForward(EntityManager em)
        {
            EntityQuery planetQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            bool hasPlanet = !planetQuery.IsEmpty;
            if (hasPlanet && em.HasComponent<TimeScrubControl>(planetQuery.GetSingletonEntity()))
            {
                Entity planet = planetQuery.GetSingletonEntity();
                TimeScrubControl scrub = em.GetComponentData<TimeScrubControl>(planet);
                scrub.Active = 1;
                scrub.PauseDuringScrub = 1;
                scrub.TargetTick = _pendingTargetTick;
                scrub.FrameBudgetMs = 8f;
                em.SetComponentData(planet, scrub);
            }
            planetQuery.Dispose();
        }

        private void WriteAutosave(World world, EntityManager em, in GameTime time)
        {
            int ringIndex = _autosave.RingCursor;
            SnapshotHeader header = BuildHeader(em, time, _snapshots.RingSize);
            header.Flags |= SnapshotFlags.AutoSnapshot | SnapshotFlags.RewindCheckpoint;
            SnapshotIoResult result = _snapshots.WriteRing(world, ringIndex, header);
            if (result.Success)
            {
                _autosave.RecordSnapshot(time.AbsoluteDay, _snapshots.RingSize);
                SimLogs.Push(LogCategory.Time, SimLogCodes.SnapshotWritten, time.TotalTicks, result.Milliseconds, ringIndex);
            }
        }

        /// <summary>Builds a header for the current world state (saves, autosaves, exports).</summary>
        public SnapshotHeader BuildHeader(EntityManager em, in GameTime time, int ringSize)
        {
            EntityQuery settingsQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WorldSettingsData>());
            EntityQuery planetQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            EntityQuery organismQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            EntityQuery speciesQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SpeciesPopulationRecord>());

            WorldSettingsData settings = settingsQuery.IsEmpty ? default : settingsQuery.GetSingleton<WorldSettingsData>();
            ClockConfig clock = settings.SecondsPerGameDay == 0u ? CalendarMath.Default : settings.ToClockConfig();
            SimDate date = CalendarMath.FromTicks(time.TotalTicks, clock);

            var header = new SnapshotHeader
            {
                FormatVersion = SnapshotHeader.CurrentFormatVersion,
                AppVersion = BuildInfo.DefaultVersion,
                GitHash = BuildInfo.Editor.GitHash,
                WorldSeed = settings.WorldSeed,
                WorldName = SaveNames.DefaultWorldName,
                TotalTicks = time.TotalTicks,
                AbsoluteDay = time.AbsoluteDay,
                Year = date.Year,
                Season = (byte)date.Season,
                SimTicksPerSecond = settings.SimTicksPerSecond,
                SecondsPerGameDay = settings.SecondsPerGameDay,
                DaysPerSeason = settings.DaysPerSeason,
                SeasonsPerYear = settings.SeasonsPerYear,
                OrganismCount = organismQuery.CalculateEntityCount(),
                SpeciesCount = speciesQuery.IsEmpty ? 0 : em.GetBuffer<SpeciesPopulationRecord>(speciesQuery.GetSingletonEntity()).Length,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                Locale = settings.Locale,
            };
            if (!planetQuery.IsEmpty)
            {
                Entity planet = planetQuery.GetSingletonEntity();
                header.BalanceHash = BalanceHash.Compute(clock, 1f, 1f, 1f);
                if (em.HasComponent<TerrainVersion>(planet))
                {
                    header.CatalogHash ^= em.GetComponentData<TerrainVersion>(planet).Value;
                }
            }
            settingsQuery.Dispose();
            planetQuery.Dispose();
            organismQuery.Dispose();
            speciesQuery.Dispose();
            return header;
        }

        private string RingPath(int index) =>
            System.IO.Path.Combine(_snapshots.SaveDirectory, SaveNames.RingFileName(index, _snapshots.RingSize));

        /// <summary>Writes a manual slot save and returns the result (save panel calls this).</summary>
        public SnapshotIoResult SaveToSlot(World world, int slot, string worldName)
        {
            EntityManager em = world.EntityManager;
            EntityQuery timeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            if (timeQuery.IsEmpty)
            {
                timeQuery.Dispose();
                return SnapshotIoResult.Fail("no simulation clock");
            }
            GameTime time = timeQuery.GetSingleton<GameTime>();
            timeQuery.Dispose();

            SnapshotHeader header = BuildHeader(em, time, _snapshots.RingSize);
            if (!string.IsNullOrEmpty(worldName)) header.WorldName = SaveNames.Sanitize(worldName);
            SnapshotIoResult result = _snapshots.WriteSlot(world, slot, header);
            if (result.Success)
            {
                _autosave.ResetTimer(time.AbsoluteDay);
                SimLogs.Push(LogCategory.Time, SimLogCodes.SnapshotWritten, time.TotalTicks, result.Milliseconds, 100u + (uint)slot);
            }
            return result;
        }

        /// <summary>Loads a manual slot (save panel calls this).</summary>
        public SnapshotIoResult LoadSlot(World world, int slot)
        {
            SnapshotIoResult result = _snapshots.LoadSlot(world, slot);
            SimLogs.Push(LogCategory.Time, SimLogCodes.SnapshotLoaded, result.StateHash, result.Success ? 1f : 0f);
            return result;
        }
    }
}
