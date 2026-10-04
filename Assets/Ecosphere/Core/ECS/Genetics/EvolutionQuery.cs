using System;
using System.Collections.Generic;
using Unity.Entities;

namespace Ecosphere.Core.ECS
{
    /// <summary>
    /// Read-only, snapshot-oriented access to evolution state on a planet entity.
    /// The API copies buffer rows into managed lists so callers never retain ECS buffer
    /// handles across structural changes. Construct it on the main thread from the
    /// planet entity owned by the active world.
    /// </summary>
    public sealed class EvolutionQuery
    {
        private readonly EntityManager _entityManager;
        private readonly Entity _planet;

        public EvolutionQuery(EntityManager entityManager, Entity planet)
        {
            if (!entityManager.Exists(planet) || !entityManager.HasComponent<EvolutionStateData>(planet))
                throw new ArgumentException("The entity must be a live evolution-enabled planet.", nameof(planet));
            _entityManager = entityManager;
            _planet = planet;
        }

        public Entity PlanetEntity => _planet;

        public EvolutionStateData State => _entityManager.GetComponentData<EvolutionStateData>(_planet);

        public int TotalPopulation
        {
            get
            {
                DynamicBuffer<SpeciesPopulationRecord> records = GetBuffer<SpeciesPopulationRecord>();
                int population = 0;
                for (int i = 0; i < records.Length; i++) population += records[i].Population;
                return population;
            }
        }

        public int LivingSpeciesCount
        {
            get
            {
                DynamicBuffer<SpeciesPopulationRecord> records = GetBuffer<SpeciesPopulationRecord>();
                int count = 0;
                for (int i = 0; i < records.Length; i++)
                    if (records[i].IsExtinct == 0 && records[i].Population > 0) count++;
                return count;
            }
        }

        public List<SpeciesPopulationRecord> GetSpecies(bool includeExtinct = true)
        {
            DynamicBuffer<SpeciesPopulationRecord> source = GetBuffer<SpeciesPopulationRecord>();
            var result = new List<SpeciesPopulationRecord>(source.Length);
            for (int i = 0; i < source.Length; i++)
                if (includeExtinct || source[i].IsExtinct == 0) result.Add(source[i]);
            return result;
        }

        public bool TryGetSpecies(uint speciesId, out SpeciesPopulationRecord species)
        {
            DynamicBuffer<SpeciesPopulationRecord> source = GetBuffer<SpeciesPopulationRecord>();
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].SpeciesId != speciesId) continue;
                species = source[i];
                return true;
            }
            species = default;
            return false;
        }

        public List<CellSpeciesPopulation> GetCellPopulation(int cellIndex)
        {
            DynamicBuffer<CellSpeciesPopulation> source = GetBuffer<CellSpeciesPopulation>();
            var result = new List<CellSpeciesPopulation>();
            for (int i = 0; i < source.Length; i++)
                if (source[i].CellIndex == cellIndex && source[i].Population > 0) result.Add(source[i]);
            return result;
        }

        public List<EvolutionEventRecord> GetEvents(uint speciesId = 0)
        {
            DynamicBuffer<EvolutionEventElement> source = GetBuffer<EvolutionEventElement>();
            var result = new List<EvolutionEventRecord>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                EvolutionEventRecord value = source[i].Value;
                if (speciesId == 0 || value.SpeciesId == speciesId || value.ParentSpeciesId == speciesId)
                    result.Add(value);
            }
            return result;
        }

        public List<EvolutionMetricsRecord> GetMetrics(uint speciesId = 0, int year = int.MinValue)
        {
            DynamicBuffer<EvolutionMetricElement> source = GetBuffer<EvolutionMetricElement>();
            var result = new List<EvolutionMetricsRecord>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                EvolutionMetricsRecord value = source[i].Value;
                if ((speciesId == 0 || value.SpeciesId == speciesId) &&
                    (year == int.MinValue || value.Year == year)) result.Add(value);
            }
            return result;
        }

        public bool TryGetLatestMetric(uint speciesId, out EvolutionMetricsRecord metric)
        {
            DynamicBuffer<EvolutionMetricElement> source = GetBuffer<EvolutionMetricElement>();
            for (int i = source.Length - 1; i >= 0; i--)
            {
                if (source[i].Value.SpeciesId != speciesId) continue;
                metric = source[i].Value;
                return true;
            }
            metric = default;
            return false;
        }

        public List<EvolutionPhylogenyEdge> GetPhylogenyEdges()
        {
            DynamicBuffer<EvolutionPhylogenyEdge> source = GetBuffer<EvolutionPhylogenyEdge>();
            var result = new List<EvolutionPhylogenyEdge>(source.Length);
            for (int i = 0; i < source.Length; i++) result.Add(source[i]);
            return result;
        }

        public List<LineageRecord> GetLineage(ulong organismId = 0)
        {
            DynamicBuffer<LineageRecord> source = GetBuffer<LineageRecord>();
            var result = new List<LineageRecord>();
            for (int i = 0; i < source.Length; i++)
                if (organismId == 0 || source[i].OrganismId == organismId) result.Add(source[i]);
            return result;
        }

        public bool IsPhylogenyAcyclic()
        {
            List<SpeciesPopulationRecord> nodes = GetSpecies();
            List<EvolutionPhylogenyEdge> edges = GetPhylogenyEdges();
            var parents = new Dictionary<uint, uint>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) parents[nodes[i].SpeciesId] = nodes[i].ParentSpeciesId;
            for (int i = 0; i < edges.Count; i++)
            {
                EvolutionPhylogenyEdge edge = edges[i];
                if (edge.ParentSpeciesId == edge.ChildSpeciesId ||
                    !parents.TryGetValue(edge.ParentSpeciesId, out _) ||
                    !parents.TryGetValue(edge.ChildSpeciesId, out uint parent) || parent != edge.ParentSpeciesId)
                    return false;
                uint cursor = edge.ParentSpeciesId;
                int remaining = nodes.Count + 1;
                while (cursor != 0 && remaining-- > 0)
                {
                    if (cursor == edge.ChildSpeciesId) return false;
                    if (!parents.TryGetValue(cursor, out cursor)) return false;
                }
                if (remaining <= 0) return false;
            }
            return true;
        }

        private DynamicBuffer<T> GetBuffer<T>() where T : unmanaged, IBufferElementData
        {
            if (!_entityManager.Exists(_planet) || !_entityManager.HasBuffer<T>(_planet))
                throw new InvalidOperationException($"The evolution buffer {typeof(T).Name} is not available.");
            return _entityManager.GetBuffer<T>(_planet, true);
        }
    }
}
