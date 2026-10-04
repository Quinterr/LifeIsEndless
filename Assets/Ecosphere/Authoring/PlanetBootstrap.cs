using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Ecosphere.UX;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Authoring
{
    /// <summary>Runtime seed generation. Blob belongs to this component and is disposed on teardown.</summary>
    public class PlanetBootstrap : MonoBehaviour
    {
        public GameBalance Balance;
        public WorldPalette Palette;
        private BlobAssetReference<PlanetTopologyBlob> topology;
        private Entity planet;
        private Mesh[] meshes;
        private int[][] vertexCellIds;
        private Color32[][] overlayColors;
        private Color32[] cellOverlayColors;
        private bool[] eventCellFlags;
        private int overlayMode;
        private ulong lastOverlayTick=ulong.MaxValue;
        private uint terrainVersionSeen;
        private bool productStarted;
        private int[] cellPopulation;
        private int cellPopulationMax;

        /// <summary>Current overlay mode (0 = biome palette). Stage-07 UI drives this.</summary>
        public int OverlayMode
        {
            get => overlayMode;
            set { if (value != overlayMode) { overlayMode = value; lastOverlayTick = ulong.MaxValue; } }
        }

        /// <summary>True once terrain meshes exist (the UX waits for this before drawing).</summary>
        public bool IsTerrainReady => visuals != null && meshes != null;

        /// <summary>Number of rendered terrain chunks (diagnostics).</summary>
        public int ChunkCount => meshes != null ? meshes.Length : 0;
        private Material terrainMaterial;
        private GameObject visuals;
        private EntityQuery timeQuery;
        private bool timeQueryReady;
        private Light sunlight;
        private void Start()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null) return;
            var em=world.EntityManager;
            WorldSettingsData settings;
            using(var settingsQuery=em.CreateEntityQuery(typeof(WorldSettingsData))) settings=settingsQuery.GetSingleton<WorldSettingsData>();
            timeQuery=em.CreateEntityQuery(typeof(GameTime));
            timeQueryReady=true;
            var balance=Balance!=null?Balance:ScriptableObject.CreateInstance<GameBalance>();
            topology=Icosphere.Build(balance.Planet.Subdivision);
            planet=em.CreateEntity(typeof(PlanetState),typeof(ClimateConfig));
            em.SetComponentData(planet,new PlanetState {Topology=topology,Radius=balance.Planet.Radius,SunDirection=SunMath.Direction(.5f,0)});
            EvolutionStateData evolution = EvolutionStateData.Default;
            evolution.MutationMultiplier = math.clamp(balance.Evolution.MutationRateScale, 0f, 10f);
            evolution.StructuralMutationChance = math.clamp(balance.Evolution.StructuralMutationChance, 0f, 0.1f);
            evolution.SelectionPressure = math.clamp(balance.Evolution.SelectionPressure, 0f, 4f);
            evolution.CompatibilityThreshold = math.clamp(balance.Evolution.CompatibilityThreshold, 0.01f, 1f);
            evolution.SpeciationDriftThreshold = math.clamp(balance.Evolution.SpeciationDriftThreshold, 0.01f, 1f);
            evolution.SpeciationFailureFraction = math.saturate(balance.Evolution.SpeciationFailureFraction);
            evolution.MinimumSpeciationPopulation = math.max(2, balance.Evolution.MinimumSpeciationPopulation);
            evolution.MaxConceptionsPerTick = math.max(1, balance.Evolution.MaxConceptionsPerTick);
            evolution.MaxEggUpdatesPerTick = math.max(1, balance.Evolution.MaxEggUpdatesPerTick);
            evolution.MaxTrackedEvents = math.max(64, balance.Evolution.MaxTrackedEvents);
            evolution.MaxTrackedMetrics = math.max(256, balance.Evolution.MaxTrackedMetrics);
            em.AddComponentData(planet, evolution);
            ClimateConfig climateConfig=ClimateConfig.Default;
            climateConfig.WindScale=balance.Climate.WindScale;
            climateConfig.TemperatureResponseLand*=balance.Climate.TemperatureScale;
            climateConfig.TemperatureResponseOcean*=balance.Climate.TemperatureScale;
            em.SetComponentData(planet,climateConfig);
            em.AddBuffer<WeatherEvent>(planet).EnsureCapacity(128);
            em.AddBuffer<WeatherEventLog>(planet).EnsureCapacity(512);
            var cells=em.AddBuffer<PlanetCell>(planet);
            ref var blob=ref topology.Value;
            cells.ResizeUninitialized(blob.Centers.Length);
            cellOverlayColors=new Color32[cells.Length];
            eventCellFlags=new bool[cells.Length];
            for(int i=0;i<cells.Length;i++)
            {
                PlanetCell cell=TerrainMath.Generate(blob.Centers[i],settings.WorldSeed,balance.Planet.SeaLevel,balance.Planet.MountainAmplitude);
                cell.Temperature=27f-58f*math.abs(cell.Latitude);
                cell.Pressure=ClimateMath.Pressure(cell.Temperature,cell.Elevation,cell.Latitude);
                cell.SoilMoisture=cell.Land != 0 ? .5f : 0f;
                cell.OceanTemperature=cell.Land == 0 ? cell.Temperature : 0f;
                cell.Nutrient=cell.Land == 0 ? .5f : 0f;
                cells[i]=cell;
            }
            visuals=new GameObject("Planet terrain");
            var sunObject=new GameObject("Sun");sunObject.transform.SetParent(visuals.transform);
            sunlight=sunObject.AddComponent<Light>(); sunlight.type=LightType.Directional;
            terrainMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            terrainMaterial.enableInstancing=true;
            terrainMaterial.SetFloat("_Smoothness",0);
            int chunkCount=math.min(16,math.max(1,blob.Faces.Length/1200));
            meshes=new Mesh[chunkCount];
            vertexCellIds=new int[chunkCount][];
            overlayColors=new Color32[chunkCount][];
            for(int chunk=0;chunk<chunkCount;chunk++)
            {
                int start=blob.Faces.Length*chunk/chunkCount, end=blob.Faces.Length*(chunk+1)/chunkCount;
                int count=(end-start)*3;
                var mesh=new Mesh {name="Planet chunk "+chunk,indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};
                var data=Mesh.AllocateWritableMeshData(1);
                var md=data[0];
                md.SetVertexBufferParams(count,new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position),
                    new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal),
                    new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Color, UnityEngine.Rendering.VertexAttributeFormat.UNorm8,4));
                md.SetIndexBufferParams(count,UnityEngine.Rendering.IndexFormat.UInt32);
                var vertices=md.GetVertexData<Vertex>(); var indices=md.GetIndexData<uint>();
                vertexCellIds[chunk]=new int[count];
                overlayColors[chunk]=new Color32[count];
                for(int f=start;f<end;f++)
                {
                    int3 face=blob.Faces[f];
                    int[] ids={face.x,face.y,face.z};
                    Vector3[] positions=new Vector3[3];
                    for(int j=0;j<3;j++)
                    {
                        var c=cells[ids[j]]; float r=balance.Planet.Radius*(1+math.max(0,c.Elevation)*.035f);
                        positions[j]=(Vector3)(blob.Centers[ids[j]]*r);
                    }
                    Vector3 normal=Vector3.Cross(positions[1]-positions[0],positions[2]-positions[0]).normalized;
                    if(Vector3.Dot(normal,positions[0])<0) normal=-normal;
                    for(int j=0;j<3;j++)
                    {
                        int index=(f-start)*3+j;
                        Color32 terrain=Palette!=null?Palette.ColorFor(cells[ids[j]].Biome,cells[ids[j]].Latitude,0):WorldPalette.DefaultColor(cells[ids[j]].Biome);
                        vertices[index]=new Vertex {Position=positions[j],Normal=normal,Color=terrain};
                        vertexCellIds[chunk][index]=ids[j];
                        overlayColors[chunk][index]=terrain;
                        indices[index]=(uint)index;
                    }
                }
                md.subMeshCount=1;
                md.SetSubMesh(0,new UnityEngine.Rendering.SubMeshDescriptor(0,count));
                Mesh.ApplyAndDisposeWritableMeshData(data,mesh);
                mesh.RecalculateBounds(); meshes[chunk]=mesh;
                var go=new GameObject("Chunk "+chunk);go.transform.SetParent(visuals.transform);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=terrainMaterial;
            }
        }
        private struct Vertex { public Vector3 Position; public Vector3 Normal; public Color32 Color; }
        private void Update()
        {
            // Stage 07: hand the player-facing layer the overlay setter once the terrain
            // exists (Update runs after Start, so the meshes are already built).
            if(!productStarted)
            {
                productStarted=true;
                ProductBootstrap.Ensure(mode => OverlayMode = mode);
                OverlayBridge.SetMode = mode => OverlayMode = mode;
            }
            if(visuals==null) return;
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null || !world.IsCreated) return;
            var em=world.EntityManager;
            if(!em.Exists(planet)) return;
            var sun=em.GetComponentData<PlanetState>(planet).SunDirection;
            RenderSettings.ambientLight=new Color(.18f,.24f,.34f);
            if(sunlight!=null) {sunlight.transform.rotation=Quaternion.LookRotation(-(Vector3)sun);sunlight.intensity=1.5f;}
            // Stage 07: overlay selection is owned by the UX layer (PlanetOverlayController);
            // this renderer only reacts to OverlayMode changes.
            if(!timeQueryReady || timeQuery.IsEmpty || !em.HasBuffer<PlanetCell>(planet)) return;

            // A god terrain edit rewrites elevation: rebuild geometry once per revision.
            uint terrainVersion = em.HasComponent<TerrainVersion>(planet) ? em.GetComponentData<TerrainVersion>(planet).Value : 0u;
            if(terrainVersion != terrainVersionSeen)
            {
                terrainVersionSeen = terrainVersion;
                if(terrainVersion > 1u) { RebuildTerrainGeometry(em); lastOverlayTick=ulong.MaxValue; }
            }
            GameTime clock=timeQuery.GetSingleton<GameTime>();
            if(lastOverlayTick==clock.TotalTicks) return;
            lastOverlayTick=clock.TotalTicks;
            DynamicBuffer<PlanetCell> climateCells=em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> events=em.GetBuffer<WeatherEvent>(planet);
            ClimateSampler sampler=new ClimateSampler(em.GetComponentData<PlanetState>(planet),climateCells,events);
            if(overlayMode==(int)OverlayMode.PopulationDensity) RefreshPopulationDensity(em,climateCells.Length);
            for(int i=0;i<eventCellFlags.Length;i++) eventCellFlags[i]=false;
            for(int e=0;e<events.Length;e++) if(events[e].Cell>=0 && events[e].Cell<eventCellFlags.Length) eventCellFlags[events[e].Cell]=true;
            for(int i=0;i<climateCells.Length;i++)
            {
                PlanetCell c=climateCells[i];
                ClimateSample sample=sampler.Sample(i);
                float density=cellPopulation!=null && i<cellPopulation.Length
                    ? (float)cellPopulation[i]/cellPopulationMax
                    : 0f;
                Color32 color=overlayMode==0
                    ? (Palette!=null?Palette.ColorFor(c.Biome,c.Latitude,0):WorldPalette.DefaultColor(c.Biome))
                    : OverlayColor(sample,overlayMode,density);
                if(overlayMode==8 && eventCellFlags[i]) color=new Color32(255,45,35,255);
                // Smooth cross-fade between overlay switches (stage-07 "smooth blending").
                Color32 blended=Color32.Lerp(cellOverlayColors[i],color,.35f);
                cellOverlayColors[i]=blended;
            }
            for(int chunk=0;chunk<meshes.Length;chunk++)
            {
                for(int v=0;v<vertexCellIds[chunk].Length;v++)
                    overlayColors[chunk][v]=cellOverlayColors[vertexCellIds[chunk][v]];
                meshes[chunk].colors32=overlayColors[chunk];
            }
        }

        /// <summary>
        /// Rebuilds chunk vertex positions/normals after a god terrain edit (elevation
        /// changed). Cell colours are refreshed by the normal overlay pass.
        /// </summary>
        private void RebuildTerrainGeometry(EntityManager em)
        {
            if(meshes==null || vertexCellIds==null || !em.HasBuffer<PlanetCell>(planet)) return;
            DynamicBuffer<PlanetCell> cells=em.GetBuffer<PlanetCell>(planet);
            float radius=em.GetComponentData<PlanetState>(planet).Radius;
            var blob=em.GetComponentData<PlanetState>(planet).Topology;
            if(!blob.IsCreated) return;
            for(int chunk=0;chunk<meshes.Length;chunk++)
            {
                if(meshes[chunk]==null) continue;
                int count=vertexCellIds[chunk].Length;
                var positions=new Vector3[count];
                var data=Mesh.AllocateWritableMeshData(1);
                var md=data[0];
                md.SetVertexBufferParams(count,new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position),
                    new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Normal),
                    new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Color, UnityEngine.Rendering.VertexAttributeFormat.UNorm8,4));
                md.SetIndexBufferParams(count,UnityEngine.Rendering.IndexFormat.UInt32);
                var vertices=md.GetVertexData<Vertex>();
                var indices=md.GetIndexData<uint>();
                for(int v=0;v<count;v+=3)
                {
                    int[] ids=new int[3];
                    for(int j=0;j<3;j++) ids[j]=vertexCellIds[chunk][v+j];
                    for(int j=0;j<3;j++)
                    {
                        PlanetCell c=cells[ids[j]];
                        float r=radius*(1+math.max(0,c.Elevation)*.035f);
                        positions[j]=(Vector3)(blob.Value.Centers[ids[j]]*r);
                    }
                    Vector3 normal=Vector3.Cross(positions[1]-positions[0],positions[2]-positions[0]).normalized;
                    if(Vector3.Dot(normal,positions[0])<0) normal=-normal;
                    for(int j=0;j<3;j++)
                    {
                        int index=v+j;
                        vertices[index]=new Vertex {Position=positions[j],Normal=normal,Color=overlayColors[chunk][index]};
                        indices[index]=(uint)index;
                    }
                }
                md.subMeshCount=1;
                md.SetSubMesh(0,new UnityEngine.Rendering.SubMeshDescriptor(0,count));
                Mesh.ApplyAndDisposeWritableMeshData(data,meshes[chunk]);
                meshes[chunk].RecalculateBounds();
            }
        }

        /// <summary>
        /// Overlay colours come from the shared ramp table in Core.Simulation so the mesh and
        /// the UI legend can never disagree (stage 07 moved them out of this renderer).
        /// </summary>
        private static Color32 OverlayColor(ClimateSample c,int mode,float density)
        {
            if(mode==0) return new Color32(120,120,120,255);
            OverlayMode overlay=(OverlayMode)math.clamp(mode,0,OverlayRampMath.ModeCount-1);
            float value=overlay==OverlayMode.PopulationDensity?density:TintValue(c,overlay);
            Rgba32 direction=OverlayRampMath.DirectionTint((float)System.Math.Atan2(c.Wind.z,c.Wind.x));
            Rgba32 color=OverlayRampMath.ColorFor(overlay,value,direction);
            if(overlay==OverlayMode.Currents && c.IsSubmerged) color=OverlayRampMath.ColorFor(overlay,math.length(c.OceanCurrent),direction);
            return new Color32(color.R,color.G,color.B,color.A);
        }

        /// <summary>
        /// Population density overlay: sums the per-cell species bins maintained by the
        /// evolution systems (stage 06) so the god view shows where life actually is.
        /// </summary>
        private void RefreshPopulationDensity(EntityManager em,int cellCount)
        {
            if(cellPopulation==null || cellPopulation.Length!=cellCount) cellPopulation=new int[cellCount];
            for(int i=0;i<cellPopulation.Length;i++) cellPopulation[i]=0;
            if(em.HasBuffer<CellSpeciesPopulation>(planet))
            {
                var bins=em.GetBuffer<CellSpeciesPopulation>(planet);
                for(int i=0;i<bins.Length;i++)
                {
                    int cell=bins[i].CellIndex;
                    if(cell<0 || cell>=cellPopulation.Length) continue;
                    cellPopulation[cell]+=bins[i].Population;
                }
            }
            cellPopulationMax=1;
            for(int i=0;i<cellPopulation.Length;i++) if(cellPopulation[i]>cellPopulationMax) cellPopulationMax=cellPopulation[i];
        }

        private static float TintValue(ClimateSample c,OverlayMode overlay)
        {
            switch(overlay)
            {
                case OverlayMode.Temperature: return c.Temperature;
                case OverlayMode.Pressure: return c.Pressure;
                case OverlayMode.Wind: return c.WindStrength;
                case OverlayMode.Humidity: return math.max(c.Humidity,c.Precipitation/5f*.6f);
                case OverlayMode.SoilMoisture: return c.SoilMoisture;
                case OverlayMode.Snow: return c.SnowCover;
                case OverlayMode.Currents: return c.IsSubmerged?math.length(c.OceanCurrent):0f;
                case OverlayMode.Storminess: return c.Storminess;
                case OverlayMode.Insolation: return c.Insolation;
                case OverlayMode.Fertility: return c.Nutrient;
                default: return 0f;
            }
        }

        private static Color32 Ramp(Color32 a,Color32 b,float t)
        {
            t=math.saturate(t);
            return new Color32((byte)(a.r+(b.r-a.r)*t),(byte)(a.g+(b.g-a.g)*t),(byte)(a.b+(b.b-a.b)*t),255);
        }
        private void OnDestroy()
        {
            if(timeQueryReady) {timeQuery.Dispose();timeQueryReady=false;}
            var world=World.DefaultGameObjectInjectionWorld;
            if(world!=null && world.IsCreated && world.EntityManager.Exists(planet)) world.EntityManager.DestroyEntity(planet);
            if(topology.IsCreated) topology.Dispose();
            if(meshes!=null) foreach(var mesh in meshes) if(mesh!=null) Destroy(mesh);
            if(terrainMaterial!=null) Destroy(terrainMaterial);
            if(visuals!=null) Destroy(visuals);
        }
    }
}
