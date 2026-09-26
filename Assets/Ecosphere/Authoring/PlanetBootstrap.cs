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
        private Material terrainMaterial;
        private GameObject visuals;
        private Light sunlight;
        private void Start()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null) return;
            var em=world.EntityManager;
            var settings=em.CreateEntityQuery(typeof(WorldSettingsData)).GetSingleton<WorldSettingsData>();
            var balance=Balance!=null?Balance:ScriptableObject.CreateInstance<GameBalance>();
            topology=Icosphere.Build(balance.Planet.Subdivision);
            planet=em.CreateEntity(typeof(PlanetState));
            em.SetComponentData(planet,new PlanetState {Topology=topology,Radius=balance.Planet.Radius,SunDirection=SunMath.Direction(.5f,0)});
            var cells=em.AddBuffer<PlanetCell>(planet);
            ref var blob=ref topology.Value;
            cells.ResizeUninitialized(blob.Centers.Length);
            for(int i=0;i<cells.Length;i++) cells[i]=TerrainMath.Generate(blob.Centers[i],settings.WorldSeed,balance.Planet.SeaLevel,balance.Planet.MountainAmplitude);
            visuals=new GameObject("Planet terrain");
            var sunObject=new GameObject("Sun");sunObject.transform.SetParent(visuals.transform);
            sunlight=sunObject.AddComponent<Light>(); sunlight.type=LightType.Directional;
            terrainMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            terrainMaterial.enableInstancing=true;
            terrainMaterial.SetFloat("_Smoothness",0);
            int chunkCount=math.min(16,math.max(1,blob.Faces.Length/1200));
            meshes=new Mesh[chunkCount];
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
                        vertices[index]=new Vertex {Position=positions[j],Normal=normal,Color=Palette!=null?Palette.ColorFor(cells[ids[j]].Biome,cells[ids[j]].Latitude,0):WorldPalette.DefaultColor(cells[ids[j]].Biome)};
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
            var em=World.DefaultGameObjectInjectionWorld.EntityManager;
            if(!em.Exists(planet)) return;
            var sun=em.GetComponentData<PlanetState>(planet).SunDirection;
            RenderSettings.ambientLight=new Color(.18f,.24f,.34f);
            if(sunlight!=null) {sunlight.transform.rotation=Quaternion.LookRotation(-(Vector3)sun);sunlight.intensity=1.5f;}
        }
        private void OnDestroy()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world!=null && world.IsCreated && world.EntityManager.Exists(planet)) world.EntityManager.DestroyEntity(planet);
            if(topology.IsCreated) topology.Dispose();
            if(meshes!=null) foreach(var mesh in meshes) if(mesh!=null) Destroy(mesh);
            if(terrainMaterial!=null) Destroy(terrainMaterial);
            if(visuals!=null) Destroy(visuals);
        }
    }
}
