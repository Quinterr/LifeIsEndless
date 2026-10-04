// Ecosphere — Plant simulation system (stage 05).
// Passive lifecycle: photosynthesis, respiration, biomass accumulation, seasonal dormancy, damage, detritus deposit.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(Ecosphere.Genetics.PhenotypeUpdateSystem))]
    public partial struct PlantLifeSystem : ISystem
    {
        private EntityQuery _plantQuery;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _plantQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<PlantLifeData>(),
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<LifeStageData>(),
                ComponentType.ReadOnly<OrganismSize>(),
                ComponentType.Exclude<DeadTag>());
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock)) return;
            if (clock.TotalTicks == _lastProcessedTick) return;
            _lastProcessedTick = clock.TotalTicks;

            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity)) return;

            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<PlanetCell>(planetEntity) || !em.HasBuffer<CellResources>(planetEntity)) return;

            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<CellResources> resources = em.GetBuffer<CellResources>(planetEntity);
            DynamicBuffer<WeatherEvent> weatherEvents = em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default;
            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            var sampler = new ClimateSampler(planetState, cells, weatherEvents);

            Season season = clock.Season;
            float dt = 1.0f; // 1 sim tick

            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

            var entities = _plantQuery.ToEntityArray(Allocator.Temp);
            var plantDatas = _plantQuery.ToComponentDataArray<PlantLifeData>(Allocator.Temp);
            var needsDatas = _plantQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);
            var phenotypes = _plantQuery.ToComponentDataArray<PhenotypeData>(Allocator.Temp);
            var organismCells = _plantQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var lifeStages = _plantQuery.ToComponentDataArray<LifeStageData>(Allocator.Temp);
            var sizes = _plantQuery.ToComponentDataArray<OrganismSize>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                PlantLifeData plant = plantDatas[i];
                NeedsData needs = needsDatas[i];
                Phenotype p = phenotypes[i].Value;
                int cellIdx = organismCells[i].CellIndex;
                LifeStage stage = lifeStages[i].Stage;
                float size = sizes[i].Value;

                if (cellIdx < 0 || cellIdx >= cells.Length) continue;

                ClimateSample climate = sampler.Sample(cellIdx);
                CellResources cellRes = resources[cellIdx];

                // 1. Check dormancy
                bool dormant = PlantLifeMath.ShouldBeDormant(season, climate.EffectiveTemperature, p.SeasonalGrowthGate);
                plant.IsDormant = (byte)(dormant ? 1 : 0);
                if (dormant)
                {
                    ecb.SetComponentEnabled<PlantDormant>(e, true);
                }
                else
                {
                    ecb.SetComponentEnabled<PlantDormant>(e, false);
                }

                // 2. Photosynthesis & Respiration
                float lightInsolation = climate.Insolation;
                float cloudCover = climate.CloudCover;
                float leafMass = math.max(0.1f, plant.LeafBiomass + p.LeafCount * p.LeafSize * size);
                float waterAvail = climate.IsSubmerged ? 1.0f : climate.SoilMoisture;

                float respCost;
                float grossPhotosynthesis = 0f;
                if (!dormant)
                {
                    grossPhotosynthesis = PlantLifeMath.ComputePhotosynthesis(
                        lightInsolation,
                        cloudCover,
                        leafMass,
                        waterAvail,
                        climate.EffectiveTemperature,
                        p.TemperatureOptimum,
                        p.TemperatureTolerance,
                        p.MetabolicRate,
                        out respCost);
                }
                else
                {
                    // Dormant respiration is near zero
                    respCost = p.MetabolicRate * 0.0005f;
                }

                plant.PhotosynthesisRate = grossPhotosynthesis;
                plant.RespirationCost = respCost;

                float netBiomassGain = grossPhotosynthesis - respCost;

                // 3. Needs update (Plant passives)
                needs.Light = math.saturate(lightInsolation * (1.0f - cloudCover * 0.5f));
                needs.Water = math.saturate(waterAvail);
                needs.ThermalComfort = NeedsMath.ComputeThermalComfort(climate.EffectiveTemperature, p.TemperatureOptimum, p.TemperatureTolerance);
                needs.Nutrients = math.saturate(cellRes.SoilFertility);

                // 4. Biomass allocation
                if (netBiomassGain > 0f)
                {
                    PlantLifeMath.ComputeBiomassAllocation(season, p, out float rF, out float sF, out float lF, out float fF);
                    plant.RootBiomass += netBiomassGain * rF;
                    plant.StemBiomass += netBiomassGain * sF;
                    plant.LeafBiomass += netBiomassGain * lF;
                    plant.FruitBiomass += netBiomassGain * fF;
                    plant.AccumulatedBiomass += netBiomassGain;

                    // Add to cell vegetation biomass pool!
                    cellRes.VegetationBiomass = math.min(100f, cellRes.VegetationBiomass + netBiomassGain * 0.5f);
                }

                // 5. Weather damage & Death checks
                float damage = PlantLifeMath.ComputeWeatherDamage(
                    climate.EffectiveTemperature,
                    p.TemperatureOptimum,
                    p.TemperatureTolerance,
                    climate.SoilMoisture,
                    climate.Storminess,
                    p.Woodiness);

                if (damage > 0f)
                {
                    plant.AccumulatedBiomass -= damage;
                    if (plant.AccumulatedBiomass < 0f && stage == LifeStage.Senescent)
                    {
                        CauseOfDeath cause = climate.SoilMoisture < 0.12f
                            ? CauseOfDeath.Drought
                            : climate.EffectiveTemperature < p.TemperatureOptimum - p.TemperatureTolerance
                                ? CauseOfDeath.ExposureFreezing
                                : climate.EffectiveTemperature > p.TemperatureOptimum + p.TemperatureTolerance
                                    ? CauseOfDeath.ExposureOverheating
                                    : climate.Storminess > 0.85f
                                        ? CauseOfDeath.SevereStorm : CauseOfDeath.Drought;
                        ecb.AddComponent(e, new DeadTag { Cause = cause });
                    }
                }

                resources[cellIdx] = cellRes;
                plantDatas[i] = plant;
                needsDatas[i] = needs;
            }

            _plantQuery.CopyFromComponentDataArray(plantDatas);
            _plantQuery.CopyFromComponentDataArray(needsDatas);

            ecb.Playback(em);
            ecb.Dispose();
            entities.Dispose();
            plantDatas.Dispose();
            needsDatas.Dispose();
            phenotypes.Dispose();
            organismCells.Dispose();
            lifeStages.Dispose();
            sizes.Dispose();
        }
    }
}
