// Ecosphere — Morphology-bound locomotion on the sphere (stage 05).
// Great-circle tangent space steering, morphology-derived speeds/accel/costs,
// surface & wind modulation, limb phase offset, gait animation.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [BurstCompile]
    public static class LocomotionMath
    {
        /// <summary>
        /// Derive locomotion params from morphology and phenotype (brief §4.5).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DeriveParams(
            in Phenotype p,
            float size,
            out LocomotionMode defaultMode,
            out float baseSpeed,
            out float strideLength,
            out float strideFrequency)
        {
            float limbs = math.round(p.LimbCount);
            float fins = math.round(p.FinCount);

            // Determine primary mode
            if (fins >= 2 && p.FinSize > 0.4f && limbs < 2)
            {
                // Mostly aquatic or flyer
                defaultMode = (p.Symmetry > 0.7f && p.FinSize > 0.6f) ? LocomotionMode.Fly : LocomotionMode.Swim;
            }
            else if (limbs >= 2)
            {
                defaultMode = LocomotionMode.Walk;
            }
            else if (fins >= 1)
            {
                defaultMode = LocomotionMode.Swim;
            }
            else
            {
                // Slithering/crawling walk
                defaultMode = LocomotionMode.Walk;
            }

            float bodyScale = math.max(0.1f, (p.BodyLengthScale + p.TorsoLength * 0.5f) * size * math.max(0.2f, p.SizeMultiplier));

            // Stride length ≈ 2 * LimbLength * bodyScale (or fin flap)
            strideLength = math.max(0.05f, 2.0f * math.max(0.1f, p.LimbLength) * bodyScale);

            // Stride frequency ∝ MetabolicRate * (1 - 0.3 * Endothermy-cost)
            strideFrequency = math.max(0.2f, (p.MetabolicRate * 1.5f + 0.5f) * (1.0f - 0.3f * math.saturate(p.Endothermy)));

            // Base speed = strideLength * strideFrequency
            baseSpeed = strideLength * strideFrequency * 0.5f; // scaled to planet cell coordinate steps
        }

        /// <summary>
        /// Compute movement on sphere surface towards target cell.
        /// Accounts for terrain slope, wind (headwind/tailwind), rest need (tiredness),
        /// and juvenile stage speed penalty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 GreatCircleTangentDirection(float3 currentPos, float3 targetPos)
        {
            float3 normCurrent = math.normalizesafe(currentPos);
            float3 toTarget = targetPos - currentPos;
            // Project onto tangent plane of current sphere surface
            float3 tangent = toTarget - normCurrent * math.dot(toTarget, normCurrent);
            return math.normalizesafe(tangent);
        }

        /// <summary>
        /// Speed modifier based on environmental and internal state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputeEffectiveSpeed(
            float baseSpeed,
            LocomotionMode mode,
            float restNeed,
            float stageMultiplier,
            float3 moveDirection,
            float3 windVector,
            float slopeElevationDelta,
            bool isSnow)
        {
            // Tired creatures move slower
            float stamina = math.clamp(restNeed, 0.25f, 1.0f);

            // Slope penalty (uphill costs speed, downhill gives slight boost)
            float slopeMod = 1.0f - math.clamp(slopeElevationDelta * 3.0f, -0.3f, 0.7f);

            // Wind effect: dot product between movement and wind
            float windDot = math.dot(moveDirection, windVector);
            float windSpeed = math.length(windVector);
            float windMod = 1.0f;
            if (mode == LocomotionMode.Fly)
            {
                // Fliers heavily affected by wind
                windMod = 1.0f + (windDot / math.max(0.1f, windSpeed + 1f)) * 0.6f;
            }
            else
            {
                // Walkers: headwind slows down, tailwind mild push
                windMod = 1.0f + math.clamp(windDot * 0.05f, -0.4f, 0.2f);
            }

            // Surface friction
            float surfaceMod = isSnow ? 0.7f : 1.0f;

            return baseSpeed * stamina * stageMultiplier * slopeMod * math.max(0.2f, windMod) * surfaceMod;
        }

        /// <summary>
        /// Procedural gait phase advancement and limb phase calculation.
        /// Limb phase offset: diagonal pairs antiphase for 4+ limbs
        /// offset = 0.5 * (pairIndex mod 2) + 0.25 * (side mod 2)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float UpdateGaitPhase(float currentPhase, float strideFreq, float actualSpeed, float dt)
        {
            float cycleSpeed = (actualSpeed > 1e-4f) ? strideFreq : 0f;
            return math.frac(currentPhase + cycleSpeed * dt);
        }
    }
}
