// Ecosphere — Action score evaluation for utility AI (stage 05).
// Score(action) = Σ_need urgency × action efficacy × feasibility × genome MLP bias.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [BurstCompile]
    public static class UtilityScoring
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EvaluateAction(
            CreatureAction action,
            in NeedsData needs,
            in NeedWeightsData weights,
            in SensoryData senses,
            in ActionBiases mlpBiases,
            float effectiveTemp,
            float dayFraction,
            bool isNight,
            float dietHerbivory,
            float dietCarnivory,
            float dietScavenging,
            float endothermy,
            out byte dominantNeed)
        {
            float energyU = (1.0f - needs.Energy) * weights.Energy;
            float hydrationU = (1.0f - needs.Hydration) * weights.Hydration;
            float comfortU = (1.0f - needs.ThermalComfort) * weights.ThermalComfort;
            float restU = (1.0f - needs.Rest) * weights.Rest;
            float safetyU = (1.0f - needs.Safety) * weights.Safety;
            float socialU = (1.0f - needs.Social) * weights.Social;
            float reproU = (1.0f - needs.Reproduction) * weights.Reproduction;
            float exploreU = (1.0f - needs.Exploration) * weights.Exploration;

            // Find dominant need for debug explanation
            dominantNeed = 0;
            float maxU = energyU;
            if (hydrationU > maxU) { maxU = hydrationU; dominantNeed = 1; }
            if (comfortU > maxU) { maxU = comfortU; dominantNeed = 2; }
            if (restU > maxU) { maxU = restU; dominantNeed = 3; }
            if (safetyU > maxU) { maxU = safetyU; dominantNeed = 4; }
            if (socialU > maxU) { maxU = socialU; dominantNeed = 5; }
            if (reproU > maxU) { maxU = reproU; dominantNeed = 6; }
            if (exploreU > maxU) { maxU = exploreU; dominantNeed = 7; }

            float needScore = 0f;
            float feasibility = 1f;

            switch (action)
            {
                case CreatureAction.Flee:
                    // Flee driven by Safety urgency & predator/hazard proximity
                    needScore = safetyU * 2.5f;
                    feasibility = (senses.NearestPredatorDist < 15.0f || senses.WeatherHazard > 0.6f) ? 1.0f : 0.05f;
                    break;

                case CreatureAction.TakeShelter:
                    // Driven by thermal comfort, safety, storm hazard
                    needScore = (comfortU + safetyU) * 1.5f;
                    feasibility = (senses.WeatherHazard > 0.5f || needs.ThermalComfort < 0.3f) ? 1.0f : 0.1f;
                    break;

                case CreatureAction.Graze:
                    // Only herbivores/omnivores graze
                    needScore = energyU * (dietHerbivory * 1.5f);
                    feasibility = (senses.FoodQuantity > 0.01f) ? 1.0f : 0.1f;
                    break;

                case CreatureAction.Forage:
                    needScore = energyU * (dietHerbivory * 1.2f);
                    feasibility = (senses.FoodQuantity > 0.005f) ? 0.9f : 0.2f;
                    break;

                case CreatureAction.Hunt:
                    // Only carnivores/omnivores hunt
                    needScore = energyU * (dietCarnivory * 1.8f);
                    feasibility = (dietCarnivory > 0.15f && senses.NearestPreyDist < 30.0f) ? 1.0f : 0.05f;
                    break;

                case CreatureAction.Scavenge:
                    needScore = energyU * (dietScavenging * 1.5f);
                    feasibility = (dietScavenging > 0.1f) ? 0.8f : 0.1f;
                    break;

                case CreatureAction.Drink:
                    needScore = hydrationU * 2.0f;
                    feasibility = (senses.WaterDistance < 25.0f) ? 1.0f : 0.2f;
                    break;

                case CreatureAction.Sleep:
                    needScore = restU * 2.0f;
                    feasibility = (isNight || needs.Rest < 0.25f) && needs.Safety > 0.4f ? 1.0f : 0.2f;
                    break;

                case CreatureAction.Rest:
                    needScore = restU * 1.4f;
                    feasibility = (needs.Safety > 0.3f) ? 0.9f : 0.2f;
                    break;

                case CreatureAction.Bask:
                    // Ectotherms basking in sun to warm up
                    needScore = comfortU * 1.5f;
                    feasibility = (endothermy < 0.5f && !isNight && effectiveTemp < 25f && needs.Safety > 0.4f) ? 1.0f : 0.05f;
                    break;

                case CreatureAction.Migrate:
                    // Driven by severe comfort urgency or seasonal change
                    needScore = comfortU * 1.6f;
                    feasibility = (senses.ComfortGradient > 0.1f || needs.ThermalComfort < 0.35f) ? 1.0f : 0.1f;
                    break;

                case CreatureAction.Socialize:
                    needScore = socialU * 1.2f;
                    feasibility = (needs.Safety > 0.5f && needs.Energy > 0.4f) ? 0.7f : 0.1f;
                    break;

                case CreatureAction.SeekMate:
                    needScore = reproU * 1.3f;
                    feasibility = (needs.Safety > 0.5f && needs.Energy > 0.5f) ? 0.6f : 0.05f;
                    break;

                case CreatureAction.Explore:
                    needScore = exploreU * 1.0f;
                    feasibility = (needs.Safety > 0.5f && needs.Energy > 0.5f && needs.Hydration > 0.5f) ? 0.7f : 0.1f;
                    break;

                case CreatureAction.Wander:
                default:
                    // Default baseline fallback action
                    needScore = 0.2f;
                    feasibility = 0.5f;
                    break;
            }

            // Genome MLP bias: [-1..1] mapped to multiplier [0.3..1.7]
            float mlpBias = mlpBiases.GetBias(action);
            float mlpMultiplier = 1.0f + mlpBias * 0.7f;

            float score = needScore * feasibility * mlpMultiplier;
            return math.max(0.01f, score);
        }
    }
}
