using Ecosphere.Core.Simulation;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>Stable save/query id. Unity Entity indices are intentionally not exposed as lineage ids.</summary>
    public struct OrganismIdentity : IComponentData
    {
        public ulong OrganismId;
    }

    public struct SpeciesIdentity : IComponentData
    {
        public uint SpeciesId;
    }

    /// <summary>Per-organism lineage retained while alive; history is mirrored to the world buffer.</summary>
    public struct LineageData : IComponentData
    {
        public ulong MotherId;
        public ulong FatherId;
        public ulong BirthTick;
        public uint Generation;
        public uint SpeciesId;
    }

    /// <summary>Incremental snapshot used to maintain cell/species counters on births, moves and deaths.</summary>
    public struct OrganismEvolutionSnapshot : IComponentData
    {
        public int CellIndex;
        public uint SpeciesId;
        public EvolutionTraitVector Traits;
        public float Biomass;
        public float MutationLoad;
        public byte Counted;
    }

    /// <summary>Movement delta emitted only when SphereLocomotion crosses a cell boundary.</summary>
    public struct PopulationMovementEvent : IBufferElementData
    {
        public Entity Organism;
        public ulong OrganismId;
        public uint SpeciesId;
        public int FromCell;
        public int ToCell;
        public float Biomass;
    }

    /// <summary>One occupied species/cell bin; maintained incrementally, never rebuilt per tick.</summary>
    public struct CellSpeciesPopulation : IBufferElementData
    {
        public uint SpeciesId;
        public int CellIndex;
        public int Population;
        public int Births;
        public int Deaths;
        public float Biomass;
    }

    /// <summary>Serializable running species moments and lifecycle metadata.</summary>
    public struct SpeciesPopulationRecord : IBufferElementData
    {
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public GeneKingdom Kingdom;
        public int Population;
        public int StoredGenomeCount;
        public ulong FoundedAtTick;
        public ulong ExtinctAtTick;
        public EvolutionTraitVector MeanTraits;
        public EvolutionTraitVector M2Traits;
        public EvolutionTraitVector ReproducerMeanTraits;
        public int ReproducerCount;
        public float GenomeDiversity;
        public float EffectivePopulationSize; // latest annual distinct-breeder proxy
        public float MutationLoad;
        public byte IsExtinct;
    }

    public struct EvolutionPhylogenyEdge : IBufferElementData
    {
        public uint ParentSpeciesId;
        public uint ChildSpeciesId;
        public ulong Tick;
        public EvolutionCause Cause;
    }

    public struct EvolutionEventElement : IBufferElementData
    {
        public EvolutionEventRecord Value;
    }

    public struct EvolutionMetricElement : IBufferElementData
    {
        public EvolutionMetricsRecord Value;
    }

    /// <summary>Append-only lineage tombstone for ancestors whose organism entities were destroyed.</summary>
    public struct LineageRecord : IBufferElementData
    {
        public ulong OrganismId;
        public ulong MotherId;
        public ulong FatherId;
        public ulong BirthTick;
        public uint Generation;
        public uint SpeciesId;
        public GeneKingdom Kingdom;
    }

    /// <summary>World-level evolution cursors and budgets, stored on the planet entity.</summary>
    public struct EvolutionStateData : IComponentData
    {
        public ulong NextOrganismId;
        public uint NextSpeciesId;
        public uint ConceptionOrdinal;
        public ulong LastPopulationTick;
        public ulong LastMetricsYear;
        public int MaxConceptionsPerTick;
        public int MaxEggUpdatesPerTick;
        public float MutationMultiplier;
        public float StructuralMutationChance;
        public float SelectionPressure;
        public float CompatibilityThreshold;
        public float SpeciationDriftThreshold;
        public float SpeciationFailureFraction;
        public int MinimumSpeciationPopulation;
        public int MaxTrackedEvents;
        public int MaxTrackedMetrics;

        public static EvolutionStateData Default => new EvolutionStateData
        {
            NextOrganismId = 1UL,
            NextSpeciesId = 1u,
            MaxConceptionsPerTick = 8,
            MaxEggUpdatesPerTick = 32,
            MutationMultiplier = 1f,
            StructuralMutationChance = 0.0005f,
            SelectionPressure = 1f,
            CompatibilityThreshold = GenomeEvolutionMath.DefaultCompatibilityThreshold,
            SpeciationDriftThreshold = 0.12f,
            SpeciationFailureFraction = 0.6f,
            MinimumSpeciationPopulation = 8,
            MaxTrackedEvents = 4096,
            MaxTrackedMetrics = 65536
        };
    }

    public enum StoredGenomeKind : byte
    {
        LiveBirth = 0,
        Egg = 1,
        PlantSeed = 2
    }

    /// <summary>Off-world embryo/egg/seed genome; only promoted to GenomeHeader at birth or germination.</summary>
    public struct StoredGenomeData : IComponentData
    {
        public GeneKingdom Kingdom;
        public StoredGenomeKind Kind;
        public uint GenomeSeed;
        public uint SpeciesId;
        public uint Generation;
        public int CellIndex;
        public Entity Mother;
        public Entity Father;
        public ulong MotherId;
        public ulong FatherId;
        public ulong CreatedTick;
        public ulong DueTick;
        public int Chr0Start;
        public int Chr1Start;
        public int Chr2Start;
        public int Chr3Start;
        public float IncubationProgress;
        public float Viability;
        public float ParentCare;
        public float MutationLoad;
        public byte Pollinated;
    }

    [InternalBufferCapacity(130)]
    public struct StoredGenomeGene : IBufferElementData
    {
        public Gene Value;
    }

    public struct OrganismReproductionState : IComponentData
    {
        public Entity CourtshipPartner;
        public ulong CourtshipStartTick;
        public ulong NextEligibleTick;
        public Entity GestatingGenome;
        public ulong GestationDueTick;
        public ulong LastPlantSeedTick;
        public int LastEvolutionReproducerYear;
        public uint LastEvolutionReproducerSpeciesId;
    }

    public struct ParentalCareData : IComponentData
    {
        public Entity Caregiver;
        public ulong CaregiverId;
        public ulong NextFeedTick;
        public float CareRemaining;
    }

    /// <summary>Marks a newborn/founder until it has been added to incremental census bins.</summary>
    public struct PopulationUncounted : IComponentData { }

    /// <summary>Marks an entity whose death was already applied to population counters.</summary>
    public struct PopulationDeathRecorded : IComponentData { }
}
