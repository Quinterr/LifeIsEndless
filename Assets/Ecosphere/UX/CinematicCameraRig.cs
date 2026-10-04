// Ecosphere — stage 07: cinematic camera + focus requests.
//
// Three framed modes plus free flight:
//   Orbit   — the observation view from space, with gentle idle drift.
//   Region  — low orbit over a clicked cell (weather/terrain study).
//   Follow  — locks a creature and keeps it framed (inspector "follow" button).
//   Free    — photo-mode flight, no framing.
// Transitions are eased (SmoothDamp on position, exponential blend on yaw/pitch/distance),
// so switching between orbit and a creature is readable rather than a cut. FOV widens with
// distance to sell the scale: a shift zoom, not a teleport.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Camera framing modes.</summary>
    public enum CameraMode : byte
    {
        Orbit = 0,
        Region = 1,
        Follow = 2,
        Free = 3,
    }

    /// <summary>
    /// Cross-component focus requests (event feed → camera, inspector → camera, god tools).
    /// One pending request is enough: the newest wins, which matches player expectation.
    /// </summary>
    public static class CameraFocusService
    {
        public static bool HasRequest { get; private set; }
        public static CameraMode RequestedMode { get; private set; }
        public static int RequestedCell { get; private set; } = -1;
        public static Entity RequestedEntity { get; private set; } = Entity.Null;
        public static string RequestLabel { get; private set; } = string.Empty;

        public static void FocusOrbit()
        {
            HasRequest = true;
            RequestedMode = CameraMode.Orbit;
            RequestedCell = -1;
            RequestedEntity = Entity.Null;
            RequestLabel = string.Empty;
        }

        public static void FocusCell(int cell, string label = null)
        {
            HasRequest = true;
            RequestedMode = CameraMode.Region;
            RequestedCell = cell;
            RequestedEntity = Entity.Null;
            RequestLabel = label ?? string.Empty;
        }

        public static void FollowEntity(Entity entity, string label = null)
        {
            HasRequest = true;
            RequestedMode = CameraMode.Follow;
            RequestedEntity = entity;
            RequestedCell = -1;
            RequestLabel = label ?? string.Empty;
        }

        /// <summary>Consumed by the rig; clears the request.</summary>
        public static void Consume(out CameraMode mode, out int cell, out Entity entity, out string label)
        {
            mode = RequestedMode;
            cell = RequestedCell;
            entity = RequestedEntity;
            label = RequestLabel;
            HasRequest = false;
            RequestedCell = -1;
            RequestedEntity = Entity.Null;
            RequestLabel = string.Empty;
        }
    }

    /// <summary>Orbit/region/follow camera with smooth transitions and planet picking.</summary>
    [DisallowMultipleComponent]
    public class CinematicCameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] private CameraMode _mode = CameraMode.Orbit;
        [SerializeField] private float _orbitDistanceFactor = 2.6f;
        [SerializeField] private float _regionDistanceFactor = 1.25f;
        [SerializeField] private float _followDistance = 26f;
        [SerializeField] private float _minFov = 32f;
        [SerializeField] private float _maxFov = 62f;

        [Header("Feel")]
        [SerializeField] private float _transitionSeconds = 0.85f;
        [SerializeField] private float _idleDriftDegreesPerSecond = 1.6f;
        [SerializeField] private float _idleDelaySeconds = 12f;
        [SerializeField] private bool _inputEnabled = true;

        private Camera _camera;
        private EntityQuery _planetQuery;
        private EntityQuery _organismQuery;
        private EntityQuery _timeQuery;
        private bool _ready;

        // Current (smoothed) framing state.
        private double _yaw = 0.7;
        private double _pitch = 0.35;
        private double _distance = 2600.0;
        private float _fov = 55f;
        private Vector3 _focus;
        private Vector3 _focusVelocity;

        // Target framing state.
        private double _targetYaw;
        private double _targetPitch;
        private double _targetDistance;
        private Vector3 _targetFocus;
        private CameraMode _targetMode = CameraMode.Orbit;

        private float _idleTimer;
        private float _lastInputTime;
        private Entity _followEntity = Entity.Null;
        private float _planetRadius = 1000f;
        private bool _dragging;
        private bool _freeFlight;

        public CameraMode Mode => _mode;
        public CameraMode TargetMode => _targetMode;
        public float PlanetRadius => _planetRadius;
        public string FocusLabel { get; private set; } = string.Empty;
        public Entity FollowedEntity => _followEntity;
        /// <summary>Cell under the cursor from the last click (-1 when none).</summary>
        public int LastPickedCell { get; private set; } = -1;

        /// <summary>Raised when the player clicks a planet cell (god tools + inspector use it).</summary>
        public event System.Action<int> CellPicked;

        /// <summary>Raised when the player clicks an organism (-1 = clicked empty planet).</summary>
        public event System.Action<Entity> OrganismPicked;

        public void Configure(Camera camera) => _camera = camera;

        /// <summary>Photo mode takes the rig: it keeps the state but stops reading input.</summary>
        public void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

        /// <summary>Freezes idle drift while a modal UI panel is open.</summary>
        public bool IdleDriftEnabled { get; set; } = true;

        private void Awake()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) _camera = FindFirstObjectByType<Camera>();
            if (_camera != null)
            {
                _camera.farClipPlane = 40000f;
                _targetDistance = _distance = 2600.0;
                _targetMode = _mode;
            }
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            _planetQuery = em.CreateEntityQuery(typeof(PlanetState));
            _organismQuery = em.CreateEntityQuery(ComponentType.ReadOnly<OrganismCell>(),
                ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
            _timeQuery = em.CreateEntityQuery(typeof(GameTime));
            _ready = true;
        }

        private void OnDestroy()
        {
            if (!_ready) return;
            _planetQuery.Dispose();
            _organismQuery.Dispose();
            _timeQuery.Dispose();
            _ready = false;
        }

        private void Update()
        {
            if (_camera == null) return;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            if (!_ready || _planetQuery.IsEmpty) return;

            Entity planetEntity = _planetQuery.GetSingletonEntity();
            PlanetState planet = em.GetComponentData<PlanetState>(planetEntity);
            _planetRadius = planet.Radius;

            if (CameraFocusService.HasRequest)
            {
                CameraFocusService.Consume(out CameraMode mode, out int cell, out Entity entity, out string label);
                ApplyFocus(em, planetEntity, mode, cell, entity, label);
            }

            HandleInput(em, planetEntity, planet);
            UpdateTransition(em, planetEntity, planet);

            if (_camera != null)
            {
                _camera.fieldOfView = _fov;
            }
        }

        // ── Focus requests ─────────────────────────────────────────────────────────────

        private void ApplyFocus(EntityManager em, Entity planetEntity, CameraMode mode, int cell, Entity entity, string label)
        {
            _targetMode = mode;
            FocusLabel = label ?? string.Empty;
            if (mode == CameraMode.Orbit)
            {
                _followEntity = Entity.Null;
                _targetDistance = _planetRadius * _orbitDistanceFactor;
                _targetFocus = Vector3.zero;
                return;
            }

            if (mode == CameraMode.Follow && em.Exists(entity))
            {
                _followEntity = entity;
                _targetDistance = Mathf.Max(8f, _followDistance * math.max(0.4f, OrganismScale(em, entity)));
                return;
            }

            if (cell >= 0 && em.HasBuffer<PlanetCell>(planetEntity))
            {
                DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planetEntity);
                if (cell < cells.Length)
                {
                    float3 direction = CellDirection(em, planetEntity, cell);
                    _followEntity = Entity.Null;
                    _targetFocus = (Vector3)direction * SurfaceRadius(cells[cell]);
                    _targetDistance = _planetRadius * _regionDistanceFactor;
                    OrientToward(_targetFocus);
                }
            }
        }

        private static float OrganismScale(EntityManager em, Entity entity)
        {
            if (em.HasComponent<OrganismSize>(entity)) return math.max(0.2f, em.GetComponentData<OrganismSize>(entity).Value);
            return 1f;
        }

        private float SurfaceRadius(in PlanetCell cell) =>
            _planetRadius * (1f + math.max(0f, cell.Elevation) * 0.035f);

        private static float3 CellDirection(EntityManager em, Entity planetEntity, int cell)
        {
            PlanetState state = em.GetComponentData<PlanetState>(planetEntity);
            if (!state.Topology.IsCreated) return new float3(0f, 1f, 0f);
            return state.Topology.Value.Centers[cell];
        }

        private void OrientToward(Vector3 focus)
        {
            Vector3 fromCenter = focus.sqrMagnitude > 1e-4f ? focus.normalized : Vector3.up;
            _targetPitch = Mathf.Asin(Mathf.Clamp(fromCenter.y, -1f, 1f));
            _targetYaw = Mathf.Atan2(fromCenter.z, fromCenter.x);
        }

        // ── Input ──────────────────────────────────────────────────────────────────────

        private void HandleInput(EntityManager em, Entity planetEntity, in PlanetState planet)
        {
            if (!_inputEnabled)
            {
                _dragging = false;
                return;
            }

            // UI Toolkit consumes clicks on panels; never let a button press also re-target
            // the camera or trigger a god-tool pick behind the panel.
            if (ProductUi.IsPointerOverUi)
            {
                _dragging = false;
                return;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                float factor = Mathf.Exp(-scroll * 0.16f);
                _targetDistance = System.Math.Clamp(_targetDistance * factor,
                    _planetRadius * 0.02, _planetRadius * 8.0);
                _lastInputTime = Time.unscaledTime;
            }

            bool rotateHeld = Input.GetMouseButton(1) || (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftAlt));
            if (rotateHeld)
            {
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                if (Mathf.Abs(mouseX) > 0.0001f || Mathf.Abs(mouseY) > 0.0001f)
                {
                    _targetYaw += mouseX * 0.06;
                    _targetPitch = Mathf.Clamp((float)(_targetPitch - mouseY * 0.06f), -1.5f, 1.5f);
                    _lastInputTime = Time.unscaledTime;
                }
                _dragging = true;
            }
            else
            {
                _dragging = false;
            }

            // Keyboard: WASD pans the focus tangentially in region/free mode, Q/E rotate.
            float pan = Input.GetAxis("Horizontal");
            float panZ = Input.GetAxis("Vertical");
            if (Mathf.Abs(pan) > 0.01f || Mathf.Abs(panZ) > 0.01f)
            {
                Vector3 right = transform.right;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _focus.sqrMagnitude > 1e-4f ? _focus.normalized : Vector3.up).normalized;
                float speed = (float)_targetDistance * 0.35f * Time.unscaledDeltaTime;
                _targetFocus += (right * pan + forward * panZ) * speed;
                _targetFocus = _targetFocus.normalized * _planetRadius;
                _lastInputTime = Time.unscaledTime;
            }

            if (Input.GetKey(KeyCode.Q)) { _targetYaw -= 0.6 * Time.unscaledDeltaTime; _lastInputTime = Time.unscaledTime; }
            if (Input.GetKey(KeyCode.E)) { _targetYaw += 0.6 * Time.unscaledDeltaTime; _lastInputTime = Time.unscaledTime; }

            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.LeftControl))
            {
                Pick(em, planetEntity, planet);
            }

            if (Input.GetKeyDown(KeyCode.Tab)) CameraFocusService.FocusOrbit();
        }

        /// <summary>Raycasts the planet sphere, resolves the nearest cell and the organism in it.</summary>
        private void Pick(EntityManager em, Entity planetEntity, in PlanetState planet)
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            Vector3 origin = ray.origin;
            Vector3 direction = ray.direction;
            float b = Vector3.Dot(origin, direction);
            float c = origin.sqrMagnitude - _planetRadius * _planetRadius;
            float discriminant = b * b - c;
            if (discriminant < 0f) return;

            float t = -b - Mathf.Sqrt(discriminant);
            if (t <= 0f) return;
            Vector3 hit = origin + direction * t;
            if (!planet.Topology.IsCreated) return;

            BlobAssetReference<PlanetTopologyBlob> topology = planet.Topology;
            int cell = Icosphere.Nearest(ref topology.Value, (float3)hit);
            LastPickedCell = cell;
            CellPicked?.Invoke(cell);

            Entity organism = FindOrganismInCell(em, cell);
            OrganismPicked?.Invoke(organism);
        }

        private Entity FindOrganismInCell(EntityManager em, int cell)
        {
            if (_organismQuery.IsEmpty) return Entity.Null;
            using NativeArray<Entity> entities = _organismQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<OrganismCell> cells = _organismQuery.ToComponentDataArray<OrganismCell>(Allocator.Temp);
            float3 cameraPosition = _camera.transform.position;
            Entity best = Entity.Null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < entities.Length; i++)
            {
                if (cells[i].CellIndex != cell) continue;
                float distance = em.HasComponent<OrganismSize>(entities[i])
                    ? 0f
                    : 0f;
                distance = math.lengthsq((float3)Vector3.zero);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = entities[i];
                }
            }
            _ = cameraPosition;
            return best;
        }

        // ── Framing ────────────────────────────────────────────────────────────────────

        private void UpdateTransition(EntityManager em, Entity planetEntity, in PlanetState planet)
        {
            if (_targetMode == CameraMode.Follow && em.Exists(_followEntity))
            {
                Vector3 position = OrganismPosition(em, planetEntity, _followEntity);
                _targetFocus = position;
                SurfaceFrame(position);
                FocusLabel = string.Empty;
                if (_mode != CameraMode.Follow)
                {
                    _targetDistance = Mathf.Max(6f, _followDistance * OrganismScale(em, _followEntity));
                }
            }

            float dt = Time.unscaledDeltaTime;
            float blend = _transitionSeconds <= 0.01f ? 1f : Mathf.Clamp01(dt / _transitionSeconds * 1.2f);
            _yaw += ShortestAngleDelta(_yaw, _targetYaw) * blend;
            _pitch = Mathf.Lerp((float)_pitch, (float)_targetPitch, blend);
            _distance = System.Math.Exp(System.Math.Log(_distance) + (System.Math.Log(_targetDistance) - System.Math.Log(System.Math.Max(1.0, _distance))) * blend);
            _focus = Vector3.SmoothDamp(_focus, _targetFocus, ref _focusVelocity, _transitionSeconds * 0.6f, Mathf.Infinity, dt);

            bool settled = Mathf.Abs((float)(_yaw - _targetYaw)) < 0.01f && Mathf.Abs((float)(_pitch - _targetPitch)) < 0.01f;
            if (settled) _mode = _targetMode;

            // Idle drift: very slow orbital rotation when the player has not touched anything.
            if (_mode == CameraMode.Orbit && IdleDriftEnabled && !_dragging && Time.unscaledTime - _lastInputTime > _idleDelaySeconds)
            {
                _targetYaw += _idleDriftDegreesPerSecond * Mathf.Deg2Rad * dt;
            }

            Quaternion rotation = Quaternion.Euler((float)(_pitch * Mathf.Rad2Deg), (float)(_yaw * Mathf.Rad2Deg), 0f);
            Vector3 direction = rotation * Vector3.back;
            Vector3 positionTarget = _focus + direction * (float)_distance;
            if (_focus.sqrMagnitude < 1e-4f)
            {
                positionTarget = direction * (float)_distance;
            }
            transform.position = positionTarget;
            transform.LookAt(_focus);
            if (_camera != null)
            {
                _camera.nearClipPlane = _distance < _planetRadius * 1.4 ? 0.05f : 1f;
                _camera.fieldOfView = Mathf.Lerp(_minFov, _maxFov,
                    Mathf.InverseLerp((float)(_planetRadius * 0.05), (float)(_planetRadius * 3.0), (float)_distance));
            }
        }

        /// <summary>Keeps the camera above the local surface while following a creature.</summary>
        private void SurfaceFrame(Vector3 position)
        {
            Vector3 normal = position.sqrMagnitude > 1e-4f ? position.normalized : Vector3.up;
            float altitude = position.magnitude;
            _targetFocus = position;
            if (altitude < _planetRadius) _targetFocus = normal * _planetRadius;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).sqrMagnitude > 1e-4f
                ? Vector3.Cross(normal, Vector3.up).normalized
                : Vector3.right;
            _ = tangent;
        }

        private Vector3 OrganismPosition(EntityManager em, Entity planetEntity, Entity entity)
        {
            if (em.HasComponent<LocomotionData>(entity))
            {
                float3 position = em.GetComponentData<LocomotionData>(entity).Position;
                if (math.lengthsq(position) > 1e-6f) return position;
            }
            if (em.HasComponent<OrganismCell>(entity))
            {
                int cell = em.GetComponentData<OrganismCell>(entity).CellIndex;
                return (Vector3)CellDirection(em, planetEntity, cell) * _planetRadius;
            }
            return _focus;
        }

        private static float ShortestAngleDelta(double from, double to)
        {
            double difference = (to - from) % (System.Math.PI * 2.0);
            if (difference > System.Math.PI) difference -= System.Math.PI * 2.0;
            if (difference < -System.Math.PI) difference += System.Math.PI * 2.0;
            return (float)difference;
        }

        /// <summary>Snap framing to the target (used by fast-forward/load transitions).</summary>
        public void SnapToTarget()
        {
            _yaw = _targetYaw;
            _pitch = _targetPitch;
            _distance = _targetDistance;
            _focus = _targetFocus;
            _mode = _targetMode;
        }

        /// <summary>Free-flight mode for photo mode: the transform is driven externally.</summary>
        public void SetFreeFlight(bool enabled)
        {
            _freeFlight = enabled;
            if (enabled) _mode = CameraMode.Free;
            else _targetMode = CameraMode.Orbit;
        }

        public bool FreeFlight => _freeFlight;
        public float Fov => _fov;
        public void SetFov(float fov) => _fov = Mathf.Clamp(fov, 12f, 100f);
        public double Yaw => _yaw;
        public double Pitch => _pitch;
        public double Distance => _distance;
        public Vector3 FocusPoint => _focus;
    }
}
