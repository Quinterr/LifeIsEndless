// Ecosphere — stage 07: generative ambient audio.
//
// No licensed assets: every clip is synthesized at boot from AudioSynthMath (wind noise,
// rain, ocean surf, storm rumbles, a day/night bed and dawn chorus) and creature calls are
// generated per phenotype on demand. The mix follows the weather at the camera cell, so the
// audio reacts audibly to a summoned storm or a passing front without any authored event
// mapping.
//
// Layers are looped AudioSources with generated AudioClips; the mix is smoothed every frame
// (AmbientMix + Smooth) so a weather change is a swell, never a click.

using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Ecosphere.UX
{
    /// <summary>Procedural ambience + creature call blips.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        private const int SampleRate = 24000;
        private const int ClipSeconds = 4;
        private const int CallVoices = 6;

        private readonly List<AudioSource> _layers = new List<AudioSource>(7);
        private readonly List<AudioClip> _clips = new List<AudioClip>(8);
        private readonly AudioSource[] _calls = new AudioSource[CallVoices];
        private readonly float[] _callBuffer = new float[SampleRate];

        private ProductSettingsData _settings = ProductSettingsData.Default;
        private AmbientMix _mix;
        private float _master = 0.8f;
        private float _ambient = 0.7f;
        private float _uiVolume = 0.6f;
        private float _callTimer = 4f;
        private int _voice;
        private EntityQuery _planetQuery;
        private EntityQuery _organismQuery;
        private bool _ready;

        public void Configure(ProductSettingsData settings)
        {
            _settings = settings;
            _master = settings.Muted ? 0f : settings.MasterVolume;
            _ambient = settings.AmbientVolume;
            _uiVolume = settings.UiVolume;
            ApplyVolumes();
        }

        public void SetMuted(bool muted)
        {
            _settings.Muted = muted;
            ApplyVolumes();
        }

        /// <summary>Plays a short UI click (buttons, panel opens).</summary>
        public void PlayUiClick(float pitch = 1.6f)
        {
            if (_calls[0] == null) return;
            AudioClip clip = _clips.Count > 0 ? _clips[0] : null;
            if (clip == null) return;
            _calls[0].pitch = pitch;
            _calls[0].volume = _uiVolume * 0.35f;
            _calls[0].PlayOneShot(clip);
        }

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                EntityManager em = world.EntityManager;
                _planetQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
                _organismQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GenomeHeader>(), ComponentType.Exclude<DeadTag>());
                _ready = true;
            }
            BuildClips();
        }

        private void OnDestroy()
        {
            if (!_ready) return;
            _planetQuery.Dispose();
            _organismQuery.Dispose();
            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null) Destroy(_clips[i]);
            }
            _ready = false;
        }

        private void BuildClips()
        {
            CreateLayer("Wind", AmbientLayer.Wind);
            CreateLayer("Rain", AmbientLayer.Rain);
            CreateLayer("Ocean", AmbientLayer.Ocean);
            CreateLayer("Storm", AmbientLayer.Storm);
            CreateLayer("Night", AmbientLayer.Night);
            CreateLayer("Dawn", AmbientLayer.Dawn);
            for (int i = 0; i < CallVoices; i++)
            {
                var voice = new GameObject("CallVoice" + i).AddComponent<AudioSource>();
                voice.transform.SetParent(transform);
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                _calls[i] = voice;
            }
        }

        private void CreateLayer(string name, AmbientLayer layer)
        {
            var buffer = new float[SampleRate * ClipSeconds];
            Generate(buffer, layer, LayerIntensity(layer), 12345 + (int)layer);
            AudioClip clip = AudioClip.Create("eco_" + name, buffer.Length, 1, SampleRate, false);
            clip.SetData(buffer, 0);
            _clips.Add(clip);

            var source = new GameObject("Layer_" + name).AddComponent<AudioSource>();
            source.transform.SetParent(transform);
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.Play();
            _layers.Add(source);
        }

        /// <summary>Fills one clip by dispatching to the pure synthesis helpers.</summary>
        private static void Generate(float[] buffer, AmbientLayer layer, float intensity, int seed)
        {
            var rng = RngState.Create((ulong)(uint)seed);
            var lowpass = default(OnePole);
            var second = default(OnePole);
            var dc = default(DcBlocker);
            switch (layer)
            {
                case AmbientLayer.Wind:
                    AudioSynthMath.FillWind(buffer, 0, buffer.Length, intensity, SampleRate, ref rng, ref lowpass, ref second, ref dc);
                    break;
                case AmbientLayer.Rain:
                    AudioSynthMath.FillRain(buffer, 0, buffer.Length, intensity, 0.15f, SampleRate, ref rng, ref lowpass, ref dc);
                    break;
                case AmbientLayer.Ocean:
                    AudioSynthMath.FillOcean(buffer, 0, buffer.Length, intensity, SampleRate, ref rng, ref lowpass, ref second);
                    break;
                case AmbientLayer.Storm:
                    AudioSynthMath.FillStorm(buffer, 0, buffer.Length, intensity, SampleRate, ref rng, ref lowpass, ref second, ref dc);
                    break;
                case AmbientLayer.Night:
                    AudioSynthMath.FillNight(buffer, 0, buffer.Length, intensity, SampleRate, ref rng, ref lowpass);
                    break;
                default:
                    AudioSynthMath.FillDawn(buffer, 0, buffer.Length, intensity, SampleRate, ref rng);
                    break;
            }
        }

        private static float LayerIntensity(AmbientLayer layer)
        {
            switch (layer)
            {
                case AmbientLayer.Wind: return 0.5f;
                case AmbientLayer.Rain: return 0.6f;
                case AmbientLayer.Ocean: return 0.5f;
                case AmbientLayer.Storm: return 0.7f;
                case AmbientLayer.Night: return 0.45f;
                default: return 0.4f;
            }
        }

        private void Update()
        {
            if (!_ready || _layers.Count == 0) return;
            AmbientWeatherInput input = ReadWeatherInput();
            AmbientMix target = AudioSynthMath.MixFromWeather(input);
            float dt = Time.unscaledDeltaTime;
            _mix.Wind = AudioSynthMath.Smooth(_mix.Wind, target.Wind, dt, 2.5f);
            _mix.Rain = AudioSynthMath.Smooth(_mix.Rain, target.Rain, dt, 2.0f);
            _mix.Ocean = AudioSynthMath.Smooth(_mix.Ocean, target.Ocean, dt, 3.0f);
            _mix.Storm = AudioSynthMath.Smooth(_mix.Storm, target.Storm, dt, 1.5f);
            _mix.Night = AudioSynthMath.Smooth(_mix.Night, target.Night, dt, 6.0f);
            _mix.Dawn = AudioSynthMath.Smooth(_mix.Dawn, target.Dawn, dt, 6.0f);
            _mix.Creature = AudioSynthMath.Smooth(_mix.Creature, target.Creature, dt, 4.0f);

            for (int i = 0; i < _layers.Count; i++)
            {
                float layer = _mix.Get((AmbientLayer)i);
                _layers[i].volume = layer * _ambient * _master;
            }

            TickCreatureCalls(input, dt);
        }

        private void ApplyVolumes()
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                float layer = _mix.Get((AmbientLayer)i);
                _layers[i].volume = layer * _ambient * _master;
            }
        }

        private AmbientWeatherInput ReadWeatherInput()
        {
            var input = new AmbientWeatherInput
            {
                WindSpeed = 4f,
                Precipitation = 0.4f,
                Storminess = 0.1f,
                CloudCover = 0.4f,
                DayFraction = 0.5f,
                CameraAltitude = 0.2f,
                CreatureDensity = 0.3f,
                SeasonPhase = 0f,
            };

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _planetQuery.IsEmpty) return input;
            EntityManager em = world.EntityManager;
            Entity planet = _planetQuery.GetSingletonEntity();
            if (!em.HasBuffer<PlanetCell>(planet)) return input;

            PlanetState state = em.GetComponentData<PlanetState>(planet);
            DynamicBuffer<PlanetCell> cells = em.GetBuffer<PlanetCell>(planet);
            DynamicBuffer<WeatherEvent> events = em.HasBuffer<WeatherEvent>(planet) ? em.GetBuffer<WeatherEvent>(planet) : default;
            Camera camera = Camera.main;
            int cell = 0;
            if (camera != null && state.Topology.IsCreated)
            {
                BlobAssetReference<PlanetTopologyBlob> topology = state.Topology;
                cell = Icosphere.Nearest(ref topology.Value, camera.transform.position.normalized);
            }
            var sampler = new ClimateSampler(state, cells, events);
            ClimateSample sample = sampler.Sample(cell);
            input.WindSpeed = sample.WindStrength;
            input.Precipitation = sample.Precipitation;
            input.SnowCover = sample.SnowCover;
            input.Storminess = sample.Storminess;
            input.CloudCover = sample.CloudCover;
            input.IsSubmerged = sample.IsSubmerged;
            input.CameraAltitude = Mathf.Clamp01(camera != null ? camera.transform.position.magnitude / Mathf.Max(1f, state.Radius * 0.25f) : 0.5f);
            if (camera != null)
            {
                Vector3 local = camera.transform.position.normalized;
                input.DayFraction = Mathf.Repeat(Mathf.Atan2(-local.z, -local.x) / (Mathf.PI * 2f) + 0.5f, 1f);
            }
            if (!_organismQuery.IsEmpty)
            {
                int organisms = _organismQuery.CalculateEntityCount();
                input.CreatureDensity = Mathf.Clamp01(organisms / 400f);
            }
            return input;
        }

        private void TickCreatureCalls(in AmbientWeatherInput input, float dt)
        {
            _callTimer -= dt;
            if (_callTimer > 0f) return;
            float sociability = 0.5f;
            _callTimer = AudioSynthMath.CallInterval(sociability, 0.5f, input.CreatureDensity);
            if (_mix.Creature <= 0.01f || _organismQuery.IsEmpty) return;

            CreatureCallParams parameters = AudioSynthMath.CallParamsFromTraits(
                Mathf.Lerp(0.4f, 2.2f, UnityEngine.Random.value), Mathf.Lerp(0.4f, 2f, UnityEngine.Random.value),
                0.3f, 0.5f);
            int length = Mathf.Clamp(Mathf.RoundToInt(parameters.Duration * SampleRate), 512, _callBuffer.Length);
            var callRng = RngState.Create((ulong)(uint)(1000 + _voice * 7919));
            AudioSynthMath.FillCreatureCall(_callBuffer, 0, length, parameters, SampleRate, ref callRng);

            AudioSource voice = _calls[_voice];
            _voice = (_voice + 1) % CallVoices;
            if (voice == null) return;
            AudioClip clip = AudioClip.Create("eco_call", length, 1, SampleRate, false);
            clip.SetData(_callBuffer, 0);
            voice.clip = clip;
            voice.volume = parameters.Amplitude * _mix.Creature * _ambient * _master;
            voice.Play();
            voice.SetScheduledEndTime(AudioSettings.dspTime + parameters.Duration);
            Destroy(clip, parameters.Duration + 0.5f);
        }

        /// <summary>Current smoothed mix (perf overlay + tests).</summary>
        public AmbientMix Mix => _mix;
    }
}
