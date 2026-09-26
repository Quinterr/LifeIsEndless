using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    public interface ISunlightProvider { float Sample(int cellIndex, float altitude); }

    /// <summary>Read-only per-tick snapshot; callers must not retain the buffer across structural changes.</summary>
    public struct PlanetSampler : ISunlightProvider
    {
        private BlobAssetReference<PlanetTopologyBlob> topology;
        private readonly DynamicBuffer<PlanetCell> cells;
        private readonly float3 sun;
        private readonly float radius;
        public PlanetSampler(PlanetState state, DynamicBuffer<PlanetCell> data)
        { topology=state.Topology; cells=data; sun=state.SunDirection; radius=state.Radius; }
        public int Nearest(float3 position) { var copy=topology; return Icosphere.Nearest(ref copy.Value,position); }
        public float Altitude(float3 position) => cells[Nearest(position)].Elevation*radius*.035f;
        public float Sample(int cellIndex,float altitude)
        {
            if(cellIndex<0 || cellIndex>=cells.Length) return 0;
            return SunMath.Insolation(topology.Value.Centers[cellIndex],sun,altitude/radius);
        }
        // Movement seam: return neighbor whose center best matches target direction.
        public int StepToward(int cellIndex,float3 destination)
        {
            if(cellIndex<0 || cellIndex>=cells.Length) return Nearest(destination);
            int winner=cellIndex; float score=math.dot(topology.Value.Centers[winner],math.normalizesafe(destination));
            for(int i=topology.Value.NeighborOffsets[cellIndex];i<topology.Value.NeighborOffsets[cellIndex+1];i++)
            {
                int n=topology.Value.Neighbors[i]; float candidate=math.dot(topology.Value.Centers[n],math.normalizesafe(destination));
                if(candidate>score) {score=candidate;winner=n;}
            }
            return winner;
        }
    }
}
