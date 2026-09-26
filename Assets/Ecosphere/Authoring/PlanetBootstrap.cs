using Ecosphere.Core.ECS;
using Ecosphere.Planet;
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
            if(visuals==null) return;
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null || !world.IsCreated) return;
            var em=world.EntityManager;
            if(!em.Exists(planet)) return;
            var sun=em.GetComponentData<PlanetState>(planet).SunDirection;
            RenderSettings.ambientLight=new Color(.18f,.24f,.34f);
            if(sunlight!=null) {sunlight.transform.rotation=Quaternion.LookRotation(-(Vector3)sun);sunlight.intensity=1.5f;}
            int requestedMode=overlayMode;
            for(int i=0;i<8;i++) if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i))) requestedMode=i+1;
            if(Input.GetKeyDown(KeyCode.Alpha0)) requestedMode=0;
            if(requestedMode!=overlayMode) {overlayMode=requestedMode;lastOverlayTick=ulong.MaxValue;}
            if(!timeQueryReady || timeQuery.IsEmpty || !em.HasBuffer<PlanetCell>(planet)) return;
            GameTime clock=timeQuery.GetSingleton<GameTime>();
            if(lastOverlayTick==clock.TotalTicks) return;
            lastOverlayTick=clock.TotalTicks;
            DynamicBuffer<PlanetCell> climateCells=em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> events=em.GetBuffer<WeatherEvent>(planet);
            ClimateSampler sampler=new ClimateSampler(em.GetComponentData<PlanetState>(planet),climateCells,events);
            for(int i=0;i<eventCellFlags.Length;i++) eventCellFlags[i]=false;
            for(int e=0;e<events.Length;e++) if(events[e].Cell>=0 && events[e].Cell<eventCellFlags.Length) eventCellFlags[events[e].Cell]=true;
            for(int i=0;i<climateCells.Length;i++)
            {
                PlanetCell c=climateCells[i];
                ClimateSample sample=sampler.Sample(i);
                Color32 color=overlayMode==0
                    ? (Palette!=null?Palette.ColorFor(c.Biome,c.Latitude,0):WorldPalette.DefaultColor(c.Biome))
                    : OverlayColor(sample,overlayMode);
                if(overlayMode==8 && eventCellFlags[i]) color=new Color32(255,45,35,255);
                cellOverlayColors[i]=color;
            }
            for(int chunk=0;chunk<meshes.Length;chunk++)
            {
                for(int v=0;v<vertexCellIds[chunk].Length;v++)
                    overlayColors[chunk][v]=cellOverlayColors[vertexCellIds[chunk][v]];
                meshes[chunk].colors32=overlayColors[chunk];
            }
        }

        private static Color32 OverlayColor(ClimateSample c,int mode)
        {
            if(mode==1) return Ramp(new Color32(25,65,210,255),new Color32(250,55,25,255),math.saturate((c.Temperature+45f)/85f));
            if(mode==2)
            {
                float band=math.round(c.Pressure*28f)/28f;
                return Ramp(new Color32(30,65,150,255),new Color32(255,205,70,255),math.saturate((band-.32f)/.42f));
            }
            if(mode==3)
            {
                float speed=math.saturate(c.WindStrength/18f);
                float east=math.saturate((c.Wind.z+18f)/36f);
                Color32 direction=Ramp(new Color32(35,190,230,255),new Color32(235,65,205,255),east);
                return Ramp(new Color32(25,35,70,255),direction,speed);
            }
            if(mode==4)
            {
                float rain=math.saturate(c.Precipitation/5f);
                Color32 humid=Ramp(new Color32(155,125,70,255),new Color32(45,100,220,255),c.Humidity);
                return Ramp(humid,new Color32(245,250,255,255),rain*.8f);
            }
            if(mode==5) return Ramp(new Color32(135,75,38,255),new Color32(35,195,75,255),c.SoilMoisture);
            if(mode==6) return Ramp(new Color32(45,80,115,255),new Color32(250,250,255,255),c.SnowCover);
            if(mode==7) return c.IsSubmerged?Ramp(new Color32(5,30,85,255),new Color32(40,235,205,255),math.saturate(math.length(c.OceanCurrent)/2.5f)):new Color32(42,52,58,255);
            return Ramp(new Color32(20,35,80,255),new Color32(250,55,20,255),c.Storminess);
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
