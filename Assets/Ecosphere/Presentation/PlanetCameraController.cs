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
        private void Awake() { cam=GetComponent<Camera>(); cam.farClipPlane=10000; }
        private void Update()
        {
            var world=World.DefaultGameObjectInjectionWorld;
            if(world==null || !world.IsCreated) return;
            var em=world.EntityManager;
            using(var query=em.CreateEntityQuery(typeof(PlanetState)))
            {
                if(query.IsEmpty) return;
                var planet=em.GetComponentData<PlanetState>(query.GetSingletonEntity());
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
            GUI.Label(new Rect(12,100,300,30),"Camera: "+(SurfaceMode?"Surface":"Orbit")+"  Cell: "+SelectedCell);
        }
    }
}
