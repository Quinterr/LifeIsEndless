// Ecosphere — Life ECS components and structs (stage 05).
// Needs, status effects, behavior, memory, locomotion, resource pools, scent, death.

using System;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    /// <summary>
    /// Needs component for both animals and plants.
    /// Inverted urgency convention: 1.0 = fully satisfied (no urgency), 0.0 = completely depleted (critical urgency).
    /// Urgency used by utility AI is (1.0f - value).
    /// </summary>
    public struct NeedsData : IComponentData
    {
        // Animals use:
        public float Energy;           // 0..1 (1 = full, 0 = starving)
        public float Hydration;        // 0..1 (1 = hydrated, 0 = dehydrated)
        public float ThermalComfort;   // 0..1 (1 = ideal temp, 0 = extreme cold/heat)
        public float Rest;             // 0..1 (1 = well-rested, 0 = exhausted)
        public float Safety;           // 0..1 (1 = safe, 0 = imminent predator/hazard)
        public float Social;           // 0..1 (1 = socially satisfied, 0 = lonely)
        public float Reproduction;     // 0..1 (1 = satisfied, 0 = high mating urge)
        public float Exploration;      // 0..1 (1 = content, 0 = curious/restless)

        public static NeedsData CreateAnimalDefault()
        {
            return new NeedsData
            {
                Energy = 0.8f,
                Hydration = 0.8f,
                ThermalComfort = 0.8f,
                Rest = 0.9f,
                Safety = 0.9f,
                Social = 0.7f,
                Reproduction = 0.5f,
                Exploration = 0.6f
            };
        }

        public static NeedsData CreatePlantDefault()
        {
            // Plants map: Energy -> Light, Hydration -> Water, Rest -> Nutrients, ThermalComfort -> Temperature
            return new NeedsData
            {
                Energy = 0.8f,         // Light
                Hydration = 0.8f,      // Water
                ThermalComfort = 0.8f, // Temperature
                Rest = 0.8f,           // Nutrients / Soil fertility
                Safety = 1.0f,
                Social = 1.0f,
                Reproduction = 0.5f,
                Exploration = 1.0f
            };
        }

        // Helpers for plant semantics
        public float Light { get => Energy; set => Energy = value; }
        public float Water { get => Hydration; set => Hydration = value; }
        public float Nutrients { get => Rest; set => Rest = value; }
    }

    /// <summary>
    /// Needs importance weights derived from genome and archetypes.
    /// </summary>
    public struct NeedWeightsData : IComponentData
    {
        public float Energy;
        public float Hydration;
        public float ThermalComfort;
        public float Rest;
        public float Safety;
        public float Social;
        public float Reproduction;
        public float Exploration;
    }

    /// <summary>
    /// Status effect enableable components representing crossed thresholds.
    /// Used by systems for modifiers and death checks.
    /// </summary>
    public struct Starving : IComponentData, IEnableableComponent { }
    public struct Dehydrated : IComponentData, IEnableableComponent { }
    public struct Exhausted : IComponentData, IEnableableComponent { }
    public struct Freezing : IComponentData, IEnableableComponent { }
    public struct Overheating : IComponentData, IEnableableComponent { }
    public struct Panicked : IComponentData, IEnableableComponent { }
    public struct PlantDormant : IComponentData, IEnableableComponent { }

    /// <summary>
    /// Archetype classification derived from diet and kingdom at phenotype time.
    /// </summary>
    public enum OrganismArchetype : byte
    {
        Plant = 0,
        Herbivore = 1,
        Carnivore = 2,
        Omnivore = 3,
        Scavenger = 4,
        Detritivore = 5,
        FilterFeeder = 6
    }

    public struct ArchetypeData : IComponentData
    {
        public OrganismArchetype Value;
    }

    /// <summary>
    /// Actions available to utility AI.
    /// </summary>
    public enum CreatureAction : byte
    {
        Wander = 0,
        Forage = 1,
        Graze = 2,
        Hunt = 3,
        Scavenge = 4,
        Drink = 5,
        Rest = 6,
        Sleep = 7,
        Flee = 8,
        Explore = 9,
        Migrate = 10,
        Socialize = 11,
        SeekMate = 12,
        Bask = 13,
        TakeShelter = 14
    }

    public enum LocomotionMode : byte
    {
        None = 0,
        Walk = 1,
        Swim = 2,
        Fly = 3
    }

    /// <summary>
    /// Current behavior decision and debug explanation. Read by Stage-07 inspector and tests.
    /// </summary>
    public struct BehaviorData : IComponentData
    {
        public CreatureAction CurrentAction;
        public float ActionScore;
        public LocomotionMode Locomotion;
        public float3 TargetDirection;
        public int TargetCell;
        public byte DominantNeedIndex; // 0:Energy, 1:Hydration, 2:ThermalComfort, 3:Rest, 4:Safety, 5:Social, 6:Reproduction, 7:Exploration
        public byte DecisionCooldown;  // ticks until re-eval
        public FixedString64Bytes Explanation; // e.g. "Flee: 0.82 — predator near, Safety 0.90"
    }

    /// <summary>
    /// Sensory perception snapshot refreshed every few ticks or on interrupt.
    /// </summary>
    public struct SensoryData : IComponentData
    {
        public int NearestFoodCell;
        public float FoodQuantity;
        public int NearestWaterCell;
        public float WaterDistance;
        public int NearestPredatorCell;
        public float NearestPredatorDist;
        public Entity NearestPreyEntity;
        public int NearestPreyCell;
        public float NearestPreyDist;
        public float ComfortGradient;     // delta comfort in best neighbor cell
        public int BestComfortCell;
        public float WeatherHazard;       // storminess / blizzard intensity
        public byte NextSenseTick;
    }

    /// <summary>
    /// Tiny memory structure (4-8 fields, strictly no lists).
    /// </summary>
    public struct CreatureMemory : IComponentData
    {
        public int LastFoodCell;
        public int LastWaterCell;
        public int HomeCell;
        public ulong LastThreatTick;
        public int ThreatCell;
    }

    /// <summary>
    /// Locomotion capabilities and state on the sphere.
    /// </summary>
    public struct LocomotionData : IComponentData
    {
        public float3 Position;          // Cartesian 3D on planet sphere
        public float3 Velocity;
        public float CurrentSpeed;
        public float BaseSpeed;
        public float StrideLength;
        public float StrideFrequency;
        public float GaitPhase;          // 0..1 procedural gait animation phase
        public LocomotionMode Mode;
        public float ObstacleAvoidance;
    }

    /// <summary>
    /// Dynamic buffer on planet entity holding resource pools per cell.
    /// Life owns this buffer.
    /// </summary>
    public struct CellResources : IBufferElementData
    {
        public float VegetationBiomass;  // Added by plants, decremented by herbivores/foragers
        public float Detritus;           // Added by deaths, decays to fertility
        public float FreshWater;         // Replenished by rain / soil moisture
        public float Plankton;           // Ocean cells
        public float SoilFertility;      // 0..1 fed by detritus breakdown, feeds plant growth
    }

    /// <summary>
    /// Scent field dynamic buffer on planet entity (food, water, mates).
    /// Life writes scents; Climate advects by wind; smellers read downwind.
    /// </summary>
    public struct CellScent : IBufferElementData
    {
        public float FoodScent;
        public float WaterScent;
        public float MateScent;
        public float ThreatScent;
    }

    /// <summary>
    /// Buffer of seeds accumulated on a cell with wind/current dispersal offset.
    /// </summary>
    public struct CellSeedBank : IBufferElementData
    {
        public uint PlantGenomeSeed;
        public float Viability;
        public ulong DispersalTick;
    }

    public enum CauseOfDeath : byte
    {
        OldAge = 0,
        Starvation = 1,
        Dehydration = 2,
        ExposureFreezing = 3,
        ExposureOverheating = 4,
        Exhaustion = 5,
        Predation = 6,
        SevereStorm = 7
    }

    /// <summary>
    /// Record logged when an organism dies.
    /// </summary>
    public struct DeathRecord : IBufferElementData
    {
        public ulong Tick;
        public CauseOfDeath Cause;
        public ulong AgeTicks;
        public int CellIndex;
        public GeneKingdom Kingdom;
    }

    /// <summary>
    /// Plant-specific biomass allocation and growth tracking.
    /// </summary>
    public struct PlantLifeData : IComponentData
    {
        public float AccumulatedBiomass;
        public float RootBiomass;
        public float StemBiomass;
        public float LeafBiomass;
        public float FruitBiomass;
        public float PhotosynthesisRate;
        public float RespirationCost;
        public float FrostDamage;
        public float DroughtDamage;
        public byte IsDormant;
    }

    /// <summary>
    /// Tag marking dead entity for ECB destruction and detritus deposit.
    /// </summary>
    public struct DeadTag : IComponentData
    {
        public CauseOfDeath Cause;
    }
}
