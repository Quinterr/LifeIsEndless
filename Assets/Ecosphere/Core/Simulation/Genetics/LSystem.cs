// Ecosphere — L-system expansion for plant morphogenesis.
// Pure C# (no engine references). Deterministic: same genes + seed → same structure.

using System;
using System.Collections.Generic;

namespace Ecosphere.Core.Simulation
{
    /// <summary>
    /// L-system instruction for plant mesh building.
    /// The interpreter maps these to parametric mesh primitives.
    /// </summary>
    public enum LInstruction : byte
    {
        /// <summary>Draw a stem/branch segment forward by Length.</summary>
        Forward,
        /// <summary>Push transform state (position + orientation).</summary>
        Push,
        /// <summary>Pop transform state.</summary>
        Pop,
        /// <summary>Turn left (yaw+) by Angle degrees.</summary>
        TurnLeft,
        /// <summary>Turn right (yaw−) by Angle degrees.</summary>
        TurnRight,
        /// <summary>Pitch up by Angle degrees.</summary>
        PitchUp,
        /// <summary>Pitch down by Angle degrees.</summary>
        PitchDown,
        /// <summary>Roll clockwise by Angle degrees.</summary>
        RollCW,
        /// <summary>Place a leaf at current position.</summary>
        Leaf,
        /// <summary>Place a flower at current position.</summary>
        Flower,
        /// <summary>Place a fruit at current position.</summary>
        Fruit,
        /// <summary>Taper: reduce the radius for subsequent segments.</summary>
        Taper,
        /// <summary>Mark the start of a new branch level (for mesh builder grouping).</summary>
        BranchLevel,
        /// <summary>
        /// Start a side branch: tilt the growth axis away from the parent by Angle
        /// degrees toward the horizontal direction at world yaw Yaw (degrees).
        /// (Plain Turn/Pitch are ineffective while the axis is vertical, so branch
        /// placement uses this single well-defined operation.)
        /// </summary>
        Branch
    }

    /// <summary>
    /// A single L-system instruction with parameters.
    /// </summary>
    public struct LCommand
    {
        public LInstruction Instruction;
        public float Length;   // segment length (Forward) or leaf/flower size
        public float Angle;    // turn/pitch/tilt angle in degrees
        public float Yaw;      // world yaw for Branch (degrees)
        public float Radius;   // segment radius
        public int Level;      // branch recursion level
    }

    /// <summary>
    /// Deterministic L-system plant expander.
    /// Reads plant phenotype parameters and expands a recursive branching structure
    /// as a list of <see cref="LCommand"/>. The mesh builder interprets these
    /// commands into geometry.
    ///
    /// Simplifications:
    /// - One axiom ("F") with context-sensitive branching.
    /// - Branching depth capped at 5 levels (from genes).
    /// - No tropism computation (that's stage 05); just static branching angles.
    /// - Total command count bounded to prevent runaway recursion.
    /// </summary>
    public static class LSystemExpander
    {
        /// <summary>Hard cap on total commands to prevent runaway structures.</summary>
        public const int MaxCommands = 2000;

        /// <summary>
        /// Expand an L-system from plant phenotype parameters.
        /// </summary>
        /// <param name="stemHeight">Total stem height.</param>
        /// <param name="stemThickness">Stem base radius.</param>
        /// <param name="branchingDepth">Max recursion depth (0–5).</param>
        /// <param name="branchingAngle">Angle of branches from parent.</param>
        /// <param name="branchLengthRatio">Child/parent segment length ratio.</param>
        /// <param name="leafSize">Size of leaf quads.</param>
        /// <param name="leafCount">Leaves per node (1–8).</param>
        /// <param name="flowerSize">Flower size (0 = none).</param>
        /// <param name="flowerCount">Number of flower positions.</param>
        /// <param name="fruitSize">Fruit size (0 = none).</param>
        /// <param name="nodeSpacing">Internode length ratio.</param>
        /// <param name="crownShape">0=spherical, 0.5=conical, 1=flat.</param>
        /// <param name="growthHabit">0=herb, 0.33=bush, 0.66=vine, 1=tree.</param>
        /// <param name="organismSize">Current organism maturity (0..1).</param>
        /// <param name="rng">Seeded RNG for symmetry-breaking.</param>
        /// <returns>List of L-system commands.</returns>
        public static List<LCommand> Expand(
            float stemHeight, float stemThickness,
            float branchingDepth, float branchingAngle, float branchLengthRatio,
            float leafSize, int leafCount,
            float flowerSize, int flowerCount,
            float fruitSize,
            float nodeSpacing, float crownShape, float growthHabit,
            float organismSize, ref RngState rng)
        {
            var commands = new List<LCommand>(256);
            if (organismSize < 0.01f) return commands;

            int maxDepth = (int)Math.Max(0, Math.Min(5, branchingDepth));
            float segLength = stemHeight * Math.Max(0.05f, nodeSpacing) * organismSize;
            float baseRadius = stemThickness * 0.5f * organismSize;
            float angle = Math.Max(5f, Math.Min(85f, branchingAngle));

            // A simple recursive expansion
            ExpandBranch(commands, ref rng, 0, maxDepth, segLength, baseRadius,
                branchLengthRatio, angle, leafSize, leafCount,
                flowerSize, flowerCount, fruitSize, crownShape, growthHabit, organismSize);

            // Cap-exit can return from inside a branch, leaving open pushes.
            // Close them so the expansion is always well-formed (balanced push/pop).
            int open = 0;
            foreach (var c in commands)
            {
                if (c.Instruction == LInstruction.Push) open++;
                else if (c.Instruction == LInstruction.Pop) open--;
            }
            for (int i = 0; i < open; i++)
                commands.Add(new LCommand { Instruction = LInstruction.Pop });

            return commands;
        }

        private static void ExpandBranch(List<LCommand> commands, ref RngState rng,
            int depth, int maxDepth, float segLength, float radius,
            float lengthRatio, float angle, float leafSize, int leafCount,
            float flowerSize, int flowerCount, float fruitSize,
            float crownShape, float growthHabit, float organismSize)
        {
            if (commands.Count >= MaxCommands) return;
            if (segLength < 0.005f) return;

            int segments = Math.Max(2, (int)(3f + growthHabit * 4f));

            // Draw the main stem segments
            for (int i = 0; i < segments; i++)
            {
                if (commands.Count >= MaxCommands) return;
                float taper = 1f - (float)i / segments * 0.5f;
                commands.Add(new LCommand
                {
                    Instruction = LInstruction.Forward,
                    Length = segLength,
                    Radius = radius * taper,
                    Level = depth
                });

                // Place leaves at each node
                if (leafSize > 0.01f && organismSize > 0.3f)
                {
                    int actualLeaves = Math.Max(1, Math.Min(4, leafCount));
                    for (int l = 0; l < actualLeaves; l++)
                    {
                        if (commands.Count >= MaxCommands) return;
                        float rotAngle = (360f / actualLeaves) * l + rng.NextFloat01() * 30f;
                        commands.Add(new LCommand
                        {
                            Instruction = LInstruction.TurnLeft,
                            Angle = rotAngle
                        });
                        commands.Add(new LCommand
                        {
                            Instruction = LInstruction.Leaf,
                            Length = leafSize * organismSize,
                            Level = depth
                        });
                        commands.Add(new LCommand
                        {
                            Instruction = LInstruction.TurnRight,
                            Angle = rotAngle
                        });
                    }
                }

                // Branching at certain nodes (skip first and last segment)
                if (depth < maxDepth && i > 0 && i < segments - 1)
                {
                    // Crown shape affects branch angle
                    float crownMod = crownShape < 0.5f
                        ? 1f + crownShape  // spherical: more varied
                        : 1f - (crownShape - 0.5f); // flat/conical: more upright

                    float branchAngle = angle * crownMod;

                    // Two branches: left and right (deterministic yaw + tilt).
                    float yawL = 60f + (rng.NextFloat01() - 0.5f) * 40f;
                    float yawR = 300f + (rng.NextFloat01() - 0.5f) * 40f;
                    float tiltL = Math.Max(15f, Math.Min(80f, branchAngle));
                    float tiltR = Math.Max(15f, Math.Min(80f, branchAngle));

                    commands.Add(new LCommand { Instruction = LInstruction.Push });
                    commands.Add(new LCommand { Instruction = LInstruction.BranchLevel, Level = depth + 1 });
                    commands.Add(new LCommand { Instruction = LInstruction.Branch, Angle = tiltL, Yaw = yawL });
                    ExpandBranch(commands, ref rng, depth + 1, maxDepth,
                        segLength * lengthRatio, radius * lengthRatio * 0.7f,
                        lengthRatio, angle, leafSize, leafCount,
                        flowerSize, flowerCount, fruitSize,
                        crownShape, growthHabit, organismSize);
                    commands.Add(new LCommand { Instruction = LInstruction.Pop });

                    commands.Add(new LCommand { Instruction = LInstruction.Push });
                    commands.Add(new LCommand { Instruction = LInstruction.BranchLevel, Level = depth + 1 });
                    commands.Add(new LCommand { Instruction = LInstruction.Branch, Angle = tiltR, Yaw = yawR });
                    ExpandBranch(commands, ref rng, depth + 1, maxDepth,
                        segLength * lengthRatio, radius * lengthRatio * 0.7f,
                        lengthRatio, angle, leafSize, leafCount,
                        flowerSize, flowerCount, fruitSize,
                        crownShape, growthHabit, organismSize);
                    commands.Add(new LCommand { Instruction = LInstruction.Pop });
                }
            }

            // Place flowers/fruits at branch tips (only at terminal branches)
            if (depth >= maxDepth - 1 && organismSize > 0.6f)
            {
                if (flowerSize > 0.01f && flowerCount > 0)
                {
                    int fc = Math.Min(flowerCount, 4);
                    for (int f = 0; f < fc; f++)
                    {
                        if (commands.Count >= MaxCommands) return;
                        commands.Add(new LCommand
                        {
                            Instruction = LInstruction.Flower,
                            Length = flowerSize * organismSize * 0.5f,
                            Level = depth
                        });
                    }
                }
                if (fruitSize > 0.01f && organismSize > 0.8f)
                {
                    commands.Add(new LCommand
                    {
                        Instruction = LInstruction.Fruit,
                        Length = fruitSize * organismSize * 0.3f,
                        Level = depth
                    });
                }
            }
        }
    }
}