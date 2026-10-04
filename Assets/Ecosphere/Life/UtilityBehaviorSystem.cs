// Ecosphere — UtilityBehaviorSystem with genome MLP and explained decisions (stage 05).
// Evaluates actions every N ticks or on interrupt, computes steering targets & effectors.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Life
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AnimalSensingSystem))]
    public partial struct UtilityBehaviorSystem : ISystem
    {
        private EntityQuery _behaviorQuery;
        private ulong _lastProcessedTick;

        public void OnCreate(ref SystemState state)
        {
            _behaviorQuery = state.EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<BehaviorData>(),
                ComponentType.ReadWrite<NeedsData>(),
                ComponentType.ReadOnly<NeedWeightsData>(),
                ComponentType.ReadOnly<SensoryData>(),
                ComponentType.ReadOnly<PhenotypeData>(),
                ComponentType.ReadOnly<ArchetypeData>(),
                ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<CreatureMemory>(),
                ComponentType.Exclude<PlantLifeData>(),
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

            PlanetState planetState = em.GetComponentData<PlanetState>(planetEntity);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
            DynamicBuffer<CellResources> resources = em.GetBuffer<CellResources>(planetEntity);
            DynamicBuffer<WeatherEvent> weatherEvents = em.HasBuffer<WeatherEvent>(planetEntity) ? em.GetBuffer<WeatherEvent>(planetEntity) : default;
            var sampler = new ClimateSampler(planetState, cells, weatherEvents);
            ref PlanetTopologyBlob topo = ref planetState.Topology.Value;

            float dayFrac = clock.DayFraction;
            bool isNight = (dayFrac < 0.2f || dayFrac > 0.8f);

            var entities = _behaviorQuery.ToEntityArray(Allocator.Temp);
            var behaviors = _behaviorQuery.ToComponentDataArray<BehaviorData>(Allocator.Temp);
            var needsDatas = _behaviorQuery.ToComponentDataArray<NeedsData>(Allocator.Temp);
            var weightsDatas = _behaviorQuery.ToComponentDataArray<NeedWeightsData>(Allocator.Temp);
            var sensoryDatas = _behaviorQuery.ToComponentDataArray<SensoryData>(Allocator.Temp);
            var phenotypes = _behaviorQuery.ToComponentDataArray<PhenotypeData>(Allocator.Temp);
            var archetypes = _behaviorQuery.ToComponentDataArray<ArchetypeData>(Allocator.Temp);
            var organismCells = _behaviorQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            var memories = _behaviorQuery.ToComponentDataArray<CreatureMemory>(Allocator.Temp);

            byte currentTickMod = (byte)(clock.TotalTicks % 8UL);

            for (int i = 0; i < entities.Length; i++)
            {
                BehaviorData beh = behaviors[i];
                NeedsData needs = needsDatas[i];
                NeedWeightsData weights = weightsDatas[i];
                SensoryData senses = sensoryDatas[i];
                Phenotype p = phenotypes[i].Value;
                OrganismArchetype arch = archetypes[i].Value;
                int cellIdx = organismCells[i].CellIndex;
                CreatureMemory mem = memories[i];

                if (cellIdx < 0 || cellIdx >= cells.Length) continue;

                // Re-evaluate every 8 ticks or on interrupt (hazard, threat, starving)
                bool interrupt = (senses.NearestPredatorDist < 5.0f || senses.WeatherHazard > 0.6f ||
                                  needs.Energy < 0.15f || needs.Hydration < 0.15f);

                if (!interrupt && beh.DecisionCooldown > 0)
                {
                    beh.DecisionCooldown--;
                    // Still apply continuous action effectors (eating, drinking, resting tick deltas)
                    ApplyActionEffectors(ref beh, ref needs, ref resources, cellIdx, p, arch);
                    behaviors[i] = beh;
                    needsDatas[i] = needs;
                    continue;
                }

                beh.DecisionCooldown = 8; // Reset cooldown

                ClimateSample climate = sampler.Sample(cellIdx);

                // Prepare MLP input
                var mlpInput = new BehaviorMlpInput
                {
                    EnergyUrgency = (1.0f - needs.Energy),
                    HydrationUrgency = (1.0f - needs.Hydration),
                    ThermalComfortUrgency = (1.0f - needs.ThermalComfort),
                    RestUrgency = (1.0f - needs.Rest),
                    SafetyUrgency = (1.0f - needs.Safety),
                    SocialUrgency = (1.0f - needs.Social),
                    ReproductionUrgency = (1.0f - needs.Reproduction),
                    ExplorationUrgency = (1.0f - needs.Exploration),

                    FoodNearby = math.clamp(senses.FoodQuantity * 5.0f, 0f, 1f),
                    WaterNearby = senses.WaterDistance < 2.0f ? 1.0f : 0.0f,
                    PredatorNearby = senses.NearestPredatorDist < 10.0f ? 1.0f : 0.0f,
                    PreyNearby = senses.NearestPreyDist < 10.0f ? 1.0f : 0.0f,
                    ComfortNearby = math.clamp(senses.ComfortGradient * 5.0f, 0f, 1f),
                    WeatherHazard = senses.WeatherHazard,

                    EffectiveTempNormalized = math.clamp((climate.EffectiveTemperature - 20f) / 30f, -1f, 1f),
                    WindSpeedNormalized = math.clamp(climate.WindStrength / 20f, 0f, 1f),
                    DayFraction = dayFrac,
                    IsNight = isNight ? 1.0f : 0.0f,

                    InstinctAggression = p.InstinctAggression,
                    InstinctCuriosity = p.InstinctCuriosity,
                    InstinctFear = p.InstinctFear,
                    InstinctSociability = p.InstinctSociability
                };

                // Burst MLP evaluation
                BehaviorMlp.Evaluate(in mlpInput, in p, out ActionBiases mlpBiases);

                // Score candidate actions
                CreatureAction bestAction = CreatureAction.Wander;
                float bestScore = -1f;
                byte bestDomNeed = 0;

                for (int a = 0; a <= (int)CreatureAction.TakeShelter; a++)
                {
                    CreatureAction action = (CreatureAction)a;
                    float score = UtilityScoring.EvaluateAction(
                        action,
                        in needs,
                        in weights,
                        in senses,
                        in mlpBiases,
                        climate.EffectiveTemperature,
                        dayFrac,
                        isNight,
                        p.DietHerbivory,
                        p.DietCarnivory,
                        p.DietScavenging,
                        p.Endothermy,
                        out byte domNeed);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAction = action;
                        bestDomNeed = domNeed;
                    }
                }

                beh.CurrentAction = bestAction;
                beh.ActionScore = bestScore;
                beh.DominantNeedIndex = bestDomNeed;

                // Format debug explanation string for stage-07 inspector
                beh.Explanation = FormatExplanation(bestAction, bestScore, bestDomNeed, in needs);

                // Set steering target cell & target direction
                switch (bestAction)
                {
                    case CreatureAction.Flee:
                        // Head away from predator or threat
                        int threatCell = (senses.NearestPredatorCell >= 0) ? senses.NearestPredatorCell : mem.ThreatCell;
                        if (threatCell >= 0 && threatCell < cells.Length)
                        {
                            float3 fromThreat = topo.Centers[cellIdx] - topo.Centers[threatCell];
                            beh.TargetDirection = math.normalizesafe(fromThreat);
                            beh.TargetCell = cellIdx; // will steer greedy away
                        }
                        else
                        {
                            beh.TargetDirection = -climate.Wind;
                            beh.TargetCell = cellIdx;
                        }
                        break;

                    case CreatureAction.TakeShelter:
                    case CreatureAction.Migrate:
                    case CreatureAction.Bask:
                        beh.TargetCell = senses.BestComfortCell;
                        beh.TargetDirection = topo.Centers[beh.TargetCell] - topo.Centers[cellIdx];
                        break;

                    case CreatureAction.Graze:
                    case CreatureAction.Forage:
                    case CreatureAction.Scavenge:
                        beh.TargetCell = senses.NearestFoodCell;
                        beh.TargetDirection = topo.Centers[beh.TargetCell] - topo.Centers[cellIdx];
                        break;

                    case CreatureAction.Hunt:
                        beh.TargetCell = (senses.NearestPreyCell >= 0) ? senses.NearestPreyCell : cellIdx;
                        beh.TargetDirection = topo.Centers[beh.TargetCell] - topo.Centers[cellIdx];
                        break;

                    case CreatureAction.Drink:
                        beh.TargetCell = senses.NearestWaterCell;
                        beh.TargetDirection = topo.Centers[beh.TargetCell] - topo.Centers[cellIdx];
                        break;

                    case CreatureAction.Sleep:
                    case CreatureAction.Rest:
                        // Stay in place
                        beh.TargetCell = cellIdx;
                        beh.TargetDirection = float3.zero;
                        break;

                    case CreatureAction.Wander:
                    case CreatureAction.Explore:
                    default:
                        // Pick random neighboring cell
                        int startN = topo.NeighborOffsets[cellIdx];
                        int endN = topo.NeighborOffsets[cellIdx + 1];
                        int numN = endN - startN;
                        if (numN > 0)
                        {
                            int pick = startN + (int)(clock.TotalTicks % (ulong)numN);
                            beh.TargetCell = topo.Neighbors[pick];
                            beh.TargetDirection = topo.Centers[beh.TargetCell] - topo.Centers[cellIdx];
                        }
                        break;
                }

                // Apply action effectors
                ApplyActionEffectors(ref beh, ref needs, ref resources, cellIdx, p, arch);

                behaviors[i] = beh;
                needsDatas[i] = needs;
            }

            _behaviorQuery.CopyFromComponentDataArray(behaviors);
            _behaviorQuery.CopyFromComponentDataArray(needsDatas);

            entities.Dispose();
            behaviors.Dispose();
            needsDatas.Dispose();
            weightsDatas.Dispose();
            sensoryDatas.Dispose();
            phenotypes.Dispose();
            archetypes.Dispose();
            organismCells.Dispose();
            memories.Dispose();
        }

        private static void ApplyActionEffectors(
            ref BehaviorData beh,
            ref NeedsData needs,
            ref DynamicBuffer<CellResources> resources,
            int cellIdx,
            in Phenotype p,
            OrganismArchetype arch)
        {
            CellResources cellRes = resources[cellIdx];

            switch (beh.CurrentAction)
            {
                case CreatureAction.Graze:
                    ResourceEcologyMath.Graze(ref cellRes, ref needs, p.DietHerbivory, p.DigestiveEfficiency, p.MouthSize);
                    break;
                case CreatureAction.Forage:
                    ResourceEcologyMath.Forage(ref cellRes, ref needs, p.DietHerbivory, p.DigestiveEfficiency);
                    break;
                case CreatureAction.Scavenge:
                    ResourceEcologyMath.Scavenge(ref cellRes, ref needs, p.DietScavenging, p.DigestiveEfficiency);
                    break;
                case CreatureAction.Drink:
                    ResourceEcologyMath.Drink(ref cellRes, ref needs);
                    break;
                case CreatureAction.Rest:
                    needs.Rest = math.saturate(needs.Rest + 0.005f);
                    break;
                case CreatureAction.Sleep:
                    needs.Rest = math.saturate(needs.Rest + 0.015f);
                    break;
                case CreatureAction.Bask:
                    needs.ThermalComfort = math.saturate(needs.ThermalComfort + 0.01f);
                    break;
                case CreatureAction.TakeShelter:
                    needs.ThermalComfort = math.saturate(needs.ThermalComfort + 0.008f);
                    needs.Safety = math.saturate(needs.Safety + 0.005f);
                    break;
            }

            resources[cellIdx] = cellRes;
        }

        private static FixedString64Bytes FormatExplanation(CreatureAction action, float score, byte dominantNeed, in NeedsData needs)
        {
            FixedString64Bytes str = default;
            // e.g. "Flee: 0.82 — Safety 0.90"
            switch (action)
            {
                case CreatureAction.Flee: str.Append("Flee"); break;
                case CreatureAction.TakeShelter: str.Append("TakeShelter"); break;
                case CreatureAction.Graze: str.Append("Graze"); break;
                case CreatureAction.Forage: str.Append("Forage"); break;
                case CreatureAction.Hunt: str.Append("Hunt"); break;
                case CreatureAction.Scavenge: str.Append("Scavenge"); break;
                case CreatureAction.Drink: str.Append("Drink"); break;
                case CreatureAction.Sleep: str.Append("Sleep"); break;
                case CreatureAction.Rest: str.Append("Rest"); break;
                case CreatureAction.Bask: str.Append("Bask"); break;
                case CreatureAction.Migrate: str.Append("Migrate"); break;
                case CreatureAction.Socialize: str.Append("Socialize"); break;
                case CreatureAction.SeekMate: str.Append("SeekMate"); break;
                case CreatureAction.Explore: str.Append("Explore"); break;
                default: str.Append("Wander"); break;
            }

            str.Append(": ");
            str.Append((int)(score * 100f));
            str.Append("% - Need: ");

            switch (dominantNeed)
            {
                case 0: str.Append("Energy"); break;
                case 1: str.Append("Hydration"); break;
                case 2: str.Append("Thermal"); break;
                case 3: str.Append("Rest"); break;
                case 4: str.Append("Safety"); break;
                case 5: str.Append("Social"); break;
                case 6: str.Append("Repro"); break;
                default: str.Append("Curiosity"); break;
            }

            return str;
        }
    }
}
