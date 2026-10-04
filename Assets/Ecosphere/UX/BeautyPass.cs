// Ecosphere — stage 07: beauty pass (presentation only).
//
// Cheap, honest improvements to the look of the planet, all built at runtime so the repository
// stays free of binary assets:
//   • An animated cloud shell: a UV sphere with a procedurally generated alpha cloud texture,
//     scrolled by the weather wind and tinted by season/time of day.
//   • Seasonal palette transitions and a soft terminator: the sun light, ambient colour and
//     cloud tint follow the season, so summer and winter look different from orbit.
//   • Aurora ribbons (Ultra/High only, quality-gated): two translucent bands above the polar
//     circles that fade in during the local night of winter.
//
// Not included on purpose (documented in Docs/manual.md §Known gaps): wind sway on plants and
// snow sparkle need vertex-shader work on the organism/terrain materials, which arrives with
// the terrain-LOD/shader pass. Nothing here writes simulation state.

using System.Collections.Generic;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Ecosphere.Planet;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.UX
{
    [DisallowMultipleComponent]
    public sealed class BeautyPass : MonoBehaviour
    {
        private const int CloudSegments = 48;
        private const int CloudRings = 24;

        private readonly List<Mesh> _meshes = new List<Mesh>(4);
        private readonly List<Material> _materials = new List<Material>(4);

        private Transform _cloudShell;
        private MeshRenderer _cloudRenderer;
        private Material _cloudMaterial;
        private Texture2D _cloudTexture;
        private readonly List<Transform> _auroras = new List<Transform>(2);
        private float _planetRadius = 1000f;
        private float _time;
        private Color _baseSunColor = new Color(1f, 0.96f, 0.9f);

        private EntityQuery _planetQuery;
        private bool _ready;

        private void Start()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                _planetQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlanetState>());
                _ready = true;
            }
        }

        private void OnDestroy()
        {
            if (_ready) _planetQuery.Dispose();
            for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
            for (int i = 0; i < _materials.Count; i++) if (_materials[i] != null) Destroy(_materials[i]);
            if (_cloudTexture != null) Destroy(_cloudTexture);
        }

        private void Update()
        {
            if (World.DefaultGameObjectInjectionWorld == null) return;
            if (_planetQuery.IsEmpty) return;

            PlanetState planet = _planetQuery.GetSingleton<PlanetState>();
            _ = _ready;
            if (!Mathf.Approximately(planet.Radius, _planetRadius) || _cloudShell == null)
            {
                _planetRadius = planet.Radius;
                BuildCloudShell();
                BuildAuroras();
            }

            _time += Time.deltaTime;
            UpdateSeasonAndLight(planet);
            UpdateClouds(planet);
            UpdateAuroras(planet);
        }

        private void BuildCloudShell()
        {
            if (_cloudShell != null) Destroy(_cloudShell.gameObject);
            if (_cloudTexture == null) _cloudTexture = GenerateCloudTexture(256, 128);

            Mesh mesh = BuildSphere(_planetRadius * 1.02f, CloudSegments, CloudRings);
            _meshes.Add(mesh);

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _cloudMaterial = new Material(shader) { mainTexture = _cloudTexture };
            if (_cloudMaterial.HasProperty("_BaseMap")) _cloudMaterial.SetTexture("_BaseMap", _cloudTexture);
            if (_cloudMaterial.HasProperty("_BaseColor")) _cloudMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
            if (_cloudMaterial.HasProperty("_Surface")) _cloudMaterial.SetFloat("_Surface", 1f);   // transparent
            if (_cloudMaterial.HasProperty("_Blend")) _cloudMaterial.SetFloat("_Blend", 0f);       // alpha
            if (_cloudMaterial.HasProperty("_ZWrite")) _cloudMaterial.SetFloat("_ZWrite", 0f);
            _cloudMaterial.renderQueue = 3000;
            _materials.Add(_cloudMaterial);

            var go = new GameObject("CloudShell");
            go.transform.SetParent(transform);
            _cloudRenderer = go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _cloudRenderer.sharedMaterial = _cloudMaterial;
            _cloudRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cloudRenderer.receiveShadows = false;
            _cloudShell = go.transform;
        }

        private void BuildAuroras()
        {
            if (_auroras.Count > 0) return;   // built once; visibility is toggled per quality/season
            for (int hemisphere = -1; hemisphere <= 1; hemisphere += 2)
            {
                Mesh ribbon = BuildAuroraRibbon(hemisphere);
                _meshes.Add(ribbon);
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                var material = new Material(shader) { mainTexture = null };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.35f, 0.95f, 0.6f, 0.22f));
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
                material.renderQueue = 3010;
                _materials.Add(material);

                var go = new GameObject("Aurora" + (hemisphere > 0 ? "N" : "S"));
                go.transform.SetParent(transform);
                go.AddComponent<MeshFilter>().sharedMesh = ribbon;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _auroras.Add(go.transform);
            }
        }

        // ── Procedural assets ───────────────────────────────────────────────────────────

        private static Texture2D GenerateCloudTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / (height - 1);
                // Cloud bands: a few octaves of value noise, thinning at the poles.
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / (width - 1);
                    float density = 0f;
                    density += 0.55f * Noise(u * 6f, v * 4f);
                    density += 0.28f * Noise(u * 13f + 4.1f, v * 9f + 1.7f);
                    density += 0.17f * Noise(u * 27f + 9.3f, v * 19f + 5.5f);
                    density *= Mathf.Clamp01(1.35f - Mathf.Abs(v * 2f - 1f));   // polar thinning
                    byte alpha = (byte)Mathf.Clamp(density * 230f, 0f, 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.Apply(updateMipmaps: true);
            return texture;
        }

        /// <summary>Deterministic value noise (no UnityEngine.Random, no allocations).</summary>
        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float fx = x - xi;
            float fy = y - yi;
            float a = Hash(xi, yi);
            float b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1);
            float d = Hash(xi + 1, yi + 1);
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static Mesh BuildSphere(float radius, int segments, int rings)
        {
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * rings * 6];
            for (int y = 0; y <= rings; y++)
            {
                float v = (float)y / rings;
                float phi = v * Mathf.PI;
                for (int x = 0; x <= segments; x++)
                {
                    float u = (float)x / segments;
                    float theta = u * Mathf.PI * 2f;
                    var direction = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));
                    vertices[y * (segments + 1) + x] = direction * radius;
                    uv[y * (segments + 1) + x] = new Vector2(u, v);
                }
            }
            int index = 0;
            for (int y = 0; y < rings; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int a = y * (segments + 1) + x;
                    int b = a + segments + 1;
                    triangles[index++] = a;
                    triangles[index++] = b;
                    triangles[index++] = a + 1;
                    triangles[index++] = a + 1;
                    triangles[index++] = b;
                    triangles[index++] = b + 1;
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildAuroraRibbon(int hemisphere)
        {
            const int steps = 64;
            var vertices = new Vector3[steps * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(steps - 1) * 6];
            float latitude = hemisphere > 0 ? 70f : -70f;
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                float longitude = t * 360f;
                float wobble = 3.5f * Mathf.Sin(t * Mathf.PI * 4f);
                float latLow = latitude + (hemisphere > 0 ? -1.5f : 1.5f);
                float latHigh = latitude + (hemisphere > 0 ? 7f + wobble : -(7f + wobble));
                vertices[i * 2] = Direction(latLow, longitude) * _planetRadius * 1.004f;
                vertices[i * 2 + 1] = Direction(latHigh, longitude) * _planetRadius * 1.004f;
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
            }
            int index = 0;
            for (int i = 0; i < steps - 1; i++)
            {
                int a = i * 2;
                triangles[index++] = a;
                triangles[index++] = a + 1;
                triangles[index++] = a + 2;
                triangles[index++] = a + 2;
                triangles[index++] = a + 1;
                triangles[index++] = a + 3;
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Direction(float latitudeDegrees, float longitudeDegrees)
        {
            float lat = latitudeDegrees * Mathf.Deg2Rad;
            float lon = longitudeDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
        }

        // ── Season + weather driven look ───────────────────────────────────────────────

        private void UpdateSeasonAndLight(in PlanetState planet)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            EntityQuery timeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameTime>());
            EntityQuery settingsQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WorldSettingsData>());
            SimDate date = CalendarMath.FromTicks(0UL, CalendarMath.Default);
            if (!timeQuery.IsEmpty)
            {
                GameTime time = timeQuery.GetSingleton<GameTime>();
                WorldSettingsData settings = settingsQuery.IsEmpty ? default : settingsQuery.GetSingleton<WorldSettingsData>();
                ClockConfig clock = settings.SecondsPerGameDay == 0u ? CalendarMath.Default : settings.ToClockConfig();
                date = CalendarMath.FromTicks(time.TotalTicks, clock);
            }
            timeQuery.Dispose();
            settingsQuery.Dispose();

            float winter = date.Season == Season.Winter ? 1f : 0f;
            float summer = date.Season == Season.Summer ? 1f : 0f;
            float daylight = Mathf.Clamp01(Mathf.Sin((float)date.DayFraction * Mathf.PI * 2f - Mathf.PI * 0.5f) * 0.5f + 0.5f);

            Light sun = null;
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type != LightType.Directional) continue;
                sun = lights[i];
                break;
            }
            if (sun != null)
            {
                Color winterTint = new Color(0.82f, 0.88f, 1f);
                Color summerTint = new Color(1f, 0.96f, 0.86f);
                Color tint = Color.Lerp(_baseSunColor, winter > 0.5f ? winterTint : summerTint, 0.6f);
                sun.color = tint;
                sun.intensity = Mathf.Lerp(0.15f, 1.6f, daylight) * (winter > 0.5f ? 0.9f : 1f);
            }
            RenderSettings.ambientLight = Color.Lerp(
                new Color(0.10f, 0.14f, 0.22f), new Color(0.24f, 0.28f, 0.34f), daylight);
            _ = planet;

            bool aurora = QualityRuntime.Aurora && (winter > 0.5f || QualityRuntime.Tier == QualityTier.Ultra);
            for (int i = 0; i < _auroras.Count; i++) _auroras[i].gameObject.SetActive(aurora && !CrashGuard.ShouldSuppressHeavyFeatures);
        }

        private void UpdateClouds(in PlanetState planet)
        {
            if (_cloudMaterial == null || _cloudShell == null) return;
            World world = World.DefaultGameObjectInjectionWorld;
            float wind = 0.5f;
            if (world != null && world.IsCreated && !_planetQuery.IsEmpty)
            {
                Entity planetEntity = _planetQuery.GetSingletonEntity();
                if (world.EntityManager.HasBuffer<PlanetCell>(planetEntity))
                {
                    DynamicBuffer<PlanetCell> cells = world.EntityManager.GetBuffer<PlanetCell>(planetEntity);
                    if (cells.Length > 0) wind = math.length(cells[cells.Length / 2].Wind);
                }
            }
            _cloudMaterial.mainTextureOffset = new Vector2(_time * (0.002f + wind * 0.0008f), Mathf.Sin(_time * 0.05f) * 0.002f);
            if (_cloudMaterial.HasProperty("_BaseMap"))
                _cloudMaterial.SetTextureOffset("_BaseMap", _cloudMaterial.mainTextureOffset);

            // Keep the shell aligned with the planet's axial framing (sun direction drives the tilt).
            _cloudShell.rotation = Quaternion.Euler(0f, 0f, 0f);
            _ = planet;
        }

        private void UpdateAuroras(in PlanetState planet)
        {
            for (int i = 0; i < _auroras.Count; i++)
            {
                _auroras[i].Rotate(Vector3.up, 3f * Time.deltaTime, Space.Self);
            }
            _ = planet;
        }
    }
}
