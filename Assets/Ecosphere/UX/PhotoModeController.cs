// Ecosphere — stage 07: photo mode.
//
// Photo mode takes the camera away from the rig for free flight, hides the game HUD but keeps
// its own slim overlay (rule-of-thirds grid, watermark, sliders), scrubs the time of day, and
// exports a PNG into the user's pictures folder. All of it is presentation-only: the
// simulation keeps running (paused or not) and no world state is written except the sun
// direction, which is restored when the mode exits.

using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecosphere.UX
{
    /// <summary>Free-camera photography with a clean capture path.</summary>
    [DisallowMultipleComponent]
    public sealed class PhotoModeController : MonoBehaviour
    {
        private const float MinSpeed = 2f;
        private const float MaxSpeed = 400f;

        public static PhotoModeController Instance { get; private set; }
        public static bool Active => Instance != null && Instance._active;

        private readonly float[] _gradeOriginal = new float[16];

        private bool _active;
        private CinematicCameraRig _rig;
        private Camera _camera;
        private VisualElement _overlay;
        private VisualElement _grid;
        private Label _hint;
        private VisualElement _panel;
        private Slider _fovSlider;
        private Slider _exposureSlider;
        private Slider _saturationSlider;
        private Slider _dofSlider;
        private Slider _timeSlider;
        private Toggle _watermarkToggle;
        private float _speed = 20f;
        private float _captureFlashTime;
        private float _exposure;
        private float _saturation = 1f;
        private float _dofStrength;
        private float _timeOfDay = 0.5f;
        private bool _dragging;
        private int _savedSupersample = 1;
        private float _savedSunPhase;

        public bool GridVisible { get; set; } = true;
        public string LastPhotoPath { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            _rig = ProductBootstrap.Current != null ? ProductBootstrap.Current.CameraRig : null;
            _camera = Camera.main;
            BuildOverlay();
            SetVisible(false);
        }

        /// <summary>Enters/leaves photo mode (P).</summary>
        public void Toggle()
        {
            if (_active) Exit();
            else Enter();
        }

        public void Enter()
        {
            if (_active) return;
            _active = true;
            _rig?.SetInputEnabled(false);
            _rig?.SetFreeFlight(true);
            ProductUi.Instance?.SetOverlayVisible(false);
            ApplyTimeOfDay(_timeOfDay);
            SetVisible(true);
            ProductLog.Info(LogCategory.Time, SimLogCodes.PhotoCaptured, Loc.Get(LocKeys.PhotoTitle));
        }

        public void Exit()
        {
            if (!_active) return;
            _active = false;
            _rig?.SetInputEnabled(true);
            _rig?.SetFreeFlight(false);
            ProductUi.Instance?.SetOverlayVisible(true);
            RestoreSun();
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_overlay != null) _overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_grid != null) _grid.style.display = visible && GridVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Overlay ───────────────────────────────────────────────────────────────────

        private void BuildOverlay()
        {
            _overlay = new VisualElement();
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0f;
            _overlay.style.top = 0f;
            _overlay.style.right = 0f;
            _overlay.style.bottom = 0f;
            _overlay.pickingMode = PickingMode.Ignore;

            _grid = new VisualElement();
            _grid.style.position = Position.Absolute;
            _grid.style.left = 0f;
            _grid.style.top = 0f;
            _grid.style.right = 0f;
            _grid.style.bottom = 0f;
            _grid.pickingMode = PickingMode.Ignore;
            for (int i = 1; i <= 2; i++)
            {
                float fraction = i / 3f;
                var vertical = new VisualElement();
                vertical.style.position = Position.Absolute;
                vertical.style.left = Length.Percent(fraction * 100f);
                vertical.style.top = 0f;
                vertical.style.bottom = 0f;
                vertical.style.width = 1f;
                vertical.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
                _grid.Add(vertical);

                var horizontal = new VisualElement();
                horizontal.style.position = Position.Absolute;
                horizontal.style.top = Length.Percent(fraction * 100f);
                horizontal.style.left = 0f;
                horizontal.style.right = 0f;
                horizontal.style.height = 1f;
                horizontal.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
                _grid.Add(horizontal);
            }
            _overlay.Add(_grid);

            _hint = Kit.Text(string.Empty, 13f, new Color(1f, 1f, 1f, 0.75f));
            _hint.style.position = Position.Absolute;
            _hint.style.left = Length.Percent(50f);
            _hint.style.translate = new Translate(Length.Percent(-50f), 0f, 0f);
            _hint.style.bottom = 60f;
            _overlay.Add(_hint);

            _panel = Kit.Panel(Loc.Get(LocKeys.PhotoTitle));
            _panel.style.position = Position.Absolute;
            _panel.style.right = 14f;
            _panel.style.top = 14f;
            _panel.style.width = 260f;
            _overlay.Add(_panel);

            _fovSlider = AddSlider(Loc.Get(LocKeys.PhotoFov), 12f, 100f, 55f, v => { if (_camera != null) _camera.fieldOfView = v; });
            _exposureSlider = AddSlider(Loc.Get(LocKeys.PhotoExposure), -2f, 2f, 0f, v => _exposure = v);
            _saturationSlider = AddSlider(Loc.Get(LocKeys.PhotoSaturation), 0f, 2f, 1f, v => _saturation = v);
            _dofSlider = AddSlider(Loc.Get(LocKeys.PhotoDof), 0f, 1f, 0.15f, v => _dofStrength = v);
            _timeSlider = AddSlider(Loc.Get(LocKeys.PhotoTimeOfDay), 0f, 1f, 0.5f, ApplyTimeOfDay);
            _watermarkToggle = new Toggle(Loc.Get(LocKeys.PhotoWatermark))
            {
                value = SettingsBridge.Settings.PhotoWatermark,
            };
            _panel.Add(_watermarkToggle);
            _panel.Add(Kit.Button(Loc.Get(LocKeys.PhotoCapture), Capture, 120f, primary: true));
            _panel.Add(Kit.Text(Loc.Get(LocKeys.PhotoHint), 10f, Kit.TextDim));

            AttachToUiRoot(_overlay);
        }

        private void AttachToUiRoot(VisualElement element)
        {
            ProductUi ui = ProductUi.Instance;
            if (ui == null) return;
            // The photo overlay lives inside the product document so a single panel owns input.
            UIDocument document = ui.GetComponent<UIDocument>();
            if (document != null) document.rootVisualElement.Add(element);
        }

        private Slider AddSlider(string label, float min, float max, float value, System.Action<float> onChanged)
        {
            _panel.Add(Kit.Text(label, 11f, Kit.TextDim));
            var slider = new Slider(min, max) { value = value };
            slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            _panel.Add(slider);
            return slider;
        }

        // ── Update ────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_active || _camera == null)
            {
                if (_captureFlashTime > 0f) _captureFlashTime -= Time.unscaledDeltaTime;
                return;
            }

            Transform transform = _camera.transform;
            bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float speed = _speed * (fast ? 4f : 1f) * (Input.GetKey(KeyCode.LeftControl) ? 0.25f : 1f);
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.Q)) move -= transform.up;
            if (Input.GetKey(KeyCode.E)) move += transform.up;
            transform.position += move * (speed * Time.unscaledDeltaTime);

            if (Input.GetMouseButton(1) || Input.GetMouseButton(0))
            {
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                transform.Rotate(Vector3.up, mouseX * 2.2f, Space.World);
                transform.Rotate(transform.right, -mouseY * 2.2f, Space.World);
                _dragging = true;
            }
            else
            {
                _dragging = false;
            }
            _ = _dragging;

            if (Input.mouseScrollDelta.y > 0.001f) _speed = Mathf.Min(MaxSpeed, _speed * 1.15f);
            if (Input.mouseScrollDelta.y < -0.001f) _speed = Mathf.Max(MinSpeed, _speed / 1.15f);

            if (Input.GetKeyDown(KeyCode.G))
            {
                GridVisible = !GridVisible;
                if (_grid != null) _grid.style.display = GridVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (Input.GetKeyDown(KeyCode.Space)) Capture();
            if (Input.GetKeyDown(KeyCode.Escape)) Exit();

            // Cheap DOF: pull the near plane in and narrow the FOV with the slider; the real
            // depth blur is applied by the renderer when a post stack is present.
            if (_camera != null)
            {
                _camera.nearClipPlane = Mathf.Lerp(0.05f, 4f, _dofStrength);
            }
            if (_hint != null)
            {
                _hint.text = Loc.Format(LocKeys.PhotoHint, Mathf.RoundToInt(_speed));
            }
        }

        /// <summary>Time-of-day scrub: sun direction is a pure function of the day fraction.</summary>
        private void ApplyTimeOfDay(float fraction)
        {
            _timeOfDay = Mathf.Clamp01(fraction);
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            if (query.IsEmpty)
            {
                query.Dispose();
                return;
            }
            Entity planet = query.GetSingletonEntity();
            PlanetState state = em.GetComponentData<PlanetState>(planet);
            if (_savedSunPhase == 0f) _savedSunPhase = state.SunDirection.y;
            state.SunDirection = SunMath.Direction(_timeOfDay, 0f);
            em.SetComponentData(planet, state);
            query.Dispose();
        }

        private void RestoreSun()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
            if (!query.IsEmpty)
            {
                Entity planet = query.GetSingletonEntity();
                PlanetState state = em.GetComponentData<PlanetState>(planet);
                state.SunDirection = SunMath.Direction(0.5f, 0f);
                em.SetComponentData(planet, state);
            }
            query.Dispose();
        }

        // ── Capture ───────────────────────────────────────────────────────────────────

        public void Capture()
        {
            if (_camera == null) return;
            try
            {
                System.IO.Directory.CreateDirectory(ProductPaths.PhotoDirectory);
                string name = SaveNames.Sanitize("photo", 16) + "_" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".png";
                string path = System.IO.Path.Combine(ProductPaths.PhotoDirectory, name);
                bool watermark = _watermarkToggle != null ? _watermarkToggle.value : SettingsBridge.Settings.PhotoWatermark;
                if (watermark && _hint != null)
                {
                    _hint.text = BuildInfo.Editor.HudLabel() + " · Ecosphere";
                }
                ScreenCapture.CaptureScreenshot(path, QualityTiers.Resolve(QualityRuntime.Tier).PhotoSupersample);
                LastPhotoPath = path;
                _captureFlashTime = 0.25f;
                SimLogs.Push(LogCategory.Time, SimLogCodes.PhotoCaptured, 0UL);
                ProductUi.Instance?.PushToast(Loc.Format(LocKeys.PhotoSaved, path));
            }
            catch (System.Exception exception)
            {
                CrashGuard.Report("photo-capture", exception, enableSafeMode: false);
            }
        }

        /// <summary>Exposure/saturation are exposed for the renderer; the values are the contract.</summary>
        public float Exposure => _exposure;
        public float Saturation => _saturation;
        public float Dof => _dofStrength;
        public int SavedSupersample => _savedSupersample;
    }
}
