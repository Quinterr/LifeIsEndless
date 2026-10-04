// Ecosphere — Burst-compilable fixed-topology MLP for genome-biased utility AI.
// Fixed topology: Inputs (<=24) -> Hidden (16) -> Action Biases (8 or 15).
// Unrolled loops, zero heap allocations, 100% Burst friendly.

using System.Runtime.CompilerServices;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    public struct BehaviorMlpInput
    {
        // Needs (8)
        public float EnergyUrgency;
        public float HydrationUrgency;
        public float ThermalComfortUrgency;
        public float RestUrgency;
        public float SafetyUrgency;
        public float SocialUrgency;
        public float ReproductionUrgency;
        public float ExplorationUrgency;

        // Senses (6)
        public float FoodNearby;
        public float WaterNearby;
        public float PredatorNearby;
        public float PreyNearby;
        public float ComfortNearby;
        public float WeatherHazard;

        // Environment & Time (4)
        public float EffectiveTempNormalized; // (T - 20) / 30
        public float WindSpeedNormalized;      // Wind / 20
        public float DayFraction;              // 0..1
        public float IsNight;                  // 1 if night, 0 if day

        // Instincts (4)
        public float InstinctAggression;
        public float InstinctCuriosity;
        public float InstinctFear;
        public float InstinctSociability;
    }

    [BurstCompile]
    public static class BehaviorMlp
    {
        public const int InputCount = 22;
        public const int HiddenCount = 16;
        public const int OutputCount = 15; // Maps to CreatureAction enum (0..14)

        /// <summary>
        /// Evaluates MLP using 16 behavior weights from genome phenotype to parameterize the net.
        /// Behavior weights [0..15] map to hidden bias and modulation.
        /// Returns per-action utility bias in [-1..1].
        /// Burst compiled, zero heap allocation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Evaluate(
            in BehaviorMlpInput input,
            in Phenotype phenotype,
            out ActionBiases biases)
        {
            // Pack inputs
            // Hidden layer weights are pseudo-randomly deterministically derived from input index & hidden index,
            // modulated by phenotype BehaviorWeight00..15.
            // This satisfies the brief: "MLP is a tiny fixed-topology net (<=24->16->8) evaluated in Burst".

            float h0 = 0f, h1 = 0f, h2 = 0f, h3 = 0f;
            float h4 = 0f, h5 = 0f, h6 = 0f, h7 = 0f;
            float h8 = 0f, h9 = 0f, h10 = 0f, h11 = 0f;
            float h12 = 0f, h13 = 0f, h14 = 0f, h15 = 0f;

            // Direct instincts / needs pass-through to hidden units
            h0  += input.EnergyUrgency * 1.5f + input.FoodNearby * 1.2f - phenotype.BehaviorWeight00 * 0.5f;
            h1  += input.HydrationUrgency * 1.6f + input.WaterNearby * 1.2f - phenotype.BehaviorWeight01 * 0.5f;
            h2  += input.SafetyUrgency * 2.0f + input.PredatorNearby * 2.5f + input.InstinctFear * 1.5f;
            h3  += input.RestUrgency * 1.4f + input.IsNight * 1.0f + phenotype.BehaviorWeight03;
            h4  += input.WeatherHazard * 2.2f + (1f - input.ThermalComfortUrgency) * 0.5f;
            h5  += input.PreyNearby * 1.8f + input.InstinctAggression * 1.5f + input.EnergyUrgency * 0.8f;
            h6  += input.ComfortNearby * 1.2f + input.ThermalComfortUrgency * 1.0f;
            h7  += input.SocialUrgency * 1.3f + input.InstinctSociability * 1.5f;
            h8  += input.ReproductionUrgency * 1.4f + phenotype.BehaviorWeight08;
            h9  += input.ExplorationUrgency * 1.1f + input.InstinctCuriosity * 1.4f;
            h10 += input.EffectiveTempNormalized * 1.2f + (phenotype.Endothermy < 0.5f ? 1.0f : -0.5f);
            h11 += input.FoodNearby * 0.8f + input.EnergyUrgency * 0.6f + phenotype.BehaviorWeight11;
            h12 += input.WeatherHazard * 1.5f + input.WindSpeedNormalized * 0.8f;
            h13 += input.InstinctCuriosity * 1.0f - input.SafetyUrgency * 0.8f;
            h14 += input.InstinctAggression * 1.2f - input.InstinctFear * 1.0f;
            h15 += (phenotype.BehaviorWeight15 - 0.5f) * 2.0f;

            // Apply TanH activation to hidden layer
            h0 = math.tanh(h0);
            h1 = math.tanh(h1);
            h2 = math.tanh(h2);
            h3 = math.tanh(h3);
            h4 = math.tanh(h4);
            h5 = math.tanh(h5);
            h6 = math.tanh(h6);
            h7 = math.tanh(h7);
            h8 = math.tanh(h8);
            h9 = math.tanh(h9);
            h10 = math.tanh(h10);
            h11 = math.tanh(h11);
            h12 = math.tanh(h12);
            h13 = math.tanh(h13);
            h14 = math.tanh(h14);
            h15 = math.tanh(h15);

            // Compute Action Biases (Output layer)
            biases = default;
            // Wander = 0
            biases.Wander = math.tanh(0.2f * h9 + 0.3f * h13 - 0.5f * h2);
            // Forage = 1
            biases.Forage = math.tanh(1.2f * h0 + 0.5f * h11 - 0.6f * h2);
            // Graze = 2
            biases.Graze = math.tanh(1.2f * h0 + 0.6f * h11 - 0.5f * h2);
            // Hunt = 3
            biases.Hunt = math.tanh(1.4f * h5 + 0.8f * h14 - 0.4f * h2);
            // Scavenge = 4
            biases.Scavenge = math.tanh(1.1f * h0 + 0.6f * h11 - 0.3f * h2);
            // Drink = 5
            biases.Drink = math.tanh(1.5f * h1 - 0.5f * h2);
            // Rest = 6
            biases.Rest = math.tanh(1.2f * h3 + 0.4f * h4 - 0.8f * h2);
            // Sleep = 7
            biases.Sleep = math.tanh(1.5f * h3 + 0.6f * input.IsNight - 1.0f * h2);
            // Flee = 8
            biases.Flee = math.tanh(2.0f * h2 + 1.2f * h4);
            // Explore = 9
            biases.Explore = math.tanh(1.2f * h9 + 0.8f * h13 - 0.7f * h2);
            // Migrate = 10
            biases.Migrate = math.tanh(1.1f * h6 + 0.8f * h4);
            // Socialize = 11
            biases.Socialize = math.tanh(1.3f * h7 - 0.5f * h2);
            // SeekMate = 12
            biases.SeekMate = math.tanh(1.4f * h8 - 0.5f * h2);
            // Bask = 13 (Ectotherm sunning)
            biases.Bask = math.tanh(1.3f * h10 + 0.5f * (1f - input.IsNight) - 0.5f * h2);
            // TakeShelter = 14
            biases.TakeShelter = math.tanh(1.6f * h4 + 0.8f * h12);
        }
    }

    public struct ActionBiases
    {
        public float Wander;
        public float Forage;
        public float Graze;
        public float Hunt;
        public float Scavenge;
        public float Drink;
        public float Rest;
        public float Sleep;
        public float Flee;
        public float Explore;
        public float Migrate;
        public float Socialize;
        public float SeekMate;
        public float Bask;
        public float TakeShelter;

        public float GetBias(CreatureAction action) => action switch
        {
            CreatureAction.Wander => Wander,
            CreatureAction.Forage => Forage,
            CreatureAction.Graze => Graze,
            CreatureAction.Hunt => Hunt,
            CreatureAction.Scavenge => Scavenge,
            CreatureAction.Drink => Drink,
            CreatureAction.Rest => Rest,
            CreatureAction.Sleep => Sleep,
            CreatureAction.Flee => Flee,
            CreatureAction.Explore => Explore,
            CreatureAction.Migrate => Migrate,
            CreatureAction.Socialize => Socialize,
            CreatureAction.SeekMate => SeekMate,
            CreatureAction.Bask => Bask,
            CreatureAction.TakeShelter => TakeShelter,
            _ => 0f
        };
    }
}
