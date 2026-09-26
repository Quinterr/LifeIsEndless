using Unity.Entities;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    public enum Biome : byte { Ocean, Beach, Desert, Savanna, Grassland, Forest, Taiga, Tundra, Glacier, Wetland, Reef, OpenOcean }
    public struct PlanetState : IComponentData { public BlobAssetReference<PlanetTopologyBlob> Topology; public float Radius; public float3 SunDirection; }
    public struct PlanetCell : IBufferElementData
    {
        public float Elevation, Latitude, Longitude;
        public Biome Biome;
        public byte Land, Continent;
        // Stage 03 Climate writes weather + soil + snow; stage 05 Life writes biomass/detritus.
        public float Temperature, Pressure, Humidity, SoilMoisture, SnowCover, Fertility, Biomass, Detritus;
        public float3 Wind;
        public float Insolation;
    }
    public interface IBiomeClassifier { Biome Classify(float elevation, float latitude, float moisture); }
    public struct ProxyBiomeClassifier : IBiomeClassifier
    {
        public Biome Classify(float elevation,float latitude,float moisture)
        {
            float lat=math.abs(latitude);
            if(elevation<0) return elevation>-.06f ? Biome.Reef : Biome.OpenOcean;
            if(elevation<.035f) return Biome.Beach;
            if(lat>.82f || (lat>.68f && elevation>.5f)) return Biome.Glacier;
            if(lat>.68f) return Biome.Tundra;
            if(lat>.54f) return Biome.Taiga;
            if(moisture>.8f && elevation<.15f) return Biome.Wetland;
            if(moisture<.27f) return Biome.Desert;
            if(moisture<.43f) return Biome.Savanna;
            return moisture>.65f ? Biome.Forest : Biome.Grassland;
        }
    }
    public static class TerrainMath
    {
        // Integer hash and interpolated value noise: pure, seed-dependent, Burst compatible.
        static uint Hash(uint x) { x^=x>>16; x*=0x7feb352du; x^=x>>15; x*=0x846ca68bu; return x^(x>>16); }
        static float Noise(float3 p,uint seed)
        {
            int3 cell=(int3)math.floor(p); float3 f=math.frac(p); f=f*f*(3-2*f);
            float result=0;
            for(int z=0;z<2;z++) for(int y=0;y<2;y++) for(int x=0;x<2;x++)
            {
                int3 c=cell+new int3(x,y,z);
                uint h=Hash((uint)c.x*73856093u ^ (uint)c.y*19349663u ^ (uint)c.z*83492791u ^ seed);
                result+=((h & 0xffffffu)/8388607.5f-1f)* (x==0?1-f.x:f.x)*(y==0?1-f.y:f.y)*(z==0?1-f.z:f.z);
            }
            return result;
        }
        public static float Elevation(float3 dir,ulong seed,float seaLevel,float mountainAmplitude)
        {
            uint s=(uint)(seed ^ (seed>>32));
            float3 warp=dir*2.4f+new float3(Noise(dir*4,s+1),Noise(dir*4,s+2),Noise(dir*4,s+3))*.25f;
            float continent=Noise(warp,s)*.8f+Noise(warp*2,s+4)*.3f-seaLevel;
            float ridge=1-math.abs(Noise(warp*7,s+5));
            float height=continent+math.max(0,continent)*ridge*ridge*mountainAmplitude;
            return math.clamp(math.round(height*12)/12,-1,1);
        }
        public static PlanetCell Generate(float3 dir,ulong seed,float seaLevel,float mountains)
        {
            float elev=Elevation(dir,seed,seaLevel,mountains);
            float lat=math.asin(math.clamp(dir.y,-1,1))/math.PI*2;
            float moisture=math.saturate(.5f+Noise(dir*6,(uint)seed+71)*.5f-elev*.25f);
            return new PlanetCell { Elevation=elev,Latitude=lat,Longitude=math.atan2(dir.z,dir.x),Land=(byte)(elev>=0?1:0),
                Biome=new ProxyBiomeClassifier().Classify(elev,lat,moisture), Continent=(byte)(elev>=0?1:0),
                Temperature=20,Pressure=101.3f,Humidity=.5f,SoilMoisture=.5f,Fertility=.5f };
        }
    }
    public static class SunMath
    {
        // Local longitude zero faces sun at dayFraction=0.5. Summer solstice at yearPhase=.375.
        public static float3 Direction(float dayFraction,float yearPhase,float tiltRadians=.4091f)
        {
            float declination=math.sin((yearPhase-.125f)*2*math.PI)*tiltRadians;
            float hour=(dayFraction-.5f)*2*math.PI;
            return new float3(math.cos(declination)*math.cos(hour),math.sin(declination),math.cos(declination)*math.sin(hour));
        }
        public static float Insolation(float3 normal,float3 sun,float altitude=0) => math.saturate(math.dot(math.normalizesafe(normal),sun) * (1+math.max(0,altitude)*.02f));
    }
}
