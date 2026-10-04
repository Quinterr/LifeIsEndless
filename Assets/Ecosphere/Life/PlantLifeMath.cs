// Ecosphere — Plant biology math (stage 05).
// Photosynthesis, respiration, biomass allocation, seed bank, frost/drought/hail damage, dormancy.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [BurstCompile]
    public static class PlantLifeMath
    {
        /// <summary>
        /// Photosynthesis per tick:
        /// insolation (daylight at cell * season * cloudCover) * leaf mass * water availability
        /// * temperature factor (from genes' optimum/range) - respiration cost.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputePhotosynthesis(
            float daylightInsolation,
            float cloudCover,
            float leafMass,
            float waterAvailability,
            float effectiveTemp,
            float tempOptimum,
            float tempTolerance,
            float metabolicRate,
            out float respirationCost)
        {
            // Daylight insolation reduced by cloud cover
            float light = daylightInsolation * (1.0f - math.clamp(cloudCover * 0.7f, 0f, 0.7f));

            // Temperature curve: bell curve around optimum
            float deltaT = math.abs(effectiveTemp - tempOptimum);
            float tol = math.max(2.0f, tempTolerance);
            float tempFactor = math.exp(-0.5f * (deltaT * deltaT) / (tol * tol));

            // Gross photosynthetic output
            float gross = light * math.max(0.1f, leafMass) * math.clamp(waterAvailability, 0.05f, 1.0f) * tempFactor * 0.02f;

            // Respiration: higher when warm
            float tempResp = math.max(0.2f, 1.0f + (effectiveTemp - 20f) * 0.03f);
            respirationCost = metabolicRate * 0.005f * tempResp;

            return math.max(0f, gross);
        }

        /// <summary>
        /// Biomass allocation decision:
        /// root/stem/leaf/fruit fractions from genes * season bias.
        /// Spring -> leaves, Autumn -> roots/seeds.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ComputeBiomassAllocation(
            Season season,
            in Phenotype p,
            out float rootFrac,
            out float stemFrac,
            out float leafFrac,
            out float fruitFrac)
        {
            float baseRoot = math.max(0.1f, p.RootDepth + p.RootSpread * 0.5f);
            float baseStem = math.max(0.1f, p.StemHeight + p.Woodiness * 0.5f);
            float baseLeaf = math.max(0.1f, p.LeafCount * p.LeafSize);
            float baseFruit = math.max(0.05f, p.FlowerCount * p.FruitCount * 0.5f);

            // Seasonal bias
            switch (season)
            {
                case Season.Spring:
                    baseLeaf *= 2.0f;
                    baseStem *= 1.3f;
                    break;
                case Season.Summer:
                    baseFruit *= 1.5f;
                    baseLeaf *= 1.2f;
                    break;
                case Season.Autumn:
                    baseRoot *= 2.0f;
                    baseFruit *= 2.5f;
                    break;
                case Season.Winter:
                    baseRoot *= 1.5f;
                    baseLeaf *= 0.2f;
                    break;
            }

            float total = baseRoot + baseStem + baseLeaf + baseFruit;
            if (total < 1e-5f)
            {
                rootFrac = 0.25f;
                stemFrac = 0.25f;
                leafFrac = 0.25f;
                fruitFrac = 0.25f;
                return;
            }

            float inv = 1.0f / total;
            rootFrac = baseRoot * inv;
            stemFrac = baseStem * inv;
            leafFrac = baseLeaf * inv;
            fruitFrac = baseFruit * inv;
        }

        /// <summary>
        /// Check damage from frost, drought, storminess.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeWeatherDamage(
            float effectiveTemp,
            float tempOptimum,
            float tempTolerance,
            float soilMoisture,
            float storminess,
            float woodiness)
        {
            float damage = 0f;

            // Frost damage: temp < optimum - tolerance
            float minT = tempOptimum - tempTolerance * 1.5f;
            if (effectiveTemp < minT)
            {
                damage += (minT - effectiveTemp) * 0.002f;
            }

            // Drought damage: soil moisture < 0.15
            if (soilMoisture < 0.15f)
            {
                damage += (0.15f - soilMoisture) * 0.003f;
            }

            // Storm/hail damage: storminess * exposure * (1 - woodiness)
            if (storminess > 0.5f)
            {
                damage += (storminess - 0.5f) * (1.0f - math.clamp(woodiness, 0.1f, 0.9f)) * 0.005f;
            }

            return damage;
        }

        /// <summary>
        /// Dormancy evaluation.
        /// Winter / cold -> drop leaves and near-zero metabolism; resumes in spring.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldBeDormant(Season season, float effectiveTemp, float seasonalGate)
        {
            if (season == Season.Winter) return true;
            if (seasonalGate > 0.4f && effectiveTemp < 5.0f) return true;
            return false;
        }
    }
}
