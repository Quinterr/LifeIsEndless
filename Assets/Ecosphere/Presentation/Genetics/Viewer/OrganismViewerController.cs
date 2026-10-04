// Ecosphere — OrganismViewerController: the debug "organism viewer" scene driver.
//
// Demonstrates the stage-04 acceptance criteria in one scene:
//  * a single specimen grown from a genome (age slider → watch it grow)
//  * climate sliders → environment EMA → visible epigenetic modulation
//  * 24 × 2 gallery of random genomes (the "every body is different" proof)
//  * gene list (readable names + values), per-LOD mesh stats, JSON export
//
// DEBUG-TOOL EXCEPTION (documented, Docs/genetics.md): like the stage-07 god
// tools, this controller writes organism debug state (genome, age, manual
// environment) to the simulation world. Normal gameplay code never does this.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ecosphere.Core.ECS;
using Ecosphere.Core.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Ecosphere.Presentation.Genetics
{
    [DefaultExecutionOrder(100)]
    public class OrganismViewerController : MonoBehaviour
    {
        [Header("Catalog (optional — falls back to the built-in catalog)")]
        [SerializeField] private GeneCatalogAsset _catalogAsset;

        [Header("Camera")]
        [SerializeField] private float _orbitDistance = 4f;
        [SerializeField] private float _orbitSensitivity = 0.5f;

        // ── state ────────────────────────────────────────────────────
        private World _world;
        private EntityManager _em;
        private GeneCatalog _catalog;

        private Entity _specimen = Entity.Null;
        private List<Gene> _specimenGenes = new List<Gene>(128);
        private uint _specimenSeed;
        private GeneKingdom _kingdom = GeneKingdom.Animal;

        private bool _agePinned;
        private float _ageFraction = 0.45f;
        private float _lastPinnedFraction = -1f;

        private float _envTemp = 18f, _envLight = 0.6f, _envWind = 2f, _envMoisture = 0.5f;
        private int _lod = 0;
        private bool _xray;
        private bool _galleryMode;
        private List<Entity> _galleryEntities = new List<Entity>(64);

        private uint _viewerSeed = 1;
        private string _exportPath = "(not exported)";
        private string _galleryStatus = "";
        private string _statsLine = "—";
        private bool _statsDirty = true;
        private float _statsTimer;

        private readonly Dictionary<Entity, GameObject> _visuals = new Dictionary<Entity, GameObject>(64);
        private Material _material;
        private GameObject _visualRoot;
        private Vector3 _orbitTarget;
        private float _yaw = 30f, _pitch = 18f, _dist = 4f;
        private Vector3 _panOffset;
        private Camera _camera;

        // ── lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            if (_world == null)
            {
                Debug.LogError("[OrganismViewer] No default injection world — is WorldBootstrap present?");
                enabled = false;
                return;
            }
            _em = _world.EntityManager;
            _catalog = BuildCatalog();

            _visualRoot = new GameObject("Organism visuals");
            _material = CreateMaterial();
            _dist = _orbitDistance;
            _viewerSeed = WorldSeedOrDefault();
            _specimenSeed = NextSeed();
            SpawnSpecimen(_kingdom, _specimenSeed);
        }

        private void OnDestroy()
        {
            if (_visualRoot != null) Destroy(_visualRoot);
            if (_material != null) Destroy(_material);
        }

        private GeneCatalog BuildCatalog()
        {
            if (_catalogAsset != null) return _catalogAsset.BuildCatalog();
            GeneCatalogData data = TrySingleton<GeneCatalogData>();
            if (data.Catalog.IsCreated)
            {
                return GenomeHeaderBridge.BuildCatalog(data);
            }
            return GeneCatalog.Create();
        }

        private uint WorldSeedOrDefault()
        {
            WorldSettingsData s = TrySingleton<WorldSettingsData>();
            return (uint)(s.WorldSeed ^ (s.WorldSeed >> 32));
        }

        private T TrySingleton<T>() where T : unmanaged, IComponentData
        {
            if (_em == null) return default;
            EntityQuery q = _em.CreateEntityQuery(typeof(T));
            try
            {
                return q.IsEmpty ? default : q.GetSingleton<T>();
            }
            finally
            {
                q.Dispose();
            }
        }

        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        private uint NextSeed()
        {
            // Advance a viewer-local stream (deterministic per session seed).
            RngState rng = RngState.Create(_viewerSeed, (uint)(_visuals.Count + _galleryEntities.Count + 1));
            return rng.NextU32();
        }

        // ── specimen management ──────────────────────────────────────

        private void SpawnSpecimen(GeneKingdom kingdom, uint seed)
        {
            KillSpecimen();
            _kingdom = kingdom;
            _specimenSeed = seed;
            RngState rng = SimRandom.ForStream((ulong)seed, StreamIds.Fnv1a("Organism.Viewer"));
            _specimenGenes = GenomeFactory.CreateGenome(kingdom, _catalog, ref rng);
            _specimen = OrganismFactory.Spawn(_em, _specimenGenes, kingdom, seed, cellIndex: -1);
            SetAgeFraction(0.45f);
            ApplyManualEnv();
            EnsureVisual(_specimen);
            _statsDirty = true;
        }

        private void KillSpecimen()
        {
            if (_specimen == Entity.Null) return;
            ForgetVisual(_specimen);
            if (_em.Exists(_specimen)) _em.DestroyEntity(_specimen);
            _specimen = Entity.Null;
            _agePinned = false;
        }

        private void Randomize()
        {
            _specimenSeed = NextSeed();
            SpawnSpecimen(_kingdom, _specimenSeed);
            SetStatus($"Randomized {(_kingdom == GeneKingdom.Animal ? "animal" : "plant")} genome #{_specimenSeed:X8}");
        }

        private void Mutate(int direction)
        {
            if (_specimen == Entity.Null) return;
            // Signed mutation: push 3 random genes up or down deterministically.
            RngState mutRng = RngState.Create(_specimenSeed, (uint)(direction + 4));
            for (int i = 0; i < 3; i++)
            {
                int idx = (int)mutRng.NextUInt((uint)_specimenGenes.Count);
                var g = _specimenGenes[idx];
                g.Value = Clamp01(g.Value + direction * (0.04f + mutRng.NextFloat01() * 0.05f));
                _specimenGenes[idx] = g;
            }
            OrganismFactory.SetGenome(_em, _specimen, _specimenGenes, _kingdom, _specimenSeed);
            _statsDirty = true;
            SetStatus($"Mutated {direction > 0 ? "up" : "down"} (3 genes)");
        }

        private void Regenerate()
        {
            if (_specimen == Entity.Null) return;
            OrganismMeshManager.Forget(_specimen);
            _em.AddComponent(_specimen, typeof(MeshDirty));
            _statsDirty = true;
            SetStatus("Mesh regenerated");
        }

        private void SetAgeFraction(float frac)
        {
            _ageFraction = Mathf.Clamp01(frac);
            _agePinned = true;
            _lastPinnedFraction = _ageFraction;
            if (_specimen == Entity.Null) return;
            float lifespan = LifespanTicks();
            ulong ticks = (ulong)(_ageFraction * lifespan);
            _em.SetComponentData(_specimen, new OrganismAge { Ticks = ticks });
            _em.SetComponentData(_specimen, new OrganismDevAge { Ticks = (float)ticks });
            _em.AddComponent(_specimen, typeof(MeshDirty));
            _statsDirty = true;
        }

        private float LifespanTicks()
        {
            if (_specimen == Entity.Null) return 3600f;
            PhenotypeData ph = _em.GetComponentData<PhenotypeData>(_specimen);
            return Math.Max(200f, ph.Value.Lifespan);
        }

        private void ApplyManualEnv()
        {
            if (_specimen == Entity.Null) return;
            _em.SetComponentData(_specimen, new ManualEnvironment
            {
                Enabled = 1,
                Temperature = _envTemp,
                Light = _envLight,
                Wind = _envWind,
                Moisture = _envMoisture,
            });
            _statsDirty = true;
        }

        // ── gallery ──────────────────────────────────────────────────

        private void ToggleGallery()
        {
            _galleryMode = !_galleryMode;
            if (_galleryMode) BuildGallery();
            else ClearGallery();
        }

        private void BuildGallery()
        {
            ClearGallery();
            var sw = Stopwatch.StartNew();
            int perKingdom = 24;
            float spacing = 2.4f;
            for (int k = 0; k < 2; k++)
            {
                GeneKingdom kingdom = k == 0 ? GeneKingdom.Animal : GeneKingdom.Plant;
                float zBase = k == 0 ? -14f : -30f;
                for (int i = 0; i < perKingdom; i++)
                {
                    int col = i % 6, row = i / 6;
                    float x = (col - 2.5f) * spacing;
                    float z = zBase - row * spacing;

                    uint seed = NextSeed();
                    RngState rng = SimRandom.ForStream((ulong)seed, StreamIds.Fnv1a("Organism.Gallery"));
                    List<Gene> genes = GenomeFactory.CreateGenome(kingdom, _catalog, ref rng);
                    Entity e = OrganismFactory.Spawn(_em, genes, kingdom, seed, cellIndex: -1);
                    // Start the gallery at 70% of lifespan (adult, fully expressed).
                    float lifespan = 3600f;
                    foreach (var g in genes)
                        if (g.TypeId == GeneId.Lifespan)
                        {
                            float expressed = GenomeEvolutionMath.ExpressAllele(
                                g.Value, g.Dominance, _catalog.Get(GeneId.Lifespan).Default);
                            lifespan = Math.Max(200f, _catalog.MapToRange(GeneId.Lifespan, expressed));
                        }
                    ulong ticks = (ulong)(lifespan * 0.7f);
                    _em.SetComponentData(e, new OrganismAge { Ticks = ticks });
                    _em.SetComponentData(e, new OrganismDevAge { Ticks = (float)ticks });
                    _em.SetComponentData(e, new ManualEnvironment
                    {
                        Enabled = 1, Temperature = 18f, Light = 0.6f, Wind = 2f, Moisture = 0.5f
                    });
                    _em.AddComponent(e, typeof(MeshDirty));
                    _galleryEntities.Add(e);
                    var v = EnsureVisual(e);
                    v.transform.SetParent(_visualRoot.transform);
                    v.transform.localPosition = new Vector3(x, 0, z);
                }
            }
            sw.Stop();
            _galleryStatus = $"Gallery: 2 kingdoms × {perKingdom} genomes built in {sw.ElapsedMilliseconds} ms (budget: < 1000 ms)";
            _statsDirty = true;
        }

        private void ClearGallery()
        {
            foreach (var e in _galleryEntities)
            {
                ForgetVisual(e);
                OrganismMeshManager.Forget(e);
                if (_em.Exists(e)) _em.DestroyEntity(e);
            }
            _galleryEntities.Clear();
        }

        // ── frame loop ───────────────────────────────────────────────

        private void Update()
        {
            if (_world == null || _em == null) return;

            // Re-pin the age slider before the sim tick (debug override).
            // MeshDirty only on actual slider movement (the sim otherwise
            // re-develops from the pinned age on its own).
            if (_agePinned && _specimen != Entity.Null)
            {
                bool changed = math.abs(_ageFraction - _lastPinnedFraction) > 1e-4f;
                if (changed) _lastPinnedFraction = _ageFraction;
                ulong ticks = (ulong)(_ageFraction * LifespanTicks());
                _em.SetComponentData(_specimen, new OrganismAge { Ticks = ticks });
                _em.SetComponentData(_specimen, new OrganismDevAge { Ticks = (float)ticks });
                if (changed) _em.AddComponent(_specimen, typeof(MeshDirty));
            }

            UpdateOrbitCamera();
        }

        private void LateUpdate()
        {
            if (_world == null || _em == null) return;

            // Rebuild dirty meshes (cheap: only stage/bucket transitions).
            int lod = _lod;
            OrganismMeshManager.BuildAll(_em, lod);

            // Visual sync.
            SyncVisual(_specimen, Vector3.zero, 1.4f);
            float spacing = 2.4f;
            for (int i = 0; i < _galleryEntities.Count; i++)
            {
                int k = i < 24 ? 0 : 1;
                int local = i < 24 ? i : i - 24;
                int col = local % 6, row = local / 6;
                float zBase = k == 0 ? -14f : -30f;
                SyncVisual(_galleryEntities[i], new Vector3((col - 2.5f) * spacing, 0, zBase - row * spacing), 1.0f);
            }

            // Stats (throttled).
            _statsTimer += Time.unscaledDeltaTime;
            if (_statsDirty && _statsTimer > 0.25f)
            {
                _statsTimer = 0f;
                _statsDirty = false;
                RefreshStats();
            }
        }

        private void SyncVisual(Entity e, Vector3 worldPos, float extraScale)
        {
            if (e == Entity.Null || !_em.Exists(e)) return;
            if (!_visuals.TryGetValue(e, out GameObject v)) v = EnsureVisual(e);
            if (v == null) return;

            OrganismMeshRef meshRef = OrganismMeshManager.EnsureMesh(_em, e, _lod);
            var mf = v.GetComponent<MeshFilter>();
            if (mf.sharedMesh != meshRef.Mesh) mf.sharedMesh = meshRef.Mesh;

            // Smooth growth between 10% buckets: scale by size.
            float size = _em.GetComponentData<OrganismSize>(e).Value;
            float s = (0.6f + 0.9f * size) * extraScale;
            v.transform.localScale = Vector3.one * s;
            v.transform.position = worldPos;

            if (_lod == 3 && _camera != null)
            {
                // Billboard the impostor quad toward the camera.
                Vector3 toCam = _camera.transform.position - v.transform.position;
                if (toCam.sqrMagnitude > 1e-4f)
                    v.transform.rotation = Quaternion.LookRotation(-toCam);
            }

            // X-ray roots (plants, specimen only).
            SyncRoots(e, v, size);
        }

        private void SyncRoots(Entity e, GameObject v, float size)
        {
            var rootGo = v.transform.Find("Roots");
            bool show = _xray && _em.GetComponentData<GenomeHeader>(e).Kingdom == GeneKingdom.Plant && size > 0.05f;
            if (!show)
            {
                if (rootGo != null) Destroy(rootGo);
                return;
            }
            if (rootGo == null)
            {
                rootGo = new GameObject("Roots");
                rootGo.transform.SetParent(v.transform, false);
                rootGo.AddComponent<MeshFilter>();
                var mr = rootGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
            }
            MeshDescriptor roots = OrganismMeshManager.GetRoots(_em, e);
            if (roots == null) return;
            var mf = rootGo.GetComponent<MeshFilter>();
            if (mf.sharedMesh == null)
            {
                uint rootsHash = GenomeMath.HashPhenotype(_em.GetComponentData<PhenotypeData>(e).Value);
                mf.sharedMesh = OrganismMeshPool.GetOrBuild(roots, rootsHash, 0, GeneKingdom.Plant, 0);
            }
        }

        private GameObject EnsureVisual(Entity e)
        {
            if (_visuals.TryGetValue(e, out GameObject v) && v != null) return v;
            if (_visualRoot == null) _visualRoot = new GameObject("Organism visuals");
            v = new GameObject("Organism");
            v.transform.SetParent(_visualRoot.transform, false);
            var mf = v.AddComponent<MeshFilter>();
            var mr = v.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            _visuals[e] = v;
            return v;
        }

        private void ForgetVisual(Entity e)
        {
            if (_visuals.TryGetValue(e, out GameObject v) && v != null)
            {
                Destroy(v);
                _visuals[e] = null;
            }
        }

        private void UpdateOrbitCamera()
        {
            if (_camera == null)
            {
                Camera main = Camera.main;
                if (main != null)
                {
                    _camera = main;
                }
                else
                {
                    var go = new GameObject("Viewer camera");
                    go.tag = "MainCamera";
                    _camera = go.AddComponent<Camera>();
                    _camera.clearFlags = CameraClearFlags.SolidColor;
                    _camera.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
                    go.AddComponent<AudioListener>();
                }
            }

            if (Input.GetMouseButton(0))
            {
                _yaw += Input.GetAxis("Mouse X") * _orbitSensitivity;
                _pitch = Mathf.Clamp(_pitch + Input.GetAxis("Mouse Y") * _orbitSensitivity, -5f, 85f);
            }
            if (Input.GetMouseButton(2) || Input.GetMouseButton(1))
            {
                Vector3 right = Vector3.right * _camera.transform.rotation;
                Vector3 up = Vector3.up * _camera.transform.rotation;
                _orbitTarget -= right * Input.GetAxis("Mouse X") * _dist * 0.25f;
                _orbitTarget += up * Input.GetAxis("Mouse Y") * _dist * 0.25f;
            }
            _dist = Mathf.Clamp(_dist - Input.mouseScrollDelta.y * 0.5f, 0.5f, 40f);

            float yaw = math.radians(_yaw), pitch = math.radians(_pitch);
            Vector3 offset = new Vector3(
                Mathf.Cos(pitch) * Mathf.Sin(yaw),
                Mathf.Sin(pitch),
                Mathf.Cos(pitch) * Mathf.Cos(yaw)) * _dist;
            Vector3 desired = _orbitTarget + offset;
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, desired, 0.35f);
            _camera.transform.LookAt(_orbitTarget);
        }

        // ── stats ────────────────────────────────────────────────────

        private void RefreshStats()
        {
            if (_specimen == Entity.Null || !_em.Exists(_specimen)) { _statsLine = "—"; return; }
            var header = _em.GetComponentData<GenomeHeader>(_specimen);
            var ph = _em.GetComponentData<PhenotypeData>(_specimen);
            var stage = _em.GetComponentData<LifeStageData>(_specimen);
            uint hash = GenomeMath.HashPhenotype(ph.Value);

            int t0 = 0, t1 = 0, t2 = 0, t3 = 0;
            var d0 = OrganismMeshBuilder.Build(ph.Value, header.Kingdom, 0, header.GenomeSeed);
            var d1 = OrganismMeshBuilder.Build(ph.Value, header.Kingdom, 1, header.GenomeSeed);
            var d2 = OrganismMeshBuilder.Build(ph.Value, header.Kingdom, 2, header.GenomeSeed);
            var d3 = OrganismMeshBuilder.Build(ph.Value, header.Kingdom, 3, header.GenomeSeed);
            t0 = d0.TriCount; t1 = d1.TriCount; t2 = d2.TriCount; t3 = d3.TriCount;

            var pool = OrganismMeshPool.GetStats();
            int budget = header.Kingdom == GeneKingdom.Animal ? 300 : 400;
            _statsLine =
                $"Stage {stage.Stage} ({stage.StageProgress:P0})  size {ph.Value.Size:P0}  hash {hash:X8}\n" +
                $"Tris  LOD0 {t0}/{budget}  LOD1 {t1}/60  LOD2 {t2}/12  LOD3 {t3}/2\n" +
                $"Pool  builds {pool.BuildCount}  hits {pool.HitCount}  alive {pool.PoolSize}" +
                (_galleryMode ? $"\n{_galleryStatus}" : "");
        }

        // ── IMGUI ────────────────────────────────────────────────────

        private string _statusLine = "Ready.";

        private void SetStatus(string s) { _statusLine = s; }

        private void OnGUI()
        {
            if (_world == null) return;
            GUI.BeginGroup(new Rect(8, 8, 340, Screen.height - 16));

            GUILayout.Label("ECOSPHERE — ORGANISM VIEWER", GUI.skin.GetStyle("Legend"));
            GUILayout.Label(_statusLine);
            GUILayout.Space(4);

            // Kingdom + genome controls.
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_kingdom == GeneKingdom.Plant ? "◀ Plant" : "Plant"))
                SpawnSpecimen(GeneKingdom.Plant, NextSeed());
            if (GUILayout.Button(_kingdom == GeneKingdom.Animal ? "Animal ▶" : "Animal"))
                SpawnSpecimen(GeneKingdom.Animal, NextSeed());
            if (GUILayout.Button("Randomize")) Randomize();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Mutate −")) Mutate(-1);
            if (GUILayout.Button("Mutate +")) Mutate(+1);
            if (GUILayout.Button("Regenerate")) Regenerate();
            EditorGUILayout.EndHorizontal();

            // Age.
            float lifespan = LifespanTicks();
            if (_specimen != Entity.Null && _em.Exists(_specimen))
            {
                LifeStageData stage = _em.GetComponentData<LifeStageData>(_specimen);
                _ageFraction = GUILayout.HorizontalSlider(_ageFraction, 0f, 1f,
                    new GUIContent($"Age {stage.Stage}"));
                GUILayout.Label($"day {(_ageFraction * lifespan / 1200f):0.0} / {(lifespan / 1200f):0.0}  (seed {_specimenSeed:X8})");
            }
            GUILayout.Space(2);

            // Climate.
            GUILayout.Label("Environment (manual EMA)", GUI.skin.GetStyle("BoldLabel"));
            _envTemp = GUILayout.HorizontalSlider(_envTemp, -40f, 50f, new GUIContent($"Temp  {_envTemp:0.0}°C"));
            _envLight = GUILayout.HorizontalSlider(_envLight, 0f, 1f, new GUIContent($"Light  {_envLight:0.2}"));
            _envWind = GUILayout.HorizontalSlider(_envWind, 0f, 20f, new GUIContent($"Wind   {_envWind:0.0} m/s"));
            _envMoisture = GUILayout.HorizontalSlider(_envMoisture, 0f, 1f, new GUIContent($"Moist  {_envMoisture:0.2}"));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cold")) { _envTemp = -18f; _envLight = 0.3f; _envWind = 6f; _envMoisture = 0.5f; ApplyManualEnv(); }
            if (GUILayout.Button("Hot")) { _envTemp = 42f; _envLight = 0.9f; _envWind = 3f; _envMoisture = 0.2f; ApplyManualEnv(); }
            if (GUILayout.Button("Dry")) { _envMoisture = 0.05f; _envTemp = 30f; ApplyManualEnv(); }
            if (GUILayout.Button("Windy")) { _envWind = 18f; ApplyManualEnv(); }
            if (GUILayout.Button("Neutral")) { _envTemp = 18f; _envLight = 0.6f; _envWind = 2f; _envMoisture = 0.5f; ApplyManualEnv(); }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            _xray = GUILayout.Toggle(_xray, "X-ray roots");
            for (int l = 0; l < 4; l++)
            {
                if (GUILayout.Button($"LOD{l}")) _lod = l;
            }
            if (GUILayout.Button(_galleryMode ? "Hide gallery" : "Gallery 2×24")) ToggleGallery();
            EditorGUILayout.EndHorizontal();

            // Gene list.
            GUILayout.Label("Genes", GUI.skin.GetStyle("BoldLabel"));
            DrawGeneList();

            // Mesh stats.
            GUILayout.Label("Mesh stats", GUI.skin.GetStyle("BoldLabel"));
            GUILayout.Label(_statsLine);

            // Export.
            if (GUILayout.Button("Export genome JSON")) ExportJson();
            GUILayout.Label($"Export: {_exportPath}");

            GUILayout.Label("LMB orbit · RMB/MMB pan · wheel zoom", GUI.skin.GetStyle("HelpBox"));
            GUI.EndGroup();
        }

        private Vector2 _geneScroll;

        private void DrawGeneList()
        {
            if (_specimen == Entity.Null) return;
            _geneScroll = GUILayout.BeginScrollView(_geneScroll, GUILayout.Height(180));
            foreach (var g in _specimenGenes)
            {
                GeneDefinition def = _catalog.Get(g.TypeId);
                float expressed = GenomeEvolutionMath.ExpressAllele(g.Value, g.Dominance, def.Default);
                float mapped = _catalog.MapToRange(g.TypeId, expressed);
                GUILayout.Label($"{def.Name,-22} allele {g.Value:0.00} dom {g.Dominance:0.00} → {mapped:0.00}  ({def.Min:0.##}..{def.Max:0.##})");
            }
            GUILayout.EndScrollView();
        }

        private void ExportJson()
        {
            if (_specimen == Entity.Null) return;
            string json = GenomeJson.Export(_specimenGenes, _kingdom, _specimenSeed);
            try
            {
                string dir = Application.streamingAssetsPath;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _exportPath = Path.Combine(dir, "organism_genome.json");
                File.WriteAllText(_exportPath, json);
                SetStatus($"Genome exported ({json.Length} bytes)");
            }
            catch (Exception ex)
            {
                SetStatus($"Export failed: {ex.Message}");
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}