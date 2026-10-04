// Ecosphere — Gene TypeId constants and Gene struct.
// This file is the long-lived contract for gene identities. Every consumer references
// these constants; the numeric IDs are stable across versions (append-only).
//
// Ranges:
//   0–4    Regulatory (Both)
//   5–34   Body plan — Animals
//   35–64  Body plan — Plants
//   65–79  Metabolism (Both, shared catalog)
//   80–89  Growth & life history (Both)
//   90–99  Appearance (Both)
//   100–119 Behavior weights (Animals, stage 05 consumer)
//   120–129 Reproduction (Both)
//
// See Docs/genetics.md for the full gene catalog table.

namespace Ecosphere.Core.Simulation
{
    /// <summary>Kingdom applicability flags for each gene type.</summary>
    [System.Flags]
    public enum GeneKingdom : byte
    {
        None   = 0,
        Plant  = 1,
        Animal = 2,
        Both   = Plant | Animal
    }

    /// <summary>
    /// Which phenotype field a gene feeds. Used by the catalog to document
    /// gene-to-phenotype mapping; not consumed at runtime (PhenotypeSystem
    /// has hard-coded mapping).
    /// </summary>
    public enum PhenotypeField : byte
    {
        None,
        Regulatory,
        BodyPlan,
        Metabolism,
        Growth,
        Appearance,
        Behavior,
        Reproduction
    }

    /// <summary>
    /// Gene groups determine stage-gate expression windows and allometry categories.
    /// </summary>
    public enum GeneGroup : byte
    {
        Regulatory,
        AnimalBodyPlan,
        PlantBodyPlan,
        Metabolism,
        Growth,
        Appearance,
        Behavior,
        Reproduction
    }

    /// <summary>
    /// Environment axis for epigenetic modulation of environmentally sensitive genes.
    /// </summary>
    public enum EnvironmentAxis : byte
    {
        Temperature,
        Light,
        Wind,
        Moisture
    }

    /// <summary>
    /// Compact genome element stored in <c>DynamicBuffer&lt;Gene&gt;</c> on each organism.
    /// Blittable, Burst-friendly, 16 bytes. Value is always 0..1; consumers map it to
    /// the gene's valid range via the catalog.
    /// </summary>
    public struct Gene
    {
        /// <summary>Index into <see cref="GeneCatalog"/>. Stable across versions.</summary>
        public ushort TypeId;
        /// <summary>Primary value in [0, 1]. Mapped to gene-specific range by the catalog.</summary>
        public float Value;
        /// <summary>Allelic dominance (0..1). Used during crossover blending (stage 06).</summary>
        public float Dominance;
        /// <summary>Per-gene mutation probability (0..1). Consumed by stage 06 crossover.</summary>
        public float MutationRate;

        public Gene(ushort typeId, float value, float dominance, float mutationRate)
        {
            TypeId = typeId;
            Value = value;
            Dominance = dominance;
            MutationRate = mutationRate;
        }
    }

    /// <summary>
    /// Stable numeric IDs for every gene type. Append-only: new genes get the next ID
    /// in their range; existing IDs never change meaning. Consumers use these constants
    /// to index into <c>DynamicBuffer&lt;Gene&gt;</c>.
    /// </summary>
    public static class GeneId
    {
        // ── Regulatory (0–4) ─────────────────────────────────────────────
        public const ushort ExpressionSensitivity  = 0;  // master expression multiplier
        public const ushort StageThresholdShift    = 1;  // shifts all stage gate thresholds
        public const ushort TemperatureResponseKnob= 2;  // scales temperature-response magnitude
        public const ushort SizeMultiplier         = 3;  // master body-size scalar
        public const ushort DevelopmentRate        = 4;  // scales ontogenetic clock speed

        // ── Body plan — Animals (5–34) ──────────────────────────────────
        public const ushort Symmetry           = 5;   // 0=bilateral, 0.5=radial-4, 1=radial-6
        public const ushort TorsoSegments      = 6;   // 1–8 body segments
        public const ushort TorsoLength        = 7;   // total torso length factor
        public const ushort TorsoGirth         = 8;   // torso thickness
        public const ushort LimbCount          = 9;   // 0–4 pairs
        public const ushort LimbLength         = 10;
        public const ushort LimbThickness      = 11;
        public const ushort LimbJointness      = 12;  // 0=straight, 1=bent/segmented
        public const ushort NeckLength         = 13;  // 0=none (invertebrate)
        public const ushort HeadSize           = 14;
        public const ushort JawType            = 15;  // 0=none, 0.25=beak, 0.5=mammal, 0.75=mandible
        public const ushort MouthSize          = 16;
        public const ushort TailLength         = 17;  // 0=tailless
        public const ushort EyeCount           = 18;  // 0–8
        public const ushort EyeSize            = 19;
        public const ushort EyeForwardAngle    = 20;  // 0=side-facing, 1=fully forward
        public const ushort EarSize            = 21;  // 0=none
        public const ushort AntennaLength      = 22;  // 0=none
        public const ushort IntegumentType     = 23;  // discrete: skin/scales/feathers/fur/shell/spines
        public const ushort IntegumentDensity  = 24;  // cover fraction
        public const ushort BodyLengthScale    = 25;  // overall body length multiplier
        public const ushort FinSize            = 26;  // 0=none, >0=fin/wing size
        public const ushort FinCount           = 27;  // fin/wing pairs
        public const ushort ShellThickness     = 28;
        public const ushort SpineLength        = 29;
        public const ushort HeadFlatten        = 30;  // dorsoventral flattening (rays, snakes)
        public const ushort TorsoFlatten       = 31;  // lateral flattening (fish)
        public const ushort LimbWebbing        = 32;  // 0=none, 1=full (duck, frog)
        public const ushort TongueLength       = 33;  // for anteaters, chameleons
        public const ushort ClawLength         = 34;

        // ── Body plan — Plants (35–64) ──────────────────────────────────
        public const ushort RootDepth          = 35;
        public const ushort RootSpread         = 36;
        public const ushort StemHeight         = 37;
        public const ushort StemThickness      = 38;
        public const ushort Woodiness          = 39;  // 0=herbaceous, 1=full wood
        public const ushort BranchingDepth     = 40;  // 0–5 recursion levels
        public const ushort BranchingAngle     = 41;
        public const ushort BranchLengthRatio  = 42;  // child/parent branch length
        public const ushort LeafSize           = 43;
        public const ushort LeafCount          = 44;  // leaves per node
        public const ushort LeafThickness      = 45;
        public const ushort LeafAngle          = 46;  // droop/tilt from stem
        public const ushort FlowerSize         = 47;  // 0=none
        public const ushort FlowerCount        = 48;
        public const ushort FlowerPetalCount   = 49;
        public const ushort FruitSize          = 50;  // 0=none
        public const ushort FruitCount         = 51;
        public const ushort SeedCount          = 52;
        public const ushort SeedSize           = 53;
        public const ushort GrowthHabit        = 54;  // 0=herb, 0.33=bush, 0.66=vine, 1=tree
        public const ushort CrownShape         = 55;  // 0=spherical, 0.5=conical, 1=flat
        public const ushort BarkTexture        = 56;  // roughness / detail scalar
        public const ushort Thorns             = 57;  // 0=none, >0=thorn length
        public const ushort VineCurl           = 58;  // curliness for vines
        public const ushort NodeSpacing        = 59;  // internode length
        public const ushort AerialRoots        = 60;  // 0=none (banyan/epiphyte)
        public const ushort TendrilLength      = 61;
        public const ushort RootNodules        = 62;  // nitrogen fixation organs
        public const ushort BulbSize           = 63;  // underground storage
        public const ushort RosetteSpread      = 64;  // basal rosette diameter

        // ── Metabolism (65–79, shared Both) ─────────────────────────────
        public const ushort MetabolicRate      = 65;
        public const ushort Endothermy         = 66;  // 0=ecto, 1=full endo
        public const ushort WaterNeed          = 67;
        public const ushort DietPhotosynthesis = 68;  // fraction of energy from light
        public const ushort DietHerbivory      = 69;
        public const ushort DietCarnivory      = 70;
        public const ushort DietScavenging     = 71;
        public const ushort DietFilterFeeding  = 72;
        public const ushort ToxinDefense       = 73;
        public const ushort TemperatureOptimum = 74;  // mapped to gene range
        public const ushort TemperatureTolerance= 75; // width of comfort zone
        public const ushort OsmoregulationCost = 76;
        public const ushort DigestiveEfficiency= 77;
        public const ushort StorageCapacity    = 78;
        public const ushort AnaerobicCapacity  = 79;

        // ── Growth & life history (80–89, Both) ─────────────────────────
        public const ushort GrowthCurveType    = 80;  // 0=logistic, 0.5=Gompertz, 1=linear
        public const ushort GrowthRate         = 81;
        public const ushort EmbryoDuration     = 82;  // fraction of lifespan
        public const ushort JuvenileDuration   = 83;
        public const ushort AdultDuration      = 84;
        public const ushort SenescentOnset     = 85;
        public const ushort MaturityAge        = 86;
        public const ushort Lifespan           = 87;
        public const ushort SeasonalGrowthGate = 88;  // how strongly season limits growth
        public const ushort GrowthBurstAge     = 89;  // age of maximum growth velocity

        // ── Appearance (90–99, Both) ────────────────────────────────────
        public const ushort PigmentRed         = 90;
        public const ushort PigmentGreen       = 91;
        public const ushort PigmentBlue        = 92;
        public const ushort PatternType        = 93;  // 0=solid, 0.33=stripes, 0.66=spots, 1=gradient
        public const ushort PatternScale       = 94;
        public const ushort PatternSymmetry    = 95;
        public const ushort SecondaryRed       = 96;
        public const ushort SecondaryGreen     = 97;
        public const ushort SecondaryBlue      = 98;
        public const ushort Bioluminescence    = 99;

        // ── Behavior weights (100–119, Animals, stage 05 consumer) ──────
        public const ushort BehaviorWeight00   = 100;
        public const ushort BehaviorWeight01   = 101;
        public const ushort BehaviorWeight02   = 102;
        public const ushort BehaviorWeight03   = 103;
        public const ushort BehaviorWeight04   = 104;
        public const ushort BehaviorWeight05   = 105;
        public const ushort BehaviorWeight06   = 106;
        public const ushort BehaviorWeight07   = 107;
        public const ushort BehaviorWeight08   = 108;
        public const ushort BehaviorWeight09   = 109;
        public const ushort BehaviorWeight10   = 110;
        public const ushort BehaviorWeight11   = 111;
        public const ushort BehaviorWeight12   = 112;
        public const ushort BehaviorWeight13   = 113;
        public const ushort BehaviorWeight14   = 114;
        public const ushort BehaviorWeight15   = 115;
        public const ushort InstinctAggression = 116;
        public const ushort InstinctCuriosity  = 117;
        public const ushort InstinctFear       = 118;
        public const ushort InstinctSociability= 119;

        // ── Reproduction (120–129, Both) ────────────────────────────────
        public const ushort ReproductionMode   = 120; // 0=asexual, 0.5=mixed, 1=sexual
        public const ushort ClutchSize         = 121;
        public const ushort EggSeedSize        = 122;
        public const ushort ParentalCare       = 123;
        public const ushort BreedingSeason     = 124; // 0=spring, 0.25=summer, 0.5=autumn, 0.75=winter
        public const ushort MutationRateMod    = 125; // global mutation rate modifier
        public const ushort ReproFrequency     = 126; // breeding events per year
        public const ushort GestationDuration  = 127;
        public const ushort SexualDimorphism   = 128;
        public const ushort PheromoneStrength  = 129;

        /// <summary>Total number of defined gene types.</summary>
        public const int Count = 130;
    }
}