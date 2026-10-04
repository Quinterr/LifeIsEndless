// Ecosphere — Morphogen math: deterministic phenotype computation.
// Pure C# (no engine references). All functions are deterministic:
// same (genome, age, environment history) → same outputs.

using System;

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Life stages of an organism. Values are ordered so comparison works:
    /// Embryo/Seed &lt; Juvenile &lt; Adult &lt; Senescent.
    /// </summary>
    public enum LifeStage : byte
    {
        Embryo = 0,   // animals: egg/embryo; plants: seed
        Juvenile = 1,
        Adult = 2,
        Senescent = 3
    }

    /// <summary>
    /// Environment history as exponential moving averages. Updated per-tick from
    /// <c>IClimateSampler</c> at the organism's cell. Used for epigenetic modulation.
    /// All values are normalized to approximately [0, 1] or °C-like ranges.
    /// </summary>
    public struct EnvironmentEMA
    {
        /// <summary>Air temperature EMA (°C-like). Range ~[-50, 50].</summary>
        public float Temperature;
        /// <summary>PAR / insolation EMA (0..1).</summary>
        public float Light;
        /// <summary>Wind speed EMA (m/s-like, 0..32).</summary>
        public float Wind;
        /// <summary>Soil moisture EMA (0..1).</summary>
        public float Moisture;

        /// <summary>Per-axis decay factor per tick (0.99..0.999 typical).</summary>
        public float DecayTemperature;
        public float DecayLight;
        public float DecayWind;
        public float DecayMoisture;

        /// <summary>
        /// Update EMAs with a new climate sample. Call once per tick.
        /// EMA_new = EMA_old × decay + current × (1 − decay).
        /// </summary>
        public void Update(float temperature, float light, float wind, float moisture)
        {
            Temperature = Temperature * DecayTemperature + temperature * (1f - DecayTemperature);
            Light       = Light       * DecayLight       + light       * (1f - DecayLight);
            Wind        = Wind        * DecayWind        + wind        * (1f - DecayWind);
            Moisture    = Moisture    * DecayMoisture    + moisture    * (1f - DecayMoisture);
        }

        /// <summary>Default decay factors (per-tick at 10 Hz).</summary>
        public static EnvironmentEMA CreateDefault()
        {
            return new EnvironmentEMA
            {
                DecayTemperature = 0.998f,
                DecayLight       = 0.997f,
                DecayWind        = 0.995f,
                DecayMoisture    = 0.996f
            };
        }
    }

    /// <summary>
    /// Flat struct containing every phenotypic parameter derived from the genome.
    /// Written by PhenotypeSystem; consumed by mesh synthesis, behavior, life systems.
    /// Organisms with identical genomes and environment histories produce identical
    /// Phenotype structs (determinism contract).
    /// </summary>
    public struct Phenotype
    {
        // ── Size & age (derived, not from genes directly) ────────────
        public float Size;          // 0..1 relative maturity (growth curve output)
        public float SizeMultiplier; // from regulatory gene

        // ── Body plan — Animals ──────────────────────────────────────
        public float Symmetry;
        public float TorsoSegments;  // rounded to int in mesh builder
        public float TorsoLength;
        public float TorsoGirth;
        public float LimbCount;     // rounded to int
        public float LimbLength;
        public float LimbThickness;
        public float LimbJointness;
        public float NeckLength;
        public float HeadSize;
        public float JawType;
        public float MouthSize;
        public float TailLength;
        public float EyeCount;      // rounded to int
        public float EyeSize;
        public float EyeForwardAngle;
        public float EarSize;
        public float AntennaLength;
        public float IntegumentType;   // discrete mapping in mesh builder
        public float IntegumentDensity;
        public float BodyLengthScale;
        public float FinSize;
        public float FinCount;
        public float ShellThickness;
        public float SpineLength;
        public float HeadFlatten;
        public float TorsoFlatten;
        public float LimbWebbing;
        public float TongueLength;
        public float ClawLength;

        // ── Body plan — Plants ───────────────────────────────────────
        public float RootDepth;
        public float RootSpread;
        public float StemHeight;
        public float StemThickness;
        public float Woodiness;
        public float BranchingDepth;
        public float BranchingAngle;
        public float BranchLengthRatio;
        public float LeafSize;
        public float LeafCount;
        public float LeafThickness;
        public float LeafAngle;
        public float FlowerSize;
        public float FlowerCount;
        public float FlowerPetalCount;
        public float FruitSize;
        public float FruitCount;
        public float SeedCount;
        public float SeedSize;
        public float GrowthHabit;
        public float CrownShape;
        public float BarkTexture;
        public float Thorns;
        public float VineCurl;
        public float NodeSpacing;
        public float AerialRoots;
        public float TendrilLength;
        public float RootNodules;
        public float BulbSize;
        public float RosetteSpread;

        // ── Metabolism ───────────────────────────────────────────────
        public float MetabolicRate;
        public float Endothermy;
        public float WaterNeed;
        public float DietPhotosynthesis;
        public float DietHerbivory;
        public float DietCarnivory;
        public float DietScavenging;
        public float DietFilterFeeding;
        public float ToxinDefense;
        public float TemperatureOptimum;
        public float TemperatureTolerance;
        public float OsmoregulationCost;
        public float DigestiveEfficiency;
        public float StorageCapacity;
        public float AnaerobicCapacity;

        // ── Growth & life history ────────────────────────────────────
        public float GrowthCurveType;
        public float GrowthRate;
        public float EmbryoDuration;
        public float JuvenileDuration;
        public float AdultDuration;
        public float SenescentOnset;
        public float MaturityAge;
        public float Lifespan;
        public float SeasonalGrowthGate;
        public float GrowthBurstAge;

        // ── Appearance ───────────────────────────────────────────────
        public float PigmentRed;
        public float PigmentGreen;
        public float PigmentBlue;
        public float PatternType;
        public float PatternScale;
        public float PatternSymmetry;
        public float SecondaryRed;
        public float SecondaryGreen;
        public float SecondaryBlue;
        public float Bioluminescence;

        // ── Behavior weights blob (stage 05 reads; PhenotypeSystem just copies) ─
        public float BehaviorWeight00, BehaviorWeight01, BehaviorWeight02, BehaviorWeight03;
        public float BehaviorWeight04, BehaviorWeight05, BehaviorWeight06, BehaviorWeight07;
        public float BehaviorWeight08, BehaviorWeight09, BehaviorWeight10, BehaviorWeight11;
        public float BehaviorWeight12, BehaviorWeight13, BehaviorWeight14, BehaviorWeight15;
        public float InstinctAggression;
        public float InstinctCuriosity;
        public float InstinctFear;
        public float InstinctSociability;

        // ── Reproduction ─────────────────────────────────────────────
        public float ReproductionMode;
        public float ClutchSize;
        public float EggSeedSize;
        public float ParentalCare;
        public float BreedingSeason;
        public float MutationRateMod;
        public float ReproFrequency;
        public float GestationDuration;
        public float SexualDimorphism;
        public float PheromoneStrength;
    }

    /// <summary>
    /// Pure math for developmental biology: gene expression, stage gating,
    /// allometry, morphogen gradients, and epigenetic modulation.
    /// Every function is deterministic and Burst-friendly (no allocations,
    /// no virtual calls, System.Math only).
    /// </summary>
    public static class MorphogenMath
    {
        // ── Stage gates ─────────────────────────────────────────────

        /// <summary>
        /// Stage-gate fraction for a gene group at a given normalized age.
        /// Returns 0 if the gene is not yet expressed, 1 if fully expressed,
        /// and a ramp value while the gene is transitioning into expression.
        ///
        /// Onset thresholds are fractions of the organism's (gene-set) lifespan,
        /// so the gate and the stage boundaries (also gene-driven) stay consistent:
        /// regulatory/metabolism/growth are on from the embryo, body plan and
        /// appearance turn on through the juvenile stage, behavior and
        /// reproduction only in the adult window.
        /// </summary>
        /// <param name="group">Gene group.</param>
        /// <param name="ageFraction">Normalized age: devAge / lifespan, in [0, 1].</param>
        /// <param name="thresholdShift">Shift from the StageThresholdShift regulatory gene (−0.3..+0.3).</param>
        public static float StageGate(GeneGroup group, float ageFraction, float thresholdShift)
        {
            float onset = group switch
            {
                GeneGroup.Regulatory     => 0f,     // always expressed
                GeneGroup.AnimalBodyPlan => 0.15f,  // start in mid-embryo
                GeneGroup.PlantBodyPlan  => 0.1f,   // start at germination
                GeneGroup.Metabolism     => 0.05f,  // metabolic genes early
                GeneGroup.Growth         => 0f,     // growth genes always on
                GeneGroup.Appearance     => 0.25f,  // pigment / juvenile plumage
                GeneGroup.Behavior       => 0.6f,   // behavior in the adult window
                GeneGroup.Reproduction   => 0.65f,  // reproduction at maturity
                _ => 0f
            };

            onset = Math.Max(0f, onset + thresholdShift);
            if (ageFraction < onset) return 0f;

            // Ramp over 10% of lifespan after onset.
            float rampEnd = onset + 0.1f;
            if (ageFraction >= rampEnd) return 1f;
            return (ageFraction - onset) / (rampEnd - onset);
        }

        // ── Effective gene value ─────────────────────────────────────

        /// <summary>
        /// Compute the effective gene value considering stage gate, environmental
        /// modulation, and regulatory sensitivity.
        /// </summary>
        /// <param name="rawValue">Gene.Value (0..1).</param>
        /// <param name="catalogMappedValue">Value mapped to gene's range.</param>
        /// <param name="stageGate">Stage gate fraction (0..1).</param>
        /// <param name="expressionSensitivity">From regulatory gene 0 (0.5..2).</param>
        /// <param name="envModulation">Epigenetic modulation factor (−0.4..+0.4).</param>
        /// <returns>Fully modulated effective gene value.</returns>
        public static float EffectiveValue(float catalogMappedValue, float stageGate,
            float expressionSensitivity, float envModulation)
        {
            // Sensitivity scales how strongly genes express
            float base_ = catalogMappedValue * expressionSensitivity;
            // Apply epigenetic modulation (capped ±40% in ComputeEnvModulation)
            float modulated = base_ * (1f + envModulation);
            // Apply stage gate
            return modulated * stageGate;
        }

        // ── Allometry ───────────────────────────────────────────────

        /// <summary>
        /// Allometric scaling: different body parts grow at different rates.
        /// Returns a 0..1 maturity factor for a specific body part at the given
        /// organism size.
        /// </summary>
        /// <param name="organismSize">Overall organism size/maturity (0..1).</param>
        /// <param name="growthExponent">
        /// How quickly this part matures: &lt;1 = slower (limbs, branches),
        /// 1 = isometric, &gt;1 = faster (head, leaves).
        /// </param>
        public static float AllometricScale(float organismSize, float growthExponent)
        {
            // power curve: partMaturity = size^exponent
            // exponent > 1 → part grows fast (head/leaves mature early)
            // exponent < 1 → part grows slow (limbs/branches lag behind)
            // exponent = 1 → isometric (proportional to body)
            return Math.Max(0f, Math.Min(1f, (float)Math.Pow(organismSize, growthExponent)));
        }

        /// <summary>
        /// Allometric exponents for animal body parts (head matures early, limbs late).
        /// </summary>
        public static class AnimalAllometry
        {
            public const float Head    = 1.4f;  // head/brain matures early
            public const float Torso   = 1.0f;  // isometric
            public const float Limbs   = 0.7f;  // limbs lag behind
            public const float Tail    = 0.65f; // tail grows slow
            public const float Sensors = 1.3f;  // eyes/ears mature early
            public const float Neck    = 0.9f;  // slightly slower
            public const float Cover   = 1.1f;  // integument develops with torso
        }

        /// <summary>
        /// Allometric exponents for plant parts (leaves mature early, branches lag).
        /// </summary>
        public static class PlantAllometry
        {
            public const float Stem     = 1.0f;  // isometric
            public const float Leaves   = 1.5f;  // leaves unfurl early
            public const float Branches = 0.7f;  // branches lag
            public const float Roots    = 1.2f;  // roots establish early
            public const float Flowers  = 0.5f;  // flowers appear late
            public const float Fruit    = 0.4f;  // fruit only at maturity
        }

        // ── Morphogen gradients ──────────────────────────────────────

        /// <summary>
        /// Body-axis gradient: returns a factor (0..1) for a given segment index
        /// along the body. Used for limb placement and plant node branching.
        /// 0 = anterior/apical, 1 = posterior/basal.
        /// </summary>
        /// <param name="segmentIndex">0-based segment index.</param>
        /// <param name="totalSegments">Total number of segments.</param>
        public static float BodyAxisGradient(int segmentIndex, int totalSegments)
        {
            if (totalSegments <= 1) return 0.5f;
            return (float)segmentIndex / (totalSegments - 1);
        }

        /// <summary>
        /// Segment-dependent size factor. Limbs at the center of the body are largest;
        /// those at the ends are smaller. Returns 0.4..1.0.
        /// </summary>
        public static float SegmentSizeFactor(float axisGradient)
        {
            // Bell curve peaking at 0.3 (slightly anterior of center)
            float x = axisGradient - 0.3f;
            return 0.4f + 0.6f * (float)Math.Exp(-x * x * 8.0);
        }

        // ── Epigenetic modulation ────────────────────────────────────

        /// <summary>Hard epigenetic cap: ±40% of the gene value (documented contract).</summary>
        public const float EpigeneticCap = 0.4f;

        /// <summary>
        /// Compute environment modulation factor for an environmentally sensitive gene.
        /// The factor is clamped to ±<see cref="EpigeneticCap"/> (±40% of gene value,
        /// the documented cap). Polity selects the response sign (e.g. cold makes cover
        /// density grow and extremity length shrink).
        /// </summary>
        /// <param name="ema">Current environment EMA values.</param>
        /// <param name="axis">Which environment axis modulates this gene.</param>
        /// <param name="polarity">+1 or −1 response sign from the catalog.</param>
        /// <param name="temperatureOptimum">Organism's temperature optimum (from genes).</param>
        /// <param name="temperatureTolerance">Width of the organism's comfort zone.</param>
        /// <param name="sensitivity">From the ExpressionSensitivity regulatory gene.</param>
        public static float ComputeEnvModulation(in EnvironmentEMA ema, EnvironmentAxis axis,
            float polarity, float temperatureOptimum, float temperatureTolerance, float sensitivity)
        {
            float raw = axis switch
            {
                EnvironmentAxis.Temperature => TemperatureFactor(ema.Temperature, temperatureOptimum, temperatureTolerance),
                EnvironmentAxis.Light       => LightFactor(ema.Light),
                EnvironmentAxis.Wind        => WindFactor(ema.Wind),
                EnvironmentAxis.Moisture    => MoistureFactor(ema.Moisture),
                _ => 0f
            };
            // polarity picks the sign, sensitivity scales, then clamp to the ±40% cap.
            float mod = raw * polarity * sensitivity;
            return Math.Max(-EpigeneticCap, Math.Min(EpigeneticCap, mod));
        }

        /// <summary>
        /// Temperature factor: positive when cold (→ more cover), negative when warm
        /// relative to optimum. Returns approximately −1..+1.
        /// </summary>
        private static float TemperatureFactor(float envTemp, float optimum, float tolerance)
        {
            float halfWidth = Math.Max(1f, tolerance * 0.5f);
            float deviation = optimum - envTemp; // positive when environment is cold
            return Math.Max(-1f, Math.Min(1f, deviation / halfWidth));
        }

        /// <summary>
        /// Light factor: negative in low light (→ larger leaves), positive in high light.
        /// Normalized around 0.5 insolation as neutral.
        /// </summary>
        private static float LightFactor(float light)
        {
            return Math.Max(-1f, Math.Min(1f, (light - 0.5f) * 2f));
        }

        /// <summary>
        /// Wind factor: positive in high wind (→ thicker stems, lower growth).
        /// Neutral at ~2 m/s.
        /// </summary>
        private static float WindFactor(float wind)
        {
            return Math.Max(-1f, Math.Min(1f, (wind - 2f) * 0.25f));
        }

        /// <summary>
        /// Moisture factor: positive in wet conditions (→ more growth), negative in dry.
        /// Neutral at 0.5 soil moisture.
        /// </summary>
        private static float MoistureFactor(float moisture)
        {
            return Math.Max(-1f, Math.Min(1f, (moisture - 0.5f) * 2f));
        }

        // ── Growth curves ────────────────────────────────────────────

        /// <summary>
        /// Compute organism size (0..1 maturity) given age and growth parameters.
        /// Supports logistic, Gompertz, and linear growth curves.
        /// </summary>
        /// <param name="curveType">0=logistic, 0.5=Gompertz, 1=linear.</param>
        /// <param name="growthRate">Rate parameter.</param>
        /// <param name="age">Age in ticks.</param>
        /// <param name="lifespan">Total lifespan in ticks.</param>
        /// <param name="burstAge">Age of maximum growth velocity (inflection).</param>
        public static float GrowthSize(float curveType, float growthRate, float age, float lifespan, float burstAge)
        {
            if (lifespan <= 0f) return 1f;
            float t = Math.Max(0f, age / lifespan); // normalized age 0..1+
            float r = Math.Max(0.01f, growthRate);
            float t0 = Math.Max(0.01f, Math.Min(0.99f, burstAge));
            float k = 4f + 8f * r; // steepness from the growth-rate gene

            if (curveType < 0.25f)
            {
                // Logistic, normalized so size(1) = 1 exactly:
                //   size = σ(k(t − t0)) / σ(k(1 − t0))
                return Sigmoid(k * (t - t0)) / Sigmoid(k * (1f - t0));
            }
            else if (curveType < 0.75f)
            {
                // Gompertz, normalized the same way (slow start, fast middle, slow end).
                float g = (u) => (float)Math.Exp(-Math.Exp(-k * (u - t0)));
                return g(t) / g(1f);
            }
            else
            {
                // Linear: the rate gene tilts the slope (clamped to reach 1 at t=1).
                return Math.Min(1f, t * Math.Max(1f, r));
            }
        }

        private static float Sigmoid(float x)
        {
            x = Math.Max(-60f, Math.Min(60f, x));
            return 1f / (1f + (float)Math.Exp(-x));
        }

        // ── Life stage determination ─────────────────────────────────

        /// <summary>
        /// Determine the current life stage from growth params and age.
        /// </summary>
        public static LifeStage DetermineStage(float embryoFrac, float juvenileFrac,
            float adultFrac, float senescenceFrac, float age, float lifespan)
        {
            if (lifespan <= 0f) return LifeStage.Adult;
            float t = age / lifespan;
            if (t < embryoFrac) return LifeStage.Embryo;
            if (t < embryoFrac + juvenileFrac) return LifeStage.Juvenile;
            if (t < senescenceFrac) return LifeStage.Adult;
            return LifeStage.Senescent;
        }

        /// <summary>
        /// Progress within the current life stage (0..1).
        /// </summary>
        public static float StageProgress(LifeStage stage, float embryoFrac, float juvenileFrac,
            float adultFrac, float senescenceFrac, float age, float lifespan)
        {
            if (lifespan <= 0f) return 1f;
            float t = age / lifespan;
            return stage switch
            {
                LifeStage.Embryo    => embryoFrac > 0 ? t / embryoFrac : 1f,
                LifeStage.Juvenile  => juvenileFrac > 0 ? (t - embryoFrac) / juvenileFrac : 1f,
                LifeStage.Adult     => adultFrac > 0 ? (t - embryoFrac - juvenileFrac) / adultFrac : 1f,
                LifeStage.Senescent => (1f - senescenceFrac) > 0 ? (t - senescenceFrac) / (1f - senescenceFrac) : 1f,
                _ => 0f
            };
        }

        // ── Diet vector normalization ────────────────────────────────

        /// <summary>
        /// Normalize diet fractions so they sum to 1. Preserves relative proportions.
        /// </summary>
        public static void NormalizeDiet(ref float photosynthesis, ref float herbivory,
            ref float carnivory, ref float scavenging, ref float filterFeeding)
        {
            float sum = photosynthesis + herbivory + carnivory + scavenging + filterFeeding;
            if (sum <= 0.001f)
            {
                // Default to herbivore if all zero
                herbivory = 1f;
                return;
            }
            float inv = 1f / sum;
            photosynthesis *= inv;
            herbivory *= inv;
            carnivory *= inv;
            scavenging *= inv;
            filterFeeding *= inv;
        }
    }
}