// Ecosphere — stage 07: product layer bootstrap.
//
// One entry point (called by PlanetBootstrap once the world exists) wires the whole
// player-facing surface in a fixed order, each step guarded so a failure cannot take the
// world down with it:
//   settings → localization → paths → crash guard → snapshots/rewind → quality controller
//   → camera rig (replacing the developer controllers) → HUD → audio → photo mode.
//
// It also owns the single keyboard shortcut table, so the stage-01 set and the stage-07
// extensions live in exactly one place.

using System;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Persistence;
using Ecosphere.Presentation;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.UX
{
    [DisallowMultipleComponent]
    public sealed class ProductBootstrap : MonoBehaviour
    {
        public static ProductBootstrap Current { get; private set; }

        private ProductSettingsData _settings;
        private SnapshotService _snapshots;
        private RewindController _rewind;
        private QualityController _quality;
        private CinematicCameraRig _rig;
        private AudioDirector _audio;
        private PhotoModeController _photo;
        private ProductUi _ui;
        private EntityQuery _planetQuery;
        private bool _ready;
        private bool _loadTimerRunning;
        private double _loadStartTime;

        /// <summary>
        /// Creates the product layer once per session. The world bootstrap passes its overlay
        /// setter as a delegate, which keeps the UX assembly free of an Authoring reference.
        /// </summary>
        public static ProductBootstrap Ensure(System.Action<int> setOverlayMode = null)
        {
            if (Current != null) return Current;
            var go = new GameObject("ProductLayer");
            Current = go.AddComponent<ProductBootstrap>();
            Current.StartProductLayer(setOverlayMode);
            return Current;
        }

        public ProductSettingsData Settings => _settings;
        public RewindController Rewind => _rewind;
        public SnapshotService Snapshots => _snapshots;
        public CinematicCameraRig CameraRig => _rig;
        public ProductUi Ui => _ui;

        private void StartProductLayer(System.Action<int> setOverlayMode)
        {
            ProductSettingsData loaded = CrashGuard.Run(LoadSettings, "settings", ProductSettingsData.Default);
            _settings = loaded.Sanitized();
            SettingsBridge.Install(_settings);
            SettingsBridge.ApplyStartupLocale();
            _settings.Locale = Loc.Locale;

            ConfigurePaths();
            CrashGuard.Install(ProductPaths.CrashDirectory);

            _snapshots = CrashGuard.Run(() => SnapshotService.CreateDefault(Application.persistentDataPath,
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), "snapshots", null);
            if (_snapshots != null)
            {
                _snapshots.RingSize = _settings.RingSize;
                _rewind = new RewindController(_snapshots);
                _rewind.ConfigureAutosave(_settings.AutosaveIntervalDays, _settings.RingSize);
                _rewind.RewindFailed += reason => _ui?.PushToast(Loc.Format(LocKeys.SaveFailed, reason));
                _rewind.RewindCompleted += tick => _ui?.PushToast(Loc.Format(LocKeys.SaveChecksumOk, tick));
            }

            _quality = gameObject.AddComponent<QualityController>();
            _quality.Configure(ResolveWorldMaxOrganisms(), _settings.Aurora && !CrashGuard.ShouldSuppressHeavyFeatures,
                _settings.Quality);
            _quality.TierChanged += (tier, reason) =>
            {
                ProductSettingsData updated = _settings;
                updated.Quality = tier;
                _settings = updated;
                _ui?.PushToast(Loc.Get(LocKeys.SettingsQuality) + ": " + Loc.Quality(tier));
                _ = reason;
            };

            _ui = CrashGuard.Run(() => ProductUi.Create(_settings), "ui", null);
            if (_ui != null)
            {
                _ui.ScrubRequested += tick => _ui.ApplyScrubRequest(tick);
                SettingsBridge.Changed += OnSettingsChanged;
            }

            _rig = CrashGuard.Run(() => InstallCameraRig(), "camera", null);
            if (setOverlayMode != null) OverlayBridge.SetMode = setOverlayMode;
            InstallBridges();

            _audio = CrashGuard.Run(() => gameObject.AddComponent<AudioDirector>(), "audio", null);
            if (_audio != null) _audio.Configure(_settings);

            _photo = CrashGuard.Run(() => gameObject.AddComponent<PhotoModeController>(), "photo", null);
            CrashGuard.Run(() => gameObject.AddComponent<BeautyPass>(), "beauty-pass", enableSafeMode: false);

            ProductLog.Info(LogCategory.Time, SimLog.Codes.Boot, "stage-07 product layer up (" +
                BuildInfo.Editor.Describe() + ", " + Loc.Catalog?.Describe() + ")");
            _ready = true;
            _loadTimerRunning = true;
            _loadStartTime = Time.realtimeSinceStartupAsDouble;
        }

        private void ConfigurePaths()
        {
            ProductPaths.SaveDirectory = System.IO.Path.Combine(Application.persistentDataPath, "saves");
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures)) pictures = Application.persistentDataPath;
            ProductPaths.ExportDirectory = System.IO.Path.Combine(pictures, "Ecosphere");
            ProductPaths.PhotoDirectory = System.IO.Path.Combine(pictures, "Ecosphere");
            ProductPaths.StatsDirectory = System.IO.Path.Combine(Application.persistentDataPath, "stats");
            ProductPaths.CrashDirectory = System.IO.Path.Combine(Application.persistentDataPath, "crashes");
        }

        private static ProductSettingsData LoadSettings()
        {
            ProductSettings asset = Resources.Load<ProductSettings>("ProductSettings");
            if (asset != null) return asset.Data;
            return ProductSettings.DefaultData();
        }

        private int ResolveWorldMaxOrganisms()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return 10000;
            EntityManager em = world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<WorldSettingsData>());
            int max = query.IsEmpty ? 10000 : query.GetSingleton<WorldSettingsData>().MaxOrganisms;
            query.Dispose();
            return max;
        }

        /// <summary>Replaces the developer camera/HUD controllers with the stage-07 rig.</summary>
        private CinematicCameraRig InstallCameraRig()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            PlanetCameraController legacy = camera.GetComponent<PlanetCameraController>();
            if (legacy != null) legacy.enabled = false;
            foreach (SimHud hud in FindObjectsByType<SimHud>(FindObjectsSortMode.None)) hud.enabled = false;
            foreach (TimeControlInput input in FindObjectsByType<TimeControlInput>(FindObjectsSortMode.None)) input.enabled = false;

            CinematicCameraRig rig = camera.GetComponent<CinematicCameraRig>();
            if (rig == null) rig = camera.gameObject.AddComponent<CinematicCameraRig>();
            rig.Configure(camera);
            return rig;
        }

        /// <summary>Installs the delegates the panels use to move the camera.</summary>
        private void InstallBridges()
        {
            if (_rig != null)
            {
                OverlayBridge.FocusCell = cell => CameraFocusService.FocusCell(cell);
                OverlayBridge.FocusOrganism = (index, label) =>
                {
                    World world = World.DefaultGameObjectInjectionWorld;
                    if (world == null || !world.IsCreated) return;
                    Entity entity = world.EntityManager.GetEntityFromIndex(index);
                    if (entity != Entity.Null) CameraFocusService.FollowEntity(entity, label);
                };
            }
            if (_snapshots != null)
            {
                OverlayBridge.CaptureThumbnail = path => CaptureThumbnail(path);
            }
        }

        /// <summary>Renders the current camera view to a PNG (orbit thumbnail for the save panel).</summary>
        public bool CaptureThumbnail(string path)
        {
            try
            {
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                if (shot == null) return false;
                int width = 256;
                int height = Mathf.Max(1, shot.height * width / Mathf.Max(1, shot.width));
                RenderTexture target = RenderTexture.GetTemporary(width, height);
                Graphics.Blit(shot, target);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                scaled.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                System.IO.File.WriteAllBytes(path, scaled.EncodeToPNG());
                Destroy(scaled);
                Destroy(shot);
                return true;
            }
            catch (Exception exception)
            {
                ProductLog.Warn("thumbnail capture failed: " + exception.Message);
                return false;
            }
        }

        private void OnSettingsChanged(ProductSettingsData settings)
        {
            _settings = settings;
            Loc.SetLocale(settings.Locale);
            _quality?.SetTier(settings.Quality, "settings");
            _rewind?.ConfigureAutosave(settings.AutosaveIntervalDays, settings.RingSize);
            _audio?.Configure(settings);
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            _planetQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
        }

        private void OnDestroy()
        {
            SettingsBridge.Changed -= OnSettingsChanged;
            if (_ready) _planetQuery.Dispose();
            if (_audio != null) _audio.SetMuted(true);
            if (Current == this) Current = null;
        }

        private void Update()
        {
            if (!_ready) return;
            if (_loadTimerRunning)
            {
                _loadTimerRunning = false;
                _quality?.RecordWorldLoad(Time.realtimeSinceStartupAsDouble - _loadStartTime);
            }

            // Autosave + rewind run every frame; both are no-ops when idle.
            CrashGuard.Run(() => _rewind?.Pulse(World.DefaultGameObjectInjectionWorld, !_ui || !_ui.HideUi),
                "rewind-pulse", enableSafeMode: false);

            HandleShortcuts();
        }

        // ── Keyboard shortcuts ─────────────────────────────────────────────────────────

        private void HandleShortcuts()
        {
            if (_ui != null && (_ui.IsTextInputFocused || PhotoModeController.Active)) return;

            if (Input.GetKeyDown(KeyCode.Space)) _ui?.TogglePause();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _ui?.CycleTimeScale(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) _ui?.CycleTimeScale(1);
            if (Input.GetKeyDown(KeyCode.Comma)) _ui?.CycleTimeScale(-1);
            if (Input.GetKeyDown(KeyCode.Period)) _ui?.CycleTimeScale(1);

            if (Input.GetKeyDown(KeyCode.Alpha0)) _ui?.SetOverlay(OverlayMode.Biomes);
            for (int i = 1; i <= 9; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1)) && i <= OverlayRampMath.ModeCount)
                    _ui?.SetOverlay((OverlayMode)i);
            }
            if (Input.GetKeyDown(KeyCode.Minus)) _ui?.CycleOverlay(-1);
            if (Input.GetKeyDown(KeyCode.Equals)) _ui?.CycleOverlay(1);

            if (Input.GetKeyDown(KeyCode.I)) _ui?.TogglePanel(PanelKind.Inspector);
            if (Input.GetKeyDown(KeyCode.B)) _ui?.TogglePanel(PanelKind.Species);
            if (Input.GetKeyDown(KeyCode.T)) _ui?.TogglePanel(PanelKind.Evolution);
            if (Input.GetKeyDown(KeyCode.F)) _ui?.TogglePanel(PanelKind.Feed);
            if (Input.GetKeyDown(KeyCode.K)) _ui?.TogglePanel(PanelKind.Stats);
            if (Input.GetKeyDown(KeyCode.G)) _ui?.TogglePanel(PanelKind.God);
            if (Input.GetKeyDown(KeyCode.O)) _ui?.TogglePanel(PanelKind.Saves);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_ui != null && _ui.ActivePanel != PanelKind.None) _ui.ClosePanel();
                else _ui?.SetPanel(PanelKind.Settings);
            }

            if (Input.GetKeyDown(KeyCode.H)) _ui?.ToggleHideUi();
            if (Input.GetKeyDown(KeyCode.F3))
            {
                if (_ui != null)
                {
                    _ui.PerfChipVisible = !_ui.PerfChipVisible;
                    ProductSettingsData copy = _settings;
                    copy.ShowPerformanceChip = _ui.PerfChipVisible;
                    _settings = copy;
                }
            }
            if (Input.GetKeyDown(KeyCode.F1)) _ui?.SetPanel(PanelKind.Settings);

            if (Input.GetKeyDown(KeyCode.M)) ToggleMute();
            if (Input.GetKeyDown(KeyCode.P)) _photo?.Toggle();
            if (Input.GetKeyDown(KeyCode.F5)) QuickSave(0);
            if (Input.GetKeyDown(KeyCode.F9)) QuickLoad(0);
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                UndoLastGodAction();
            if (Input.GetKeyDown(KeyCode.Tab)) CameraFocusService.FocusOrbit();
        }

        public void ToggleMute()
        {
            ProductSettingsData copy = _settings;
            copy.Muted = !copy.Muted;
            SettingsBridge.Update(copy);
            _ui?.PushToast(copy.Muted ? Loc.Get(LocKeys.SettingsMuted) : Loc.Get(LocKeys.TimeResume));
        }

        public void QuickSave(int slot)
        {
            if (_rewind == null || World.DefaultGameObjectInjectionWorld == null) return;
            SnapshotIoResult result = _rewind.SaveToSlot(World.DefaultGameObjectInjectionWorld, slot, _settings.OnboardingSeen ? SaveNames.DefaultWorldName : Loc.Get(LocKeys.AppTitle));
            _ui?.PushToast(result.Success
                ? Loc.Format(LocKeys.SaveWritten, SaveNames.SlotLabel(slot))
                : Loc.Format(LocKeys.SaveFailed, result.Error));
        }

        public void QuickLoad(int slot)
        {
            if (_rewind == null || World.DefaultGameObjectInjectionWorld == null) return;
            SnapshotIoResult result = _rewind.LoadSlot(World.DefaultGameObjectInjectionWorld, slot);
            _ui?.PushToast(result.Success
                ? Loc.Format(LocKeys.SaveLoaded, SaveNames.SlotLabel(slot))
                : Loc.Format(LocKeys.SaveFailed, result.Error));
        }

        /// <summary>Undo = rewind to the newest ring snapshot (god tools are logged, saves are snapshots).</summary>
        public void UndoLastGodAction()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _rewind == null) return;
            EntityManager em = world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            if (query.IsEmpty)
            {
                query.Dispose();
                return;
            }
            GameTime time = query.GetSingleton<GameTime>();
            query.Dispose();
            _rewind.RequestRewind(time.TotalTicks);
            _ui?.PushToast(Loc.Get(LocKeys.SaveRewind));
        }
    }
}
