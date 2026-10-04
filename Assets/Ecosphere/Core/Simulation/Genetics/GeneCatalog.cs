// Ecosphere — Gene catalog: metadata for every gene TypeId.
// Pure C# (no engine references). Used by PhenotypeSystem, tests, and the viewer
// to map TypeId → name, range, default, kingdom, group, and epigenetic sensitivity.

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// Metadata for one gene type. Stored in <see cref="GeneCatalog"/> arrays.
    /// </summary>
    public struct GeneDefinition
    {
        public ushort Id;
        public string Name;
        public float Min;              // lower bound of the gene's mapped range
        public float Max;              // upper bound
        public float Default;          // default gene Value (0..1)
        public GeneKingdom Kingdom;    // which kingdom(s) use this gene
        public GeneGroup Group;        // expression group (determines stage gate)
        public PhenotypeField Field;   // which phenotype field it feeds
        public bool EnvironmentSensitive;
        public EnvironmentAxis EnvAxis; // which environment axis modulates this gene
        /// <summary>
        /// Sign of the epigenetic response to a +1 factor on the environment axis.
        /// +1: harsher/higher environment → larger effective value (e.g. cold → denser cover).
        /// −1: harsher/higher environment → smaller effective value (e.g. cold → shorter extremities).
        /// </summary>
        public float EnvPolarity;
        /// <summary>Minimum number of dominant alleles for full expression (stage 06).</summary>
        public float MinDominance;
    }

    /// <summary>
    /// The gene type registry. Created once via <see cref="Create"/> and referenced
    /// by every system that needs gene metadata. Pure data, no allocations after init.
    /// </summary>
    public sealed class GeneCatalog
    {
        /// <summary>All definitions, indexed by gene TypeId.</summary>
        public readonly GeneDefinition[] Definitions;

        /// <summary>Total registered gene types.</summary>
        public int Count => Definitions.Length;

        /// <summary>Wrap a definition array (used by the SO baker and tests).</summary>
        public GeneCatalog(GeneDefinition[] defs) { Definitions = defs; }

        /// <summary>
        /// Get definition by TypeId. Returns default if out of range.
        /// </summary>
        public GeneDefinition Get(ushort typeId)
        {
            if (typeId < Definitions.Length) return Definitions[typeId];
            return default;
        }

        /// <summary>Map a gene's raw Value (0..1) to its catalog range.</summary>
        public float MapToRange(ushort typeId, float value01)
        {
            var d = Get(typeId);
            return d.Min + value01 * (d.Max - d.Min);
        }

        /// <summary>Create the full default catalog.</summary>
        public static GeneCatalog Create()
        {
            var defs = new GeneDefinition[GeneId.Count];

            // Helper
            void Reg(ushort id, string name, float min, float max, float def,
                     GeneKingdom k, GeneGroup g, PhenotypeField f,
                     bool envSens = false, EnvironmentAxis envAxis = EnvironmentAxis.Temperature,
                     float envPolarity = 1f)
            {
                defs[id] = new GeneDefinition
                {
                    Id = id, Name = name, Min = min, Max = max, Default = def,
                    Kingdom = k, Group = g, Field = f,
                    EnvironmentSensitive = envSens, EnvAxis = envAxis, EnvPolarity = envPolarity
                };
            }

            // ── Regulatory (0–4) ──────────────────────────────────────────
            Reg(0,  "ExpressionSensitivity",   0.5f, 2f,   0.5f, GeneKingdom.Both,   GeneGroup.Regulatory, PhenotypeField.Regulatory);
            Reg(1,  "StageThresholdShift",     -0.3f, 0.3f, 0.5f, GeneKingdom.Both,   GeneGroup.Regulatory, PhenotypeField.Regulatory);
            Reg(2,  "TemperatureResponseKnob", 0f,   2f,   0.5f, GeneKingdom.Both,   GeneGroup.Regulatory, PhenotypeField.Regulatory);
            Reg(3,  "SizeMultiplier",          0.2f, 3f,   0.5f, GeneKingdom.Both,   GeneGroup.Regulatory, PhenotypeField.Regulatory);
            Reg(4,  "DevelopmentRate",         0.3f, 2f,   0.5f, GeneKingdom.Both,   GeneGroup.Regulatory, PhenotypeField.Regulatory);

            // ── Body plan — Animals (5–34) ───────────────────────────────
            Reg(5,  "Symmetry",          0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(6,  "TorsoSegments",     1f,   8f,   0.25f, GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(7,  "TorsoLength",       0.3f, 3f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(8,  "TorsoGirth",        0.2f, 2f,   0.4f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(9,  "LimbCount",         0f,   4f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(10, "LimbLength",        0.2f, 2f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, -1f);
            Reg(11, "LimbThickness",     0.05f,0.5f, 0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(12, "LimbJointness",     0f,   1f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(13, "NeckLength",        0f,   1.5f, 0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(14, "HeadSize",          0.2f, 1.5f, 0.4f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(15, "JawType",           0f,   1f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(16, "MouthSize",         0f,   1f,   0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(17, "TailLength",        0f,   2f,   0.4f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, -1f);
            Reg(18, "EyeCount",          0f,   8f,   0.25f, GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(19, "EyeSize",           0f,   1f,   0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(20, "EyeForwardAngle",   0f,   1f,   0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(21, "EarSize",           0f,   1f,   0.2f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, -1f);
            Reg(22, "AntennaLength",     0f,   1.5f, 0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, -1f);
            Reg(23, "IntegumentType",    0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, 1f);
            Reg(24, "IntegumentDensity", 0f,   1f,   0.3f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Temperature, 1f);
            Reg(25, "BodyLengthScale",   0.3f, 3f,   0.5f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(26, "FinSize",           0f,   1.5f, 0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(27, "FinCount",          0f,   4f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(28, "ShellThickness",    0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(29, "SpineLength",       0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(30, "HeadFlatten",       0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(31, "TorsoFlatten",      0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(32, "LimbWebbing",       0f,   1f,   0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(33, "TongueLength",      0f,   1.5f, 0f,    GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);
            Reg(34, "ClawLength",        0f,   1f,   0.2f,  GeneKingdom.Animal, GeneGroup.AnimalBodyPlan, PhenotypeField.BodyPlan);

            // ── Body plan — Plants (35–64) ──────────────────────────────
            Reg(35, "RootDepth",        0f,   3f,   0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(36, "RootSpread",       0f,   2f,   0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(37, "StemHeight",       0.2f, 5f,   0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(38, "StemThickness",    0.05f,1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Wind);
            Reg(39, "Woodiness",        0f,   1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(40, "BranchingDepth",   0f,   5f,   0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(41, "BranchingAngle",   10f,  80f,  0.45f, GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(42, "BranchLengthRatio",0.3f, 0.9f, 0.6f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(43, "LeafSize",         0.1f, 2f,   0.5f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Light, -1f);
            Reg(44, "LeafCount",        1f,   8f,   0.5f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan, true, EnvironmentAxis.Light, -1f);
            Reg(45, "LeafThickness",    0.02f,0.3f, 0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(46, "LeafAngle",        0f,   90f,  0.4f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(47, "FlowerSize",       0f,   1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(48, "FlowerCount",      0f,   12f,  0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(49, "FlowerPetalCount", 3f,   12f,  0.35f, GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(50, "FruitSize",        0f,   1f,   0.2f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(51, "FruitCount",       0f,   20f,  0.2f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(52, "SeedCount",        1f,   100f, 0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(53, "SeedSize",         0.01f,1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(54, "GrowthHabit",      0f,   1f,   0.5f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(55, "CrownShape",       0f,   1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(56, "BarkTexture",      0f,   1f,   0.3f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(57, "Thorns",           0f,   0.5f, 0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(58, "VineCurl",         0f,   1f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(59, "NodeSpacing",      0.1f, 1f,   0.5f,  GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(60, "AerialRoots",      0f,   1f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(61, "TendrilLength",    0f,   1f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(62, "RootNodules",      0f,   1f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(63, "BulbSize",         0f,   1f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);
            Reg(64, "RosetteSpread",    0f,   2f,   0f,    GeneKingdom.Plant, GeneGroup.PlantBodyPlan, PhenotypeField.BodyPlan);

            // ── Metabolism (65–79, shared) ──────────────────────────────
            Reg(65, "MetabolicRate",      0.1f, 3f,   0.5f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(66, "Endothermy",         0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(67, "WaterNeed",          0.05f,1f,   0.4f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(68, "DietPhotosynthesis", 0f,   1f,   0.8f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(69, "DietHerbivory",      0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(70, "DietCarnivory",      0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(71, "DietScavenging",     0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(72, "DietFilterFeeding",  0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(73, "ToxinDefense",       0f,   1f,   0.1f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(74, "TemperatureOptimum", -10f, 40f,  0.5f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism, true, EnvironmentAxis.Temperature, 1f);
            Reg(75, "TemperatureTolerance",5f,  40f,  0.4f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism, true, EnvironmentAxis.Temperature, 1f);
            Reg(76, "OsmoregulationCost", 0f,   1f,   0.2f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(77, "DigestiveEfficiency", 0.3f,1f,   0.5f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(78, "StorageCapacity",    0.1f, 2f,   0.4f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);
            Reg(79, "AnaerobicCapacity",  0f,   1f,   0.1f,  GeneKingdom.Both, GeneGroup.Metabolism, PhenotypeField.Metabolism);

            // ── Growth & life history (80–89) ───────────────────────────
            Reg(80, "GrowthCurveType",   0f,   1f,   0.25f, GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(81, "GrowthRate",        0.1f, 2f,   0.5f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(82, "EmbryoDuration",    0.01f,0.15f,0.5f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(83, "JuvenileDuration",  0.1f, 0.4f, 0.5f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(84, "AdultDuration",     0.3f, 0.7f, 0.5f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(85, "SenescentOnset",    0.5f, 0.95f,0.8f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(86, "MaturityAge",       0.05f,0.5f, 0.3f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(87, "Lifespan",          100f, 10000f,0.3f, GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(88, "SeasonalGrowthGate",0f,   1f,   0.3f,  GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);
            Reg(89, "GrowthBurstAge",    0.05f,0.4f, 0.25f, GeneKingdom.Both, GeneGroup.Growth, PhenotypeField.Growth);

            // ── Appearance (90–99) ──────────────────────────────────────
            Reg(90, "PigmentRed",        0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(91, "PigmentGreen",      0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(92, "PigmentBlue",       0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(93, "PatternType",       0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(94, "PatternScale",      0.1f, 2f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(95, "PatternSymmetry",   0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(96, "SecondaryRed",      0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(97, "SecondaryGreen",    0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(98, "SecondaryBlue",     0f,   1f,   0.5f,  GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);
            Reg(99, "Bioluminescence",   0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Appearance, PhenotypeField.Appearance);

            // ── Behavior weights (100–119, Animals) ─────────────────────
            for (ushort i = 100; i <= 115; i++)
                Reg(i, $"BehaviorWeight{i - 100:D2}", -1f, 1f, 0f, GeneKingdom.Animal, GeneGroup.Behavior, PhenotypeField.Behavior);
            Reg(116, "InstinctAggression",  0f, 1f, 0.2f, GeneKingdom.Animal, GeneGroup.Behavior, PhenotypeField.Behavior);
            Reg(117, "InstinctCuriosity",   0f, 1f, 0.5f, GeneKingdom.Animal, GeneGroup.Behavior, PhenotypeField.Behavior);
            Reg(118, "InstinctFear",        0f, 1f, 0.5f, GeneKingdom.Animal, GeneGroup.Behavior, PhenotypeField.Behavior);
            Reg(119, "InstinctSociability", 0f, 1f, 0.3f, GeneKingdom.Animal, GeneGroup.Behavior, PhenotypeField.Behavior);

            // ── Reproduction (120–129) ──────────────────────────────────
            Reg(120, "ReproductionMode",  0f,   1f,   0.75f, GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(121, "ClutchSize",        1f,   20f,  0.3f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(122, "EggSeedSize",       0.05f,1f,   0.3f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(123, "ParentalCare",      0f,   1f,   0.2f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(124, "BreedingSeason",    0f,   1f,   0f,    GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(125, "MutationRateMod",    0.01f,0.2f, 0.5f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(126, "ReproFrequency",    0.1f, 4f,   0.25f, GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(127, "GestationDuration", 0.01f,0.3f, 0.3f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(128, "SexualDimorphism",  0f,   1f,   0.2f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);
            Reg(129, "PheromoneStrength", 0f,   1f,   0.2f,  GeneKingdom.Both, GeneGroup.Reproduction, PhenotypeField.Reproduction);

            return new GeneCatalog(defs);
        }
    }
}