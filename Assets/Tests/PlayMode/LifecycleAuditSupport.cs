using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Hangar;
using Nebula.Poi;
using Nebula.Progression;
using Nebula.Remote;
using Nebula.Sectors;
using Nebula.Ship;
using Nebula.Visuals;
using Nebula.World;
using Nebula.World.Composition;

namespace Nebula.Tests.PlayMode
{
    /// <summary>
    /// Everything the GENEL DENETİM PART 2 / AJAN 2 lifecycle fixtures share, and
    /// the reason there is now more than one of them.
    /// <para>
    /// The single 2,191-line fixture this was lifted out of mixed three workloads
    /// with nothing in common but a subject: short behavioural regressions that
    /// cost about four seconds each, hundred-cycle endurance measurements that cost
    /// four hundred, and hundred-and-fifty-cycle bisection probes carrying their own
    /// twenty-minute <c>Timeout</c>. Measured, that fixture ALONE — in its own Unity
    /// process, with nothing else scheduled — hit a forty-five minute watchdog having
    /// loaded fourteen scenes, and its own per-test timeout constant was longer than
    /// the entire release gate is allowed to take. A gate that cannot finish does not
    /// gate anything, so the three workloads are now three fixtures and every leaf
    /// carries exactly one of <c>ReleaseRegression</c>, <c>Soak</c> or
    /// <c>Diagnostic</c>:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="AuditPart2LifecyclePlayModeTests"/> and
    /// <see cref="LifecycleOwnershipRegressionTests"/> — <c>ReleaseRegression</c>.
    /// Short, deterministic, no loop over a cycle budget.</item>
    /// <item><see cref="AuditPart2LifecycleSoakTests"/> — <c>Soak</c>. The
    /// hundred-cycle endurance and memory-growth measurements, at their original
    /// cycle counts. Nothing here was shortened: a per-cycle delta of one is what
    /// they exist to see, and a shortened soak is a soak that reports green.</item>
    /// <item><see cref="AuditPart2LifecycleDiagnosticTests"/> — <c>Diagnostic</c>.
    /// The P2-LIFE-012 bisection ladder, likewise untouched.</item>
    /// </list>
    /// <para>
    /// The categories are the separation. <c>-testCategory</c> binds to NUnit's
    /// <c>categoryNames</c> filter, so the release lane admits leaves by name rather
    /// than by hoping a long test happens not to run.
    /// </para>
    /// </summary>
    public abstract class LifecycleAuditFixture
    {
        protected const string ScenePath = "Assets/Scenes/Nebula.unity";
        protected const int GameplaySlot = 1;

        private readonly Dictionary<string, string> slot1Backup = new Dictionary<string, string>();
        private bool inputGateBackup;
        private float timeScaleBackup;

        private static IEnumerable<string> Slot1Files()
        {
            string main = SaveService.SlotFile(GameplaySlot);
            string dir = Path.GetDirectoryName(main);
            yield return main;
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak1.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak2.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.bak3.json");
            yield return Path.Combine(dir, $"slot{GameplaySlot}.tmp.json");
        }

        [SetUp]
        public void BackupRealGameplaySlot()
        {
            inputGateBackup = InputGate.Suppressed;
            timeScaleBackup = Time.timeScale;
            slot1Backup.Clear();
            foreach (string f in Slot1Files())
                slot1Backup[f] = File.Exists(f) ? File.ReadAllText(f) : null;
        }

        [TearDown]
        public void RestoreRealGameplaySlot()
        {
            InputGate.Suppressed = inputGateBackup;
            Time.timeScale = timeScaleBackup;
            ExclusiveModal.ResetForSession();
            foreach (var kv in slot1Backup)
            {
                try
                {
                    if (kv.Value == null) { if (File.Exists(kv.Key)) File.Delete(kv.Key); }
                    else File.WriteAllText(kv.Key, kv.Value);
                }
                catch { /* the operator's slot is best-effort; never fail a test on it */ }
            }
        }

        protected static IEnumerator LoadSceneFresh()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(Path.GetFileNameWithoutExtension(ScenePath), LoadSceneMode.Single);
#endif
            yield return null;
        }

        protected static SolarSystemBootstrap Adapter()
        {
            var adapter = UnityEngine.Object.FindObjectOfType<SolarSystemBootstrap>();
            Assert.That(adapter, Is.Not.Null, "baseline: the scene carries a SolarSystemBootstrap adapter");
            return adapter;
        }

        /// <summary>
        /// Session teardown is intentionally deferred, and the strategic-map
        /// renderer is a separately-owned scene root released by the screen's
        /// OnDestroy. Four frames bound that two-stage production teardown plus
        /// runtime-resource destruction; measuring earlier counts pending-destroy
        /// objects as survivors rather than measuring the settled lifecycle.
        /// </summary>
        protected static IEnumerator DrainDeferredTeardown()
        {
            for (int i = 0; i < 4; i++) yield return null;
        }

        protected static IEnumerable<GameObject> SceneRoots()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects()) yield return root;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                if (t.parent == null && t.gameObject.scene != SceneManager.GetActiveScene())
                    yield return t.gameObject;
        }

        protected static HashSet<string> SceneRootNames()
        {
            var names = new HashSet<string>();
            foreach (GameObject root in SceneRoots()) names.Add(root.name);
            return names;
        }

        /// <summary>
        /// The exact two calls <c>NebulaSessionLifetime.DestroyAllReverse</c> makes
        /// on every tracked root, in the same order — so an in-situ probe differs
        /// from a session root in nothing but who created it.
        /// </summary>
        protected static void TearDownExactlyLikeATrackedRoot(GameObject probe)
        {
            if (probe == null) return;
            probe.SetActive(false);
            UnityEngine.Object.Destroy(probe);
        }

        /// <summary>
        /// A run budget, from the environment when the operator set one and from the
        /// compiled default otherwise. A malformed or non-positive value is ignored
        /// rather than obeyed: a probe that silently runs zero cycles reports green.
        /// </summary>
        protected static int CycleBudget(string variable, int fallback)
        {
            string raw = Environment.GetEnvironmentVariable(variable);
            return !string.IsNullOrEmpty(raw)
                   && int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int parsed)
                   && parsed > 0
                ? parsed
                : fallback;
        }

        protected static int InvocationCount(object owner, string eventName)
        {
            if (owner == null) return -1;
            FieldInfo field = owner.GetType().GetField(eventName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"{owner.GetType().Name}.{eventName} has no field-like backing store");
            return field.GetValue(owner) is Delegate handlers ? handlers.GetInvocationList().Length : 0;
        }

        protected static bool IsRegisteredParticipant(NebulaAnimationDirector director, IVisualAnimationParticipant participant)
        {
            FieldInfo field = typeof(NebulaAnimationDirector).GetField("participants",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "NebulaAnimationDirector.participants could not be inspected");
            var live = field.GetValue(director) as System.Collections.IList;
            Assert.That(live, Is.Not.Null);
            return live.Contains(participant);
        }

        protected static SystemManifest FirstForeignManifest(NebulaContext context)
        {
            Vector3 absolute = context.Ship.transform.position + FloatingOrigin.TotalOffset;
            foreach (StarSystemRecord record in StarSystemRegistry.SystemsNear(absolute))
            {
                SystemManifest manifest = StarSystemRegistry.Manifests.ResolveSystem(record.SystemId);
                if (manifest != null && !manifest.IsStartSystem) return manifest;
            }
            Assert.Fail("production manifest repository yielded no foreign system in the start sector or its 26 neighbours");
            return null;
        }

        protected static void PlaceShipAtAbsolute(NebulaContext context, Vector3 absolute)
        {
            Vector3 local = absolute - FloatingOrigin.TotalOffset;
            Rigidbody body = context.Ship.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = local;
            }
            context.Ship.transform.position = local;
        }

        // ------------------------------------------------------------- counters

        protected static readonly (Type owner, string eventName)[] StaticEvents =
        {
            (typeof(ProgressionService), nameof(ProgressionService.Awarded)),
            (typeof(ResourceTransferService), nameof(ResourceTransferService.Collected)),
            (typeof(CampaignIdentity), nameof(CampaignIdentity.IdentityChanged)),
            (typeof(DominanceSystem), nameof(DominanceSystem.PlanetFirstControlled)),
            (typeof(FloatingOrigin), nameof(FloatingOrigin.OriginShifted)),
            (typeof(DefenseFireSignal), nameof(DefenseFireSignal.Fired)),
            (typeof(EscortFireSignal), nameof(EscortFireSignal.Fired)),
            (typeof(RemoteCommandService), nameof(RemoteCommandService.CommandCompleted))
        };

        protected static int SubscriberCount(Type owner, string eventName)
        {
            FieldInfo f = owner.GetField(eventName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) return -1;
            var d = f.GetValue(null) as Delegate;
            return d == null ? 0 : d.GetInvocationList().Length;
        }

        protected static long LiveShellRegistryCount()
        {
            FieldInfo f = typeof(UI.Shell.NebulaUiShell).GetField("Live", BindingFlags.Static | BindingFlags.NonPublic);
            if (f == null) return -1;
            return f.GetValue(null) is System.Collections.ICollection c ? c.Count : -1;
        }

        /// <summary>Everything this audit counts, in one snapshot. Every entry is a zero-tolerance integer except where the name says otherwise.</summary>
        protected sealed class Snapshot
        {
            public readonly Dictionary<string, long> Values = new Dictionary<string, long>();

            public static Snapshot Take()
            {
                var s = new Snapshot();
                void Put(string k, long v) => s.Values[k] = v;

                Put("GameObject", UnityEngine.Object.FindObjectsOfType<Transform>(true).Length);
                Put("Component", UnityEngine.Object.FindObjectsOfType<Component>(true).Length);
                Put("MonoBehaviour", UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true).Length);
                Put("Light", UnityEngine.Object.FindObjectsOfType<Light>(true).Length);
                Put("Renderer", UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length);
                Put("Material(all-loaded)", Resources.FindObjectsOfTypeAll<Material>().Length);
                Put("Texture(all-loaded)", Resources.FindObjectsOfTypeAll<Texture>().Length);
                // Meshes were the one runtime resource family this snapshot did not
                // count, which is precisely why four per-session generator meshes
                // (starfield, galaxy clouds, dust tiers, halo) leaked invisibly:
                // destroying a MeshFilter's GameObject never destroys its mesh.
                Put("Mesh(all-loaded)", Resources.FindObjectsOfTypeAll<Mesh>().Length);
                // RenderTexture and Cubemap both derive from Texture, so the counter
                // above already contains them — and that is exactly why they needed
                // their own line. One 1024³ cube RenderTexture per session is a single
                // unit inside a Texture delta of a hundred and could hide there
                // indefinitely; as its own family it cannot. SkyboxBaker is the only
                // producer today, which is what makes a non-zero delta here readable
                // rather than a search.
                Put("RenderTexture(all-loaded)", Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
                Put("Cubemap(all-loaded)", Resources.FindObjectsOfTypeAll<Cubemap>().Length);

                Put("director.Streaming", UnityEngine.Object.FindObjectsOfType<SystemStreamingDirector>(true).Length);
                Put("director.Extraction", UnityEngine.Object.FindObjectsOfType<ExtractionDirector>(true).Length);
                Put("director.Navigation", UnityEngine.Object.FindObjectsOfType<NavigationDirector>(true).Length);
                Put("director.Animation", UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>(true).Length);
                Put("participant.Animation", AnimationParticipantCount(unscaled: false));
                Put("participant.AnimationUnscaled", AnimationParticipantCount(unscaled: true));
                Put("shell.NebulaUiShell", UnityEngine.Object.FindObjectsOfType<UI.Shell.NebulaUiShell>(true).Length);
                Put("shell.LiveRegistry", LiveShellRegistryCount());

                Put("registry.PoiObject", PoiObject.All.Count);
                Put("registry.PoiLiveState", PoiLiveState.States.Count);
                Put("registry.EnemyAgent", EnemyAgent.All.Count);
                Put("registry.Drills", ExtractionState.Drills.Count);
                Put("registry.Structures", ExtractionState.Structures.Count);

                Put("adoption.StructurePose", StructurePosePresenter.PendingCount);
                Put("adoption.BuildCapsule", BuildCapsulePresenter.PendingCount);
                Put("adoption.VehicleAnimation", VehicleAnimationPresenter.PendingCount);
                Put("adoption.SupportOperation", SupportOperationPresenter.PendingCount);
                Put("adoption.EnemyCombat", EnemyCombatAnimationPresenter.PendingCount);
                Put("adoption.CombatPresenter", CombatPresenterAdoption.PendingCount);

                foreach ((Type owner, string ev) in StaticEvents)
                    Put("event." + owner.Name + "." + ev, SubscriberCount(owner, ev));

                Put("gate.InputGateSuppressed", InputGate.Suppressed ? 1 : 0);
                Put("gate.ExclusiveModalHeld", ExclusiveModal.Owner != null ? 1 : 0);
                Put("clock.timeScaleMilli", (long)Mathf.Round(Time.timeScale * 1000f));
                return s;
            }

            private static long AnimationParticipantCount(bool unscaled)
            {
                long total = 0;
                foreach (NebulaAnimationDirector director in UnityEngine.Object.FindObjectsOfType<NebulaAnimationDirector>(true))
                    if (director != null) total += unscaled ? director.UnscaledParticipantCount : director.ParticipantCount;
                return total;
            }

            /// <summary>Every counter that grew, formatted for a failure message.</summary>
            public string GrowthAgainst(Snapshot baseline, int cycles, params string[] onlyKeysPrefixed)
            {
                var sb = new StringBuilder();
                foreach (var kv in Values)
                {
                    if (onlyKeysPrefixed != null && onlyKeysPrefixed.Length > 0)
                    {
                        bool match = false;
                        foreach (string p in onlyKeysPrefixed) if (kv.Key.StartsWith(p, StringComparison.Ordinal)) { match = true; break; }
                        if (!match) continue;
                    }
                    long before = baseline.Values.TryGetValue(kv.Key, out long b) ? b : 0;
                    long delta = kv.Value - before;
                    if (delta <= 0) continue;
                    sb.Append($"\n  {kv.Key}: {before} -> {kv.Value}  (delta {delta:+#;-#;0}, {(double)delta / cycles:0.##}/cycle)");
                }
                return sb.Length == 0 ? "" : sb.ToString();
            }

            public string Full()
            {
                var sb = new StringBuilder();
                foreach (var kv in Values) sb.Append($"\n  {kv.Key} = {kv.Value}");
                return sb.ToString();
            }

            public string DifferencesAgainst(Snapshot baseline, int cycles, params string[] excludedPrefixes)
            {
                var sb = new StringBuilder();
                foreach (var kv in Values)
                {
                    bool excluded = false;
                    foreach (string prefix in excludedPrefixes)
                        if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) { excluded = true; break; }
                    if (excluded) continue;
                    long before = baseline.Values.TryGetValue(kv.Key, out long b) ? b : 0;
                    long delta = kv.Value - before;
                    if (delta == 0) continue;
                    sb.Append($"\n  {kv.Key}: {before} -> {kv.Value}  (delta {delta:+#;-#;0}, {(double)delta / cycles:0.##}/cycle)");
                }
                return sb.Length == 0 ? "" : sb.ToString();
            }
        }

        // ----------------------------------------------------- material attribution

        protected sealed class MaterialRecord
        {
            public int InstanceId;
            public string Name;
            public string Shader;
            public HideFlags HideFlags;
            public string Owner;
            public string CallChain;
            public int FirstObservedCycle;
            public string FirstOwnerPath;

            public override string ToString() =>
                $"id={InstanceId} name='{Name}' shader='{Shader}' hideFlags={HideFlags} firstCycle={FirstObservedCycle} "
                + $"firstOwner='{FirstOwnerPath}' ownerClass={Owner} callChain={CallChain}";
        }

        protected static Dictionary<int, Material> LoadedMaterials()
        {
            var result = new Dictionary<int, Material>();
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                if (material != null) result[material.GetInstanceID()] = material;
            return result;
        }

        private static MaterialRecord Describe(Material material)
        {
            string name = material != null ? material.name ?? "<unnamed>" : "<destroyed>";
            string shader = material != null && material.shader != null ? material.shader.name : "<none>";
            (string owner, string callChain) = ClassifyMaterialOwner(name, shader);
            return new MaterialRecord
            {
                InstanceId = material != null ? material.GetInstanceID() : 0,
                Name = name,
                Shader = shader,
                HideFlags = material != null ? material.hideFlags : HideFlags.None,
                Owner = owner,
                CallChain = callChain,
                FirstObservedCycle = -1,
                FirstOwnerPath = "<not observed on a live Renderer>"
            };
        }

        protected sealed class MaterialTracker
        {
            private readonly HashSet<int> baseline;
            private readonly Dictionary<int, MaterialRecord> records = new Dictionary<int, MaterialRecord>();

            public MaterialTracker(Dictionary<int, Material> baselineMaterials) =>
                baseline = new HashSet<int>(baselineMaterials.Keys);

            public void Observe(int cycle)
            {
                foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
                {
                    if (renderer == null) continue;
                    foreach (Material material in renderer.sharedMaterials)
                        ObserveMaterial(material, cycle, HierarchyPath(renderer.transform) + " [" + renderer.GetType().Name + "]");
                }
                ObserveMaterial(RenderSettings.skybox, cycle, "RenderSettings.skybox");
            }

            private void ObserveMaterial(Material material, int cycle, string ownerPath)
            {
                if (material == null) return;
                int id = material.GetInstanceID();
                if (baseline.Contains(id) || records.ContainsKey(id)) return;
                MaterialRecord record = Describe(material);
                record.FirstObservedCycle = cycle;
                record.FirstOwnerPath = ownerPath;
                records[id] = record;
            }

            public string Report(int maxDetails = 80)
            {
                Dictionary<int, Material> loaded = LoadedMaterials();
                foreach (var kv in loaded)
                {
                    if (baseline.Contains(kv.Key) || records.ContainsKey(kv.Key)) continue;
                    records[kv.Key] = Describe(kv.Value);
                }

                var counts = new Dictionary<string, int>();
                foreach (MaterialRecord record in records.Values)
                {
                    string key = record.Owner + " | " + record.Name + " | " + record.Shader + " | " + record.HideFlags;
                    counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
                }

                var sb = new StringBuilder($"\nnew material instances observed: {records.Count}");
                foreach (var kv in counts) sb.Append($"\n  group x{kv.Value}: {kv.Key}");
                int details = Mathf.Min(maxDetails, records.Count);
                sb.Append($"\nfirst {details} instance records:");
                int index = 0;
                foreach (MaterialRecord record in records.Values)
                {
                    if (index++ >= details) break;
                    sb.Append("\n  " + record);
                }
                if (details < records.Count) sb.Append($"\n  ... {records.Count - details} further records omitted from XML; group counts above remain complete");
                return sb.ToString();
            }

            private static string HierarchyPath(Transform transform)
            {
                if (transform == null) return "<none>";
                var names = new List<string>();
                for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
                names.Reverse();
                return string.Join("/", names);
            }
        }

        /// <summary>
        /// MaterialLibrary leaves generated materials with Unity's default name,
        /// so owner attribution must use the material's shader/name family plus
        /// the production call graph. The detailed records (including instance
        /// id/hideFlags) make that inference auditable rather than pretending the
        /// factory recorded allocation stacks that it does not record.
        /// </summary>
        private static (string owner, string callChain) ClassifyMaterialOwner(string name, string shader)
        {
            string joined = (name + " " + shader).ToLowerInvariant();
            if (joined.Contains("strategicmap"))
                return ("StrategicMap preview (self-destroying control)", "StrategicMap* Awake/Create -> new Material -> OnDestroy -> Destroy(material)");
            if (joined.Contains("skybox"))
                return ("SkyboxBaker", "EnvironmentInstaller.BuildWorldFoundation -> SkyboxBaker.Bake -> new Material");
            if (joined.Contains("line") || joined.Contains("trail"))
                return ("SystemMaterializer/OrbitRing", "SystemStreamingDirector.BootFull -> SystemMaterializer.UpgradeToFull -> AddTrail/OrbitRing.Create -> MaterialLibrary.CreateAdditive");
            if (joined.Contains("particle") || joined.Contains("additive") || joined.Contains("nebula"))
                return ("world additive generators or streamed-system visuals", "NebulaBootstrap.StartSession -> EnvironmentInstaller/SystemStreamingDirector -> Starfield/Galaxy/SystemMaterializer/POI -> MaterialLibrary.CreateAdditive[Fade]");
            if (joined.Contains("lit") || joined.Contains("standard"))
                return ("streamed-system/world lit visuals", "SystemStreamingDirector.BootFull -> SystemMaterializer/POI/Enemy -> MaterialLibrary.CreateLit");
            if (joined.Contains("unlit"))
                return ("star/ship/world unlit visuals", "NebulaBootstrap.StartSession -> World/Player installer -> Star/ShipFactory -> MaterialLibrary.CreateUnlit");
            return ("unclassified runtime material", "runtime owner not encoded in Material; correlate with isolated scenario and production MaterialLibrary call graph");
        }
    }
}
