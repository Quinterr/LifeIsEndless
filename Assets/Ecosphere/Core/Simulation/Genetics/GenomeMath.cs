// Ecosphere — GenomeMath: the deterministic development function.
// phenotype = f(genome, developmental age, environment history)
// Pure C# (no engine references). Used by PhenotypeUpdateSystem (simulation),
// the organism viewer (presentation), and EditMode tests (determinism proof).

using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>Inputs to one developmental evaluation.</summary>
    public struct DevelopmentInput
    {
        /// <summary>Genome gene list (buffer layout, see <see cref="GenomeFactory"/>).</summary>
        public readonly Gene[] Genes;
        /// <summary>Genome's kingdom (selects which genes apply).</summary>
        public readonly GeneKingdom Kingdom;
        /// <summary>Developmental age in ticks (scaled by DevelopmentRate upstream).</summary>
        public readonly float DevAgeTicks;
        /// <summary>Environment history EMAs at the organism's cell.</summary>
        public readonly EnvironmentEMA Ema;

        public DevelopmentInput(Gene[] genes, GeneKingdom kingdom, float devAgeTicks, in EnvironmentEMA ema)
        {
            Genes = genes;
            Kingdom = kingdom;
            DevAgeTicks = devAgeTicks;
            Ema = ema;
        }
    }

    /// <summary>Output of one developmental evaluation (fully computed phenotype).</summary>
    public struct DevelopmentOutput
    {
        public Phenotype Phenotype;
        public LifeStage Stage;
        public float StageProgress;
        /// <summary>Relative maturity size 0..1 (growth curve output, senescence-shrunk).</summary>
        public float Size;
    }

    /// <summary>
    /// The development function. Deterministic: identical inputs produce
    /// byte-identical <see cref="DevelopmentOutput.Phenotype"/> (contract).
    ///
    /// Pipeline:
    ///  1. Regulatory genes → sensitivity, stage-threshold shift, size multiplier.
    ///  2. Growth genes → lifespan, stage durations, curve params.
    ///  3. Stage + progress from (dev age, durations).
    ///  4. Size from growth curve; senescent stage shrinks the organism slightly.
    ///  5. Per gene: mapped value × stage gate × (1 + epigenetic modulation)
    ///     → written into the flat Phenotype struct.
    ///  6. Diet vector normalized to sum 1.
    /// </summary>
    public static class GenomeMath
    {
        /// <summary>
        /// Evaluate development for one organism state.
        /// </summary>
        /// <param name="catalog">Gene catalog (metadata per TypeId).</param>
        /// <param name="input">Genome + age + environment history.</param>
        public static DevelopmentOutput Compute(GeneCatalog catalog, in DevelopmentInput input)
        {
            Gene[] genes = input.Genes;
            GeneKingdom kingdom = input.Kingdom;

            // ── 1. Regulatory genes (always fully expressed) ──────────
            float sensitivity = 1f, stageShift = 0f, sizeMultiplier = 1f, developmentRate = 1f;
            foreach (Gene g in genes)
            {
                GeneDefinition definition = catalog.Get(g.TypeId);
                float expressed = GenomeEvolutionMath.ExpressAllele(g.Value, g.Dominance, definition.Default);
                float mapped = catalog.MapToRange(g.TypeId, expressed);
                switch (g.TypeId)
                {
                    case GeneId.ExpressionSensitivity: sensitivity = mapped; break;
                    case GeneId.StageThresholdShift:   stageShift = mapped; break;
                    case GeneId.SizeMultiplier:        sizeMultiplier = mapped; break;
                    case GeneId.DevelopmentRate:       developmentRate = mapped; break;
                }
            }
            sensitivity = Math.Max(0.25f, Math.Min(2f, sensitivity));

            // ── 2. Growth & life history (Growth group is always on) ──
            float embryoFrac = 0.05f, juvenileFrac = 0.25f, senescenceFrac = 0.8f;
            float curveType = 0f, growthRate = 1f, lifespan = 3600f, burstAge = 0.25f;
            foreach (Gene g in genes)
            {
                GeneDefinition definition = catalog.Get(g.TypeId);
                float expressed = GenomeEvolutionMath.ExpressAllele(g.Value, g.Dominance, definition.Default);
                float mapped = catalog.MapToRange(g.TypeId, expressed);
                switch (g.TypeId)
                {
                    case GeneId.GrowthCurveType: curveType = mapped; break;
                    case GeneId.GrowthRate:      growthRate = mapped; break;
                    case GeneId.EmbryoDuration:  embryoFrac = mapped; break;
                    case GeneId.JuvenileDuration: juvenileFrac = mapped; break;
                    case GeneId.SenescentOnset:  senescenceFrac = mapped; break;
                    case GeneId.Lifespan:        lifespan = mapped; break;
                    case GeneId.GrowthBurstAge:  burstAge = mapped; break;
                }
            }
            // Keep stage fractions sane and ordered (embryo < juvenile < senescence).
            // The clamps guarantee the adult window always spans [≤0.45, ≥0.7],
            // so the behavior (0.6) and reproduction (0.65) expression onsets —
            // fixed fractions of lifespan — always land inside the adult stage.
            embryoFrac = Math.Clamp(embryoFrac, 0.02f, 0.15f);
            juvenileFrac = Math.Clamp(juvenileFrac, 0.1f, 0.3f);
            senescenceFrac = Math.Clamp(senescenceFrac, 0.7f, 0.95f);

            // ── 3. Stage, progress, normalized age ────────────────────
            float devAge = Math.Max(0f, input.DevAgeTicks);
            float adultFrac = 1f - embryoFrac - juvenileFrac;
            LifeStage stage = MorphogenMath.DetermineStage(embryoFrac, juvenileFrac, adultFrac,
                senescenceFrac, devAge, lifespan);
            float progress = MorphogenMath.StageProgress(stage, embryoFrac, juvenileFrac,
                adultFrac, senescenceFrac, devAge, lifespan);
            progress = Math.Clamp(progress, 0f, 1f);
            float ageFraction = Math.Clamp(devAge / lifespan, 0f, 1f);

            // ── 4. Size (growth curve + senescence shrink) ────────────
            float size = MorphogenMath.GrowthSize(curveType, growthRate, devAge, lifespan, burstAge);
            if (stage == LifeStage.Senescent) size *= 1f - 0.25f * progress;
            size = Math.Clamp(size, 0f, 1f);

            // ── 5. Per-gene expression → Phenotype fields ─────────────
            var p = new Phenotype();
            p.Size = size;
            p.SizeMultiplier = sizeMultiplier;

            // Metabolism-derived values needed by env modulation (2 passes):
            float tempOptimum = catalog.MapToRange(GeneId.TemperatureOptimum, 0.5f);
            float tempTolerance = catalog.MapToRange(GeneId.TemperatureTolerance, 0.5f);
            foreach (Gene g in genes)
            {
                GeneDefinition definition = catalog.Get(g.TypeId);
                float expressed = GenomeEvolutionMath.ExpressAllele(g.Value, g.Dominance, definition.Default);
                if (g.TypeId == GeneId.TemperatureOptimum) tempOptimum = catalog.MapToRange(g.TypeId, expressed);
                else if (g.TypeId == GeneId.TemperatureTolerance) tempTolerance = catalog.MapToRange(g.TypeId, expressed);
            }

            foreach (Gene g in genes)
            {
                GeneDefinition def = catalog.Get(g.TypeId);
                if (def.Id != g.TypeId || (def.Kingdom & kingdom) == 0) continue;

                float expressed = GenomeEvolutionMath.ExpressAllele(g.Value, g.Dominance, def.Default);
                float mapped = def.Min + expressed * (def.Max - def.Min);
                float gate = MorphogenMath.StageGate(def.Group, ageFraction, stageShift);
                if (gate <= 0.0001f) continue; // gene not expressed yet at this age

                float envMod = 0f;
                if (def.EnvironmentSensitive)
                {
                    envMod = MorphogenMath.ComputeEnvModulation(input.Ema, def.EnvAxis,
                        def.EnvPolarity, tempOptimum, tempTolerance, sensitivity);
                }

                float effective = mapped * gate * (1f + envMod);
                ApplyToPhenotype(ref p, g.TypeId, effective);
            }

            // ── 6. Normalize diet vector ──────────────────────────────
            MorphogenMath.NormalizeDiet(ref p.DietPhotosynthesis, ref p.DietHerbivory,
                ref p.DietCarnivory, ref p.DietScavenging, ref p.DietFilterFeeding);

            return new DevelopmentOutput
            {
                Phenotype = p,
                Stage = stage,
                StageProgress = progress,
                Size = size
            };
        }

        /// <summary>Write one effective gene value into the flat phenotype struct.</summary>
        public static void ApplyToPhenotype(ref Phenotype p, ushort typeId, float v)
        {
            switch (typeId)
            {
                // Animals body plan
                case GeneId.Symmetry: p.Symmetry = v; break;
                case GeneId.TorsoSegments: p.TorsoSegments = v; break;
                case GeneId.TorsoLength: p.TorsoLength = v; break;
                case GeneId.TorsoGirth: p.TorsoGirth = v; break;
                case GeneId.LimbCount: p.LimbCount = v; break;
                case GeneId.LimbLength: p.LimbLength = v; break;
                case GeneId.LimbThickness: p.LimbThickness = v; break;
                case GeneId.LimbJointness: p.LimbJointness = v; break;
                case GeneId.NeckLength: p.NeckLength = v; break;
                case GeneId.HeadSize: p.HeadSize = v; break;
                case GeneId.JawType: p.JawType = v; break;
                case GeneId.MouthSize: p.MouthSize = v; break;
                case GeneId.TailLength: p.TailLength = v; break;
                case GeneId.EyeCount: p.EyeCount = v; break;
                case GeneId.EyeSize: p.EyeSize = v; break;
                case GeneId.EyeForwardAngle: p.EyeForwardAngle = v; break;
                case GeneId.EarSize: p.EarSize = v; break;
                case GeneId.AntennaLength: p.AntennaLength = v; break;
                case GeneId.IntegumentType: p.IntegumentType = v; break;
                case GeneId.IntegumentDensity: p.IntegumentDensity = v; break;
                case GeneId.BodyLengthScale: p.BodyLengthScale = v; break;
                case GeneId.FinSize: p.FinSize = v; break;
                case GeneId.FinCount: p.FinCount = v; break;
                case GeneId.ShellThickness: p.ShellThickness = v; break;
                case GeneId.SpineLength: p.SpineLength = v; break;
                case GeneId.HeadFlatten: p.HeadFlatten = v; break;
                case GeneId.TorsoFlatten: p.TorsoFlatten = v; break;
                case GeneId.LimbWebbing: p.LimbWebbing = v; break;
                case GeneId.TongueLength: p.TongueLength = v; break;
                case GeneId.ClawLength: p.ClawLength = v; break;
                // Plants body plan
                case GeneId.RootDepth: p.RootDepth = v; break;
                case GeneId.RootSpread: p.RootSpread = v; break;
                case GeneId.StemHeight: p.StemHeight = v; break;
                case GeneId.StemThickness: p.StemThickness = v; break;
                case GeneId.Woodiness: p.Woodiness = v; break;
                case GeneId.BranchingDepth: p.BranchingDepth = v; break;
                case GeneId.BranchingAngle: p.BranchingAngle = v; break;
                case GeneId.BranchLengthRatio: p.BranchLengthRatio = v; break;
                case GeneId.LeafSize: p.LeafSize = v; break;
                case GeneId.LeafCount: p.LeafCount = v; break;
                case GeneId.LeafThickness: p.LeafThickness = v; break;
                case GeneId.LeafAngle: p.LeafAngle = v; break;
                case GeneId.FlowerSize: p.FlowerSize = v; break;
                case GeneId.FlowerCount: p.FlowerCount = v; break;
                case GeneId.FlowerPetalCount: p.FlowerPetalCount = v; break;
                case GeneId.FruitSize: p.FruitSize = v; break;
                case GeneId.FruitCount: p.FruitCount = v; break;
                case GeneId.SeedCount: p.SeedCount = v; break;
                case GeneId.SeedSize: p.SeedSize = v; break;
                case GeneId.GrowthHabit: p.GrowthHabit = v; break;
                case GeneId.CrownShape: p.CrownShape = v; break;
                case GeneId.BarkTexture: p.BarkTexture = v; break;
                case GeneId.Thorns: p.Thorns = v; break;
                case GeneId.VineCurl: p.VineCurl = v; break;
                case GeneId.NodeSpacing: p.NodeSpacing = v; break;
                case GeneId.AerialRoots: p.AerialRoots = v; break;
                case GeneId.TendrilLength: p.TendrilLength = v; break;
                case GeneId.RootNodules: p.RootNodules = v; break;
                case GeneId.BulbSize: p.BulbSize = v; break;
                case GeneId.RosetteSpread: p.RosetteSpread = v; break;
                // Metabolism
                case GeneId.MetabolicRate: p.MetabolicRate = v; break;
                case GeneId.Endothermy: p.Endothermy = v; break;
                case GeneId.WaterNeed: p.WaterNeed = v; break;
                case GeneId.DietPhotosynthesis: p.DietPhotosynthesis = v; break;
                case GeneId.DietHerbivory: p.DietHerbivory = v; break;
                case GeneId.DietCarnivory: p.DietCarnivory = v; break;
                case GeneId.DietScavenging: p.DietScavenging = v; break;
                case GeneId.DietFilterFeeding: p.DietFilterFeeding = v; break;
                case GeneId.ToxinDefense: p.ToxinDefense = v; break;
                case GeneId.TemperatureOptimum: p.TemperatureOptimum = v; break;
                case GeneId.TemperatureTolerance: p.TemperatureTolerance = v; break;
                case GeneId.OsmoregulationCost: p.OsmoregulationCost = v; break;
                case GeneId.DigestiveEfficiency: p.DigestiveEfficiency = v; break;
                case GeneId.StorageCapacity: p.StorageCapacity = v; break;
                case GeneId.AnaerobicCapacity: p.AnaerobicCapacity = v; break;
                // Growth
                case GeneId.GrowthCurveType: p.GrowthCurveType = v; break;
                case GeneId.GrowthRate: p.GrowthRate = v; break;
                case GeneId.EmbryoDuration: p.EmbryoDuration = v; break;
                case GeneId.JuvenileDuration: p.JuvenileDuration = v; break;
                case GeneId.AdultDuration: p.AdultDuration = v; break;
                case GeneId.SenescentOnset: p.SenescentOnset = v; break;
                case GeneId.MaturityAge: p.MaturityAge = v; break;
                case GeneId.Lifespan: p.Lifespan = v; break;
                case GeneId.SeasonalGrowthGate: p.SeasonalGrowthGate = v; break;
                case GeneId.GrowthBurstAge: p.GrowthBurstAge = v; break;
                // Appearance
                case GeneId.PigmentRed: p.PigmentRed = v; break;
                case GeneId.PigmentGreen: p.PigmentGreen = v; break;
                case GeneId.PigmentBlue: p.PigmentBlue = v; break;
                case GeneId.PatternType: p.PatternType = v; break;
                case GeneId.PatternScale: p.PatternScale = v; break;
                case GeneId.PatternSymmetry: p.PatternSymmetry = v; break;
                case GeneId.SecondaryRed: p.SecondaryRed = v; break;
                case GeneId.SecondaryGreen: p.SecondaryGreen = v; break;
                case GeneId.SecondaryBlue: p.SecondaryBlue = v; break;
                case GeneId.Bioluminescence: p.Bioluminescence = v; break;
                // Behavior blob (stage 05 consumes; copied verbatim)
                case GeneId.BehaviorWeight00: p.BehaviorWeight00 = v; break;
                case GeneId.BehaviorWeight01: p.BehaviorWeight01 = v; break;
                case GeneId.BehaviorWeight02: p.BehaviorWeight02 = v; break;
                case GeneId.BehaviorWeight03: p.BehaviorWeight03 = v; break;
                case GeneId.BehaviorWeight04: p.BehaviorWeight04 = v; break;
                case GeneId.BehaviorWeight05: p.BehaviorWeight05 = v; break;
                case GeneId.BehaviorWeight06: p.BehaviorWeight06 = v; break;
                case GeneId.BehaviorWeight07: p.BehaviorWeight07 = v; break;
                case GeneId.BehaviorWeight08: p.BehaviorWeight08 = v; break;
                case GeneId.BehaviorWeight09: p.BehaviorWeight09 = v; break;
                case GeneId.BehaviorWeight10: p.BehaviorWeight10 = v; break;
                case GeneId.BehaviorWeight11: p.BehaviorWeight11 = v; break;
                case GeneId.BehaviorWeight12: p.BehaviorWeight12 = v; break;
                case GeneId.BehaviorWeight13: p.BehaviorWeight13 = v; break;
                case GeneId.BehaviorWeight14: p.BehaviorWeight14 = v; break;
                case GeneId.BehaviorWeight15: p.BehaviorWeight15 = v; break;
                case GeneId.InstinctAggression: p.InstinctAggression = v; break;
                case GeneId.InstinctCuriosity: p.InstinctCuriosity = v; break;
                case GeneId.InstinctFear: p.InstinctFear = v; break;
                case GeneId.InstinctSociability: p.InstinctSociability = v; break;
                // Reproduction
                case GeneId.ReproductionMode: p.ReproductionMode = v; break;
                case GeneId.ClutchSize: p.ClutchSize = v; break;
                case GeneId.EggSeedSize: p.EggSeedSize = v; break;
                case GeneId.ParentalCare: p.ParentalCare = v; break;
                case GeneId.BreedingSeason: p.BreedingSeason = v; break;
                case GeneId.MutationRateMod: p.MutationRateMod = v; break;
                case GeneId.ReproFrequency: p.ReproFrequency = v; break;
                case GeneId.GestationDuration: p.GestationDuration = v; break;
                case GeneId.SexualDimorphism: p.SexualDimorphism = v; break;
                case GeneId.PheromoneStrength: p.PheromoneStrength = v; break;
            }
        }

        /// <summary>
        /// Structural hash of a phenotype (determinism / mesh-pool key input).
        /// FNV-1a over quantized (2 decimals) float bit patterns, so near-identical
        /// phenotypes (env EMA drift) collapse to the same pool bucket.
        /// </summary>
        public static uint HashPhenotype(in Phenotype p)
        {
            uint h = 2166136261u;
            void Mix(float v)
            {
                // Quantize to 2 decimals → stable under tiny EMA drift.
                int q = (int)Math.Round(v * 100f);
                uint u = unchecked((uint)q);
                for (int i = 0; i < 4; i++)
                {
                    h ^= (u >> (i * 8)) & 0xFFu;
                    h *= 16777619u;
                }
            }
            Mix(p.Size); Mix(p.SizeMultiplier);
            Mix(p.Symmetry); Mix(p.TorsoSegments); Mix(p.TorsoLength); Mix(p.TorsoGirth);
            Mix(p.LimbCount); Mix(p.LimbLength); Mix(p.LimbThickness); Mix(p.LimbJointness);
            Mix(p.NeckLength); Mix(p.HeadSize); Mix(p.JawType); Mix(p.MouthSize);
            Mix(p.TailLength); Mix(p.EyeCount); Mix(p.EyeSize); Mix(p.EyeForwardAngle);
            Mix(p.EarSize); Mix(p.AntennaLength); Mix(p.IntegumentType); Mix(p.IntegumentDensity);
            Mix(p.BodyLengthScale); Mix(p.FinSize); Mix(p.FinCount); Mix(p.ShellThickness);
            Mix(p.SpineLength); Mix(p.HeadFlatten); Mix(p.TorsoFlatten); Mix(p.LimbWebbing);
            Mix(p.TongueLength); Mix(p.ClawLength);
            Mix(p.RootDepth); Mix(p.RootSpread); Mix(p.StemHeight); Mix(p.StemThickness);
            Mix(p.Woodiness); Mix(p.BranchingDepth); Mix(p.BranchingAngle); Mix(p.BranchLengthRatio);
            Mix(p.LeafSize); Mix(p.LeafCount); Mix(p.LeafThickness); Mix(p.LeafAngle);
            Mix(p.FlowerSize); Mix(p.FlowerCount); Mix(p.FlowerPetalCount);
            Mix(p.FruitSize); Mix(p.FruitCount); Mix(p.SeedCount); Mix(p.SeedSize);
            Mix(p.GrowthHabit); Mix(p.CrownShape); Mix(p.BarkTexture); Mix(p.Thorns);
            Mix(p.VineCurl); Mix(p.NodeSpacing); Mix(p.AerialRoots); Mix(p.TendrilLength);
            Mix(p.RootNodules); Mix(p.BulbSize); Mix(p.RosetteSpread);
            Mix(p.MetabolicRate); Mix(p.Endothermy); Mix(p.WaterNeed);
            Mix(p.DietPhotosynthesis); Mix(p.DietHerbivory); Mix(p.DietCarnivory);
            Mix(p.DietScavenging); Mix(p.DietFilterFeeding);
            Mix(p.ToxinDefense); Mix(p.TemperatureOptimum); Mix(p.TemperatureTolerance);
            Mix(p.OsmoregulationCost); Mix(p.DigestiveEfficiency);
            Mix(p.StorageCapacity); Mix(p.AnaerobicCapacity);
            Mix(p.GrowthCurveType); Mix(p.GrowthRate); Mix(p.EmbryoDuration);
            Mix(p.JuvenileDuration); Mix(p.AdultDuration); Mix(p.SenescentOnset);
            Mix(p.MaturityAge); Mix(p.Lifespan); Mix(p.SeasonalGrowthGate); Mix(p.GrowthBurstAge);
            Mix(p.PigmentRed); Mix(p.PigmentGreen); Mix(p.PigmentBlue);
            Mix(p.PatternType); Mix(p.PatternScale); Mix(p.PatternSymmetry);
            Mix(p.SecondaryRed); Mix(p.SecondaryGreen); Mix(p.SecondaryBlue);
            Mix(p.Bioluminescence);
            Mix(p.BehaviorWeight00); Mix(p.BehaviorWeight01); Mix(p.BehaviorWeight02);
            Mix(p.BehaviorWeight03); Mix(p.BehaviorWeight04); Mix(p.BehaviorWeight05);
            Mix(p.BehaviorWeight06); Mix(p.BehaviorWeight07); Mix(p.BehaviorWeight08);
            Mix(p.BehaviorWeight09); Mix(p.BehaviorWeight10); Mix(p.BehaviorWeight11);
            Mix(p.BehaviorWeight12); Mix(p.BehaviorWeight13); Mix(p.BehaviorWeight14);
            Mix(p.BehaviorWeight15); Mix(p.InstinctAggression); Mix(p.InstinctCuriosity);
            Mix(p.InstinctFear); Mix(p.InstinctSociability);
            Mix(p.ReproductionMode); Mix(p.ClutchSize); Mix(p.EggSeedSize);
            Mix(p.ParentalCare); Mix(p.BreedingSeason); Mix(p.MutationRateMod);
            Mix(p.ReproFrequency); Mix(p.GestationDuration); Mix(p.SexualDimorphism);
            Mix(p.PheromoneStrength);
            return h;
        }
    }
}