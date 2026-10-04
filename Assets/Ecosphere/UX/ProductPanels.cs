// Ecosphere — stage 07: god tools, saves/rewind and settings panels.
//
// God tools never touch simulation state here: the panel is a request builder. It reads the
// descriptor table (GodTools.All) to decide which sliders to show, stamps a request id from
// GodToolState and appends a GodToolRequest to the planet buffer. GodToolSystem applies it on
// the next tick, which is what makes paused-mode editing and undo (snapshot rewind) work.

using System;
using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Life;
using Ecosphere.Planet;
using Ecosphere.Persistence;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecosphere.UX
{
    // ── God tools ───────────────────────────────────────────────────────────────────────

    internal sealed class GodPanel
    {
        private readonly ProductUi _ui;
        private readonly VisualElement _body = Kit.Column();
        private GodToolKind _selected = GodToolKind.SeedLife;
        private float _amount = 0.6f;
        private float _radius = 0.06f;
        private int _count = 64;
        private byte _weatherType = (byte)WeatherEventType.Storm;
        private byte _kingdom = 0; // GeneKingdom bits: 0 = plants, 1 = animals
        private int _targetCell = -1;

        public VisualElement Root { get; }

        public GodPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelGod));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 500);
        }

        private void Rebuild()
        {
            _body.Clear();
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet))
            {
                return;
            }

            _body.Add(Kit.Text(Loc.Get(LocKeys.GodTargetHint) + " (cell " + (_targetCell >= 0 ? _targetCell.ToString() : "—") + ")",
                11f, Kit.TextDim));
            if (_targetCell < 0 && CameraFocusService.HasRequest == false)
            {
                CinematicCameraRig rig = ProductBootstrap.Current != null ? ProductBootstrap.Current.CameraRig : null;
                if (rig != null && rig.LastPickedCell >= 0) _targetCell = rig.LastPickedCell;
            }

            for (int i = 0; i < GodTools.All.Length; i++)
            {
                GodToolDescriptor descriptor = GodTools.All[i];
                bool selected = descriptor.Kind == _selected;
                Button button = Kit.Button(Loc.Get(descriptor.NameKey), () => SelectTool(descriptor.Kind), 0f, selected);
                button.style.fontSize = 11f;
                _body.Add(button);
                if (!selected) continue;

                _body.Add(Kit.Text(Loc.Get(descriptor.HelpKey), 11f, Kit.TextDim));
                if (descriptor.HasAmount) AddSlider(Loc.Get(LocKeys.GodAmount), _amount, 0f, 1f, v => _amount = v);
                if (descriptor.HasRadius) AddSlider(Loc.Get(LocKeys.GodRadius), _radius, 0.01f, 0.35f, v => _radius = v);
                if (descriptor.HasCount) AddSlider(Loc.Get(LocKeys.HudPopulation), _count, 8f, 512f, v => _count = Mathf.RoundToInt(v));
                if (descriptor.HasWeatherType) AddWeatherPicker();
                if (descriptor.Kind == GodToolKind.SeedLife) AddKingdomPicker();
                if (descriptor.Kind == GodToolKind.TimeJump) AddTimeJumpButtons();
                if (descriptor.NeedsTarget)
                {
                    _body.Add(Kit.Text(_targetCell >= 0
                        ? Loc.Get(LocKeys.GodTargetHint) + " " + _targetCell
                        : Loc.Get(LocKeys.GodCancelTarget), 11f, _targetCell >= 0 ? Kit.TextDim : Kit.AccentWarn));
                }
                Button apply = Kit.Button(Loc.Get(LocKeys.Apply), () => Apply(em, planet, descriptor), 110f, primary: true);
                apply.style.marginTop = 6f;
                _body.Add(apply);
            }

            _body.Add(Kit.Divider());
            _body.Add(Kit.Button(Loc.Get(LocKeys.GodUndo), () => ProductBootstrap.Current?.UndoLastGodAction(), 160f));
            _body.Add(Kit.Text(Loc.Get(LocKeys.SaveRewindHelp), 11f, Kit.TextDim));
        }

        private void SelectTool(GodToolKind kind)
        {
            _selected = kind;
            if (kind == GodToolKind.SeedLife && _count <= 0) _count = 64;
            Rebuild();
        }

        private void AddSlider(string label, float value, float min, float max, Action<float> onChanged)
        {
            VisualElement row = Kit.Row();
            Label name = Kit.Text(label, 11f, Kit.TextDim);
            name.style.width = 90f;
            row.Add(name);
            var slider = new Slider(min, max) { value = value };
            slider.style.flexGrow = 1f;
            Label amount = Kit.Text(value.ToString("0.00"), 11f);
            amount.style.width = 44f;
            slider.RegisterValueChangedCallback(evt =>
            {
                onChanged(evt.newValue);
                amount.text = evt.newValue.ToString("0.00");
            });
            row.Add(slider);
            row.Add(amount);
            _body.Add(row);
        }

        private void AddWeatherPicker()
        {
            VisualElement row = Kit.Row();
            for (int i = 0; i < GodTools.WeatherTypeCount; i++)
            {
                byte type = (byte)i;
                bool selected = type == _weatherType;
                Button button = Kit.Button(Loc.Weather(type), () => _weatherType = type, 0f, selected);
                button.style.fontSize = 10f;
                row.Add(button);
            }
            _body.Add(row);
        }

        private void AddKingdomPicker()
        {
            VisualElement row = Kit.Row();
            row.Add(Kit.Button(Loc.Get(LocKeys.GodKingdomPlant), () => _kingdom = 0, 0f, _kingdom == 0));
            row.Add(Kit.Button(Loc.Get(LocKeys.GodKingdomAnimal), () => _kingdom = 1, 0f, _kingdom == 1));
            _body.Add(row);
        }

        private void AddTimeJumpButtons()
        {
            VisualElement row = Kit.Row();
            row.Add(Kit.Button("+1 day", () => EnqueueTimeJump(1), 80f));
            row.Add(Kit.Button("+7 days", () => EnqueueTimeJump(7), 80f));
            row.Add(Kit.Button("+1 year", () => EnqueueTimeJump(60), 80f));
            _body.Add(row);
        }

        private void EnqueueTimeJump(int days)
        {
            if (!ObserverPanels.TryWorld(out World world, out EntityManager em) ||
                !ObserverPanels.TryPlanet(em, out Entity planet)) return;
            ulong ticksPerDay = TicksPerDay(em);
            EntityQuery timeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            if (timeQuery.IsEmpty)
            {
                timeQuery.Dispose();
                return;
            }
            GameTime time = timeQuery.GetSingleton<GameTime>();
            timeQuery.Dispose();
            Enqueue(em, planet, new GodToolRequest
            {
                Kind = GodToolKind.TimeJump,
                TargetTick = time.TotalTicks + ticksPerDay * (ulong)Mathf.Max(1, days),
                Cell = -1,
            });
        }

        private void Apply(EntityManager em, Entity planet, in GodToolDescriptor descriptor)
        {
            int cell = _targetCell;
            if (descriptor.NeedsTarget && cell < 0)
            {
                _ui?.PushToast(Loc.Get(LocKeys.GodTargetHint));
                return;
            }
            ulong requestId = NextRequestId(em, planet);
            Enqueue(em, planet, new GodToolRequest
            {
                Kind = descriptor.Kind,
                Cell = cell,
                Amount = _amount,
                Radius = _radius,
                Secondary = _amount,
                Count = _count,
                SpeciesId = 0u,
                Kingdom = _kingdom,
                WeatherType = _weatherType,
                TargetTick = 0UL,
                RequestId = requestId,
            });
            _ui?.PushToast(Loc.Get(LocKeys.GodLogged));
        }

        private ulong NextRequestId(EntityManager em, Entity planet)
        {
            if (!em.HasComponent<GodToolState>(planet)) return 1UL;
            GodToolState state = em.GetComponentData<GodToolState>(planet);
            state.NextRequestId++;
            em.SetComponentData(planet, state);
            return state.NextRequestId;
        }

        private static void Enqueue(EntityManager em, Entity planet, in GodToolRequest request)
        {
            if (!em.HasBuffer<GodToolRequest>(planet)) return;
            em.GetBuffer<GodToolRequest>(planet).Add(request);
        }

        private static ulong TicksPerDay(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<WorldSettingsData>());
            ulong ticks = 1200UL;
            if (!query.IsEmpty)
            {
                WorldSettingsData settings = query.GetSingleton<WorldSettingsData>();
                ticks = Math.Max(1UL, settings.SimTicksPerSecond * settings.SecondsPerGameDay);
            }
            query.Dispose();
            return ticks;
        }
    }

    // ── Saves / rewind ──────────────────────────────────────────────────────────────────

    internal sealed class SavePanel
    {
        private readonly ProductUi _ui;
        private readonly VisualElement _body = Kit.Column();
        private readonly List<SnapshotSlotInfo> _ring = new List<SnapshotSlotInfo>(24);

        public VisualElement Root { get; }

        public SavePanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelSaves));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 1500);
        }

        private void Rebuild()
        {
            _body.Clear();
            RewindController rewind = ProductBootstrap.Current != null ? ProductBootstrap.Current.Rewind : RewindController.Instance;
            if (rewind == null)
            {
                _body.Add(Kit.Text(Loc.Get(LocKeys.SaveFailed), 12f, Kit.AccentWarn));
                return;
            }

            List<SnapshotSlotInfo> slots = rewind.Snapshots.ListSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                SnapshotSlotInfo slot = slots[i];
                VisualElement row = Kit.Row();
                row.Add(Kit.Text(SaveNames.SlotLabel(i), 12f, Kit.TextColor));
                row.Add(Kit.Text(slot.Exists ? slot.Header.Describe() : Loc.Get(LocKeys.SaveEmpty), 11f, Kit.TextDim));
                int index = i;
                row.Add(Kit.Button(Loc.Get(LocKeys.SaveNow), () => ProductBootstrap.Current?.QuickSave(index), 70f));
                row.Add(Kit.Button(Loc.Get(LocKeys.SaveLoad), () => ProductBootstrap.Current?.QuickLoad(index), 70f));
                if (slot.Exists)
                {
                    row.Add(Kit.Button(Loc.Get(LocKeys.SaveExport), () => Export(slot), 70f));
                    row.Add(Kit.Button(Loc.Get(LocKeys.SaveDelete), () => rewind.Snapshots.Delete(slot.FullPath), 70f));
                }
                _body.Add(row);
            }

            _body.Add(Kit.Divider());
            _body.Add(Kit.Text(Loc.Get(LocKeys.SaveRewind) + " · " + Loc.Get(LocKeys.SaveAutosaveInterval) + " " +
                               rewind.Autosave.IntervalDays.ToString("0.#") + " d", 12f, Kit.AccentColor));
            _ring.Clear();
            _ring.AddRange(AutosavePolicy.SliderEntries(rewind.Snapshots.ListRing()));
            _body.Add(Kit.Text(Loc.Get(LocKeys.SaveRewindHelp), 11f, Kit.TextDim));
            for (int i = _ring.Count - 1; i >= 0 && i >= _ring.Count - 8; i--)
            {
                SnapshotSlotInfo entry = _ring[i];
                VisualElement row = Kit.Row();
                row.Add(Kit.Text(entry.Header.Describe(), 11f, Kit.TextDim));
                ulong tick = entry.Header.TotalTicks;
                row.Add(Kit.Button(Loc.Get(LocKeys.SaveRewind), () =>
                {
                    rewind.RequestRewind(tick);
                    _ui?.PushToast(Loc.Get(LocKeys.SaveRewind));
                }, 80f));
                _body.Add(row);
            }
        }

        private void Export(in SnapshotSlotInfo slot)
        {
            RewindController rewind = RewindController.Instance;
            if (rewind == null) return;
            SnapshotIoResult result = rewind.Snapshots.Export(slot.FullPath, slot.Header.WorldName);
            _ui?.PushToast(result.Success ? Loc.Format(LocKeys.StatsExportDone, result.Path)
                : Loc.Format(LocKeys.SaveFailed, result.Error));
        }
    }

    // ── Settings ────────────────────────────────────────────────────────────────────────

    internal sealed class SettingsPanel
    {
        private readonly ProductUi _ui;
        private readonly VisualElement _body = Kit.Column();

        public VisualElement Root { get; }

        public SettingsPanel(ProductUi ui)
        {
            _ui = ui;
            Root = Kit.Panel(Loc.Get(LocKeys.PanelSettings));
            Root.Add(_body);
            ObserverPanels.Refresh(Root, Rebuild, 2000);
        }

        private void Rebuild()
        {
            _body.Clear();
            ProductSettingsData settings = SettingsBridge.Settings;

            VisualElement localeRow = Kit.Row();
            localeRow.Add(Kit.Text(Loc.Get(LocKeys.SettingsLocale), 12f, Kit.TextDim));
            localeRow.Add(Kit.Button(Loc.Get(LocKeys.LocaleEnglish), () => SetLocale(Locale.En), 70f, settings.Locale == Locale.En));
            localeRow.Add(Kit.Button(Loc.Get(LocKeys.LocaleRussian), () => SetLocale(Locale.Ru), 70f, settings.Locale == Locale.Ru));
            _body.Add(localeRow);

            VisualElement qualityRow = Kit.Row();
            qualityRow.Add(Kit.Text(Loc.Get(LocKeys.SettingsQuality), 12f, Kit.TextDim));
            qualityRow.Add(Kit.Button("◀", () => StepQuality(-1), 32f));
            qualityRow.Add(Kit.Text(Loc.Quality(settings.Quality), 12f));
            qualityRow.Add(Kit.Button("▶", () => StepQuality(1), 32f));
            qualityRow.Add(Kit.Text(QualityTiers.Describe(settings.Quality), 11f, Kit.TextDim));
            _body.Add(qualityRow);

            AddVolume(Loc.Get(LocKeys.SettingsMaster), settings.MasterVolume, v => Mutate(s => { s.MasterVolume = v; return s; }));
            AddVolume(Loc.Get(LocKeys.SettingsAmbient), settings.AmbientVolume, v => Mutate(s => { s.AmbientVolume = v; return s; }));
            AddVolume(Loc.Get(LocKeys.SettingsUi), settings.UiVolume, v => Mutate(s => { s.UiVolume = v; return s; }));

            VisualElement toggleRow = Kit.Row();
            toggleRow.Add(Kit.Button(Loc.Get(LocKeys.SettingsMuted), () => Mutate(s => { s.Muted = !s.Muted; return s; }), 100f, settings.Muted));
            toggleRow.Add(Kit.Button(Loc.Get(LocKeys.SettingsShowPerf), () => Mutate(s => { s.ShowPerformanceChip = !s.ShowPerformanceChip; return s; }), 130f, settings.ShowPerformanceChip));
            toggleRow.Add(Kit.Button(Loc.Get(LocKeys.PhotoWatermark), () => Mutate(s => { s.PhotoWatermark = !s.PhotoWatermark; return s; }), 130f, settings.PhotoWatermark));
            _body.Add(toggleRow);

            AddVolume(Loc.Get(LocKeys.SaveAutosaveInterval), settings.AutosaveIntervalDays / 30f,
                v => Mutate(s => { s.AutosaveIntervalDays = Mathf.Round(v * 30f); return s; }));
            AddVolume(Loc.Get(LocKeys.TimeScrub), settings.ScrubFrameBudgetMs / 16f,
                v => Mutate(s => { s.ScrubFrameBudgetMs = Mathf.Max(0.5f, v * 16f); return s; }));

            _body.Add(Kit.Divider());
            _body.Add(Kit.Text(BuildInfo.Editor.Describe(), 11f, Kit.TextDim));
            _body.Add(Kit.Text(Loc.Get(LocKeys.HudVersion) + " " + BuildInfo.Editor.HudLabel(), 11f, Kit.TextDim));
            _body.Add(Kit.Text(PerfBudget.Report(ProductBootstrap.Current != null && ProductBootstrap.Current.Rewind != null
                ? new PerfSample { Fps = 60f, SimTickMs = 0f, RenderedInstances = 0, MemoryMb = 0f, LoadSeconds = 0f, Organisms = 0, GcBytesPerFrame = 0L }
                : default, BuildInfo.Editor, settings.Quality), 10f, Kit.TextDim));
            _ = _ui;
        }

        private void AddVolume(string label, float value, Action<float> onChanged)
        {
            VisualElement row = Kit.Row();
            Label name = Kit.Text(label, 12f, Kit.TextDim);
            name.style.width = 150f;
            row.Add(name);
            var slider = new Slider(0f, 1f) { value = Mathf.Clamp01(value) };
            slider.style.flexGrow = 1f;
            slider.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            row.Add(slider);
            _body.Add(row);
        }

        private void SetLocale(Locale locale)
        {
            Mutate(s => { s.Locale = locale; return s; });
            Loc.SetLocale(locale);
            ProductLog.Info(LogCategory.Time, SimLogCodes.LocaleChanged, Loc.Catalog?.Describe() ?? "none", (float)locale);
            _ui?.PushToast(Loc.Get(LocKeys.SettingsLocale) + ": " + Loc.Get(locale == Locale.Ru ? LocKeys.LocaleRussian : LocKeys.LocaleEnglish));
            _ui?.InvalidatePanel(PanelKind.Settings);
        }

        private void StepQuality(int direction)
        {
            Mutate(s =>
            {
                int index = Mathf.Clamp((int)s.Quality + direction, 0, QualityTiers.Count - 1);
                s.Quality = (QualityTier)index;
                return s;
            });
        }

        /// <summary>
        /// Settings are a value type, so edits are expressed as a function that returns the
        /// updated copy — a lambda that only mutates its own parameter would silently drop the
        /// change.
        /// </summary>
        private static void Mutate(Func<ProductSettingsData, ProductSettingsData> change)
        {
            SettingsBridge.Update(change(SettingsBridge.Settings));
        }
    }
}
