using Ecosphere.Planet;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Presentation
{
    [RequireComponent(typeof(Camera))]
    public class PlanetCameraController : MonoBehaviour
    {
        public int SelectedCell { get; private set; } = -1;
        public bool SurfaceMode => distance < 1100;
        private double distance=3200;
        private double yaw=.5, pitch=.3;
        private Vector3 focus=Vector3.zero;
        private Camera cam;
        private EntityQuery planetQuery;
        private bool queryReady;
        private void Awake() { cam=GetComponent<Camera>(); cam.farClipPlane=10000; }
        private void Start()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world!=null && world.IsCreated) {planetQuery=world.EntityManager.CreateEntityQuery(typeof(PlanetState));queryReady=true;}
        }
        private void Update()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null || !world.IsCreated) return;
            var em=world.EntityManager;
            {
                if(!queryReady || planetQuery.IsEmpty) return;
                var planet=em.GetComponentData<PlanetState>(planetQuery.GetSingletonEntity());
                double scroll=Input.mouseScrollDelta.y;
                distance=System.Math.Clamp(distance*System.Math.Exp(-scroll*.16),planet.Radius+2,planet.Radius*6);
                if(Input.GetMouseButton(1))
                {
                    yaw+=Input.GetAxis("Mouse X")*.005; pitch=Mathf.Clamp((float)(pitch-Input.GetAxis("Mouse Y")*.005),-1.5f,1.5f);
                }
                var rotation=Quaternion.Euler((float)(pitch*Mathf.Rad2Deg),(float)(yaw*Mathf.Rad2Deg),0);
                if(SurfaceMode)
                {
                    Vector3 right=rotation*Vector3.right, forward=rotation*Vector3.forward;
                    focus+=(right*Input.GetAxis("Horizontal")+forward*Input.GetAxis("Vertical"))*Time.deltaTime*30;
                    focus=focus.normalized*planet.Radius;
                }
                Vector3 direction=rotation*Vector3.back;
                transform.position=focus+direction*(float)distance;
                transform.LookAt(focus);
                cam.nearClipPlane=SurfaceMode?.05f:1f;
                if(Input.GetMouseButtonDown(0))
                {
                    Ray ray=cam.ScreenPointToRay(Input.mousePosition);
                    Vector3 origin=ray.origin, d=ray.direction;
                    float b=Vector3.Dot(origin,d), c=origin.sqrMagnitude-planet.Radius*planet.Radius;
                    float discriminant=b*b-c;
                    if(discriminant>=0)
                    {
                        float t=-b-Mathf.Sqrt(discriminant);
                        if(t>0) { var topology=planet.Topology; SelectedCell=Icosphere.Nearest(ref topology.Value,(float3)(origin+d*t)); }
                    }
                }
            }
        }
        private void OnGUI()
        {
            GUI.Label(new Rect(12,100,430,30),"Camera: "+(SurfaceMode?"Surface":"Orbit")+"  Cell: "+SelectedCell+"   Climate overlays: 1-8 (0 terrain)");
            if(SelectedCell<0 || !queryReady || planetQuery.IsEmpty) return;
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null || !world.IsCreated) return;
            var em=world.EntityManager;
            Entity entity=planetQuery.GetSingletonEntity();
            if(!em.HasBuffer<PlanetCell>(entity) || !em.HasBuffer<WeatherEvent>(entity)) return;
            var cells=em.GetBuffer<PlanetCell>(entity);
            var events=em.GetBuffer<WeatherEvent>(entity);
            if(SelectedCell>=cells.Length) return;
            var sampler=new ClimateSampler(em.GetComponentData<PlanetState>(entity),cells,events);
            ClimateSample sample=sampler.Sample(SelectedCell);
            Rect panel=new Rect(Screen.width-330,12,318,252);
            GUI.Box(panel,"Weather station • cell "+SelectedCell);
            GUI.Label(new Rect(panel.x+12,panel.y+30,295,20),string.Format("Air {0:0.0}°C (effective {1:0.0}°C)   P {2:0.000}",sample.Temperature,sample.EffectiveTemperature,sample.Pressure));
            GUI.Label(new Rect(panel.x+12,panel.y+52,295,20),string.Format("Wind {0:0.0} m/s   humidity {1:P0}   clouds {2:P0}",sample.WindStrength,sample.Humidity,sample.CloudCover));
            GUI.Label(new Rect(panel.x+12,panel.y+74,295,20),string.Format("Rain {0:0.00} mm/tick   soil {1:P0}   snow {2:P0}",sample.Precipitation,sample.SoilMoisture,sample.SnowCover));
            GUI.Label(new Rect(panel.x+12,panel.y+96,295,20),string.Format("Ocean {0:0.0}°C   current {1:0.00}   storm {2:P0}",sample.OceanTemperature,math.length(sample.OceanCurrent),sample.Storminess));
            GUI.Label(new Rect(panel.x+12,panel.y+124,295,20),string.Format("Sun {0:P0} → solar {1:+0.00;-0.00}°  adv {2:+0.00;-0.00}°",sample.Insolation,sample.TemperatureInsolationTerm,sample.TemperatureAdvectionTerm));
            GUI.Label(new Rect(panel.x+12,panel.y+146,295,20),string.Format("Moisture: evap {0:0.000}  orographic lift {1:P0}",sample.EvaporationTerm,sample.OrographicLiftTerm));
            string eventText="Clear";
            for(int i=0;i<events.Length;i++) if(events[i].Cell==SelectedCell) {eventText=events[i].Type.ToString()+" • "+events[i].Strength.ToString("P0");break;}
            GUI.Label(new Rect(panel.x+12,panel.y+172,295,20),"Local event: "+eventText);
            GUI.Label(new Rect(panel.x+12,panel.y+202,295,36),string.Format("Trend   T {0}   humidity {1}   soil {2}",TrendArrow(sample.TemperatureTrend),TrendArrow(sample.HumidityTrend),TrendArrow(sample.SoilMoistureTrend)));
        }

        private static string TrendArrow(float value) => value > .0001f ? "↑" : value < -.0001f ? "↓" : "→";

        private void OnDestroy()
        {
            if(queryReady) {planetQuery.Dispose();queryReady=false;}
        }
    }
}
