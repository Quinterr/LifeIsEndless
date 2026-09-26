using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    public struct PlanetTopologyBlob
    {
        public BlobArray<float3> Centers;
        public BlobArray<int3> Faces;
        public BlobArray<int> NeighborOffsets;
        public BlobArray<int> Neighbors;
        public BlobArray<float> Areas;
    }

    /// <summary>Primal vertex cells: 10*4^level+2. Indices are independent of seed.</summary>
    public static class Icosphere
    {
        public static BlobAssetReference<PlanetTopologyBlob> Build(int level)
        {
            level = math.clamp(level, 0, 6);
            float t = (1f + math.sqrt(5f)) / 2f;
            var v = new List<float3>();
            float3[] initial = { new float3(-1,t,0),new float3(1,t,0),new float3(-1,-t,0),new float3(1,-t,0),
                new float3(0,-1,t),new float3(0,1,t),new float3(0,-1,-t),new float3(0,1,-t),
                new float3(t,0,-1),new float3(t,0,1),new float3(-t,0,-1),new float3(-t,0,1) };
            foreach (var p in initial) v.Add(math.normalize(p));
            var faces = new List<int3> { new int3(0,11,5),new int3(0,5,1),new int3(0,1,7),new int3(0,7,10),new int3(0,10,11),
                new int3(1,5,9),new int3(5,11,4),new int3(11,10,2),new int3(10,7,6),new int3(7,1,8),
                new int3(3,9,4),new int3(3,4,2),new int3(3,2,6),new int3(3,6,8),new int3(3,8,9),
                new int3(4,9,5),new int3(2,4,11),new int3(6,2,10),new int3(8,6,7),new int3(9,8,1) };
            for (int n=0;n<level;n++)
            {
                var edges = new Dictionary<ulong,int>();
                int Mid(int a,int b)
                {
                    ulong key = ((ulong)(uint)math.min(a,b)<<32) | (uint)math.max(a,b);
                    if (edges.TryGetValue(key,out int index)) return index;
                    index=v.Count; v.Add(math.normalize(v[a]+v[b])); edges.Add(key,index); return index;
                }
                var next = new List<int3>(faces.Count*4);
                foreach (var f in faces)
                {
                    int a=Mid(f.x,f.y), b=Mid(f.y,f.z), c=Mid(f.z,f.x);
                    next.Add(new int3(f.x,a,c)); next.Add(new int3(f.y,b,a));
                    next.Add(new int3(f.z,c,b)); next.Add(new int3(a,b,c));
                }
                faces=next;
            }
            var neighbors=new List<int>[v.Count]; var area=new float[v.Count];
            for (int i=0;i<v.Count;i++) neighbors[i]=new List<int>(6);
            foreach (var f in faces)
            {
                Add(f.x,f.y); Add(f.y,f.z); Add(f.z,f.x);
                float spherical = 0.5f*math.length(math.cross(v[f.y]-v[f.x],v[f.z]-v[f.x]))/3f;
                area[f.x]+=spherical; area[f.y]+=spherical; area[f.z]+=spherical;
            }
            void Add(int a,int b) { if(!neighbors[a].Contains(b)) neighbors[a].Add(b); if(!neighbors[b].Contains(a)) neighbors[b].Add(a); }
            using (var builder=new BlobBuilder(Allocator.Temp))
            {
                ref var root=ref builder.ConstructRoot<PlanetTopologyBlob>();
                var centers=builder.Allocate(ref root.Centers,v.Count);
                var tris=builder.Allocate(ref root.Faces,faces.Count);
                var offsets=builder.Allocate(ref root.NeighborOffsets,v.Count+1);
                var areas=builder.Allocate(ref root.Areas,v.Count);
                int total=0;
                for(int i=0;i<v.Count;i++) { centers[i]=v[i]; areas[i]=area[i]; offsets[i]=total; total+=neighbors[i].Count; }
                offsets[v.Count]=total;
                var adj=builder.Allocate(ref root.Neighbors,total);
                int cursor=0;
                for(int i=0;i<v.Count;i++) { neighbors[i].Sort(); foreach(int k in neighbors[i]) adj[cursor++]=k; }
                for(int i=0;i<faces.Count;i++) tris[i]=faces[i];
                return builder.CreateBlobAssetReference<PlanetTopologyBlob>(Allocator.Persistent);
            }
        }

        public static int Nearest(ref PlanetTopologyBlob topology, float3 direction)
        {
            direction=math.normalizesafe(direction,new float3(0,1,0));
            int best=0; float score=-2;
            for(int i=0;i<topology.Centers.Length;i++) { float dot=math.dot(direction,topology.Centers[i]); if(dot>score) {score=dot;best=i;} }
            return best;
        }
    }
}
