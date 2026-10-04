// Ecosphere — PhenotypeUpdateSystem: the morphogenesis engine.
// Reads GenomeHeader + DynamicBuffer<Gene> → writes PhenotypeData + LifeStageData +
// OrganismSize + EnvironmentEMAData, and marks MeshDirty on stage/bucket transitions.
//
// Determinism contract: phenotype = f(genome, developmental age, environment history).
// The system is the ONLY writer of organism developmental state. It reads the climate
// through IClimateSampler (stage 03) and never uses unseeded randomness — all
// symmetry-breaking jitter happens downstream in mesh building, from
// GenomeHeader.GenomeSeed via SimRandom.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Genetics
{
    /// <summary>
    /// Runs in SimulationSystemGroup after ClimateSystem so the environment EMA
    /// always sees this tick's climate. No per-tick managed allocations: gene lists
    /// and entity arrays are pooled per-system.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ClimateSystem))]
    public partial struct PhenotypeUpdateSystem : ISystem
    {
        private EntityQuery _organismQuery;
        private NativeArray<Entity> _entities;
        private Gene[] _geneScratch;
        private GeneCatalog _catalog;
        private bool _catalogReady;
        private ulong lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _organismQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GenomeHeader>(),
                ComponentType.ReadOnly<GeneElement>(),
                ComponentType.ReadOnly<OrganismAge>(),
                ComponentType.ReadOnly<OrganismDevAge>(),
                ComponentType.ReadOnly<OrganismSize>(),
                ComponentType.ReadOnly<EnvironmentEMAData>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<MeshSizeBucket>());
            _geneScratch = new Gene[256];
        }

        public void OnDestroy(ref SystemState state)
        {
            _organismQuery.Dispose();
            if (_entities.IsCreated) _entities.Dispose();
        }

        private GeneCatalog GetCatalog()
        {
            if (_catalogReady) return _catalog;
            if (SystemAPI.TryGetSingleton<GeneCatalogData>(out GeneCatalogData data))
            {
                _catalog = GenomeHeaderBridge.BuildCatalog(data);
            }
            else
            {
                _catalog = GeneCatalog.Create();
            }
            _catalogReady = true;
            return _catalog;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (clock.TotalTicks == lastProcessedTick) return; // tick-gated: sim ticks only
            if (_organismQuery.CalculateEntityCountWithoutFiltering() == 0)
            {
                lastProcessedTick = clock.TotalTicks;
                return;
            }
            ulong ticksAdvanced = clock.TotalTicks > lastProcessedTick
                ? clock.TotalTicks - lastProcessedTick
                : 1UL; // first run (or clock rewound in a test)
            lastProcessedTick = clock.TotalTicks;

            EntityManager em = state.EntityManager;
            GeneCatalog catalog = GetCatalog();

            // Climate sampler (frame-local; rebuilt every tick by design).
            ClimateSampler climate = default;
            bool hasClimate = SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)
                && em.HasBuffer<PlanetCell>(planetEntity);
            if (hasClimate)
            {
                climate = new ClimateSampler(
                    em.GetComponentData<PlanetState>(planetEntity),
                    em.GetBuffer<PlanetCell>(planetEntity),
                    em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default);
            }

            int count = _organismQuery.CalculateEntityCountWithoutFiltering();
            if (count > _entities.Length) _entities = _entities.ResizeAlloc(count, Allocator.TempJob);
            _organismQuery.GetEntitiesNonAlloc(_entities);

            for (int i = 0; i < count; i++)
            {
                Entity e = _entities[i];

                // ── Age advancement (1 wall tick; dev age scaled by DevelopmentRate) ──
                OrganismAge age = em.GetComponentData<OrganismAge>(e);
                OrganismDevAge dev = em.GetComponentData<OrganismDevAge>(e);

                // Read regulatory genes from the buffer (fixed layout: first 5).
                DynamicBuffer<GeneElement> genes = em.GetBuffer<GeneElement>(e);
                int geneCount = genes.Length;
                if (geneCount > _geneScratch.Length) _geneScratch = new Gene[System.Math.Max(geneCount, 256)];
                for (int g = 0; g < geneCount; g++) _geneScratch[g] = genes[g].Value;

                float devRate = 1f;
                float seasonalGate = 0f;
                GeneKingdom kingdom = em.GetComponentData<GenomeHeader>(e).Kingdom;
                for (int g = 0; g < geneCount; g++)
                {
                    Gene gene = _geneScratch[g];
                    ushort t = gene.TypeId;
                    if (t != GeneId.DevelopmentRate && t != GeneId.SeasonalGrowthGate) continue;
                    float expressed = GenomeEvolutionMath.ExpressAllele(
                        gene.Value, gene.Dominance, catalog.Get(t).Default);
                    float mapped = catalog.MapToRange(t, expressed);
                    if (t == GeneId.DevelopmentRate) devRate = mapped;
                    else seasonalGate = mapped;
                }

                float tickAdvance = 1f * math.clamp(devRate, 0.05f, 3f);
                // Seasonal gating: slow development outside the growth window.
                // Spring/summer = full speed; winter = damped (gate scales it).
                Season season = clock.Season;
                bool growthSeason = season == Season.Spring || season == Season.Summer;
                if (!growthSeason && kingdom == GeneKingdom.Plant)
                    tickAdvance *= 1f - seasonalGate * 0.65f;
                else if (!growthSeason)
                    tickAdvance *= 1f - seasonalGate * 0.25f; // animals: mild torpor

                age.Ticks += ticksAdvanced;
                dev.Ticks += tickAdvance * (float)ticksAdvanced;
                em.SetComponentData(e, age);

                // ── Environment EMA update ────────────────────────────
                var ema = em.GetComponentData<EnvironmentEMAData>(e).Value;
                if (em.TryGetComponent(e, out ManualEnvironment manual) && manual.Enabled != 0)
                {
                    ema.Update(manual.Temperature, manual.Light, manual.Wind, manual.Moisture);
                }
                else if (hasClimate && em.TryGetComponent(e, out OrganismCell cell))
                {
                    ClimateSample s = climate.Sample(cell.CellIndex);
                    float moisture = s.IsSubmerged ? s.Humidity : s.SoilMoisture;
                    ema.Update(s.Temperature, s.Insolation, s.WindStrength, moisture);
                }
                // Free-floating organisms (no cell) keep their EMA (viewer default env).
                em.SetComponentData(e, new EnvironmentEMAData { Value = ema });
                em.SetComponentData(e, dev);

                // ── Development ───────────────────────────────────────
                var input = new DevelopmentInput(_geneScratch, kingdom, dev.Ticks, ema);
                DevelopmentOutput outp = GenomeMath.Compute(catalog, input);

                LifeStageData stageData = em.GetComponentData<LifeStageData>(e);
                int bucket = (int)(outp.Size * 10f);
                if (bucket > 10) bucket = 10;
                MeshSizeBucket oldBucket = em.GetComponentData<MeshSizeBucket>(e);

                bool stageChanged = stageData.Stage != outp.Stage;
                bool bucketChanged = oldBucket.Value != bucket;

                em.SetComponentData(e, new PhenotypeData { Value = outp.Phenotype });
                em.SetComponentData(e, new LifeStageData { Stage = outp.Stage, StageProgress = outp.StageProgress });
                em.SetComponentData(e, new OrganismSize { Value = outp.Size });

                // Mesh rebuild triggers: stage transition OR size bucket change (10%).
                // Never per tick. Presentation clears MeshDirty after regenerating.
                if (stageChanged || bucketChanged)
                {
                    em.SetComponentData(e, new MeshSizeBucket { Value = bucket });
                    em.AddComponent(e, typeof(MeshDirty));
                }
                // Sub-bucket size drift: presenters scale the visual object without
                // regenerating the mesh (mesh geometry is bucket-quantized by design).
            }
        }
    }
}