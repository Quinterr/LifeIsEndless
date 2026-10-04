using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    public enum EvolutionScenarioKind : byte
    {
        Default = 0,
        IceAgeTrend = 1,
        PredatorPressure = 2,
        Drought = 3,
        MassExtinctionRecovery = 4
    }

    public enum EvolutionEventKind : byte
    {
        Birth = 0,
        Death = 1,
        Speciation = 2,
        Extinction = 3,
        BabyBoom = 4,
        MassDieOff = 5,
        Recovery = 6
    }

    public enum EvolutionCause : byte
    {
        None = 0,
        AdaptiveDivergence = 1,
        Climate = 2,
        Predation = 3,
        ResourceScarcity = 4,
        OldAge = 5,
        Unspecified = 6
    }

    /// <summary>Selection-relevant normalized traits shared by reports and species centroids.</summary>
    [Serializable]
    public struct EvolutionTraitVector
    {
        public float Size;
        public float MetabolicRate;
        public float ColdTolerance;
        public float Speed;
        public float LitterSize;
        public float CoverDensity;
        public float ToxinDefense;
        public float RootDepth;
        public float WaterEfficiency;
        public float Vigilance;
        public float Herdiness;

        public const int ComponentCount = 11;

        public float Get(int index)
        {
            switch (index)
            {
                case 0: return Size;
                case 1: return MetabolicRate;
                case 2: return ColdTolerance;
                case 3: return Speed;
                case 4: return LitterSize;
                case 5: return CoverDensity;
                case 6: return ToxinDefense;
                case 7: return RootDepth;
                case 8: return WaterEfficiency;
                case 9: return Vigilance;
                case 10: return Herdiness;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public void Set(int index, float value)
        {
            switch (index)
            {
                case 0: Size = value; break;
                case 1: MetabolicRate = value; break;
                case 2: ColdTolerance = value; break;
                case 3: Speed = value; break;
                case 4: LitterSize = value; break;
                case 5: CoverDensity = value; break;
                case 6: ToxinDefense = value; break;
                case 7: RootDepth = value; break;
                case 8: WaterEfficiency = value; break;
                case 9: Vigilance = value; break;
                case 10: Herdiness = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static EvolutionTraitVector operator +(EvolutionTraitVector a, EvolutionTraitVector b)
        {
            return new EvolutionTraitVector
            {
                Size = a.Size + b.Size,
                MetabolicRate = a.MetabolicRate + b.MetabolicRate,
                ColdTolerance = a.ColdTolerance + b.ColdTolerance,
                Speed = a.Speed + b.Speed,
                LitterSize = a.LitterSize + b.LitterSize,
                CoverDensity = a.CoverDensity + b.CoverDensity,
                ToxinDefense = a.ToxinDefense + b.ToxinDefense,
                RootDepth = a.RootDepth + b.RootDepth,
                WaterEfficiency = a.WaterEfficiency + b.WaterEfficiency,
                Vigilance = a.Vigilance + b.Vigilance,
                Herdiness = a.Herdiness + b.Herdiness
            };
        }

        public static EvolutionTraitVector operator -(EvolutionTraitVector a, EvolutionTraitVector b)
        {
            return new EvolutionTraitVector
            {
                Size = a.Size - b.Size,
                MetabolicRate = a.MetabolicRate - b.MetabolicRate,
                ColdTolerance = a.ColdTolerance - b.ColdTolerance,
                Speed = a.Speed - b.Speed,
                LitterSize = a.LitterSize - b.LitterSize,
                CoverDensity = a.CoverDensity - b.CoverDensity,
                ToxinDefense = a.ToxinDefense - b.ToxinDefense,
                RootDepth = a.RootDepth - b.RootDepth,
                WaterEfficiency = a.WaterEfficiency - b.WaterEfficiency,
                Vigilance = a.Vigilance - b.Vigilance,
                Herdiness = a.Herdiness - b.Herdiness
            };
        }

        public static EvolutionTraitVector operator *(EvolutionTraitVector a, float scale)
        {
            return new EvolutionTraitVector
            {
                Size = a.Size * scale,
                MetabolicRate = a.MetabolicRate * scale,
                ColdTolerance = a.ColdTolerance * scale,
                Speed = a.Speed * scale,
                LitterSize = a.LitterSize * scale,
                CoverDensity = a.CoverDensity * scale,
                ToxinDefense = a.ToxinDefense * scale,
                RootDepth = a.RootDepth * scale,
                WaterEfficiency = a.WaterEfficiency * scale,
                Vigilance = a.Vigilance * scale,
                Herdiness = a.Herdiness * scale
            };
        }

        public static EvolutionTraitVector FromPhenotype(in Phenotype phenotype)
        {
            return new EvolutionTraitVector
            {
                Size = Clamp01(phenotype.Size * phenotype.SizeMultiplier / 3f),
                MetabolicRate = Clamp01((phenotype.MetabolicRate - 0.1f) / 2.9f),
                ColdTolerance = Clamp01((phenotype.TemperatureTolerance - 5f) / 35f),
                Speed = Clamp01((phenotype.LimbLength - 0.2f) / 1.8f),
                LitterSize = Clamp01((phenotype.ClutchSize - 1f) / 19f),
                CoverDensity = Clamp01(phenotype.IntegumentDensity),
                ToxinDefense = Clamp01(phenotype.ToxinDefense),
                RootDepth = Clamp01(phenotype.RootDepth / 3f),
                WaterEfficiency = Clamp01((1f - phenotype.WaterNeed) / 0.95f),
                Vigilance = Clamp01(phenotype.InstinctFear),
                Herdiness = Clamp01(phenotype.InstinctSociability)
            };
        }

        public static EvolutionTraitVector FromGenome(IReadOnlyList<Gene> genome)
        {
            return new EvolutionTraitVector
            {
                Size = Allele(genome, GeneId.SizeMultiplier, 0.5f),
                MetabolicRate = Allele(genome, GeneId.MetabolicRate, 0.5f),
                ColdTolerance = Allele(genome, GeneId.TemperatureTolerance, 0.4f),
                Speed = Allele(genome, GeneId.LimbLength, 0.5f),
                LitterSize = Allele(genome, GeneId.ClutchSize, 0.3f),
                CoverDensity = Allele(genome, GeneId.IntegumentDensity, 0.3f),
                ToxinDefense = Allele(genome, GeneId.ToxinDefense, 0.1f),
                RootDepth = Allele(genome, GeneId.RootDepth, 0.4f),
                WaterEfficiency = 1f - Allele(genome, GeneId.WaterNeed, 0.4f),
                Vigilance = Allele(genome, GeneId.InstinctFear, 0.5f),
                Herdiness = Allele(genome, GeneId.InstinctSociability, 0.3f)
            };
        }

        private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

        private static float Allele(IReadOnlyList<Gene> genome, ushort typeId, float fallback)
        {
            for (int i = 0; i < genome.Count; i++)
                if (genome[i].TypeId == typeId)
                    return GenomeEvolutionMath.ExpressAllele(genome[i].Value, genome[i].Dominance, fallback);
            return fallback;
        }
    }

    /// <summary>Online Welford moments; removal is exact up to floating point error.</summary>
    [Serializable]
    public struct TraitMoments
    {
        public int Count;
        public EvolutionTraitVector Mean;
        public EvolutionTraitVector M2;

        public void Add(in EvolutionTraitVector value)
        {
            Count++;
            for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++)
            {
                float oldMean = Mean.Get(i);
                float delta = value.Get(i) - oldMean;
                float newMean = oldMean + delta / Count;
                Mean.Set(i, newMean);
                float m2 = M2.Get(i) + delta * (value.Get(i) - newMean);
                M2.Set(i, m2);
            }
        }

        public void Remove(in EvolutionTraitVector value)
        {
            if (Count <= 0) return;
            if (Count == 1)
            {
                Count = 0;
                Mean = default;
                M2 = default;
                return;
            }
            int oldCount = Count;
            int newCount = oldCount - 1;
            for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++)
            {
                float oldMean = Mean.Get(i);
                float newMean = (oldCount * oldMean - value.Get(i)) / newCount;
                float m2 = M2.Get(i) - (value.Get(i) - oldMean) * (value.Get(i) - newMean);
                Mean.Set(i, newMean);
                M2.Set(i, Math.Max(0f, m2));
            }
            Count = newCount;
        }

        public EvolutionTraitVector Variance
        {
            get
            {
                if (Count < 2) return default;
                return M2 * (1f / (Count - 1));
            }
        }
    }

    /// <summary>Serializable per-species/year report row; also the CSV schema.</summary>
    [Serializable]
    public struct EvolutionMetricsRecord
    {
        public int Year;
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public int Population;
        public EvolutionTraitVector MeanTraits;
        public EvolutionTraitVector Variance;
        public float GenomeDiversity;
        public float EffectivePopulationSize;
        public EvolutionTraitVector SelectionDifferential;
        public float MutationLoad;
        public int Births;
        public int Deaths;
        public float PreyPopulation;
        public float PredatorPopulation;

        public bool IsFinite
        {
            get
            {
                for (int i = 0; i < EvolutionTraitVector.ComponentCount; i++)
                    if (!IsFiniteValue(MeanTraits.Get(i)) || !IsFiniteValue(Variance.Get(i)) ||
                        !IsFiniteValue(SelectionDifferential.Get(i))) return false;
                return IsFiniteValue(GenomeDiversity) && IsFiniteValue(EffectivePopulationSize) &&
                       IsFiniteValue(MutationLoad) && IsFiniteValue(PreyPopulation) &&
                       IsFiniteValue(PredatorPopulation);
            }
        }

        private static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    public struct EvolutionEventRecord
    {
        public EvolutionEventKind Kind;
        public EvolutionCause Cause;
        public int Year;
        public ulong Tick;
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public int Region;
        public int Count;
        public EvolutionTraitVector TraitDelta;
    }

    [Serializable]
    public struct SpeciesNode
    {
        public uint SpeciesId;
        public uint ParentSpeciesId;
        public GeneKingdom Kingdom;
        public ulong FoundedAtTick;
        public ulong ExtinctAtTick;
        public EvolutionCause OriginCause;
        public bool IsExtinct;
    }

    [Serializable]
    public struct PhylogenyEdge
    {
        public uint ParentSpeciesId;
        public uint ChildSpeciesId;
        public ulong Tick;
        public EvolutionCause Cause;
    }

    /// <summary>Append-only species DAG and extinction tombstones for save-friendly lineage history.</summary>
    public sealed class SpeciesPhylogeny
    {
        private readonly List<SpeciesNode> _nodes = new List<SpeciesNode>();
        private readonly List<PhylogenyEdge> _edges = new List<PhylogenyEdge>();

        public IReadOnlyList<SpeciesNode> Nodes => _nodes;
        public IReadOnlyList<PhylogenyEdge> Edges => _edges;

        public void RegisterFounder(uint speciesId, GeneKingdom kingdom, ulong tick)
        {
            if (speciesId == 0 || FindNode(speciesId) >= 0)
                throw new ArgumentException("Founder species ids must be non-zero and unique.", nameof(speciesId));
            _nodes.Add(new SpeciesNode
            {
                SpeciesId = speciesId,
                Kingdom = kingdom,
                FoundedAtTick = tick,
                OriginCause = EvolutionCause.None
            });
        }

        public void Fork(uint parentSpeciesId, uint childSpeciesId, GeneKingdom kingdom,
            ulong tick, EvolutionCause cause)
        {
            if (FindNode(parentSpeciesId) < 0)
                throw new InvalidOperationException("A phylogeny edge must reference an existing parent.");
            if (childSpeciesId == 0 || FindNode(childSpeciesId) >= 0)
                throw new ArgumentException("Child species ids must be non-zero and unique.", nameof(childSpeciesId));
            if (parentSpeciesId == childSpeciesId || WouldCreateCycle(parentSpeciesId, childSpeciesId))
                throw new InvalidOperationException("Species ancestry must remain a directed acyclic graph.");
            _nodes.Add(new SpeciesNode
            {
                SpeciesId = childSpeciesId,
                ParentSpeciesId = parentSpeciesId,
                Kingdom = kingdom,
                FoundedAtTick = tick,
                OriginCause = cause
            });
            _edges.Add(new PhylogenyEdge
            {
                ParentSpeciesId = parentSpeciesId,
                ChildSpeciesId = childSpeciesId,
                Tick = tick,
                Cause = cause
            });
        }

        public void Extinguish(uint speciesId, ulong tick)
        {
            int index = FindNode(speciesId);
            if (index < 0) throw new ArgumentException("Unknown species id.", nameof(speciesId));
            SpeciesNode node = _nodes[index];
            if (node.IsExtinct) return;
            node.IsExtinct = true;
            node.ExtinctAtTick = tick;
            _nodes[index] = node;
        }

        public bool IsAcyclic()
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                PhylogenyEdge edge = _edges[i];
                if (edge.ParentSpeciesId == edge.ChildSpeciesId ||
                    FindNode(edge.ParentSpeciesId) < 0 || FindNode(edge.ChildSpeciesId) < 0)
                    return false;
                int childIndex = FindNode(edge.ChildSpeciesId);
                if (_nodes[childIndex].ParentSpeciesId != edge.ParentSpeciesId) return false;
                if (HasPath(edge.ChildSpeciesId, edge.ParentSpeciesId)) return false;
            }
            return true;
        }

        public List<uint> GetAncestors(uint speciesId)
        {
            var result = new List<uint>();
            int index = FindNode(speciesId);
            while (index >= 0 && _nodes[index].ParentSpeciesId != 0)
            {
                uint parent = _nodes[index].ParentSpeciesId;
                result.Add(parent);
                index = FindNode(parent);
            }
            return result;
        }

        private bool WouldCreateCycle(uint parent, uint child) => HasPath(child, parent);

        private bool HasPath(uint from, uint to)
        {
            uint cursor = from;
            int guard = _nodes.Count + 1;
            while (guard-- > 0)
            {
                if (cursor == to) return true;
                int index = FindNode(cursor);
                if (index < 0 || _nodes[index].ParentSpeciesId == 0) return false;
                cursor = _nodes[index].ParentSpeciesId;
            }
            return true;
        }

        private int FindNode(uint speciesId)
        {
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i].SpeciesId == speciesId) return i;
            return -1;
        }
    }

    /// <summary>Incremental regional genome centroid with per-locus Welford variance.</summary>
    public sealed class OnlineGenomeCentroid
    {
        private readonly int[] _counts = new int[GeneId.Count];
        private readonly float[] _means = new float[GeneId.Count];
        private readonly float[] _m2 = new float[GeneId.Count];
        private readonly float[] _scratchSums = new float[GeneId.Count];
        private readonly int[] _scratchCounts = new int[GeneId.Count];
        private readonly GeneCatalog _catalog = GeneCatalog.Create();
        private int _observations;

        public int Observations => _observations;

        public void Add(IReadOnlyList<Gene> genome)
        {
            if (genome == null) throw new ArgumentNullException(nameof(genome));
            Array.Clear(_scratchSums, 0, _scratchSums.Length);
            Array.Clear(_scratchCounts, 0, _scratchCounts.Length);
            for (int i = 0; i < genome.Count; i++)
            {
                Gene gene = genome[i];
                if (gene.TypeId >= GeneId.Count) continue;
                _scratchSums[gene.TypeId] += gene.Value;
                _scratchCounts[gene.TypeId]++;
            }
            for (int id = 0; id < GeneId.Count; id++)
            {
                if (_scratchCounts[id] == 0) continue;
                float value = _scratchSums[id] / _scratchCounts[id];
                int n = ++_counts[id];
                float delta = value - _means[id];
                _means[id] += delta / n;
                _m2[id] += delta * (value - _means[id]);
            }
            _observations++;
        }

        public float Mean(ushort typeId) => typeId < GeneId.Count ? _means[typeId] : 0f;
        public float Variance(ushort typeId) => typeId < GeneId.Count && _counts[typeId] > 1
            ? _m2[typeId] / (_counts[typeId] - 1) : 0f;

        public List<Gene> ToRepresentativeGenome(GeneKingdom kingdom)
        {
            var result = new List<Gene>();
            foreach (ushort typeId in GenomeFactory.GenesForKingdom(kingdom))
            {
                float value = _counts[typeId] > 0 ? _means[typeId] : _catalog.Get(typeId).Default;
                result.Add(new Gene(typeId, value, 1f, 0.002f));
            }
            return result;
        }

        public static float Distance(OnlineGenomeCentroid a, OnlineGenomeCentroid b, GeneKingdom kingdom)
        {
            if (a == null || b == null) throw new ArgumentNullException();
            float sum = 0f;
            int count = 0;
            foreach (ushort typeId in GenomeFactory.GenesForKingdom(kingdom))
            {
                sum += Math.Abs(a.Mean(typeId) - b.Mean(typeId));
                count++;
            }
            return count == 0 ? 0f : sum / count;
        }
    }

    /// <summary>Online speciation threshold rule, independently testable from the simulation loop.</summary>
    public static class SpeciationDetector
    {
        public static bool ShouldFork(float centroidDrift, float incompatibilityFraction,
            float driftThreshold, float incompatibilityThreshold, int subpopulationSize,
            int minimumSubpopulation = 8)
        {
            return subpopulationSize >= minimumSubpopulation &&
                   centroidDrift >= driftThreshold &&
                   incompatibilityFraction >= incompatibilityThreshold;
        }

        public static float IncompatibilityFraction(IReadOnlyList<Gene> regionalGenomes,
            IReadOnlyList<Gene> parentSpeciesGenomes, float compatibilityThreshold)
        {
            if (regionalGenomes == null || parentSpeciesGenomes == null)
                throw new ArgumentNullException();
            if (regionalGenomes.Count == 0 || parentSpeciesGenomes.Count == 0) return 0f;
            int trials = 0;
            int failures = 0;
            for (int i = 0; i < regionalGenomes.Count; i++)
            {
                for (int j = 0; j < parentSpeciesGenomes.Count; j++)
                {
                    trials++;
                    if (!GenomeEvolutionMath.IsCompatible(regionalGenomes[i], parentSpeciesGenomes[j], compatibilityThreshold))
                        failures++;
                }
            }
            return trials == 0 ? 0f : (float)failures / trials;
        }
    }

    /// <summary>
    /// Cell/species counters are changed by birth, death and movement deltas. Querying
    /// these bins is O(cells + occupied cell/species pairs), independent of organism count.
    /// </summary>
    public sealed class CellSpeciesPopulationLedger
    {
        private readonly Dictionary<long, CellSpeciesCounter> _entries = new Dictionary<long, CellSpeciesCounter>();
        private int _cellCount;

        public CellSpeciesPopulationLedger(int cellCount)
        {
            _cellCount = Math.Max(0, cellCount);
        }

        public void Resize(int cellCount) => _cellCount = Math.Max(0, cellCount);

        public void ApplyDelta(int cellIndex, uint speciesId, int populationDelta,
            int birthsDelta = 0, int deathsDelta = 0, float biomassDelta = 0f)
        {
            if (cellIndex < 0 || cellIndex >= _cellCount || speciesId == 0) return;
            long key = ((long)speciesId << 32) | (uint)cellIndex;
            _entries.TryGetValue(key, out CellSpeciesCounter value);
            value.CellIndex = cellIndex;
            value.SpeciesId = speciesId;
            value.Population = Math.Max(0, value.Population + populationDelta);
            value.Births += birthsDelta;
            value.Deaths += deathsDelta;
            value.Biomass = Math.Max(0f, value.Biomass + biomassDelta);
            _entries[key] = value;
        }

        public IReadOnlyCollection<CellSpeciesCounter> Entries => _entries.Values;

        public int TotalPopulation
        {
            get
            {
                int total = 0;
                foreach (CellSpeciesCounter entry in _entries.Values) total += entry.Population;
                return total;
            }
        }

        public List<int> AggregateByCell()
        {
            var result = new List<int>(_cellCount);
            for (int cell = 0; cell < _cellCount; cell++) result.Add(0);
            foreach (CellSpeciesCounter entry in _entries.Values)
                if (entry.CellIndex >= 0 && entry.CellIndex < result.Count)
                    result[entry.CellIndex] += entry.Population;
            return result;
        }
    }

    [Serializable]
    public struct CellSpeciesCounter
    {
        public int CellIndex;
        public uint SpeciesId;
        public int Population;
        public int Births;
        public int Deaths;
        public float Biomass;
    }

    /// <summary>Energy-flow account with separate, conservation-checked trophic ledgers.</summary>
    [Serializable]
    public struct EnergyFlowAudit
    {
        public float ProducerNpp;
        public float ProducerRespiration;
        public float HerbivoreIntake;
        public float ProducerStockDelta;
        public float ProducerDetritus;
        public float HerbivoreRespiration;
        public float PredatorIntake;
        public float HerbivoreStockDelta;
        public float HerbivoreDetritus;
        public float PredatorRespiration;
        public float PredatorStockDelta;
        public float PredatorDetritus;
        public float FertilityConversion;
        public float DetritusStockDelta;
        public float EcosystemExport;

        public float ProducerResidual => ProducerNpp - ProducerRespiration - HerbivoreIntake - ProducerStockDelta - ProducerDetritus;
        public float HerbivoreResidual => HerbivoreIntake - HerbivoreRespiration - PredatorIntake - HerbivoreStockDelta - HerbivoreDetritus;
        public float PredatorResidual => PredatorIntake - PredatorRespiration - PredatorStockDelta - PredatorDetritus;
        public float DetritusResidual => ProducerDetritus + HerbivoreDetritus + PredatorDetritus - FertilityConversion - DetritusStockDelta;
        public float EcosystemResidual => ProducerNpp - ProducerRespiration - HerbivoreRespiration - PredatorRespiration -
                                          ProducerStockDelta - HerbivoreStockDelta - PredatorStockDelta -
                                          FertilityConversion - DetritusStockDelta - EcosystemExport;

        public bool IsBalanced(float absoluteTolerance, float relativeTolerance = 0.05f)
        {
            float scale = Math.Max(1f, Math.Abs(ProducerNpp));
            float tolerance = Math.Max(Math.Max(0f, absoluteTolerance), scale * Math.Max(0f, relativeTolerance));
            return Math.Abs(ProducerResidual) <= tolerance && Math.Abs(HerbivoreResidual) <= tolerance &&
                   Math.Abs(PredatorResidual) <= tolerance && Math.Abs(DetritusResidual) <= tolerance &&
                   Math.Abs(EcosystemResidual) <= tolerance;
        }
    }
}
