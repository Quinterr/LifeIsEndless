// Ecosphere — Needs drain and refill math (stage 05).
// Inverted urgency: 1.0 = satisfied, 0.0 = depleted.
// Drains: metabolic rate * temperature curve * activity level * size * life stage.
// Status thresholds: Starving, Dehydrated, Exhausted, Freezing, Overheating, Panicked.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [BurstCompile]
    public static class NeedsMath
    {
        public const float CriticalThreshold = 0.15f; // Status effect triggers below this
        public const float PanicSafetyThreshold = 0.25f;

        /// <summary>
        /// Calculate metabolic temperature factor.
        /// Endotherm vs ectotherm curve against effective temperature.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TemperatureMetabolismMultiplier(
            float effectiveTemp,
            float endothermy,
            float tempOptimum,
            float tempTolerance)
        {
            if (endothermy > 0.5f)
            {
                // Endotherm: active thermoregulation. When cold, burn more energy to keep warm!
                // When optimal, metabolic rate is nominal. When very hot, slight increase for panting/cooling.
                float delta = effectiveTemp - tempOptimum;
                if (delta < 0f)
                {
                    // Cold: +5% metabolic cost per degree below optimum
                    return 1.0f + math.min(1.5f, math.abs(delta) * 0.05f);
                }
                else
                {
                    // Hot: +2% metabolic cost per degree above optimum
                    return 1.0f + math.min(1.0f, delta * 0.02f);
                }
            }
            else
            {
                // Ectotherm: Q10-like rate. Cold slows down metabolism, warm speeds it up.
                float delta = effectiveTemp - tempOptimum;
                float factor = 1.0f + delta * 0.035f;
                return math.clamp(factor, 0.2f, 2.5f);
            }
        }

        /// <summary>
        /// Calculate thermal comfort need (1.0 = comfortable, 0.0 = severe thermal stress).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeThermalComfort(
            float effectiveTemp,
            float tempOptimum,
            float tempTolerance)
        {
            float delta = math.abs(effectiveTemp - tempOptimum);
            float tol = math.max(3.0f, tempTolerance);
            if (delta <= tol * 0.5f) return 1.0f;
            float stress = (delta - tol * 0.5f) / (tol * 1.5f);
            return math.saturate(1.0f - stress);
        }

        /// <summary>
        /// Drain needs per tick for an animal.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DrainAnimalNeeds(
            ref NeedsData needs,
            in Phenotype p,
            LifeStage stage,
            float effectiveSpeed,
            float effectiveTemp,
            float storminess,
            float dt)
        {
            float tempFactor = TemperatureMetabolismMultiplier(effectiveTemp, p.Endothermy, p.TemperatureOptimum, p.TemperatureTolerance);
            float activityFactor = 1.0f + effectiveSpeed * 2.0f; // Moving costs more
            float sizeFactor = math.max(0.5f, p.Size);
            float stageMultiplier = (stage == LifeStage.Juvenile) ? 1.25f : 1.0f; // Growing juveniles burn more

            float baseMetabolism = p.MetabolicRate * 0.0008f * dt;
            float totalEnergyDrain = baseMetabolism * tempFactor * activityFactor * sizeFactor * stageMultiplier;

            // Water need from WaterNeed gene + temperature evaporation
            float waterBase = math.max(0.2f, p.WaterNeed) * 0.0010f * dt;
            float heatEvap = math.max(0f, effectiveTemp - 20f) * 0.00004f;
            float totalWaterDrain = waterBase * (1.0f + effectiveSpeed * 1.5f) + heatEvap;

            // Rest drain: activity drains rest, stillness preserves it
            float restDrain = (0.0003f + effectiveSpeed * 0.0015f) * dt;

            // Update values (subtracting drains)
            needs.Energy = math.saturate(needs.Energy - totalEnergyDrain);
            needs.Hydration = math.saturate(needs.Hydration - totalWaterDrain);
            needs.Rest = math.saturate(needs.Rest - restDrain);

            // Thermal comfort
            needs.ThermalComfort = ComputeThermalComfort(effectiveTemp, p.TemperatureOptimum, p.TemperatureTolerance);

            // Safety recovers naturally over time if not in hazard
            if (storminess > 0.6f)
            {
                needs.Safety = math.saturate(needs.Safety - (storminess - 0.6f) * 0.005f * dt);
            }
            else
            {
                needs.Safety = math.saturate(needs.Safety + 0.002f * dt);
            }

            // Exploration urgency slowly creeps up
            needs.Exploration = math.saturate(needs.Exploration - 0.0002f * dt);

            // Reproduction urge creeps up in adults
            if (stage == LifeStage.Adult)
            {
                needs.Reproduction = math.saturate(needs.Reproduction - 0.0001f * dt);
            }
        }
    }
}
