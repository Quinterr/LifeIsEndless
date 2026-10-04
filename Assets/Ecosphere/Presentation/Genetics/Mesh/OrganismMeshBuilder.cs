// Ecosphere — OrganismMeshBuilder: phenotype → MeshDescriptor (parametric primitives).
//
// Parts grammar (Docs/genetics.md §Mesh grammar):
//  Animals: torso chain + head + jaw/eyes/ears/antennae + limb chains + tail +
//           fins + cover details (shell/spines/feather tufts). Embryo = egg.
//  Plants:  L-system expansion → prism stem segments + crossed-quad leaves +
//           octahedron flowers/fruit. Seed stage = seed ellipsoid. Roots are a
//           separate descriptor, shown only in x-ray debug mode.
//
// LODs (hard triangle budgets):
//  LOD0: 300 tris (animals) / 400 (plants)
//  LOD1: 60   LOD2: 12   LOD3: 2 (impostor billboard from LOD0 snapshot)
// The builder enforces budgets with a tri-count guard: detail is cut
// (cover → sensors → segments), never character (torso/head/stem stay).
//
// Determinism: for a fixed (phenotype, size, genomeSeed, lod) the descriptor is
// byte-identical. All symmetry-breaking jitter draws from
// RngState.Create(genomeSeed, stream) — never unseeded randomness.

using System;
using System.Collections.Generic;
using Ecosphere.Core.Simulation;
using Unity.Mathematics;

namespace Ecosphere.Presentation.Genetics
{
    /// <summary>Per-part allometric scale (maturity 0..1 for a body part).</summary>
    internal static class PartScale
    {
        public static float Animal(Phenotype p, string part)
        {
            float e = part switch
            {
                "head" => MorphogenMath.AnimalAllometry.Head,
                "torso" => MorphogenMath.AnimalAllometry.Torso,
                "limbs" => MorphogenMath.AnimalAllometry.Limbs,
                "tail" => MorphogenMath.AnimalAllometry.Tail,
                "sensors" => MorphogenMath.AnimalAllometry.Sensors,
                "neck" => MorphogenMath.AnimalAllometry.Neck,
                "cover" => MorphogenMath.AnimalAllometry.Cover,
                _ => 1f
            };
            return Math.Max(0.05f, MorphogenMath.AllometricScale(p.Size, e));
        }

        public static float Plant(Phenotype p, string part)
        {
            float e = part switch
            {
                "stem" => MorphogenMath.PlantAllometry.Stem,
                "leaves" => MorphogenMath.PlantAllometry.Leaves,
                "branches" => MorphogenMath.PlantAllometry.Branches,
                "roots" => MorphogenMath.PlantAllometry.Roots,
                "flowers" => MorphogenMath.PlantAllometry.Flowers,
                "fruit" => MorphogenMath.PlantAllometry.Fruit,
                _ => 1f
            };
            return Math.Max(0.05f, MorphogenMath.AllometricScale(p.Size, e));
        }
    }

    /// <summary>
    /// Builds organism meshes. Static + allocation-light (descriptor arrays are
    /// the only managed memory; the hot path in the pool reuses Unity meshes).
    /// </summary>
    public static class OrganismMeshBuilder
    {
        // ── Public entry points ──────────────────────────────────────

        /// <summary>Build the visible body mesh at the given LOD (0..3).</summary>
        public static MeshDescriptor Build(Phenotype p, GeneKingdom kingdom, int lod, uint genomeSeed)
        {
            if (lod == 3) return BuildImpostor(p, kingdom, genomeSeed);
            int budget = lod switch
            {
                0 => kingdom == GeneKingdom.Animal ? Budget.Lod0Animal : Budget.Lod0Plant,
                1 => Budget.Lod1,
                _ => Budget.Lod2
            };
            var builder = new MeshBuilder(budget);
            RngState rng = RngState.Create(genomeSeed, StreamIds.Fnv1a("Organism.Mesh"));
            if (lod == 2)
            {
                BuildTiny(builder, p, kingdom, ref rng);
            }
            else
            {
                BuildBody(builder, p, kingdom, lod, ref rng);
            }
            return builder.Finish();
        }

        /// <summary>
        /// LOD2 (≤12 tris): one octahedron body + a 2-tri tail/blade silhouette.
        /// Reads at distance; character comes from size + color.
        /// </summary>
        private static void BuildTiny(MeshBuilder b, Phenotype p, GeneKingdom kingdom, ref RngState rng)
        {
            float scale = (0.25f + 0.75f * p.Size) * math.clamp(p.SizeMultiplier, 0.2f, 3f);
            VertexColor c = PatternColor(p, new float3(0, scale * 0.5f, 0), math.max(0.2f, scale));
            if (kingdom == GeneKingdom.Animal)
            {
                float3 center = new float3(0, scale * 0.5f, 0);
                float len = 0.8f * scale * math.clamp(p.TorsoLength, 0.3f, 3f);
                Octahedron(b, center, new float3(scale * 0.22f, scale * 0.3f, len * 0.5f), c);
                if (p.TailLength > 0.1f)
                {
                    float3 t0 = center + new float3(0, -scale * 0.1f, -len * 0.5f);
                    float3 t1 = t0 + new float3(0, -scale * 0.25f, -len * 0.4f);
                    Quad(b, t0 + new float3(0.02f, 0, 0), t0 - new float3(0.02f, 0, 0), t1 + new float3(0.01f, 0, 0), t1, c);
                }
            }
            else
            {
                float h = 0.7f * scale * math.clamp(p.StemHeight, 0.2f, 5f);
                float r = 0.25f * scale;
                Octahedron(b, new float3(0, h * 0.55f, 0), new float3(r, h * 0.5f, r), c);
                // Blade: a single upward quad for the stem silhouette.
                Quad(b, new float3(0, 0, 0) + new float3(0.03f, 0, 0), new float3(-0.03f, 0, 0),
                    new float3(0, h * 0.95f, 0.02f), new float3(0, h * 0.6f, -0.02f), c);
            }
        }

        /// <summary>Build the root system (plants only; x-ray debug mode).</summary>
        public static MeshDescriptor BuildRoots(Phenotype p, uint genomeSeed)
        {
            var builder = new MeshBuilder(80);
            RngState rng = RngState.Create(genomeSeed, StreamIds.Fnv1a("Organism.MeshRoots"));
            BuildPlantRoots(builder, p, ref rng);
            return builder.Finish();
        }

        // ── Body dispatch ────────────────────────────────────────────

        private static void BuildBody(MeshBuilder b, Phenotype p, GeneKingdom kingdom, int lod, ref RngState rng)
        {
            if (kingdom == GeneKingdom.Animal)
            {
                if (p.Size < 0.06f) BuildEgg(b, p, ref rng);
                else BuildAnimal(b, p, lod, ref rng);
            }
            else
            {
                if (p.Size < 0.05f) BuildSeed(b, p, ref rng);
                else BuildPlant(b, p, lod, ref rng);
            }
        }

        // ═══════════════════════════ ANIMALS ═══════════════════════════

        private static void BuildEgg(MeshBuilder b, Phenotype p, ref RngState rng)
        {
            float s = math.clamp(p.Size * 4f, 0.1f, 0.6f);
            float r = (0.10f + 0.12f * p.EggSeedSize) * s;
            var c = PigmentMath.PrimaryColor(p.PigmentRed, p.PigmentGreen, p.PigmentBlue);
            c = PigmentMath.Lerp(c, new VertexColor(1f, 0.95f, 0.85f), 0.35f);
            Icosphere(b, new float3(0, r, 0), new float3(r * 0.85f, r, r * 0.85f), c);
        }

        private static void BuildAnimal(MeshBuilder b, Phenotype p, int lod, ref RngState rng)
        {
            float size = p.Size;
            float scale = (0.25f + 0.75f * size) * math.clamp(p.SizeMultiplier, 0.2f, 3f);
            float body = scale * math.clamp(p.BodyLengthScale, 0.3f, 3f);
            float headS = PartScale.Animal(p, "head");
            float torsoS = PartScale.Animal(p, "torso");
            float limbS = PartScale.Animal(p, "limbs");
            float tailS = PartScale.Animal(p, "tail");
            float sensorS = PartScale.Animal(p, "sensors");
            float coverS = PartScale.Animal(p, "cover");

            int sides = lod <= 0 ? 6 : 4;
            float girth = math.clamp(p.TorsoGirth, 0.1f, 2.5f);
            float radius = 0.16f * girth * body * torsoS;
            int segments = Math.Clamp(MathfRound(p.TorsoSegments), lod <= 0 ? 2 : 1, lod <= 0 ? 8 : 4);
            float torsoLen = 0.8f * body * math.clamp(p.TorsoLength, 0.3f, 3f);
            float limbPairs = Math.Clamp(MathfRound(p.LimbCount), 0, 4);

            // Leg height keeps the torso above the ground.
            float legLen = limbPairs > 0 ? 0.5f * math.clamp(p.LimbLength, 0.2f, 2f) * body * limbS : 0f;
            float torsoY = radius + math.max(legLen, radius * 0.6f);

            float flattenX = 1f - 0.5f * math.clamp(p.TorsoFlatten, 0f, 1f);

            // ── Torso chain ──────────────────────────────────────────
            for (int i = 0; i < segments; i++)
            {
                if (!b.CanAdd(sides * 2)) break;
                float t = segments > 1 ? (float)i / (segments - 1) : 0.5f;
                float z = -torsoLen * 0.5f + torsoLen * (i + 0.5f) / segments;
                float taper = 1f - 0.45f * t; // posterior taper
                float r = radius * taper;
                float segLen = torsoLen / segments * 1.08f;
                Prism(b, new float3(0, torsoY, z - segLen * 0.5f), new float3(0, torsoY, z + segLen * 0.5f),
                    r * flattenX, r, sides, BodyColor(p, 0, torsoY, z));
            }

            // ── Head ─────────────────────────────────────────────────
            float headR = 0.22f * math.clamp(p.HeadSize, 0.2f, 1.6f) * body * headS;
            float neck = math.clamp(p.NeckLength, 0f, 1.5f) * 0.25f * body * PartScale.Animal(p, "neck");
            float headZ = torsoLen * 0.5f + neck + headR * 0.8f;
            float3 headC = new float3(0, torsoY + headR * 0.15f, headZ);
            if (neck > headR * 0.2f && b.CanAdd(2 * 4))
            {
                Prism(b, new float3(0, torsoY, torsoLen * 0.5f), headC - new float3(0, 0, headR * 0.5f),
                    radius * 0.5f * flattenX, radius * 0.5f, 4, BodyColor(p, 0, torsoY, torsoLen * 0.5f));
            }

            float headRy = headR * (1f - 0.55f * math.clamp(p.HeadFlatten, 0f, 1f));
            float headRz = headR * (1f + 0.45f * math.clamp(p.HeadFlatten, 0f, 1f));
            if (lod <= 1)
            {
                var headColor = PigmentMath.Lerp(BodyColor(p, 0, headC.y, headC.z), new VertexColor(1f, 1f, 1f), 0.12f);
                if (lod == 0) Icosphere(b, headC, new float3(headR * flattenX, headRy, headRz), headColor);
                else Octahedron(b, headC, new float3(headR * flattenX, headRy, headRz), headColor);
            }

            // ── Mouth / jaw ──────────────────────────────────────────
            if (lod == 0 && p.JawType > 0.05f)
            {
                float mouthR = 0.10f * math.clamp(p.MouthSize, 0f, 1f) * body * headS;
                float3 mouthBase = headC + new float3(0, -headR * 0.35f, headRz * 0.8f);
                if (p.JawType < 0.4f)
                {
                    // Beak: forward cone.
                    Cone(b, mouthBase, mouthBase + new float3(0, 0, 0.5f * (mouthR * 3f + 0.05f)), mouthR, 4,
                        BodyColor(p, mouthBase.x, mouthBase.y, mouthBase.z), false);
                }
                else if (p.JawType < 0.6f)
                {
                    // Mammal jaw: box.
                    Box(b, mouthBase + new float3(0, -mouthR * 0.4f, mouthR * 0.4f),
                        new float3(mouthR * 0.8f, mouthR * 0.5f, mouthR * 1.4f),
                        BodyColor(p, mouthBase.x, mouthBase.y, mouthBase.z));
                }
                else
                {
                    // Mandibles: two thin cones forward-down.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Cone(b, mouthBase + new float3(side * mouthR * 0.7f, 0, 0),
                            mouthBase + new float3(side * mouthR * 1.2f, -mouthR * 0.8f, mouthR * 2f),
                            mouthR * 0.28f, 3, BodyColor(p, mouthBase.x, mouthBase.y, mouthBase.z), false);
                    }
                }
            }

            // ── Eyes ─────────────────────────────────────────────────
            if (lod <= 1 && p.EyeSize > 0.03f)
            {
                int eyes = Math.Clamp(MathfRound(p.EyeCount), 0, 8);
                if (eyes > 0)
                {
                    float forward = math.clamp(p.EyeForwardAngle, 0f, 1f);
                    var eyeColor = p.Bioluminescence > 0.1f
                        ? new VertexColor(0.55f, 1f, 0.95f)
                        : new VertexColor(0.08f, 0.07f, 0.1f);
                    for (int i = 0; i < eyes; i++)
                    {
                        if (!b.CanAdd(4)) break;
                        // Distribute around the head: side-biased by EyeForwardAngle.
                        float sideSign = i % 2 == 0 ? 1f : -1f;
                        float vert = (float)(i / 2) * 0.35f - 0.1f;
                        float3 rightDir = new float3(sideSign, 0, 0);
                        float3 fwdDir = new float3(0, 0, 1);
                        float3 eyeDir = math.normalizesafe(rightDir * (1f - forward) + fwdDir * forward + new float3(0, vert, 0));
                        float3 eyeC = headC + eyeDir * math.max(headR * 0.95f, 0.03f);
                        float eyeR = math.max(0.02f, 0.045f * math.clamp(p.EyeSize, 0f, 1f) * body * sensorS);
                        Cone(b, eyeC - eyeDir * eyeR * 0.3f, eyeC + eyeDir * eyeR * 2.2f, eyeR, 4, eyeColor, false);
                    }
                }
            }

            // ── Ears / antennae ──────────────────────────────────────
            if (lod == 0)
            {
                if (p.EarSize > 0.05f)
                {
                    var earColor = BodyColor(p, 0, headC.y, headC.z);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!b.CanAdd(3)) break;
                        float earLen = 0.3f * math.clamp(p.EarSize, 0f, 1f) * body * sensorS;
                        float3 earBase = headC + new float3(side * headR * 0.6f, headR * 0.7f, 0);
                        float3 earTip = earBase + new float3(side * earLen * 0.6f, earLen, earLen * 0.15f);
                        Cone(b, earBase, earTip, math.max(0.015f, headR * 0.25f), 3, earColor, false);
                    }
                }
                if (p.AntennaLength > 0.05f)
                {
                    var antColor = BodyColor(p, 0, headC.y, headC.z);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!b.CanAdd(8)) continue;
                        float seg1 = 0.35f * math.clamp(p.AntennaLength, 0f, 1.5f) * body * sensorS;
                        float bend = 0.4f + p.LimbJointness * 0.6f;
                        float3 a0 = headC + new float3(side * headR * 0.35f, headR * 0.8f, headR * 0.5f);
                        float3 a1 = a0 + new float3(side * seg1 * 0.3f, seg1 * bend, seg1 * 0.4f);
                        float3 a2 = a1 + new float3(side * seg1 * 0.2f, seg1 * bend * 0.6f, seg1 * 0.7f);
                        Prism(b, a0, a1, math.max(0.008f, headR * 0.09f), math.max(0.008f, headR * 0.09f), 3, antColor);
                        Prism(b, a1, a2, math.max(0.006f, headR * 0.06f), math.max(0.006f, headR * 0.06f), 3, antColor);
                    }
                }
            }

            // ── Limbs ────────────────────────────────────────────────
            if (limbPairs > 0)
            {
                for (int pair = 0; pair < limbPairs; pair++)
                {
                    float f = (pair + 0.5f) / limbPairs;
                    float segFactor = MorphogenMath.SegmentSizeFactor(1f - f); // front limbs slightly larger
                    float attachZ = -torsoLen * 0.5f + torsoLen * f * 0.85f;
                    float rLimb = math.max(0.015f, 0.035f * math.clamp(p.LimbThickness, 0.05f, 0.6f) * body * limbS * segFactor);
                    float upperLen = 0.5f * math.clamp(p.LimbLength, 0.2f, 2f) * body * limbS;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        int limbCost = lod == 0 ? 16 : 8;
                        if (!b.CanAdd(limbCost)) continue;
                        float3 hip = new float3(side * radius * 0.7f * flattenX, torsoY - radius * 0.4f, attachZ);
                        float outAngle = math.radians(35f + p.LimbJointness * 35f);
                        float3 knee = hip + new float3(side * math.sin(outAngle) * upperLen, -math.cos(outAngle) * upperLen, 0);
                        float3 foot = knee + new float3(side * p.LimbWebbing * 0.15f, math.min(0, -torsoY + knee.y + 0.02f), 0);
                        Prism(b, hip, knee, rLimb, rLimb, lod == 0 ? 4 : 3, BodyColor(p, hip.x, hip.y, hip.z));
                        Prism(b, knee, foot, rLimb * 0.8f, rLimb * 0.8f, lod == 0 ? 4 : 3, BodyColor(p, knee.x, knee.y, knee.z));
                        if (lod == 0)
                        {
                            if (p.LimbWebbing > 0.4f)
                            {
                                // Webbed foot: flat fan.
                                float w = rLimb * 4f * (0.5f + p.LimbWebbing);
                                Quad(b, foot + new float3(-w * 0.5f, 0, -w * 0.3f), foot + new float3(w * 0.5f, 0, -w * 0.3f),
                                    foot + new float3(side * w * 0.5f, 0, w * 0.6f), foot + new float3(0, 0.01f, w * 0.2f),
                                    BodyColor(p, foot.x, 0, foot.z));
                            }
                            else
                            {
                                Box(b, foot + new float3(0, 0.015f, 0), new float3(rLimb * 1.4f, 0.02f, rLimb * 2f),
                                    BodyColor(p, foot.x, 0, foot.z));
                            }
                        }
                    }
                }
            }

            // ── Tail ────────────────────────────────────────────────
            if (p.TailLength > 0.05f)
            {
                float tailLen = 0.5f * math.clamp(p.TailLength, 0f, 2f) * body * tailS;
                float3 t0 = new float3(0, torsoY - radius * 0.1f, -torsoLen * 0.5f);
                int tailSegs = lod == 0 ? 2 : 1;
                float3 from = t0;
                for (int i = 0; i < tailSegs && b.CanAdd(5); i++)
                {
                    float segLen = tailLen / tailSegs;
                    float droop = math.radians(18f + i * 14f);
                    float3 to = from + new float3(0, -math.sin(droop) * segLen, -math.cos(droop) * segLen);
                    float r = radius * 0.7f * (1f - 0.6f * (i + 1f) / tailSegs);
                    Cone(b, from, to, math.max(0.01f, r), lod == 0 ? 5 : 3,
                        BodyColor(p, from.x, from.y, from.z), false);
                    from = to;
                }
            }

            // ── Fins / wings ─────────────────────────────────────────
            if (lod == 0 && p.FinSize > 0.05f)
            {
                int finPairs = Math.Clamp(MathfRound(p.FinCount), 1, 4);
                float finLen = 0.35f * math.clamp(p.FinSize, 0f, 1.5f) * body;
                float wingUp = p.Symmetry > 0.6f ? 0.5f : 0f; // radial "insects" get wings
                for (int pair = 0; pair < finPairs && b.CanAdd(16); pair++)
                {
                    float f = (pair + 0.5f) / finPairs;
                    float z = -torsoLen * 0.3f + torsoLen * f * 0.7f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float3 base_ = new float3(side * radius * 0.7f, torsoY + radius * (0.2f + wingUp * 0.5f), z);
                        float3 tip = base_ + new float3(side * finLen * (0.6f + wingUp * 0.3f), finLen * wingUp, finLen * 0.4f);
                        CrossedQuad(b, base_, (tip - base_) * 0.5f, tip, finLen * 0.45f,
                            PigmentMath.Lerp(BodyColor(p, base_.x, base_.y, z), new VertexColor(1f, 1f, 1f), 0.3f));
                    }
                }
            }

            // ── Cover details (cut first when budget runs out) ───────
            BuildCover(b, p, coverS, radius, torsoLen, torsoY, lod);
        }

        private static void BuildCover(MeshBuilder b, Phenotype p, float coverS,
            float radius, float torsoLen, float torsoY, int lod)
        {
            if (lod != 0) return;
            float type = math.clamp(p.IntegumentType, 0f, 1f);
            float density = math.clamp(p.IntegumentDensity, 0f, 1f);
            if (density < 0.1f) return;

            if (type < 0.16f) return; // skin: no extra geometry (documented)

            if (type >= 0.66f && type < 0.84f)
            {
                // Shell: half-icosphere dome on the back.
                if (b.CanAdd(10))
                {
                    float shellR = radius * (1.15f + 0.5f * math.clamp(p.ShellThickness, 0f, 1f)) * coverS;
                    var shellColor = PigmentMath.Lerp(new VertexColor(0.62f, 0.56f, 0.42f),
                        BodyColor(p, 0, torsoY, 0), 0.4f);
                    IcosphereTop(b, new float3(0, torsoY, 0), new float3(shellR, shellR * 0.8f, shellR * 1.1f), shellColor);
                }
                return;
            }

            int n = Math.Clamp(MathfRound(3 + density * 10), 3, 12);
            if (type >= 0.84f && p.SpineLength > 0.03f)
            {
                // Spines along the back.
                var color = PigmentMath.Lerp(BodyColor(p, 0, torsoY + radius, 0), new VertexColor(1f, 1f, 1f), 0.4f);
                for (int i = 0; i < n; i++)
                {
                    if (!b.CanAdd(4)) break;
                    float z = -torsoLen * 0.45f + torsoLen * 0.9f * (float)i / Math.Max(1, n - 1);
                    float len = 0.25f * math.clamp(p.SpineLength, 0f, 1f) * (0.4f + radius * 2f) * coverS;
                    float3 base_ = new float3(0, torsoY + radius * 0.9f, z);
                    Cone(b, base_, base_ + new float3(0, len, -len * 0.3f), math.max(0.01f, radius * 0.12f), 4, color, false);
                }
            }
            else if (type >= 0.34f && type < 0.5f)
            {
                // Feather tufts.
                var color = PigmentMath.Lerp(BodyColor(p, 0, torsoY + radius, 0), new VertexColor(1f, 1f, 1f), 0.25f);
                for (int i = 0; i < n; i++)
                {
                    if (!b.CanAdd(8)) break;
                    float z = -torsoLen * 0.4f + torsoLen * 0.8f * (float)i / Math.Max(1, n - 1);
                    float tuft = 0.12f * (0.3f + radius * 1.5f) * coverS;
                    float3 c = new float3(0, torsoY + radius * 1.05f, z);
                    CrossedQuad(b, c, new float3(0, tuft, 0), c + new float3(0, tuft * 1.6f, tuft * 0.4f), tuft * 0.5f, color);
                }
            }
            else if (type >= 0.5f && type < 0.66f)
            {
                // Fur tufts (single quads).
                var color = PigmentMath.Lerp(BodyColor(p, 0, torsoY + radius, 0), new VertexColor(1f, 1f, 1f), 0.15f);
                for (int i = 0; i < n; i++)
                {
                    if (!b.CanAdd(4)) break;
                    float z = -torsoLen * 0.4f + torsoLen * 0.8f * (float)i / Math.Max(1, n - 1);
                    float tuft = 0.1f * (0.3f + radius * 1.5f) * coverS;
                    float3 c = new float3(0, torsoY + radius * 0.95f, z);
                    Quad(b, c + new float3(-tuft * 0.4f, 0, -tuft * 0.3f), c + new float3(tuft * 0.4f, 0, -tuft * 0.3f),
                        c + new float3(0, tuft, tuft * 0.2f), c + new float3(0, tuft * 0.3f, tuft * 0.5f), color);
                }
            }
            // Scales (0.16..0.34): color-only treatment (documented simplification).
        }

        // ═══════════════════════════ PLANTS ═══════════════════════════

        private static void BuildSeed(MeshBuilder b, Phenotype p, ref RngState rng)
        {
            float s = math.clamp(p.Size * 6f, 0.1f, 0.5f);
            float r = (0.05f + 0.06f * p.SeedSize) * s;
            var c = PigmentMath.PrimaryColor(p.PigmentRed, p.PigmentGreen, p.PigmentBlue);
            c = PigmentMath.Lerp(c, new VertexColor(0.55f, 0.42f, 0.28f), 0.5f);
            Icosphere(b, new float3(0, r * 0.7f, 0), new float3(r * 0.8f, r, r * 0.8f), c);
        }

        private static void BuildPlant(MeshBuilder b, Phenotype p, int lod, ref RngState rng)
        {
            float size = p.Size;
            float scale = (0.2f + 0.8f * size) * math.clamp(p.SizeMultiplier, 0.2f, 3f);
            float stemS = PartScale.Plant(p, "stem");
            float leafS = PartScale.Plant(p, "leaves");
            float flowerS = PartScale.Plant(p, "flowers");

            int sides = lod <= 0 ? 5 : 4;
            float stemH = 0.7f * math.clamp(p.StemHeight, 0.2f, 5f) * scale * stemS;
            float stemR = math.max(0.02f, 0.05f * math.clamp(p.StemThickness, 0.05f, 1f) * scale * (0.6f + p.Woodiness * 0.6f));

            List<LCommand> commands = LSystemExpander.Expand(
                stemH, stemR,
                p.BranchingDepth, p.BranchingAngle, math.clamp(p.BranchLengthRatio, 0.3f, 0.9f),
                math.clamp(p.LeafSize, 0.1f, 2f) * 0.16f * scale * leafS,
                Math.Clamp(MathfRound(p.LeafCount), 1, 8),
                math.clamp(p.FlowerSize, 0f, 1f) * 0.14f * scale * flowerS,
                Math.Clamp(MathfRound(p.FlowerCount), 0, 12),
                math.clamp(p.FruitSize, 0f, 1f) * 0.1f * scale,
                math.clamp(p.NodeSpacing, 0.1f, 1f),
                math.clamp(p.CrownShape, 0f, 1f),
                math.clamp(p.GrowthHabit, 0f, 1f),
                size, ref rng);

            // Interpret the L-system into geometry.
            var pos = new float[commands.Count + 2];
            var fwd = new float[(commands.Count + 2) * 3];
            float px = 0, py = 0, pz = 0;
            float fx = 0, fy = 1, fz = 0;
            int sp = 0;
            var flowerColor = PigmentMath.SecondaryColor(p.SecondaryRed, p.SecondaryGreen, p.SecondaryBlue);
            var fruitColor = PigmentMath.PrimaryColor(math.max(p.PigmentRed, 0.5f), p.PigmentGreen * 0.3f, p.PigmentBlue * 0.2f);

            float leafAngle = math.radians(math.clamp(p.LeafAngle, 0f, 90f) + 20f);
            float leafLen = math.clamp(p.LeafSize, 0.1f, 2f) * 0.16f * scale * leafS;

            foreach (var cmd in commands)
            {
                switch (cmd.Instruction)
                {
                    case LInstruction.Forward:
                        {
                            float len = cmd.Length;
                            if (len < 0.004f) continue;
                            int cost = sides * 2;
                            if (!b.CanAdd(cost)) continue;
                            float3 from = new float3(px, py, pz);
                            float3 to = new float3(px + fx * len, py + fy * len, pz + fz * len);
                            Prism(b, from, to, cmd.Radius, cmd.Radius, sides,
                                PatternColor(p, to, stemH));
                            // Advance the pen along the local axis.
                            px += fx * len; py += fy * len; pz += fz * len;
                            break;
                        }
                    case LInstruction.Push:
                        if (sp >= pos.Length) break;
                        pos[sp] = px;
                        StoreFwd(fwd, sp, fx, fy, fz);
                        sp++;
                        break;
                    case LInstruction.Pop:
                        if (sp > 0) { sp--; px = pos[sp]; LoadFwd(fwd, sp, out fx, out fy, out fz); }
                        break;
                    case LInstruction.TurnLeft: Turn(ref fx, ref fz, -math.radians(cmd.Angle)); break;
                    case LInstruction.TurnRight: Turn(ref fx, ref fz, math.radians(cmd.Angle)); break;
                    case LInstruction.PitchUp:
                        {
                            float3 f = new float3(fx, fy, fz);
                            f = math.normalize(f + math.normalize(new float3(0, 1, 0) - f * fy) * math.sin(math.radians(cmd.Angle)));
                            (fx, fy, fz) = (f.x, f.y, f.z);
                            break;
                        }
                    case LInstruction.PitchDown:
                        {
                            float3 f = new float3(fx, fy, fz);
                            f = math.normalize(f - math.normalize(new float3(0, 1, 0) - f * fy) * math.sin(math.radians(cmd.Angle)));
                            (fx, fy, fz) = (f.x, f.y, f.z);
                            break;
                        }
                    case LInstruction.Leaf:
                        {
                            if (lod != 0 && b.TriCount > 40) break; // LOD1: sparse leaves
                            int cost = lod == 0 ? 8 : 4; // double-sided quads
                            if (!b.CanAdd(cost)) break;
                            float3 c = new float3(px, py, pz);
                            // Leaves droop by LeafAngle away from the stem direction.
                            float3 leafDir = math.normalizesafe(new float3(fx, fy, fz) + new float3(0, -1, 0) * math.tan(leafAngle) * 0.5f);
                            float3 leafTip = c + leafDir * leafLen;
                            if (lod == 0)
                                CrossedQuad(b, c, (leafTip - c) * 0.5f, leafTip, leafLen * 0.45f, PatternColor(p, c, stemH));
                            else
                                Quad(b, c + new float3(-leafLen * 0.25f, 0, 0), c + new float3(leafLen * 0.25f, 0, 0),
                                    leafTip, c + leafDir * leafLen * 0.5f, PatternColor(p, c, stemH));
                            break;
                        }
                    case LInstruction.Flower:
                        {
                            if (lod != 0 || !b.CanAdd(8)) break;
                            float3 c = new float3(px, py, pz) + new float3(fx, fy, fz) * cmd.Length * 0.5f;
                            Octahedron(b, c, new float3(cmd.Length, cmd.Length, cmd.Length), flowerColor);
                            // Petals: a few double-sided quads around the center.
                            int petals = Math.Clamp(MathfRound(p.FlowerPetalCount), 3, 8);
                            for (int q = 0; q < petals && b.CanAdd(4); q++)
                            {
                                float a = (float)q / petals * math.PI * 2f;
                                float3 pa = c + new float3(math.cos(a), 0.2f, math.sin(a)) * cmd.Length * 0.6f;
                                Quad(b, c + new float3(math.cos(a), 0.1f, math.sin(a)) * cmd.Length * 0.3f,
                                    pa, c + new float3(math.cos(a + 0.5f), 0.35f, math.sin(a + 0.5f)) * cmd.Length * 0.5f,
                                    c + new float3(math.cos(a - 0.5f), 0.35f, math.sin(a - 0.5f)) * cmd.Length * 0.5f,
                                    flowerColor);
                            }
                            break;
                        }
                    case LInstruction.Fruit:
                        {
                            if (lod != 0 || !b.CanAdd(8)) break;
                            float3 c = new float3(px, py, pz) + new float3(fx, fy, fz) * cmd.Length;
                            Octahedron(b, c, new float3(cmd.Length, cmd.Length * 1.1f, cmd.Length), fruitColor);
                            break;
                        }
                    case LInstruction.Branch:
                        {
                            // Tilt the axis away from the parent toward the horizontal
                            // direction at world yaw (works even when the parent axis
                            // is vertical, where Turn/Pitch are no-ops).
                            float yawRad = math.radians(cmd.Yaw);
                            float3 horizontal = new float3(math.cos(yawRad), 0f, math.sin(yawRad));
                            float a = math.radians(cmd.Angle);
                            float3 f = new float3(fx, fy, fz);
                            float3 nf = math.normalize(f * math.cos(a) + horizontal * math.sin(a));
                            (fx, fy, fz) = (nf.x, nf.y, nf.z);
                            break;
                        }
                    case LInstruction.BranchLevel:
                    case LInstruction.Taper:
                    case LInstruction.RollCW:
                        break; // no-op at this detail level (documented)
                }
            }
        }

        private static void StoreFwd(float[] buf, int i, float x, float y, float z)
        {
            buf[i * 3] = x; buf[i * 3 + 1] = y; buf[i * 3 + 2] = z;
        }

        private static void LoadFwd(float[] buf, int i, out float x, out float y, out float z)
        {
            x = buf[i * 3]; y = buf[i * 3 + 1]; z = buf[i * 3 + 2];
        }

        private static void Turn(ref float fx, ref float fz, float angle)
        {
            float c = math.cos(angle), s = math.sin(angle);
            float nfx = fx * c - fz * s;
            float nfz = fx * s + fz * c;
            fx = nfx; fz = nfz;
        }

        private static void BuildPlantRoots(MeshBuilder b, Phenotype p, ref RngState rng)
        {
            float scale = (0.2f + 0.8f * p.Size) * math.clamp(p.SizeMultiplier, 0.2f, 3f);
            float rootS = PartScale.Plant(p, "roots");
            float depth = 0.4f * math.clamp(p.RootDepth, 0f, 3f) * scale * rootS;
            float spread = 0.4f * math.clamp(p.RootSpread, 0f, 2f) * scale * rootS;
            float stemR = math.max(0.015f, 0.04f * math.clamp(p.StemThickness, 0.05f, 1f) * scale);
            var color = PigmentMath.Lerp(new VertexColor(0.35f, 0.26f, 0.18f), new VertexColor(0.2f, 0.15f, 0.1f), p.Woodiness);

            int roots = Math.Clamp(MathfRound(3 + p.RootSpread * 3f), 3, 6);
            for (int i = 0; i < roots && b.CanAdd(8); i++)
            {
                float a = (float)i / roots * math.PI * 2f + rng.NextFloat01() * 0.6f;
                float len = depth * (0.7f + rng.NextFloat01() * 0.5f);
                float3 from = new float3(0, 0, 0);
                float3 to = new float3(math.cos(a) * spread, -len, math.sin(a) * spread);
                float3 mid = (from + to) * 0.5f + new float3(0, len * 0.1f, 0);
                Prism(b, from, mid, stemR * 0.6f, stemR * 0.6f, 4, color);
                Prism(b, mid, to, stemR * 0.35f, stemR * 0.35f, 4, color);
            }
        }

        // ═══════════════════════════ LOD3 ════════════════════════════

        /// <summary>
        /// Impostor: a billboard quad tinted by the LOD0 snapshot's dominant color
        /// and sized by its bounds aspect (documented simplification of
        /// "billboard from generated mesh snapshot" — no render-target capture).
        /// </summary>
        private static MeshDescriptor BuildImpostor(Phenotype p, GeneKingdom kingdom, uint genomeSeed)
        {
            // Build a tiny LOD0 first to snapshot color/bounds.
            var full = Build(p, kingdom, 0, genomeSeed);
            var desc = new MeshDescriptor();
            float w = math.max(0.05f, full.MaxX - full.MinX);
            float h = math.max(0.05f, full.MaxY - full.MinY);
            float3 center = new float3((full.MinX + full.MaxX) * 0.5f, (full.MinY + full.MaxY) * 0.5f, (full.MinZ + full.MaxZ) * 0.5f);
            VertexColor c = full.DominantColor();
            desc.ExpandIfNeeded(4);
            desc.ResetBounds();
            float3 p0 = center + new float3(-w * 0.5f, -h * 0.5f, 0);
            float3 p1 = center + new float3(w * 0.5f, -h * 0.5f, 0);
            float3 p2 = center + new float3(w * 0.5f, h * 0.5f, 0);
            float3 p3 = center + new float3(-w * 0.5f, h * 0.5f, 0);
            float3 n = new float3(0, 0, 1);
            desc.AddVertex(p0, n, c); desc.AddVertex(p1, n, c); desc.AddVertex(p2, n, c);
            desc.AddVertex(p0, n, c); desc.AddVertex(p2, n, c); desc.AddVertex(p3, n, c);
            desc.Trim();
            return desc;
        }

        // ═══════════════════════════ helpers ═══════════════════════════

        /// <summary>Pattern + bioluminescence color at a mesh-space point.</summary>
        private static VertexColor PatternColor(Phenotype p, float3 pos, float heightSpan)
        {
            VertexColor primary = PigmentMath.PrimaryColor(p.PigmentRed, p.PigmentGreen, p.PigmentBlue);
            VertexColor secondary = PigmentMath.SecondaryColor(p.SecondaryRed, p.SecondaryGreen, p.SecondaryBlue);
            VertexColor c = PigmentMath.EvaluatePattern(primary, secondary,
                p.PatternType, math.clamp(p.PatternScale, 0.1f, 2f), p.PatternSymmetry,
                pos.x, math.max(0f, pos.y), pos.z, math.max(0.2f, heightSpan));
            return PigmentMath.ApplyBioluminescence(c, p.Bioluminescence);
        }

        private static VertexColor BodyColor(Phenotype p, float x, float y, float z)
            => PatternColor(p, new float3(x, y, z), 0.8f);

        private static int MathfRound(float v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

        // ── Primitive emitters (flat-shaded triangle list) ────────────

        private static void Tri(MeshBuilder b, float3 a, float3 c1, float3 c2, VertexColor color)
        {
            if (!b.CanAdd(1)) return;
            float3 n = math.cross(c1 - a, c2 - a);
            float len = math.length(n);
            n = len > 1e-8f ? n / len : new float3(0, 1, 0);
            b.AddTri(a, n, color);
            b.AddTri(c1, n, color);
            b.AddTri(c2, n, color);
        }

        /// <summary>
        /// Triangle for closed convex solids: flips winding so the flat normal
        /// always points away from the solid's center (no back-face culling holes,
        /// regardless of the topology table's winding).
        /// </summary>
        private static void TriOutward(MeshBuilder b, float3 a, float3 c1, float3 c2, float3 center, VertexColor color)
        {
            if (!b.CanAdd(1)) return;
            float3 n = math.cross(c1 - a, c2 - a);
            float len = math.length(n);
            n = len > 1e-8f ? n / len : new float3(0, 1, 0);
            float3 centroid = (a + c1 + c2) * 0.3333334f;
            if (math.dot(n, centroid - center) < 0f)
            {
                float3 tmp = c1; c1 = c2; c2 = tmp;
                n = -n;
            }
            b.AddTri(a, n, color);
            b.AddTri(c1, n, color);
            b.AddTri(c2, n, color);
        }

        /// <summary>
        /// Double-sided quad (both windings) — leaves, fins, and blades must be
        /// visible from both sides under a front-cull material.
        /// </summary>
        private static void QuadDS(MeshBuilder b, float3 a, float3 c1, float3 c2, float3 c3, VertexColor color)
        {
            if (!b.CanAdd(4)) return;
            Tri(b, a, c1, c2, color);
            Tri(b, a, c2, c3, color);
            Tri(b, a, c3, c2, color);
            Tri(b, a, c2, c1, color);
        }

        private static void Box(MeshBuilder b, float3 center, float3 half, VertexColor color)
        {
            if (!b.CanAdd(12)) return;
            float3 x0 = center - half, x1 = center + half;
            // +Z / -Z
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x1.x, x0.y, x1.z), new float3(x1.x, x1.y, x1.z), center, color);
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x1.x, x1.y, x1.z), new float3(x0.x, x1.y, x1.z), center, color);
            TriOutward(b, new float3(x1.x, x0.y, x0.z), new float3(x0.x, x0.y, x0.z), new float3(x0.x, x1.y, x0.z), center, color);
            TriOutward(b, new float3(x1.x, x0.y, x0.z), new float3(x0.x, x1.y, x0.z), new float3(x1.x, x1.y, x0.z), center, color);
            // +Y / -Y
            TriOutward(b, new float3(x0.x, x1.y, x0.z), new float3(x0.x, x1.y, x1.z), new float3(x1.x, x1.y, x1.z), center, color);
            TriOutward(b, new float3(x0.x, x1.y, x0.z), new float3(x1.x, x1.y, x1.z), new float3(x1.x, x1.y, x0.z), center, color);
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x0.x, x0.y, x0.z), new float3(x1.x, x0.y, x0.z), center, color);
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x1.x, x0.y, x0.z), new float3(x1.x, x0.y, x1.z), center, color);
            // +X / -X
            TriOutward(b, new float3(x1.x, x0.y, x0.z), new float3(x1.x, x0.y, x1.z), new float3(x1.x, x1.y, x1.z), center, color);
            TriOutward(b, new float3(x1.x, x0.y, x0.z), new float3(x1.x, x1.y, x1.z), new float3(x1.x, x1.y, x0.z), center, color);
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x0.x, x0.y, x0.z), new float3(x0.x, x1.y, x0.z), center, color);
            TriOutward(b, new float3(x0.x, x0.y, x1.z), new float3(x0.x, x1.y, x0.z), new float3(x0.x, x1.y, x1.z), center, color);
        }

        private static void Prism(MeshBuilder b, float3 from, float3 to, float rX, float rZ, int sides, VertexColor color)
        {
            if (sides < 3) sides = 3;
            if (!b.CanAdd(sides * 2)) return;
            float3 axis = math.normalizesafe(to - from, new float3(0, 1, 0));
            float3 ref_ = math.abs(axis.y) > 0.9f ? new float3(1, 0, 0) : new float3(0, 1, 0);
            float3 t1 = math.normalize(math.cross(axis, ref_));
            float3 t2 = math.normalize(math.cross(axis, t1));
            float3 center = (from + to) * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (float)i / sides * math.PI * 2f;
                float a1 = (float)(i + 1) / sides * math.PI * 2f;
                float3 v0 = from + t1 * math.cos(a0) * rX + t2 * math.sin(a0) * rZ;
                float3 v1 = from + t1 * math.cos(a1) * rX + t2 * math.sin(a1) * rZ;
                float3 v2 = to + t1 * math.cos(a1) * rX + t2 * math.sin(a1) * rZ;
                float3 v3 = to + t1 * math.cos(a0) * rX + t2 * math.sin(a0) * rZ;
                TriOutward(b, v0, v1, v2, center, color);
                TriOutward(b, v0, v2, v3, center, color);
            }
        }

        private static void Cone(MeshBuilder b, float3 base_, float3 apex, float radius, int sides, VertexColor color, bool cap)
        {
            if (sides < 3) sides = 3;
            int cost = sides + (cap ? sides - 2 : 0);
            if (!b.CanAdd(cost)) return;
            float3 axis = math.normalizesafe(apex - base_, new float3(0, 1, 0));
            float3 ref_ = math.abs(axis.y) > 0.9f ? new float3(1, 0, 0) : new float3(0, 1, 0);
            float3 t1 = math.normalize(math.cross(axis, ref_));
            float3 t2 = math.normalize(math.cross(axis, t1));
            var ring = new float3[sides];
            float3 ringCenter = base_;
            for (int i = 0; i < sides; i++)
            {
                float a = (float)i / sides * math.PI * 2f;
                ring[i] = base_ + (t1 * math.cos(a) + t2 * math.sin(a)) * radius;
            }
            float3 center = (base_ + apex) * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                TriOutward(b, ring[i], ring[(i + 1) % sides], apex, center, color);
            }
            if (cap)
            {
                for (int i = 1; i < sides - 1; i++)
                    TriOutward(b, ring[0], ring[i], ring[i + 1], ringCenter, color);
            }
        }

        // Icosahedron topology (12 verts, 20 faces).
        private static readonly float3[] IcoVerts =
        {
            new float3(-1,  2,  0), new float3( 1,  2,  0), new float3(-1, -2,  0), new float3( 1, -2,  0),
            new float3( 0, -1,  2), new float3( 0,  1,  2), new float3( 0, -1, -2), new float3( 0,  1, -2),
            new float3( 2,  0, -1), new float3( 2,  0,  1), new float3(-2,  0, -1), new float3(-2,  0,  1)
        };
        private static readonly int[] IcoFaces =
        {
            0, 11, 5,  0, 5, 1,  0, 1, 7,  0, 7, 10,  0, 10, 11,
            1, 5, 9,  5, 11, 4,  11, 10, 2,  10, 7, 6,  7, 1, 8,
            3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
            4, 9, 5,  2, 4, 11,  6, 2, 10,  8, 6, 7,  9, 8, 1
        };

        private static void Icosphere(MeshBuilder b, float3 center, float3 radii, VertexColor color)
        {
            if (!b.CanAdd(20)) return;
            var v = new float3[12];
            for (int i = 0; i < IcoVerts.Length; i++)
                v[i] = center + (IcoVerts[i] * radii) * 0.447f;
            for (int i = 0; i < IcoFaces.Length; i += 3)
                TriOutward(b, v[IcoFaces[i]], v[IcoFaces[i + 1]], v[IcoFaces[i + 2]], center, color);
        }

        /// <summary>Top hemisphere of the icosphere (10 tris) — shells, canopies.</summary>
        private static void IcosphereTop(MeshBuilder b, float3 center, float3 radii, VertexColor color)
        {
            if (!b.CanAdd(10)) return;
            var v = new float3[12];
            for (int i = 0; i < IcoVerts.Length; i++)
                v[i] = center + (IcoVerts[i] * radii) * 0.447f;
            int emitted = 0;
            for (int i = 0; i < IcoFaces.Length && emitted < 10; i += 3)
            {
                float3 a = v[IcoFaces[i]], c1 = v[IcoFaces[i + 1]], c2 = v[IcoFaces[i + 2]];
                float3 n = math.cross(c1 - a, c2 - a);
                if (n.y <= 0) continue; // keep upward-facing only
                TriOutward(b, a, c1, c2, center, color);
                emitted++;
            }
        }

        private static void Octahedron(MeshBuilder b, float3 center, float3 radii, VertexColor color)
        {
            if (!b.CanAdd(8)) return;
            float3 px = center + new float3(radii.x, 0, 0);
            float3 nx = center - new float3(radii.x, 0, 0);
            float3 py = center + new float3(0, radii.y, 0);
            float3 ny = center - new float3(0, radii.y, 0);
            float3 pz = center + new float3(0, 0, radii.z);
            float3 nz = center - new float3(0, 0, radii.z);
            TriOutward(b, px, pz, py, center, color);
            TriOutward(b, pz, nx, py, center, color);
            TriOutward(b, nx, nz, py, center, color);
            TriOutward(b, nz, px, py, center, color);
            TriOutward(b, pz, px, ny, center, color);
            TriOutward(b, nx, pz, ny, center, color);
            TriOutward(b, nz, nx, ny, center, color);
            TriOutward(b, px, nz, ny, center, color);
        }

        /// <summary>Double-sided quad: 4 tris (see QuadDS).</summary>
        private static void Quad(MeshBuilder b, float3 a, float3 b_, float3 c, float3 d, VertexColor color)
            => QuadDS(b, a, b_, c, d, color);

        /// <summary>Two crossed double-sided quads (leaves, fins): 8 tris.</summary>
        private static void CrossedQuad(MeshBuilder b, float3 center, float3 toMid, float3 tip, float halfWidth, VertexColor color)
        {
            float3 dir = math.normalizesafe(toMid, new float3(0, 1, 0));
            float3 up = math.abs(dir.y) > 0.9f ? new float3(1, 0, 0) : new float3(0, 1, 0);
            float3 side = math.normalize(math.cross(dir, up)) * halfWidth;
            float3 mid = center + (tip - center) * 0.5f;
            if (b.CanAdd(8))
            {
                QuadDS(b, center + side, center - side, tip - side, mid, color);
                QuadDS(b, mid, tip + side, tip - side, center, color);
            }
        }
    }

    /// <summary>
    /// Triangle-budgeted mesh accumulator (managed; mesh gen is a cold path).
    /// The guard guarantees the hard LOD budgets: when the budget is exhausted the
    /// builder stops adding detail rather than producing an oversized mesh.
    /// </summary>
    public sealed class MeshBuilder
    {
        public const int Lod0Animal = 300;
        public const int Lod0Plant = 400;
        public const int Lod1 = 60;
        public const int Lod2 = 12;
        public const int Lod3 = 2;

        public readonly int Budget;
        private readonly List<float3> _positions = new List<float3>(256);
        private readonly List<float3> _normals = new List<float3>(256);
        private readonly List<float> _colors = new List<float>(768);
        public int TriCount => _positions.Count / 3;

        public MeshBuilder(int budget) { Budget = budget; }

        public bool CanAdd(int triCost) => TriCount + triCost <= Budget;

        public void AddTri(float3 pos, float3 normal, VertexColor color)
        {
            _positions.Add(pos);
            _normals.Add(normal);
            _colors.Add(color.R); _colors.Add(color.G); _colors.Add(color.B);
        }

        public MeshDescriptor Finish()
        {
            var desc = new MeshDescriptor
            {
                Positions = _positions.ToArray(),
                Normals = _normals.ToArray(),
                Colors = _colors.ToArray(),
            };
            desc.ResetBounds();
            for (int i = 0; i < desc.Positions.Length; i++)
            {
                float3 p = desc.Positions[i];
                if (p.x < desc.MinX) desc.MinX = p.x; if (p.x > desc.MaxX) desc.MaxX = p.x;
                if (p.y < desc.MinY) desc.MinY = p.y; if (p.y > desc.MaxY) desc.MaxY = p.y;
                if (p.z < desc.MinZ) desc.MinZ = p.z; if (p.z > desc.MaxZ) desc.MaxZ = p.z;
            }
            if (desc.Positions.Length == 0)
            {
                // Degenerate-safety: a single small tetra so empty meshes stay valid.
                desc = Fallback();
            }
            return desc;
        }

        private static MeshDescriptor Fallback()
        {
            float3 a = new float3(0, 0, 0), c1 = new float3(0.01f, 0, 0), c2 = new float3(0, 0.01f, 0);
            float3 n = new float3(0, 0, 1);
            var desc = new MeshDescriptor
            {
                Positions = new[] { a, c1, c2 },
                Normals = new[] { n, n, n },
                Colors = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
            };
            desc.MinX = desc.MinY = desc.MinZ = 0;
            desc.MaxX = 0.01f; desc.MaxY = 0.01f; desc.MaxZ = 0;
            return desc;
        }
    }

    /// <summary>Hard triangle budgets per LOD (see class remarks + genetics.md).</summary>
    internal static class Budget
    {
        public const int Lod0Animal = 300;
        public const int Lod0Plant = 400;
        public const int Lod1 = 60;
        public const int Lod2 = 12;
        public const int Lod3 = 2;
    }
}