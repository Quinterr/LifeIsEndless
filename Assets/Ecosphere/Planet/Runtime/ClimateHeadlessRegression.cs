using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ecosphere.Planet
{
    public struct ClimateRegressionResult
    {
        public string Csv;
        public float EquatorMeanTemperature;
        public float PolarMeanTemperature;
        public float SummerHemisphereContrast;
        public int StormEventCount;
        public bool AllFieldsFinite;
    }

    /// <summary>Deterministic offline driver for regression tests and CSV inspection.</summary>
    public static class ClimateHeadlessRegression
    {
        public static ClimateRegressionResult Run(BlobAssetReference<PlanetTopologyBlob> topology,
            NativeArray<PlanetCell> initialCells, ulong seed, int days=60, uint ticksPerDay=1200,
            int daysPerYear=60, int climateTicksPerStep=2)
        {
            if (!topology.IsCreated || initialCells.Length != topology.Value.Centers.Length || initialCells.Length == 0)
                throw new System.ArgumentException("Topology and initial cell array must have matching nonzero lengths.");
            climateTicksPerStep=math.max(1,climateTicksPerStep);
            days=math.max(1,days);
            if(ticksPerDay==0u) ticksPerDay=1u;
            daysPerYear=math.max(1,daysPerYear);
            NativeArray<PlanetCell> a=new NativeArray<PlanetCell>(initialCells.Length,Allocator.TempJob,NativeArrayOptions.UninitializedMemory);
            NativeArray<PlanetCell> b=new NativeArray<PlanetCell>(initialCells.Length,Allocator.TempJob,NativeArrayOptions.UninitializedMemory);
            NativeArray<ulong> eventCooldown=new NativeArray<ulong>(initialCells.Length,Allocator.TempJob,NativeArrayOptions.ClearMemory);
            try
            {
                NativeArray<PlanetCell>.Copy(initialCells,a);
                int steps=(int)(((ulong)days*ticksPerDay+(uint)climateTicksPerStep-1)/(uint)climateTicksPerStep);
                double equatorSum=0, poleSum=0, equatorCount=0, poleCount=0;
                double northSummer=0, southInNorthSummer=0, southSummer=0, northInSouthSummer=0;
                long northCount=0, southDuringNorthCount=0, southCount=0, northDuringSouthCount=0;
                int eventCount=0;
                uint mixedSeed=(uint)(seed^(seed>>32));
                for(int step=0;step<steps;step++)
                {
                    ulong tick=(ulong)step*(uint)climateTicksPerStep;
                    float dayFraction=(tick%ticksPerDay)/(float)ticksPerDay;
                    float yearPhase=(tick/(float)ticksPerDay)/math.max(1,daysPerYear);
                    float3 sun=SunMath.Direction(dayFraction,yearPhase);
                    for(int i=0;i<a.Length;i++)
                    {
                        PlanetCell c=a[i];
                        c.Insolation=SunMath.Insolation(topology.Value.Centers[i],sun,c.Elevation);
                        a[i]=c;
                    }
                    var job=new ClimateStepJob
                    {
                        Source=a, Destination=b, Topology=topology, YearPhase=yearPhase, WindScale=1f,
                        LandResponse=.0012f, OceanResponse=.00028f, Seed=mixedSeed,
                        ClimateTickInterval=climateTicksPerStep
                    };
                    job.Schedule(a.Length,64).Complete();
                    NativeArray<PlanetCell> swap=a; a=b; b=swap;

                    int dayOfYear=(int)((tick/ticksPerDay)%math.max(1,daysPerYear));
                    for(int i=0;i<a.Length;i++)
                    {
                        PlanetCell c=a[i];
                        if(dayOfYear>=15 && dayOfYear<30)
                        {
                            if(c.Latitude>.35f) {northSummer+=c.Temperature;northCount++;}
                            if(c.Latitude<-.35f) {southInNorthSummer+=c.Temperature;southDuringNorthCount++;}
                        }
                        if(dayOfYear>=45 && dayOfYear<60)
                        {
                            if(c.Latitude<-.35f) {southSummer+=c.Temperature;southCount++;}
                            if(c.Latitude>.35f) {northInSouthSummer+=c.Temperature;northDuringSouthCount++;}
                        }
                        if(tick%20UL==0UL && eventCooldown[i]<=tick && ClimateMath.IsStormCandidate(c.Storminess,c.Humidity))
                        {
                            eventCooldown[i]=tick+(ulong)ticksPerDay/4;
                            eventCount++;
                        }
                        float absLat=math.abs(c.Latitude);
                        if(absLat<.15f) {equatorSum+=c.Temperature;equatorCount++;}
                        if(absLat>.8f) {poleSum+=c.Temperature;poleCount++;}
                    }
                }
                bool finite=true;
                StringBuilder csv=new StringBuilder(a.Length*110);
                csv.AppendLine("cell,latitude,longitude,elevation,temperature_c,pressure_relative,humidity,wind_x,wind_y,wind_z,cloud_cover,precipitation_mm_step,soil_moisture,snow_cover,ocean_temperature_c,current_x,current_y,current_z,storminess,nutrient,insolation,temperature_solar_term,temperature_advection_term,evaporation_term,orographic_lift,temperature_trend,humidity_trend,soil_moisture_trend");
                CultureInfo inv=CultureInfo.InvariantCulture;
                for(int i=0;i<a.Length;i++)
                {
                    PlanetCell c=a[i];
                    finite &= math.isfinite(c.Temperature)&&math.isfinite(c.Pressure)&&math.isfinite(c.Humidity)
                        &&math.all(math.isfinite(c.Wind))&&math.isfinite(c.CloudCover)&&math.isfinite(c.Precipitation)
                        &&math.isfinite(c.SoilMoisture)&&math.isfinite(c.SnowCover)&&math.isfinite(c.OceanTemperature)
                        &&math.all(math.isfinite(c.OceanCurrent))&&math.isfinite(c.Storminess)&&math.isfinite(c.Nutrient)
                        &&math.isfinite(c.DryTicks)&&math.isfinite(c.TemperatureAnomalyTicks)
                        &&math.isfinite(c.TemperatureInsolationTerm)&&math.isfinite(c.TemperatureAdvectionTerm)
                        &&math.isfinite(c.EvaporationTerm)&&math.isfinite(c.OrographicLiftTerm)
                        &&math.isfinite(c.TemperatureTrend)&&math.isfinite(c.HumidityTrend)&&math.isfinite(c.SoilMoistureTrend);
                    csv.Append(i).Append(',').Append(c.Latitude.ToString("R",inv)).Append(',').Append(c.Longitude.ToString("R",inv)).Append(',')
                        .Append(c.Elevation.ToString("R",inv)).Append(',').Append(c.Temperature.ToString("R",inv)).Append(',')
                        .Append(c.Pressure.ToString("R",inv)).Append(',').Append(c.Humidity.ToString("R",inv)).Append(',')
                        .Append(c.Wind.x.ToString("R",inv)).Append(',').Append(c.Wind.y.ToString("R",inv)).Append(',').Append(c.Wind.z.ToString("R",inv)).Append(',')
                        .Append(c.CloudCover.ToString("R",inv)).Append(',').Append(c.Precipitation.ToString("R",inv)).Append(',')
                        .Append(c.SoilMoisture.ToString("R",inv)).Append(',').Append(c.SnowCover.ToString("R",inv)).Append(',')
                        .Append(c.OceanTemperature.ToString("R",inv)).Append(',').Append(c.OceanCurrent.x.ToString("R",inv)).Append(',')
                        .Append(c.OceanCurrent.y.ToString("R",inv)).Append(',').Append(c.OceanCurrent.z.ToString("R",inv)).Append(',')
                        .Append(c.Storminess.ToString("R",inv)).Append(',').Append(c.Nutrient.ToString("R",inv)).Append(',')
                        .Append(c.Insolation.ToString("R",inv)).Append(',').Append(c.TemperatureInsolationTerm.ToString("R",inv)).Append(',')
                        .Append(c.TemperatureAdvectionTerm.ToString("R",inv)).Append(',').Append(c.EvaporationTerm.ToString("R",inv)).Append(',')
                        .Append(c.OrographicLiftTerm.ToString("R",inv)).Append(',').Append(c.TemperatureTrend.ToString("R",inv)).Append(',')
                        .Append(c.HumidityTrend.ToString("R",inv)).Append(',').Append(c.SoilMoistureTrend.ToString("R",inv)).AppendLine();
                }
                float contrast=0f;
                int contrasts=0;
                if(northCount>0 && southDuringNorthCount>0) {contrast+=(float)(northSummer/northCount-southInNorthSummer/southDuringNorthCount);contrasts++;}
                if(southCount>0 && northDuringSouthCount>0) {contrast+=(float)(southSummer/southCount-northInSouthSummer/northDuringSouthCount);contrasts++;}
                if(contrasts>0) contrast/=contrasts;
                return new ClimateRegressionResult
                {
                    Csv=csv.ToString(), EquatorMeanTemperature=equatorCount>0?(float)(equatorSum/equatorCount):0f,
                    PolarMeanTemperature=poleCount>0?(float)(poleSum/poleCount):0f,
                    SummerHemisphereContrast=contrast, StormEventCount=eventCount, AllFieldsFinite=finite
                };
            }
            finally {a.Dispose();b.Dispose();eventCooldown.Dispose();}
        }
    }
}
