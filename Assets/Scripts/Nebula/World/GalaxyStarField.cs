using System.Collections.Generic;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Sectors;

namespace Nebula.World
{
    /// <summary>
    /// Every real star in the universe, as a pure function of the galaxies' geometry.
    ///
    /// The stars are strung along each galaxy's spiral arms — the same layout the visible
    /// galaxies already show — and each one's position is derived from a hash rather than
    /// from <see cref="UnityEngine.Random"/>, so the whole field is reproducible from
    /// (seed, galaxy layout) alone. That is what lets the seed-only sector generator ask
    /// "which stars are in this sector" without a scene existing.
    ///
    /// Deliberately small: one galaxy contributes ArmCount x StarsPerArm stars, so the whole
    /// field is a few hundred entries and can be enumerated in full whenever a sector needs
    /// to know its own.
    /// </summary>
    public static class GalaxyStarField
    {
        /// <summary>One star's place in the field, before it becomes a system.</summary>
        public readonly struct Placement
        {
            public readonly int GalaxyIndex;
            public readonly int Arm;
            public readonly int IndexOnArm;
            public readonly Vector3 AbsolutePosition;

            public Placement(int galaxyIndex, int arm, int indexOnArm, Vector3 absolutePosition)
            {
                GalaxyIndex = galaxyIndex; Arm = arm; IndexOnArm = indexOnArm;
                AbsolutePosition = absolutePosition;
            }

            /// <summary>Stable identity within the field, independent of any sector.</summary>
            public string Key => $"g{GalaxyIndex}a{Arm}s{IndexOnArm}";
        }

        /// <summary>One galaxy's contribution to the field.</summary>
        public readonly struct GalaxySpec
        {
            public readonly GalaxySpiral Spiral;
            public readonly int StarsPerArm;

            public GalaxySpec(GalaxySpiral spiral, int starsPerArm)
            {
                Spiral = spiral; StarsPerArm = Mathf.Max(0, starsPerArm);
            }
        }

        /// <summary>Where along an arm the stars are spread — EnvironmentInstaller's own 0.12..0.98.</summary>
        private const float ArmStartT = 0.12f;
        private const float ArmEndT = 0.98f;

        /// <summary>The campaign always begins in this sector, so a star has to exist inside it.</summary>
        public static readonly SectorCoordinate StartSector = new SectorCoordinate(0, 0, 0);

        /// <summary>
        /// How far out along its arm a star may be pushed to clear its neighbours, and in how
        /// many steps. A star that still cannot clear them is dropped rather than placed too
        /// close: <see cref="SectorManifestGenerator.RequiredSeparation"/> is a contract the
        /// whole navigation and streaming layer rests on.
        /// </summary>
        private const float MaxNudgeT = 0.09f;
        private const int NudgeSteps = 24;

        private static IReadOnlyList<GalaxySpec> configured;

        /// <summary>The galaxies the field is built from. Set once at boot from the real config.</summary>
        public static IReadOnlyList<GalaxySpec> Galaxies => configured ?? (configured = SceneDefault());

        public static void Configure(IReadOnlyList<GalaxySpec> galaxies)
        {
            configured = galaxies != null && galaxies.Count > 0 ? galaxies : SceneDefault();
            cache.Clear();
            originCrossings.Clear();
        }

        /// <summary>
        /// The scene's own four galaxies. A default rather than a duplicate authority: the
        /// installer overrides it with the live config at boot, and it exists so the pure
        /// generator and its tests can resolve a field with no scene loaded.
        /// </summary>
        private static List<GalaxySpec> SceneDefault()
        {
            const float radius = 10_000_000f;
            const int arms = 4;
            const int starsPerArm = 10;
            return new List<GalaxySpec>
            {
                new GalaxySpec(GalaxySpiral.Create(new Vector3(-4_500_000f, 0f, 0f), radius, arms), starsPerArm),
                new GalaxySpec(GalaxySpiral.Create(new Vector3(-4_500_000f, 4_000_000f, 0f), radius * 0.75f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.75f)),
                new GalaxySpec(GalaxySpiral.Create(new Vector3(10_500_000f, 4_000_000f, 0f), radius * 0.6f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.6f)),
                new GalaxySpec(GalaxySpiral.Create(new Vector3(14_500_000f, 0f, 0f), radius * 0.8f, arms),
                    Mathf.RoundToInt(starsPerArm * 0.8f))
            };
        }

        /// <summary>
        /// Built fields, keyed by seed. A single-slot cache thrashed catastrophically: every
        /// sector resolve asks for the field, and a test sweeping several seeds rebuilt all
        /// 128 stars — separation pass and origin scan included — on every alternation.
        /// </summary>
        private static readonly Dictionary<ulong, List<Placement>> cache = new Dictionary<ulong, List<Placement>>();

        /// <summary>The whole field for a seed, built once and reused — every sector resolve asks for it.</summary>
        public static List<Placement> All(ulong seed)
        {
            if (cache.TryGetValue(seed, out List<Placement> built)) return built;
            built = new List<Placement>();
            Enumerate(seed, Galaxies, built);
            cache[seed] = built;
            return built;
        }

        /// <summary>The stars that fall inside one sector, in the field's own canonical order.</summary>
        public static void InSector(ulong seed, SectorCoordinate sector, List<Placement> into)
        {
            if (into == null) return;
            foreach (Placement p in All(seed))
                if (SectorCoordinate.FromAbsolutePosition(p.AbsolutePosition).Equals(sector))
                    into.Add(p);
        }

        /// <summary>
        /// Every star of every galaxy, in a fixed order (galaxy, arm, index) so the field is
        /// identical on every rebuild.
        /// </summary>
        public static void Enumerate(ulong seed, IReadOnlyList<GalaxySpec> galaxies, List<Placement> into)
        {
            if (into == null || galaxies == null) return;
            float required = SectorManifestGenerator.RequiredSeparation;

            for (int g = 0; g < galaxies.Count; g++)
            {
                GalaxySpec spec = galaxies[g];
                if (spec.StarsPerArm <= 0) continue;

                for (int arm = 0; arm < spec.Spiral.ArmCount; arm++)
                    for (int i = 0; i < spec.StarsPerArm; i++)
                    {
                        // The campaign starts at the world origin, and GalaxyGenerator phases
                        // the first galaxy's first arm to run through it precisely so the play
                        // area sits on an arm. This one star is pinned there and never nudged:
                        // without it the start sector has nothing in it to begin at.
                        if (g == 0 && arm == 0 && i == 0)
                        {
                            into.Add(new Placement(g, arm, i, StartStar(spec.Spiral)));
                            continue;
                        }

                        // The spiral brings different arms close together near the core, and a
                        // star's hashed position along its arm can land it at a very different
                        // radius from its neighbour -- measured at 254,191 units, well inside
                        // the required 350,000. So a star that crowds one already placed is
                        // pushed further out along its own arm until it clears, and dropped if
                        // it never does. Never placed too close: the separation is a contract
                        // the navigation and streaming layers rest on.
                        bool placed = false;
                        for (int step = 0; step <= NudgeSteps && !placed; step++)
                        {
                            float nudge = step / (float)NudgeSteps * MaxNudgeT;
                            Vector3 candidate = PositionOf(seed, spec, g, arm, i, nudge);
                            if (!Clears(candidate, into, required)) continue;
                            into.Add(new Placement(g, arm, i, candidate));
                            placed = true;
                        }
                    }
            }
        }

        private static bool Clears(Vector3 candidate, List<Placement> placed, float required)
        {
            float squared = required * required;
            for (int i = 0; i < placed.Count; i++)
                if ((placed[i].AbsolutePosition - candidate).sqrMagnitude < squared) return false;
            return true;
        }

        /// <summary>
        /// The point where an arm passes closest to the world origin. Found by sampling the
        /// arm rather than solved for: the spiral is transcendental, and a few thousand
        /// samples pin it far more tightly than the star's own scatter ever varies.
        /// </summary>
        /// <summary>Origin scans, keyed by the spiral's own shape — the scan is identical every time it is asked for.</summary>
        private static readonly Dictionary<(float, float, float, float), Vector3> originCrossings =
            new Dictionary<(float, float, float, float), Vector3>();

        public static Vector3 OriginCrossing(GalaxySpiral spiral)
        {
            const int Samples = 512;
            Vector3 best = spiral.ArmPoint(0, 0f);
            float bestDistance = best.magnitude;
            for (int i = 1; i <= Samples; i++)
            {
                Vector3 candidate = spiral.ArmPoint(0, i / (float)Samples);
                float distance = candidate.magnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance; best = candidate;
            }
            return best;
        }

        /// <summary>
        /// The start star: the arm's origin crossing, but constrained to actually lie INSIDE
        /// the start sector. The sector spans [0, SectorSize) on each axis, so a crossing a
        /// few hundred units the wrong side of zero would file the star under a neighbouring
        /// sector and leave the campaign's own sector empty -- and the game boots by asking
        /// for system 0 of exactly this sector.
        /// </summary>
        public static Vector3 StartStar(GalaxySpiral spiral)
        {
            var key = (spiral.Center.x, spiral.Center.z, spiral.Radius, spiral.ArmPhase0);
            if (originCrossings.TryGetValue(key, out Vector3 cached)) return cached;

            const int Samples = 2048;
            Vector3 fallback = OriginCrossing(spiral);
            Vector3 best = default;
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i <= Samples; i++)
            {
                Vector3 candidate = spiral.ArmPoint(0, i / (float)Samples);
                if (!SectorCoordinate.FromAbsolutePosition(candidate).Equals(StartSector)) continue;
                float distance = candidate.magnitude;
                if (found && distance >= bestDistance) continue;
                bestDistance = distance; best = candidate; found = true;
            }

            // The arm genuinely never enters the start sector: nudge the crossing just inside
            // it rather than leave the campaign without a system to start in.
            if (!found)
            {
                const float Inset = 1000f;
                best = new Vector3(
                    Mathf.Clamp(fallback.x, Inset, SectorCoordinate.SectorSize - Inset),
                    Mathf.Clamp(fallback.y, Inset, SectorCoordinate.SectorSize - Inset),
                    Mathf.Clamp(fallback.z, Inset, SectorCoordinate.SectorSize - Inset));
            }
            originCrossings[key] = best;
            return best;
        }

        /// <summary>
        /// One star's absolute position: a point along its arm, nudged off the centreline so
        /// a galaxy reads as arms of stars rather than four perfect curves. The nudge is
        /// hashed from the star's own identity, never rolled, so it never moves.
        /// </summary>
        public static Vector3 PositionOf(ulong seed, GalaxySpec spec, int galaxyIndex, int arm,
            int indexOnArm, float nudgeT = 0f)
        {
            string key = $"galaxystar/{galaxyIndex}/{arm}/{indexOnArm}";
            float along = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, key + "/t"));
            float t = Mathf.Clamp01(
                Mathf.Lerp(ArmStartT, ArmEndT, (indexOnArm + along) / spec.StarsPerArm) + nudgeT);

            Vector3 local = spec.Spiral.ArmLocalPoint(arm, t);
            float r = new Vector2(local.x, local.z).magnitude;

            // The same scatter the visible galaxy uses: a fraction of the disc radius plus a
            // fraction of how far out the star already is.
            float spread = spec.Spiral.Radius * 0.006f + r * 0.02f;
            float lift = spec.Spiral.Radius * 0.003f + r * 0.01f;

            float angle = ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, key + "/angle")) * Mathf.PI * 2f;
            float reach = Mathf.Sqrt(ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, key + "/reach"))) * spread;
            float height = (ExtractionCatalog.Unit01(ExtractionCatalog.Hash(seed, key + "/height")) - 0.5f) * 2f * lift;

            local += new Vector3(Mathf.Cos(angle) * reach, height, Mathf.Sin(angle) * reach);
            return spec.Spiral.Center + local;
        }
    }
}
