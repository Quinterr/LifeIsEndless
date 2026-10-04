// Ecosphere — Resource pools, feeding, predation, Detritus decay, and Soil Fertility (stage 05).
// Sun -> Plants -> Herbivores -> Predators -> Detritus -> Fertility -> Plants.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [BurstCompile]
    public static class ResourceEcologyMath
    {
        /// <summary>
        /// Grazing/browsing on cell vegetation biomass.
        /// Decrements pool with efficiency from diet genes.
        /// Restores Energy and Hydration for juicy plants.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Graze(
            ref CellResources cellRes,
            ref NeedsData needs,
            float dietHerbivory,
            float digestiveEfficiency,
            float mouthSize,
            float maxBite = 0.05f)
        {
            if (cellRes.VegetationBiomass <= 0.001f || dietHerbivory <= 0.05f)
                return 0f;

            float bite = math.min(cellRes.VegetationBiomass, maxBite * math.max(0.5f, mouthSize));
            cellRes.VegetationBiomass -= bite;

            // Nutrition yield scaled by diet suitability and digestive efficiency
            float efficiency = dietHerbivory * math.max(0.2f, digestiveEfficiency);
            float energyGained = bite * efficiency * 4.0f;
            float waterGained = bite * efficiency * 1.5f; // Juicy vegetation restores water!

            needs.Energy = math.saturate(needs.Energy + energyGained);
            needs.Hydration = math.saturate(needs.Hydration + waterGained);

            return bite;
        }

        /// <summary>
        /// Foraging for seeds / fallen fruit / general forage.
        /// Restores Energy.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Forage(
            ref CellResources cellRes,
            ref NeedsData needs,
            float dietHerbivory,
            float digestiveEfficiency,
            float maxEat = 0.04f)
        {
            float available = cellRes.VegetationBiomass * 0.5f;
            if (available <= 0.001f || dietHerbivory <= 0.05f)
                return 0f;

            float eaten = math.min(available, maxEat);
            cellRes.VegetationBiomass -= eaten;

            float yield = eaten * dietHerbivory * math.max(0.2f, digestiveEfficiency) * 3.5f;
            needs.Energy = math.saturate(needs.Energy + yield);
            return eaten;
        }

        /// <summary>
        /// Scavenging detritus / carcasses.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Scavenge(
            ref CellResources cellRes,
            ref NeedsData needs,
            float dietScavenging,
            float digestiveEfficiency,
            float maxEat = 0.05f)
        {
            if (cellRes.Detritus <= 0.001f || dietScavenging <= 0.05f)
                return 0f;

            float eaten = math.min(cellRes.Detritus, maxEat);
            cellRes.Detritus -= eaten;

            float yield = eaten * dietScavenging * math.max(0.2f, digestiveEfficiency) * 4.0f;
            needs.Energy = math.saturate(needs.Energy + yield);
            return eaten;
        }

        /// <summary>
        /// Drinking fresh water from cell fresh-water pool or soil moisture.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Drink(
            ref CellResources cellRes,
            ref NeedsData needs,
            float maxSip = 0.15f)
        {
            float available = cellRes.FreshWater;
            if (available <= 0.001f) return 0f;

            float drank = math.min(available, maxSip);
            cellRes.FreshWater -= drank * 0.2f; // Slight depletion of surface water
            needs.Hydration = math.saturate(needs.Hydration + drank * 2.0f);
            return drank;
        }

        /// <summary>
        /// Predator-prey attack handshake:
        /// Attack roll based on predator aggression & speed vs prey safety & speed.
        /// Returns true if kill successful.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ResolvePredatorAttack(
            float predatorSpeed,
            float predatorAggression,
            float preySpeed,
            float preySafety,
            ref RngState rng)
        {
            // Attack score: predator speed & aggression + random roll
            float attackScore = predatorSpeed * 1.5f + predatorAggression * 0.5f + rng.NextFloat01() * 0.4f;
            // Defense score: prey speed & safety alertness + random roll
            float defenseScore = preySpeed * 1.5f + preySafety * 0.5f + rng.NextFloat01() * 0.4f;

            return attackScore > defenseScore;
        }

        /// <summary>
        /// Detritus breakdown into soil fertility.
        /// Closes the ecosystem loop: detritus -> fertility -> plant growth.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecayDetritusToFertility(
            ref CellResources cellRes,
            float temperatureC,
            float soilMoisture,
            float dt)
        {
            if (cellRes.Detritus <= 0.0001f) return;

            // Warmer, moist soil decomposes detritus faster
            float tempFactor = math.clamp((temperatureC + 5f) / 30f, 0.1f, 2.0f);
            float moistFactor = math.clamp(soilMoisture * 1.5f, 0.1f, 1.5f);
            float decayRate = 0.002f * tempFactor * moistFactor * dt;

            float decayed = math.min(cellRes.Detritus, cellRes.Detritus * decayRate);
            cellRes.Detritus -= decayed;

            // Increase soil fertility (capped at 1.0)
            cellRes.SoilFertility = math.saturate(cellRes.SoilFertility + decayed * 0.5f);
        }
    }
}
