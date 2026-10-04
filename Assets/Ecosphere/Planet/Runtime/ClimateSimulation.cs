using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    public enum WeatherEventType : byte { Storm, Front, Fog, Blizzard, HeatWave, ColdWave, Drought }

    /// <summary>Live event marker attached to a planet cell; Climate owns this buffer.</summary>
    public struct WeatherEvent : IBufferElementData
    {
        public WeatherEventType Type;
        public int Cell;
        public float Radius;
        public float Strength;
        public ulong StartTick;
        public ulong EndTick;
    }

    /// <summary>Compact append-only event feed (bounded to the most recent 512 entries).</summary>
    public struct WeatherEventLog : IBufferElementData
    {
        public ulong Tick;
        public WeatherEventType Type;
        public int Cell;
        public float Strength;
    }

    public struct ClimateConfig : IComponentData
    {
        public int ClimateTicksPerSimTick;
        public float WindScale;
        public float TemperatureResponseLand;
        public float TemperatureResponseOcean;
        public static ClimateConfig Default => new ClimateConfig
        {
            ClimateTicksPerSimTick = 1, WindScale = 1f,
            TemperatureResponseLand = .0012f, TemperatureResponseOcean = .00028f
        };
    }

    /// <summary>Stable value object returned to gameplay and presentation consumers.</summary>
    public struct ClimateSample
    {
        public float Temperature, EffectiveTemperature, Pressure, Humidity, Insolation;
        public float3 Wind;
        public float WindStrength;
        public float CloudCover, Precipitation, SoilMoisture, SnowCover;
        public float OceanTemperature, Storminess, Nutrient;
        public float3 OceanCurrent;
        public bool IsSubmerged;
        public float TemperatureInsolationTerm, TemperatureAdvectionTerm;
        public float EvaporationTerm, OrographicLiftTerm;
        public float TemperatureTrend, HumidityTrend, SoilMoistureTrend;
    }

    /// <summary>Read-only climate contract for later stages; buffers are valid for the current frame only.</summary>
    public interface IClimateSampler
    {
        ClimateSample Sample(int cellIndex);
        ClimateSample SampleAt(float3 direction, float altitude);
        float EffectiveTemperature(int cellIndex);
        float3 WindAt(int cellIndex);
        bool IsSubmerged(int cellIndex);
        int GetWeatherEvents(int cellIndex, NativeList<WeatherEvent> results);
    }

    public struct ClimateSampler : IClimateSampler
    {
        private BlobAssetReference<PlanetTopologyBlob> topology;
        private readonly DynamicBuffer<PlanetCell> cells;
        private readonly DynamicBuffer<WeatherEvent> events;
        public ClimateSampler(PlanetState state, DynamicBuffer<PlanetCell> cells, DynamicBuffer<WeatherEvent> events)
        { topology = state.Topology; this.cells = cells; this.events = events; }
        public ClimateSample Sample(int cellIndex)
        {
            if (cellIndex < 0 || cellIndex >= cells.Length) return default;
            PlanetCell c = cells[cellIndex];
            float wind = math.length(c.Wind);
            float effective = ClimateMath.EffectiveTemperature(c.Temperature, wind, c.Humidity);
            return new ClimateSample
            {
                Temperature = c.Temperature, EffectiveTemperature = effective,
                Pressure = c.Pressure, Humidity = c.Humidity, Insolation = c.Insolation,
                Wind = c.Wind, WindStrength = wind,
                CloudCover = c.CloudCover, Precipitation = c.Precipitation,
                SoilMoisture = c.SoilMoisture, SnowCover = c.SnowCover,
                OceanTemperature = c.OceanTemperature, Storminess = c.Storminess,
                Nutrient = c.Nutrient, OceanCurrent = c.OceanCurrent, IsSubmerged = c.Land == 0,
                TemperatureInsolationTerm = c.TemperatureInsolationTerm,
                TemperatureAdvectionTerm = c.TemperatureAdvectionTerm,
                EvaporationTerm = c.EvaporationTerm, OrographicLiftTerm = c.OrographicLiftTerm,
                TemperatureTrend = c.TemperatureTrend, HumidityTrend = c.HumidityTrend,
                SoilMoistureTrend = c.SoilMoistureTrend
            };
        }
        public ClimateSample SampleAt(float3 direction, float altitude)
        {
            if (!topology.IsCreated || cells.Length == 0) return default;
            var blob = topology;
            int i = Icosphere.Nearest(ref blob.Value, direction);
            ClimateSample s = Sample(i);
            s.Temperature -= math.max(0f, altitude) * .0065f;
            s.EffectiveTemperature -= math.max(0f, altitude) * .0065f;
            return s;
        }
        public float EffectiveTemperature(int cellIndex) => Sample(cellIndex).EffectiveTemperature;
        public float3 WindAt(int cellIndex) => Sample(cellIndex).Wind;
        public bool IsSubmerged(int cellIndex) => cellIndex >= 0 && cellIndex < cells.Length && cells[cellIndex].Land == 0;
        public int GetWeatherEvents(int cellIndex, NativeList<WeatherEvent> results)
        {
            int start = results.Length;
            for (int i = 0; i < events.Length; i++) if (events[i].Cell == cellIndex) results.Add(events[i]);
            return results.Length - start;
        }
    }

    /// <summary>Pure climate helpers shared by jobs, headless tests and debug tooling.</summary>
    public static class ClimateMath
    {
        public static float EffectiveTemperature(float temperatureC, float windMetersPerSecond, float humidity)
        {
            float chill = math.min(18f, math.max(0f, windMetersPerSecond - 1f) * .55f);
            float heat = math.max(0f, temperatureC - 25f) * math.saturate(humidity) * .12f;
            return temperatureC - chill + heat;
        }

        /// <summary>Windward lift proxy: uphill terrain in the direction air is moving.</summary>
        public static float OrographicLift(float elevationHere, float elevationDownwind, float speed)
            => math.saturate((elevationHere - elevationDownwind) * math.max(0f, speed) * 5f);

        /// <summary>Pairwise signed humidity flux. Reversing edge orientation negates it exactly.</summary>
        public static float HumidityEdgeFlux(float humidityA, float humidityB, float signedFlow)
            => signedFlow >= 0f ? signedFlow * humidityA : signedFlow * humidityB;

        public static float SnowStep(float snow, float precipitationMm, float temperatureC, float meltPerStep)
        {
            float added = temperatureC < 0f ? precipitationMm * .001f : 0f;
            float melt = temperatureC > 0f ? math.min(snow, temperatureC * meltPerStep) : 0f;
            return math.saturate(snow + added - melt);
        }

        public static float Pressure(float temperatureC, float elevation, float latitude)
        {
            float circulation = .055f * math.cos(math.abs(latitude) * 3f * math.PI);
            return math.saturate(.5f - (temperatureC - 12f) * .0042f - math.max(0f, elevation) * .075f + circulation);
        }

        public static float3 DeflectByCoriolis(float3 normal, float3 wind, float latitude)
        {
            float speed = math.length(wind);
            if (speed < 1e-5f) return wind;
            float3 tangent = math.normalizesafe(wind - normal * math.dot(wind, normal));
            float3 deflected = tangent + math.cross(normal, tangent) * (.24f * math.sin(latitude));
            return math.normalizesafe(deflected, tangent) * speed;
        }

        public static float3 WindDownGradient(float3 normal, float3 pressureGradient, float latitude, float scale)
            => DeflectByCoriolis(normal, -pressureGradient * scale, latitude);

        public static float3 WindDrivenCurrent(float3 normal, float3 wind, float3 thermohaline)
        {
            float3 current = wind * .13f + thermohaline;
            current -= normal * math.dot(current, normal);
            return math.normalizesafe(current) * math.min(2.5f, math.length(current));
        }

        public static bool IsStormCandidate(float storminess, float humidity) => storminess >= .5f && humidity >= .55f;
        public static bool IsFogCandidate(float humidity, float windSpeed, float cloudCover)
            => humidity >= .91f && windSpeed < 1.2f && cloudCover > .7f;
        public static bool IsDroughtCandidate(float dryTicks, float ticksPerDay, float soilMoisture)
            => dryTicks >= ticksPerDay * 3f && soilMoisture < .13f;
    }

    [BurstCompile]
    public struct ClimateStepJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<PlanetCell> Source;
        [WriteOnly] public NativeArray<PlanetCell> Destination;
        [ReadOnly] public BlobAssetReference<PlanetTopologyBlob> Topology;
        /// <summary>Stage-07 god-tool forcing (zeroed when no tool is active).</summary>
        [ReadOnly] public NativeArray<ClimateForcingCell> Forcing;
        public float YearPhase;
        public float WindScale;
        public float LandResponse;
        public float OceanResponse;
        public uint Seed;
        public float ClimateTickInterval;

        public void Execute(int index)
        {
            PlanetCell c = Source[index];
            ref PlanetTopologyBlob topo = ref Topology.Value;
            int begin = topo.NeighborOffsets[index], end = topo.NeighborOffsets[index + 1];
            float normalizer = math.max(1, end - begin);
            float3 normal = topo.Centers[index];

            float pressureHere = c.Pressure;
            float gradient = 0f;
            float3 pressureGradient = float3.zero;
            float upstreamTemp = c.Temperature;
            float upstreamWeight = 1f;
            float humidityFlux = 0f;
            float meanTemp = 0f;
            int oceanNeighborCount = 0;
            float meanPressure = 0f;
            float lift = 0f;
            float3 thermohaline = float3.zero;
            for (int n = begin; n < end; n++)
            {
                int j = topo.Neighbors[n];
                PlanetCell other = Source[j];
                float3 edge = math.normalizesafe(topo.Centers[j] - normal * math.dot(topo.Centers[j], normal));
                float pressureNeighbor = other.Pressure;
                float dp = pressureNeighbor - pressureHere;
                pressureGradient += edge * dp;
                meanPressure += pressureNeighbor;
                gradient += math.abs(dp);
                float departure = math.dot(c.Wind, edge);
                float w = math.max(0f, -departure) + .05f;
                upstreamTemp += other.Temperature * w;
                upstreamWeight += w;
                float3 sharedWind = (c.Wind + other.Wind) * .5f;
                float flow = math.dot(sharedWind, topo.Centers[j] - normal) * .00035f;
                // Shared edge flux is exactly antisymmetric, so advection conserves total water.
                humidityFlux -= ClimateMath.HumidityEdgeFlux(c.Humidity, other.Humidity, flow);
                float uphill = departure > 0f
                    ? math.max(0f, other.Elevation - c.Elevation) * departure
                    : math.max(0f, c.Elevation - other.Elevation) * -departure;
                lift += uphill;
                if (c.Land == 0 && other.Land == 0)
                {
                    meanTemp += other.OceanTemperature;
                    oceanNeighborCount++;
                    float3 currentEdge = math.normalizesafe(topo.Centers[j] - normal * math.dot(topo.Centers[j], normal));
                    thermohaline += currentEdge * (other.OceanTemperature - c.OceanTemperature) * .0007f;
                }
            }
            meanTemp = oceanNeighborCount > 0 ? meanTemp / oceanNeighborCount : c.OceanTemperature;
            meanPressure /= normalizer;
            pressureGradient /= normalizer;
            upstreamTemp /= upstreamWeight;
            lift = math.saturate(lift / normalizer * 4f);

            float seasonal = math.sin((YearPhase - .125f) * 2f * math.PI);
            float latitudeTarget = 27f - 58f * math.abs(c.Latitude);
            float seasonalNorm = latitudeTarget + 23f * c.Latitude * seasonal;
            // Seasonal and day/night heating come from stage-02 insolation, never a second sun model.
            float solarTerm = (c.Insolation - .25f) * 38f * (1f - c.SnowCover * .62f);
            float target = latitudeTarget + solarTerm - math.max(0f, c.Elevation) * 24f;
            float advectionTerm = (upstreamTemp - c.Temperature) * .018f;
            float response = c.Land != 0 ? LandResponse : OceanResponse;
            float temperature = c.Temperature + (target - c.Temperature) * response + advectionTerm * response
                + c.Precipitation * .00035f - lift * .0001f;

            float pressure = ClimateMath.Pressure(temperature, c.Elevation, c.Latitude);
            pressure = math.lerp(pressure, meanPressure, .28f); // one cheap smooth/diffusion pass
            float3 east = math.normalizesafe(new float3(-normal.z, 0f, normal.x));
            float absLat = math.abs(c.Latitude);
            float zonal = absLat < .5f ? -4.2f : absLat < 1.05f ? 4.1f : -2.7f;
            float3 wind = -pressureGradient * (105f * WindScale) + east * zonal;
            wind = ClimateMath.DeflectByCoriolis(normal, wind, c.Latitude);
            float gust = Hash01((uint)index ^ Seed ^ (uint)(YearPhase * 100000f));
            wind *= math.clamp(1f + (gust - .5f) * .22f, .88f, 1.12f);
            float windSpeed = math.min(32f, math.length(wind));
            wind = math.normalizesafe(wind) * windSpeed;

            float evaporation = c.Land == 0
                ? math.saturate((c.OceanTemperature + 15f) / 55f) * .0008f
                : c.SoilMoisture * math.saturate((temperature + 5f) / 35f) * .00035f;
            float humidity = math.saturate(c.Humidity + evaporation + humidityFlux);
            float orographic = math.saturate(lift * math.length(wind) * .014f);
            float threshold = temperature < -8f ? .58f : temperature > 28f ? .78f : .68f;
            float cloud = math.saturate((humidity - .3f) * 1.7f + math.max(0f, .55f - pressure) * .5f);
            float precip = math.max(0f, humidity - threshold) * (2.2f + orographic * 4f);
            precip = math.min(12f, precip);
            humidity = math.saturate(humidity - precip * .075f);

            float snow = ClimateMath.SnowStep(c.SnowCover, precip, temperature, .00045f);
            float melt = math.max(0f, c.SnowCover - snow);
            float soil = c.SoilMoisture + (c.Land != 0 ? precip * .018f + melt * .08f - evaporation * .7f : 0f);
            soil -= c.Land != 0 ? (.000012f + math.max(0f, c.Elevation) * .000015f) : 0f;
            soil = math.saturate(soil);

            float oceanTemp = c.OceanTemperature;
            float3 current = float3.zero;
            if (c.Land == 0)
            {
                current = ClimateMath.WindDrivenCurrent(normal, wind, thermohaline);
                float advectedOcean = c.OceanTemperature + (meanTemp - c.OceanTemperature) * .00015f
                    + math.dot(current, east) * .00003f;
                oceanTemp = math.lerp(c.OceanTemperature, (temperature + advectedOcean) * .5f, .0003f);
                temperature = math.lerp(temperature, oceanTemp, .18f);
            }
            float stormTarget = math.saturate(.25f + gradient * 30f + humidity * .35f + cloud * .2f - .3f);
            float storm = math.saturate(c.Storminess * .94f + stormTarget * .06f);
            float dryTicks = soil < .16f ? c.DryTicks + ClimateTickInterval : math.max(0f, c.DryTicks - 2f * ClimateTickInterval);
            float anomaly = math.abs(temperature - seasonalNorm) > 13f ? c.TemperatureAnomalyTicks + ClimateTickInterval : math.max(0f, c.TemperatureAnomalyTicks - ClimateTickInterval);

            c.TemperatureTrend = math.clamp(temperature, -100f, 75f) - c.Temperature;
            c.HumidityTrend = humidity - c.Humidity;
            c.SoilMoistureTrend = soil - c.SoilMoisture;
            c.Temperature = math.clamp(temperature, -100f, 75f);
            c.Pressure = pressure;
            c.Humidity = humidity;
            c.Wind = wind;
            c.CloudCover = cloud;
            c.Precipitation = precip;
            c.SoilMoisture = soil;
            c.SnowCover = snow;
            c.OceanTemperature = math.clamp(oceanTemp, -5f, 42f);
            c.OceanCurrent = current;
            c.Storminess = storm;
            c.Nutrient = c.Land == 0 ? math.saturate(c.Nutrient + precip * .0001f - math.length(current) * .00001f) : c.Nutrient;
            c.DryTicks = dryTicks;
            c.TemperatureAnomalyTicks = anomaly;
            c.TemperatureInsolationTerm = solarTerm;
            c.TemperatureAdvectionTerm = advectionTerm;
            c.EvaporationTerm = evaporation;
            c.OrographicLiftTerm = orographic;

            // Stage-07 god-tool forcing: temporary deltas applied on top of the model, so
            // the planet keeps its own dynamics and relaxes back once the tool expires.
            if (Forcing.Length == Source.Length)
            {
                ClimateForcingCell forcing = Forcing[index];
                if (forcing.TemperatureDelta != 0f)
                {
                    c.Temperature = math.clamp(c.Temperature + forcing.TemperatureDelta, -100f, 75f);
                }
                if (forcing.MoistureDelta != 0f)
                {
                    c.Humidity = math.saturate(c.Humidity + forcing.MoistureDelta);
                    c.SoilMoisture = math.saturate(c.SoilMoisture + forcing.MoistureDelta * .5f);
                }
                if (forcing.WindScale != 1f && forcing.WindScale > 0f)
                {
                    c.Wind *= forcing.WindScale;
                }
                if (forcing.SolarDimming != 0f)
                {
                    float dim = 1f - math.saturate(forcing.SolarDimming);
                    c.Insolation *= dim;
                    c.TemperatureInsolationTerm *= dim;
                }
            }

            Destination[index] = c;
        }

        private static float Hash01(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return (x & 0x00ffffffu) / 16777215f;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SunSystem))]
    public partial struct ClimateSystem : ISystem
    {
        private NativeArray<PlanetCell> source;
        private NativeArray<PlanetCell> destination;
        private NativeArray<ClimateForcingCell> forcing;
        private uint terrainVersionSeen;
        private ulong lastTick;
        private Entity initializedPlanet;
        private bool initialized;
        private bool hasProcessedTick;

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<GameTime>(out GameTime clock) ||
                !SystemAPI.TryGetSingleton<WorldSettingsData>(out WorldSettingsData settings)) return;
            if (!SystemAPI.TryGetSingletonEntity<PlanetState>(out Entity planetEntity))
            {
                DisposeArrays();
                hasProcessedTick = false;
                initializedPlanet = Entity.Null;
                return;
            }
            EntityManager em = state.EntityManager;
            if (!em.HasBuffer<WeatherEvent>(planetEntity)) em.AddBuffer<WeatherEvent>(planetEntity);
            if (!em.HasBuffer<WeatherEventLog>(planetEntity)) em.AddBuffer<WeatherEventLog>(planetEntity);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
            if (!initialized || source.Length != cells.Length || initializedPlanet != planetEntity)
            {
                DisposeArrays();
                source = new NativeArray<PlanetCell>(cells.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                destination = new NativeArray<PlanetCell>(cells.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                forcing = new NativeArray<ClimateForcingCell>(cells.Length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < cells.Length; i++) source[i] = destination[i] = cells[i];
                initialized = true;
                initializedPlanet = planetEntity;
                hasProcessedTick = false;
                terrainVersionSeen = 0u;
            }

            // Stage-07: a god terrain edit rewrites elevation/land/biome. The climate step
            // keeps a private copy of the cells, so refresh the terrain-owned fields when
            // the revision counter moves instead of every tick.
            uint terrainVersion = em.HasComponent<TerrainVersion>(planetEntity)
                ? em.GetComponentData<TerrainVersion>(planetEntity).Value
                : 0u;
            if (terrainVersion != terrainVersionSeen)
            {
                terrainVersionSeen = terrainVersion;
                for (int i = 0; i < cells.Length; i++)
                {
                    PlanetCell refreshed = source[i];
                    renewedTerrain(ref cells, ref refreshed, i);
                    source[i] = refreshed;
                    destination[i] = refreshed;
                }
                hasProcessedTick = false;
            }

            // Stage-07: copy the god-tool forcing buffer (zero-filled when inactive).
            if (em.HasBuffer<ClimateForcingCell>(planetEntity) && forcing.Length == cells.Length)
            {
                DynamicBuffer<ClimateForcingCell> forcingBuffer = em.GetBuffer<ClimateForcingCell>(planetEntity);
                if (forcingBuffer.Length == cells.Length)
                {
                    for (int i = 0; i < cells.Length; i++) forcing[i] = forcingBuffer[i];
                }
                else
                {
                    for (int i = 0; i < cells.Length; i++) forcing[i] = default;
                }
            }
            ClimateConfig config = em.HasComponent<ClimateConfig>(planetEntity) ? em.GetComponentData<ClimateConfig>(planetEntity) : ClimateConfig.Default;
            int interval = math.max(1, config.ClimateTicksPerSimTick);
            PlanetState planet = em.GetComponentData<PlanetState>(planetEntity);
            ClockConfig clockConfig = settings.ToClockConfig();
            bool changed = false;
            ulong firstTick = hasProcessedTick ? lastTick + 1UL : clock.TotalTicks;
            if (clock.TotalTicks >= firstTick)
            {
                for (ulong tick = firstTick; tick <= clock.TotalTicks; tick++)
                {
                    if (tick % (ulong)interval == 0UL)
                    {
                        SimDate date = CalendarMath.FromTicks(tick, clockConfig);
                        float days = math.max(1f, settings.DaysPerSeason * settings.SeasonsPerYear);
                        float yearPhase = (date.DayOfYear + date.DayFraction) / days;
                        float3 sun = SunMath.Direction(date.DayFraction, yearPhase);
                        for (int i = 0; i < cells.Length; i++)
                        {
                            PlanetCell c = source[i];
                            c.Insolation = SunMath.Insolation(planet.Topology.Value.Centers[i], sun, c.Elevation);
                            source[i] = c;
                        }
                        var job = new ClimateStepJob
                        {
                            Source = source, Destination = destination, Topology = planet.Topology,
                            Forcing = forcing,
                            YearPhase = yearPhase, WindScale = math.max(.01f, config.WindScale),
                            LandResponse = math.clamp(config.TemperatureResponseLand, .00001f, .05f),
                            OceanResponse = math.clamp(config.TemperatureResponseOcean, .00001f, .05f),
                            Seed = (uint)(settings.WorldSeed ^ (settings.WorldSeed >> 32)),
                            ClimateTickInterval = interval
                        };
                        job.Schedule(cells.Length, 64).Complete();
                        NativeArray<PlanetCell> swap = source; source = destination; destination = swap;
                        DetectEvents(em, planetEntity, source, planet.Topology, tick, clockConfig.TicksPerDay);
                        changed = true;
                    }
                    if (tick == clock.TotalTicks) break; // avoid unsigned overflow at ulong.MaxValue
                }
            }
            lastTick = clock.TotalTicks;
            hasProcessedTick = true;
            if (changed) for (int i = 0; i < cells.Length; i++) cells[i] = source[i];
        }

        private static void DetectEvents(EntityManager em, Entity entity, NativeArray<PlanetCell> cells,
            BlobAssetReference<PlanetTopologyBlob> topology, ulong tick, uint ticksPerDay)
        {
            DynamicBuffer<WeatherEvent> active = em.GetBuffer<WeatherEvent>(entity);
            DynamicBuffer<WeatherEventLog> log = em.GetBuffer<WeatherEventLog>(entity);
            for (int i = active.Length - 1; i >= 0; i--)
            {
                WeatherEvent live = active[i];
                if (live.EndTick <= tick) { active.RemoveAt(i); continue; }
                bool mobile = live.Type == WeatherEventType.Storm || live.Type == WeatherEventType.Blizzard
                    || live.Type == WeatherEventType.Front || live.Type == WeatherEventType.Fog;
                if (!mobile || !topology.IsCreated || tick <= live.StartTick || (tick - live.StartTick) % 12UL != 0UL
                    || live.Cell < 0 || live.Cell >= cells.Length) continue;
                ref PlanetTopologyBlob topo = ref topology.Value;
                int current = live.Cell, best = current;
                float bestFlow = 0f;
                float3 normal = topo.Centers[current], wind = cells[current].Wind;
                for (int n = topo.NeighborOffsets[current]; n < topo.NeighborOffsets[current + 1]; n++)
                {
                    int neighbor = topo.Neighbors[n];
                    float3 direction = math.normalizesafe(topo.Centers[neighbor] - normal * math.dot(topo.Centers[neighbor], normal));
                    float flow = math.dot(wind, direction);
                    if (flow > bestFlow) { bestFlow = flow; best = neighbor; }
                }
                live.Cell = best;
                active[i] = live;
            }

            // One strongest eligible event per climate step keeps the feed legible and bounded.
            int winner = -1;
            WeatherEventType type = WeatherEventType.Storm;
            float strength = .4f;
            for (int i = 0; i < cells.Length; i++)
            {
                PlanetCell c = cells[i];
                float candidate = ClimateMath.IsStormCandidate(c.Storminess, c.Humidity) ? c.Storminess : 0f;
                WeatherEventType candidateType = WeatherEventType.Storm;
                if (c.Temperature < 0f && c.Precipitation > .15f && candidate > .48f) candidateType = WeatherEventType.Blizzard;
                else if (ClimateMath.IsFogCandidate(c.Humidity, math.length(c.Wind), c.CloudCover)) { candidateType = WeatherEventType.Fog; candidate = math.max(candidate, .56f); }
                else if (ClimateMath.IsDroughtCandidate(c.DryTicks, ticksPerDay, c.SoilMoisture)) { candidateType = WeatherEventType.Drought; candidate = .78f; }
                else if (c.TemperatureAnomalyTicks >= ticksPerDay * 2f)
                {
                    candidateType = c.Temperature > 28f ? WeatherEventType.HeatWave : WeatherEventType.ColdWave;
                    candidate = .74f;
                }
                if (candidate < .58f && topology.IsCreated)
                {
                    ref PlanetTopologyBlob topo = ref topology.Value;
                    for (int n = topo.NeighborOffsets[i]; n < topo.NeighborOffsets[i + 1]; n++)
                        if (math.abs(c.Temperature - cells[topo.Neighbors[n]].Temperature) > 14f && math.length(c.Wind) > 2f)
                        { candidateType = WeatherEventType.Front; candidate = .58f; break; }
                }
                if (candidate <= strength) continue;
                winner = i; type = candidateType; strength = candidate;
            }
            if (winner < 0) return;
            for (int e = 0; e < active.Length; e++)
                if (active[e].Cell == winner && active[e].EndTick > tick) return;
            ulong duration = ticksPerDay / 4u;
            if (duration < 40UL) duration = 40UL;
            active.Add(new WeatherEvent { Type = type, Cell = winner, Radius = type == WeatherEventType.Storm ? .12f : .08f,
                Strength = math.saturate(strength), StartTick = tick, EndTick = tick + duration });
            if (log.Length == 512) log.RemoveAt(0);
            log.Add(new WeatherEventLog { Tick = tick, Type = type, Cell = winner, Strength = math.saturate(strength) });
        }

        /// <summary>Copies terrain-owned fields from the live cell buffer into a cached copy.</summary>
        private static void renewedTerrain(ref DynamicBuffer<PlanetCell> live, ref PlanetCell cached, int index)
        {
            PlanetCell current = live[index];
            cached.Elevation = current.Elevation;
            cached.Land = current.Land;
            cached.Biome = current.Biome;
            cached.Continent = current.Continent;
        }

        public void OnDestroy(ref SystemState state) => DisposeArrays();
        private void DisposeArrays()
        {
            if (source.IsCreated) source.Dispose();
            if (destination.IsCreated) destination.Dispose();
            if (forcing.IsCreated) forcing.Dispose();
            terrainVersionSeen = 0u;
            initialized = false;
        }
    }
}
