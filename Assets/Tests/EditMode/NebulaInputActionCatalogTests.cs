using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Nebula.Ship;
using Nebula.UI.Controls;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// The input ownership contract: one catalog, one owner per key per context, and
    /// nothing reserved that nothing reads.
    /// <para>
    /// The defect these exist for is not "a key is wrong" — it is that no test could
    /// tell a reserved key with a consumer from one without. <c>HudToggleKey == Tab</c>
    /// was green for four slices over a binding no runtime file ever read. So the
    /// assertions here are about the <i>relationship</i> between the catalog, the
    /// reserved list and the source: they read every runtime file and resolve the keys
    /// it actually samples, rather than comparing one constant to a literal.
    /// </para>
    /// </summary>
    public class NebulaInputActionCatalogTests
    {
        /// <summary>
        /// Every process-wide input static back to its shipped state, before each test
        /// rather than only after.
        /// <para>
        /// A <c>[TearDown]</c> alone is not enough and this fixture is the proof: it
        /// reset the combat bindings on the way out and nothing on the way in, so a
        /// rebind or a registered context left behind by an <i>earlier fixture</i> in
        /// the same Unity process was still in force when these tests started. Five of
        /// them went red in a batched run and green alone, which is the signature of
        /// exactly that and was very nearly diagnosed as a need for process isolation.
        /// Resetting on entry makes the run order-independent instead.
        /// </para>
        /// <para>
        /// Through the real public reset contract — <c>ResetRebindableToDefaults</c>,
        /// <c>ResetContexts</c>, <c>ResetSourceToKeyboard</c> — never reflection into a
        /// private field. A reset a test has to reach around for is a reset production
        /// cannot perform either.
        /// </para>
        /// </summary>
        [SetUp]
        public void ResetProcessWideInputState()
        {
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        [TearDown]
        public void ResetBindings()
        {
            CombatBindings.ResetToDefaults();
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        // =====================================================================
        // The catalog itself
        // =====================================================================

        [Test]
        public void EveryActionIdIsUniqueAndNonEmpty()
        {
            var seen = new HashSet<string>();
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(action.Id), "an action has no id");
                Assert.IsTrue(seen.Add(action.Id), $"duplicate action id: {action.Id}");
            }
        }

        [Test]
        public void EveryActionHasAPlayerFacingLabel()
        {
            foreach (InputActionDefinition action in NebulaInputActions.All)
                Assert.IsFalse(string.IsNullOrWhiteSpace(action.LabelTr),
                    $"{action.Id} has no player-facing label");
        }

        [Test]
        public void EveryNonAxisActionHasAKey()
        {
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (action.Press == InputPressType.Axis) continue;
                Assert.AreNotEqual(KeyCode.None, action.Key, $"{action.Id} is bound to nothing");
            }
        }

        /// <summary>
        /// Rebindable used to mean exactly "combat-backed", which is what P1-ECO-006
        /// was: six extraction actions sat in the remap catalog as rows the player
        /// could reassign, and the reassignment did nothing because the runtime read a
        /// fixed KeyCode. The contextual rows are genuinely rebindable now, so the
        /// invariant is no longer equality — it is that every combat row is still
        /// rebindable, and that nothing became rebindable without also being routed
        /// through the router (an unrouted rebindable row is the old bug returning).
        /// </summary>
        [Test]
        public void EveryCombatRowIsRebindable_AndEveryRebindableRowIsRouted()
        {
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (action.Combat.HasValue)
                    Assert.IsTrue(action.Rebindable, $"{action.Id}: a combat row must stay rebindable");
                if (action.Rebindable && !action.Combat.HasValue)
                    Assert.IsTrue(action.Arbitrated,
                        $"{action.Id} is rebindable but not arbitrated — its runtime read cannot be honouring the rebind");
            }

            Assert.AreEqual(9, NebulaInputActions.All.Count(a => a.Combat.HasValue),
                "the nine combat actions are still the combat set");
        }

        [Test]
        public void PersistedIsExactlyTheRebindableSet()
        {
            foreach (InputActionDefinition action in NebulaInputActions.All)
                Assert.AreEqual(action.Rebindable, action.Persisted,
                    $"{action.Id}: only a rebindable action may be written to the settings store");
        }

        /// <summary>
        /// The catalog states each combat action's shipped key so the docs and
        /// "restore defaults" have one answer. <see cref="CombatBindings"/> states it
        /// too, in <c>ResetToDefaults</c>. Two statements of one fact drift, so this
        /// pins them together rather than trusting them.
        /// </summary>
        [Test]
        public void CatalogCombatDefaults_MatchTheBindingServicesOwnDefaults()
        {
            CombatBindings.ResetToDefaults();
            foreach ((CombatAction action, KeyCode key) in CombatBindings.CurrentBindings())
            {
                InputActionDefinition row = NebulaInputActions.ForCombat(action);
                Assert.IsNotNull(row, $"{action} is not in the catalog");
                Assert.AreEqual(key, row.DefaultKey, $"{action}: catalog default disagrees with the service");
            }
        }

        [Test]
        public void CombatRowKey_TracksALiveRebind()
        {
            Assert.IsTrue(CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9));
            Assert.AreEqual(KeyCode.Alpha9, NebulaInputActions.ForCombat(CombatAction.CombatBoost).Key,
                "the catalog is holding a copy of the binding instead of reading the service");
        }

        // =====================================================================
        // Collisions
        // =====================================================================

        /// <summary>
        /// The U double-fire, and the only assertion that actually names it: cancelling
        /// a Quick Asset Package and opening the command menu shared U, both read it
        /// with <c>Input.GetKeyDown</c> in their own <c>Update</c>, and one press did
        /// both. They share the key still — that was never the problem — but they go
        /// through the router, so exactly one of them can answer a press.
        /// </summary>
        [Test]
        public void TheUCollisionIsGone()
        {
            InputActionDefinition menu = NebulaInputActions.Find(NebulaInputActions.MenuProgression);
            InputActionDefinition cancel = NebulaInputActions.Find(NebulaInputActions.QuickPackageCancel);

            Assert.AreEqual(menu.Key, cancel.Key, "the fixture is meaningless unless the two still share a key");
            Assert.IsTrue(menu.Arbitrated && cancel.Arbitrated,
                "both claimants of a shared key must go through the router");

            foreach (NebulaInputActions.InputCollision collision in NebulaInputActions.Collisions())
                Assert.AreNotEqual(menu.Key, collision.Key,
                    $"U is still an unresolved collision: {collision}");
        }

        [Test]
        public void TheContextualClaimOutranksTheGlobalOne()
        {
            Assert.Greater(
                NebulaInputActions.Rank(InputContext.QuickAssetPackage),
                NebulaInputActions.Rank(InputContext.Global),
                "a live sub-mode must outrank the global intent, or the router resolves U the wrong way");
        }

        /// <summary>
        /// Every shared key left in the game, stated exactly.
        /// <para>
        /// An exact set rather than "must be empty". Five keys still have two live
        /// owners and all of them live in files this slice does not own — the
        /// build-mode brackets against the processing panel's, F across extraction /
        /// logistics / POI, T across extraction and logistics, and C across down-thrust
        /// and reclaim. Asserting emptiness would mean deleting the finding; asserting
        /// the exact set means the next one that appears fails this test.
        /// </para>
        /// </summary>
        [Test]
        public void TheRemainingCollisionsAreExactlyTheKnownPreExistingOnes()
        {
            // Empty, and it is the ratchet reaching the end rather than the assertion
            // being dropped. Five keys had two live owners: the build-mode brackets
            // against the processing panel's, F across extraction / logistics / POI, T
            // across extraction and logistics, and C across down-thrust and reclaim.
            // Every one of them now has both sides registered with the router on
            // distinct context ranks, so one press reaches exactly one owner and
            // Collisions() stops counting the pair.
            //
            // C was the last, and it is the one worth naming: flight.vertical_down and
            // evacuation.reclaim are both arbitrated, Evacuation outranks Flight, and
            // the Evacuation context is live only at a planet that has actually been
            // abandoned — so C is reclaim exactly there and down-thrust everywhere
            // else. SharedC_GoesToReclaimOnlyWhereReclaimExists in
            // AuditPart3A3RegressionTests measures that behaviour; this only measures
            // that no unresolved pair is left in the catalog.
            var expected = new (KeyCode, string, string)[0];

            var actual = NebulaInputActions.Collisions()
                .Select(c => Pair(c.Key, c.First.Id, c.Second.Id))
                .OrderBy(s => s).ToArray();
            var wanted = expected
                .Select(e => Pair(e.Item1, e.Item2, e.Item3))
                .OrderBy(s => s).ToArray();

            CollectionAssert.AreEqual(wanted, actual,
                "the set of keys with two live owners changed:\n  expected " +
                string.Join("\n           ", wanted) + "\n  actual   " +
                string.Join("\n           ", actual));
        }

        private static string Pair(KeyCode key, string a, string b) =>
            string.CompareOrdinal(a, b) <= 0 ? $"{key}:{a}|{b}" : $"{key}:{b}|{a}";

        [Test]
        public void EscapeIsAShieldedSharedIntent_NotSevenCollisions()
        {
            InputActionDefinition[] escapes = NebulaInputActions.All
                .Where(a => a.Key == KeyCode.Escape).ToArray();

            Assert.Greater(escapes.Length, 1, "Escape is expected to have several owners");
            foreach (InputActionDefinition action in escapes)
                Assert.IsTrue(action.SharedIntent,
                    $"{action.Id} answers Escape without declaring it a shared cancel intent");
        }

        // =====================================================================
        // Reserved keys, derived from what the source really reads
        // =====================================================================

        /// <summary>
        /// Every key any runtime file actually samples, resolved from the source.
        /// <para>
        /// This is the check that would have caught Tab. A constant compared to a
        /// literal cannot see that nothing reads it; reading every
        /// <c>Input.GetKey*</c> argument in the project and resolving it back to a
        /// <see cref="KeyCode"/> can.
        /// </para>
        /// </summary>
        /// <summary>
        /// Removes <c>//</c> and <c>/* */</c> comments while leaving string and char
        /// literals intact, so a <c>"http://…"</c> inside a literal is not mistaken for
        /// the start of a comment. Whitespace stands in for what is removed so that
        /// nothing on either side of a comment is accidentally joined into one token.
        /// </summary>
        private static string StripComments(string source)
        {
            var output = new System.Text.StringBuilder(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                if (c == '@' && i + 1 < source.Length && source[i + 1] == '"')
                {
                    // Verbatim string: only "" escapes, and newlines are legal inside.
                    output.Append(c).Append(source[++i]);
                    while (++i < source.Length)
                    {
                        output.Append(source[i]);
                        if (source[i] != '"') continue;
                        if (i + 1 < source.Length && source[i + 1] == '"') output.Append(source[++i]);
                        else break;
                    }
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    output.Append(c);
                    while (++i < source.Length)
                    {
                        output.Append(source[i]);
                        if (source[i] == '\\' && i + 1 < source.Length) { output.Append(source[++i]); continue; }
                        if (source[i] == quote) break;
                    }
                    continue;
                }

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    output.Append('\n');
                    continue;
                }

                if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        if (source[i] == '\n') output.Append('\n');
                        i++;
                    }
                    i++;
                    output.Append(' ');
                    continue;
                }

                output.Append(c);
            }
            return output.ToString();
        }

        /// <summary>
        /// Every runtime source, keyed by file name, with comments stripped — the same
        /// treatment <see cref="KeysTheRuntimeActuallyReads"/> gives them and for the
        /// same reason: a doc comment quoting a call is prose, not a consumer.
        /// </summary>
        private static Dictionary<string, string> RuntimeSources()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            var sources = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                sources[Path.GetFileName(file)] = StripComments(File.ReadAllText(file));
            return sources;
        }

        private static HashSet<KeyCode> KeysTheRuntimeActuallyReads()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            // Comments are stripped before anything is matched. This scanner is the
            // evidence behind "every catalog key is really read" and "every key the
            // runtime reads is in the catalog", and a scanner that cannot tell code from
            // prose is not evidence: NebulaInputRouter's own doc comments explain the
            // rule by quoting `Input.GetKeyDown(SomeConst)`, and the scanner was reading
            // those sentences as two unresolvable key reads. It would equally have
            // counted a commented-out read as a live one.
            var sources = files.ToDictionary(f => f, f => StripComments(File.ReadAllText(f)));
            string blob = string.Join("\n", sources.Values);

            Dictionary<string, List<KeyCode>> Declarations(string text)
            {
                var map = new Dictionary<string, List<KeyCode>>();
                foreach (Match m in Regex.Matches(text,
                             @"\b(\w+)\s*(?:\{\s*get;\s*set;\s*\})?\s*=\s*KeyCode\.(\w+)\s*;"))
                {
                    if (!System.Enum.TryParse(m.Groups[2].Value, out KeyCode key)) continue;
                    if (!map.TryGetValue(m.Groups[1].Value, out List<KeyCode> list))
                        map[m.Groups[1].Value] = list = new List<KeyCode>();
                    list.Add(key);
                }
                return map;
            }

            Dictionary<string, string> CatalogRefs(string text)
            {
                var map = new Dictionary<string, string>();
                foreach (Match m in Regex.Matches(text,
                             @"\b(\w+)\s*=\s*NebulaInputActions\.Key\(NebulaInputActions\.(\w+)\)"))
                    map[m.Groups[1].Value] = m.Groups[2].Value;
                return map;
            }

            // The catalog's own shipped-key constants, so a `NebulaInputActions.Key(id)`
            // reference resolves without executing anything. Rows whose key comes from
            // their owner's constant rather than a `…Default` here are resolved through
            // RoutedActionKeys instead — see there for why they must not be duplicated.
            var catalogDefaults = new Dictionary<string, KeyCode>();
            foreach (Match m in Regex.Matches(blob, @"internal const KeyCode (\w+)Default = KeyCode\.(\w+);"))
                if (System.Enum.TryParse(m.Groups[2].Value, out KeyCode key))
                    catalogDefaults[m.Groups[1].Value] = key;

            Dictionary<string, List<KeyCode>> globalDecls = Declarations(blob);
            Dictionary<string, string> globalCatalog = CatalogRefs(blob);

            var read = new HashSet<KeyCode>();
            var unresolved = new List<string>();

            foreach (KeyValuePair<string, string> file in sources)
            {
                Dictionary<string, List<KeyCode>> localDecls = Declarations(file.Value);
                Dictionary<string, string> localCatalog = CatalogRefs(file.Value);

                foreach (Match m in Regex.Matches(file.Value, @"Input\.GetKey(?:Down|Up)?\(\s*([^)]+?)\s*\)"))
                {
                    string arg = m.Groups[1].Value.Trim();

                    Match literal = Regex.Match(arg, @"^KeyCode\.(\w+)$");
                    if (literal.Success && System.Enum.TryParse(literal.Groups[1].Value, out KeyCode direct))
                    { read.Add(direct); continue; }

                    string name = arg.Substring(arg.LastIndexOf('.') + 1);
                    // A pass-through parameter is not a key owner: IKeyInputSource and
                    // the router both forward whatever they are handed.
                    if (!Regex.IsMatch(name, @"^\w+$") || name == "key") continue;

                    if (localCatalog.TryGetValue(name, out string localId)
                        && catalogDefaults.TryGetValue(localId, out KeyCode fromLocalCatalog))
                    { read.Add(fromLocalCatalog); continue; }

                    // The declaring file wins over a same-named constant elsewhere:
                    // ConfirmKey is P in the package and F in extraction.
                    if (localDecls.TryGetValue(name, out List<KeyCode> local))
                    { foreach (KeyCode k in local) read.Add(k); continue; }

                    if (globalCatalog.TryGetValue(name, out string globalId)
                        && catalogDefaults.TryGetValue(globalId, out KeyCode fromCatalog))
                    { read.Add(fromCatalog); continue; }

                    if (globalDecls.TryGetValue(name, out List<KeyCode> global))
                    { foreach (KeyCode k in global) read.Add(k); continue; }

                    unresolved.Add($"{file.Key}: {arg}");
                }

                // Held as well as Pressed. Held-type rows (scan, transfer, the logistics
                // deposit/withdraw pair) route through NebulaInputRouter.Held, and a scan
                // that only knew Pressed reported them as catalog rows nothing reads —
                // which is the opposite of the truth once the read moved to the router.
                // SharedPressed counts too. It is the shared-intent read — Escape, and
                // only Escape — and it is a real consumer of a real key; leaving it out
                // would report every Escape row as a catalog entry nothing reads, which
                // is precisely the false negative this scan exists to prevent.
                foreach (Match m in Regex.Matches(file.Value,
                             @"NebulaInputRouter\.(?:Pressed|Held|SharedPressed)\(NebulaInputActions\.(\w+)\)"))
                {
                    if (catalogDefaults.TryGetValue(m.Groups[1].Value, out KeyCode routed)) read.Add(routed);
                    else if (RoutedActionKeys().TryGetValue(m.Groups[1].Value, out KeyCode viaCatalog))
                        read.Add(viaCatalog);
                    else unresolved.Add($"{file.Key}: router:{m.Groups[1].Value}");
                }

                // One hop through a local forwarder counts too, and has to: the laser,
                // plasma and EM rows are read as WeaponHeld(NebulaInputActions.Combat…),
                // a helper that exists so the fire-denied notice can be edge-triggered
                // in one place. Matching only the direct call shape reported those three
                // live rows as keys nothing reads — a false negative in the very scan
                // that is supposed to prove the catalog is not lying.
                //
                // The rule stays tight rather than accepting any call that mentions an
                // action id: a forwarder is a method in this same file that takes a
                // parameter and hands that parameter straight to the router. That is a
                // real read path, and a method that does anything else is not one.
                foreach (string forwarder in RouterForwarders(file.Value))
                {
                    foreach (Match m in Regex.Matches(file.Value,
                                 forwarder + @"\(\s*NebulaInputActions\.(\w+)\s*\)"))
                    {
                        if (catalogDefaults.TryGetValue(m.Groups[1].Value, out KeyCode routed)) read.Add(routed);
                        else if (RoutedActionKeys().TryGetValue(m.Groups[1].Value, out KeyCode viaCatalog))
                            read.Add(viaCatalog);
                        else unresolved.Add($"{file.Key}: {forwarder}:{m.Groups[1].Value}");
                    }
                }
            }

            Assert.IsEmpty(unresolved,
                "the scan could not resolve a key read — it is not evidence of anything until it can:\n  " +
                string.Join("\n  ", unresolved));

            return read;
        }

        /// <summary>
        /// Names of methods in <paramref name="source"/> that take an action id and
        /// pass it straight to <c>NebulaInputRouter.Pressed/Held/SharedPressed</c>.
        /// <para>
        /// Deliberately narrow. The parameter the router is handed must be the same
        /// identifier the method declares, so a method that picks its own action id and
        /// merely happens to accept a string is not a forwarder, and calls to it do not
        /// vouch for any catalog row.
        /// </para>
        /// </summary>
        private static IEnumerable<string> RouterForwarders(string source)
        {
            foreach (Match declaration in Regex.Matches(source,
                         @"\b(\w+)\s*\(\s*string\s+(\w+)\s*\)\s*(?:=>|\{)"))
            {
                string method = declaration.Groups[1].Value, parameter = declaration.Groups[2].Value;
                if (method == "Pressed" || method == "Held" || method == "SharedPressed") continue;

                string tail = source.Substring(declaration.Index);
                if (Regex.IsMatch(tail,
                        @"NebulaInputRouter\.(?:Pressed|Held|SharedPressed)\(\s*" + parameter + @"\s*\)"))
                    yield return method;
            }
        }

        /// <summary>
        /// Action-ID constant name → the key that action ships with, taken from the
        /// catalog itself.
        /// <para>
        /// Most routed rows carry an <c>internal const KeyCode …Default</c> the text
        /// scan can read. The bracket rows deliberately do not: their keys are declared
        /// on their owners (<c>ShipProcessing.PrevSlotKey</c>,
        /// <c>ShipExtraction</c>'s defense-node keys) and the catalog reads them there,
        /// which is the one-declaration-per-key rule this catalog was built to keep.
        /// Adding a second copy here just to satisfy a regex would break exactly the
        /// property the scan exists to protect, so the scan resolves them the honest
        /// way instead — by asking the catalog what the row's key actually is.
        /// </para>
        /// </summary>
        private static Dictionary<string, KeyCode> RoutedActionKeys()
        {
            var byName = new Dictionary<string, KeyCode>();
            foreach (System.Reflection.FieldInfo field in typeof(NebulaInputActions)
                         .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                var id = (string)field.GetRawConstantValue();
                InputActionDefinition action = NebulaInputActions.Find(id);
                if (action != null) byName[field.Name] = action.DefaultKey;
            }
            return byName;
        }

        /// <summary>
        /// Nothing may be reserved that no runtime file reads. This is the Tab
        /// contract, generalised: a reservation costs the player a key, so it has to
        /// be paid for with a consumer.
        /// </summary>
        [Test]
        public void EveryReservedKey_IsActuallyReadBySomeRuntimeFile()
        {
            HashSet<KeyCode> read = KeysTheRuntimeActuallyReads();
            foreach (KeyCode reserved in CombatBindingValidator.ReservedSystemKeys)
                Assert.IsTrue(read.Contains(reserved),
                    $"{reserved} is reserved against rebinding but no runtime file reads it");
        }

        /// <summary>
        /// And the reverse: a key the game really reads must be in the catalog.
        /// <para>
        /// The exemption is <see cref="InputActionDefinition.UnityAxisOwned"/>, read
        /// off the row rather than typed here (P1-DATA-002). It used to be a
        /// <c>HashSet</c> of two ids in this method, which put the fact "Unity owns
        /// this key" in the test instead of in the catalog — so the catalog itself
        /// still claimed those rows as Nebula's, and anything else reading it was
        /// misled. The rows carry the mark now and this reads it.
        /// </para>
        /// </summary>
        /// <summary>
        /// Which input contexts have an owner that can say whether they are live, and
        /// which do not. Both lists are pinned, because the difference decides whether
        /// suppression can work at all.
        /// <para>
        /// <c>NebulaInputRouter.Owns</c> refuses a press whose context is not live, but
        /// it can only ask that of a context some component has claimed with
        /// <c>SetContextSource</c>. Five contexts claim nothing, so for their rows the
        /// question has no answer and the router lets the press through — which is the
        /// behaviour those rows have always had, not a new hole. It is written down
        /// here rather than left to be rediscovered: a row moved into one of these
        /// contexts is a row a menu cannot suppress, and moving one out of the owned
        /// set silently disables suppression for it.
        /// </para>
        /// <para>
        /// Read from production source, so adding a registration in the runtime is what
        /// moves a context between the lists — never an edit to this test alone.
        /// </para>
        /// </summary>
        [Test]
        public void EveryInputContextEitherDeclaresAnOwnerOrIsNamedAsAmbient()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Nebula");
            string blob = string.Join("\n", Directory
                .GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Select(f => StripComments(File.ReadAllText(f))));

            var owned = new HashSet<InputContext>();
            foreach (Match m in Regex.Matches(blob, @"SetContextSource\(\s*InputContext\.(\w+)"))
                if (System.Enum.TryParse(m.Groups[1].Value, out InputContext context)) owned.Add(context);

            // Global is not registered and never needs to be: IsContextActive answers
            // it directly from the input gate.
            var ambient = new[]
            {
                InputContext.Menu, InputContext.Rebind, InputContext.StrategicMap,
                InputContext.Tutorial, InputContext.Debug,
            };

            foreach (InputContext context in ambient)
                Assert.IsFalse(owned.Contains(context),
                    $"{context} artık bir sahip bildiriyor — ambient listesinden çıkarın, " +
                    "yoksa gerçek bastırması olan bir bağlam bastırmasızmış gibi belgelenir");

            foreach (InputContext context in System.Enum.GetValues(typeof(InputContext)))
            {
                if (context == InputContext.Global || ambient.Contains(context)) continue;
                Assert.IsTrue(owned.Contains(context),
                    $"{context} bağlamını hiçbir bileşen SetContextSource ile sahiplenmiyor; " +
                    "bu bağlamdaki satırları hiçbir ekran bastıramaz");
            }
        }

        [Test]
        public void EveryCatalogKey_IsActuallyReadBySomeRuntimeFile()
        {
            HashSet<KeyCode> read = KeysTheRuntimeActuallyReads();
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (action.Press == InputPressType.Axis) continue;
                if (action.UnityAxisOwned) continue;
                Assert.IsTrue(read.Contains(action.Key),
                    $"{action.Id} claims {action.Key} but no runtime file reads it");
            }
        }

        /// <summary>
        /// The action-level half, which is the one that can see a dead row (P1-TEST-005).
        /// <para>
        /// The key-level checks above cannot. Seven rows share Escape and four share
        /// Mouse0/1/2 with nothing else; a row whose key some <i>other</i> row is read
        /// on passes a key-level scan while being wired to nothing at all. What the
        /// player loses when that happens is an entry in the settings screen that
        /// promises an action the game does not have.
        /// </para>
        /// <para>
        /// The rule: a row is consumed when a runtime file names its <b>id</b>, or
        /// when its key is read and the row is the <i>only</i> claimant of that key
        /// that could be live at the same moment — in which case the key-level
        /// evidence can only belong to it. A row sharing its key with a co-occurring
        /// claimant has to be named, because that is precisely the case where a live
        /// neighbour would vouch for a dead row.
        /// </para>
        /// <para>
        /// Two exemptions, both already stated by the catalog rather than invented
        /// here. A shared-intent pair (Escape) is the design and not an ambiguity —
        /// <c>Collisions</c> passes over the same pairs for the same reason — and a
        /// Unity-axis-owned row is read inside the engine where no source scan will
        /// ever see it. Axis rows carry no key at all, so there is nothing for a key
        /// scan to attribute either way.
        /// </para>
        /// </summary>
        [Test]
        public void EveryCatalogAction_HasAConsumerThatNamesIt_OrOwnsItsKeyAlone()
        {
            HashSet<string> named = ActionIdsTheRuntimeNames();
            HashSet<KeyCode> read = KeysTheRuntimeActuallyReads();
            IReadOnlyList<InputActionDefinition> all = NebulaInputActions.All;

            var dead = new List<string>();
            foreach (InputActionDefinition action in all)
            {
                if (action.UnityAxisOwned) continue;
                if (action.Press == InputPressType.Axis) continue;
                if (named.Contains(action.Id)) continue;

                bool hidden = false;
                foreach (InputActionDefinition other in all)
                {
                    if (ReferenceEquals(other, action) || other.Key != action.Key) continue;
                    if (!NebulaInputActions.CanCoOccur(action.Context, other.Context)) continue;
                    if (action.SharedIntent && other.SharedIntent) continue;
                    hidden = true;
                    break;
                }

                if (hidden || action.Key == KeyCode.None || !read.Contains(action.Key))
                    dead.Add($"{action.Id} [{action.Context}] {action.Key}");
            }

            Assert.IsEmpty(dead,
                "these catalog rows are named by no runtime file and share their key with a "
                + "claimant that can be live at the same moment, so nothing proves the action "
                + "exists — the settings screen would offer a control the game does not have:\n  "
                + string.Join("\n  ", dead));
        }

        /// <summary>
        /// Every action-id constant a runtime file mentions, in any form:
        /// <c>NebulaInputActions.Foo</c> through the router, through
        /// <c>NebulaInputActions.Key(...)</c>, or as the argument to anything else.
        /// The constant's <i>name</i> is what is scanned, then mapped back to the id
        /// the catalog knows it by, so a rename cannot make a row look consumed.
        /// </summary>
        private static HashSet<string> ActionIdsTheRuntimeNames()
        {
            var byConstantName = new Dictionary<string, string>();
            foreach (System.Reflection.FieldInfo field in typeof(NebulaInputActions)
                         .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string)) continue;
                byConstantName[field.Name] = (string)field.GetRawConstantValue();
            }

            var named = new HashSet<string>();
            foreach (KeyValuePair<string, string> file in RuntimeSources())
            {
                // The catalog's own declaration file names every constant by
                // definition, so it is not evidence that anything consumes them.
                if (file.Key == "NebulaInputActions.cs") continue;

                foreach (Match m in Regex.Matches(file.Value, @"NebulaInputActions\.(\w+)"))
                    if (byConstantName.TryGetValue(m.Groups[1].Value, out string id)) named.Add(id);

                // A combat row's id is reached through its CombatAction as well:
                // the settings surface arms captures by CombatAction and the catalog
                // maps that to the row. Naming the action names the row.
                foreach (Match m in Regex.Matches(file.Value, @"CombatAction\.(\w+)"))
                {
                    if (!System.Enum.TryParse(m.Groups[1].Value, out CombatAction combat)) continue;
                    InputActionDefinition row = NebulaInputActions.ForCombat(combat);
                    if (row != null) named.Add(row.Id);
                }
            }
            return named;
        }

        /// <summary>
        /// And the completeness direction: a key the game reads must be a row in the
        /// catalog. Without this the catalog is a list somebody remembered to update,
        /// which is what the reserved-key array was.
        /// <para>
        /// The four chord modifiers are the only exemption. They are read by
        /// <c>NebulaInputBindings.CommonModifierHeld</c> as part of a chord rather than
        /// as actions of their own, and the reserved-key derivation adds them
        /// explicitly for exactly that reason.
        /// </para>
        /// </summary>
        [Test]
        public void EveryKeyTheRuntimeReads_IsARowInTheCatalog()
        {
            var modifiers = new HashSet<KeyCode>
            {
                KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftCommand, KeyCode.RightCommand,
            };

            var catalogued = new HashSet<KeyCode>(NebulaInputActions.All.Select(a => a.Key));
            foreach (KeyCode key in KeysTheRuntimeActuallyReads())
            {
                if (modifiers.Contains(key)) continue;
                Assert.IsTrue(catalogued.Contains(key),
                    $"{key} is read by runtime code but is in no catalog row — add it to " +
                    "NebulaInputActions so the collision scan and the reserved list can see it");
            }
        }

        [Test]
        public void TheKeysTheHandKeptListMissed_AreReservedNow()
        {
            // Both bracket keys are read over live gameplay — the processing panel's
            // slot selection (ShipProcessing.cs:91-92) and build mode's defense node
            // (ShipExtraction.cs:247-248) — and Enter is read over live gameplay too,
            // by the tutorial card (TutorialHud.cs:176-178), which has no InputGate
            // check at all. All four were assignable to a combat action.
            foreach (KeyCode key in new[] { KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Return, KeyCode.KeypadEnter })
                Assert.IsTrue(CombatBindingValidator.IsReservedBySystem(key),
                    $"{key} has a live owner and must not be assignable to a combat action");
        }

        [Test]
        public void ScreenOnlyKeys_AreContextuallyNotedRatherThanBlocked()
        {
            // Home, Backspace and Delete belong to the strategic map; the arrows
            // belong to the command menu and to uGUI's own Horizontal/Vertical axes.
            // All four surfaces raise InputGate while they are open, so combat cannot
            // read anything at the same moment — blocking them would be blocking
            // without evidence.
            foreach (KeyCode key in new[] { KeyCode.Home, KeyCode.Backspace, KeyCode.Delete })
            {
                Assert.IsFalse(CombatBindingValidator.IsReservedBySystem(key),
                    $"{key} is only owned inside a screen and must stay assignable");
                Assert.IsNotNull(CombatBindingValidator.ContextualOwnerOf(key),
                    $"{key} has a screen owner and the player must be told about it");
                Assert.IsNotNull(NebulaControlBindings.ContextNote(key),
                    $"{key} produces no note for the player");
                Assert.AreEqual(ControlRebindResult.Accepted,
                    NebulaControlBindings.Check(CombatAction.Repair, key),
                    $"{key} was refused despite having no live owner");
            }

            // The arrows are no longer in that group. P1-UI-005 moved the command
            // menu's page stepper to PageUp/PageDown precisely so the arrows belong to
            // uGUI navigation alone, which is what made keyboard menu use possible.
            // They stay assignable, and there is now no second owner to note.
            foreach (KeyCode key in new[] { KeyCode.LeftArrow, KeyCode.RightArrow })
            {
                Assert.IsFalse(CombatBindingValidator.IsReservedBySystem(key),
                    $"{key} has no Nebula owner and must stay assignable");
                Assert.IsNull(CombatBindingValidator.ContextualOwnerOf(key),
                    $"{key} regained a Nebula owner — the page stepper must stay on PageUp/PageDown");
                Assert.AreEqual(ControlRebindResult.Accepted,
                    NebulaControlBindings.Check(CombatAction.Repair, key),
                    $"{key} was refused despite having no live owner");
            }
        }

        [Test]
        public void SubmitAndCancelKeysKeepWorking_MeaningNothingMayTakeThemFromTheUi()
        {
            // Enter and Escape are what uGUI's Submit and Cancel axes are bound to
            // (ProjectSettings/InputManager.asset). A combat action on either would
            // fire alongside a menu press, so both are reserved outright.
            Assert.IsTrue(CombatBindingValidator.IsReservedBySystem(KeyCode.Return));
            Assert.IsTrue(CombatBindingValidator.IsReservedBySystem(KeyCode.Escape));

            Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, KeyCode.Return));
            Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, KeyCode.Escape));
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey);
        }

        [Test]
        public void BracketKeysStayWithProcessingAndBuildMode()
        {
            Assert.AreEqual(KeyCode.LeftBracket, NebulaInputActions.Key(NebulaInputActions.ProcessingPrevSlot));
            Assert.AreEqual(KeyCode.RightBracket, NebulaInputActions.Key(NebulaInputActions.ProcessingNextSlot));
            Assert.IsFalse(CombatBindings.TrySetBinding(CombatAction.Repair, KeyCode.LeftBracket));
        }

        // =====================================================================
        // Debug-only actions
        // =====================================================================

        [Test]
        public void DebugOnlyActions_NeverReachTheReleaseVisibleSet()
        {
            InputActionDefinition[] release = NebulaInputActions.PlayerVisible(includeDebugOnly: false).ToArray();
            foreach (InputActionDefinition action in release)
                Assert.IsFalse(action.DebugOnly, $"{action.Id} is debug-only and reached the release list");

            Assert.IsTrue(NebulaInputActions.PlayerVisible(includeDebugOnly: true).Any(a => a.DebugOnly),
                "no action is marked debug-only, so this test is asserting nothing");
        }

        /// <summary>
        /// The settings page's controls summary is a fixed-height column that has
        /// already overflowed once — the class's own comment records three rows being
        /// unreachable off the bottom. So the number of rows is a contract, not a
        /// preference: adding a ninth means finding room for it first.
        /// </summary>
        [Test]
        public void TheControlsSummaryIsExactlyEightRows()
        {
            InputActionDefinition[] featured = NebulaInputActions.All.Where(a => a.Featured).ToArray();
            Assert.AreEqual(8, featured.Length,
                "the settings controls summary changed size: " +
                string.Join(", ", featured.Select(a => a.Id)));

            foreach (InputActionDefinition action in featured)
            {
                // A featured row used to be barred from being rebindable, because a
                // rebindable row belonged in the rebinding panel and a fixed one in
                // this summary. The extraction rows are now both: the summary states
                // them and the panel reassigns them. What still has to hold is that
                // the summary can state a key at all.
                Assert.AreNotEqual(InputPressType.Axis, action.Press, $"{action.Id} has no key to state");
                Assert.AreNotEqual(KeyCode.None, action.Key, $"{action.Id} is featured but bound to nothing");
            }
        }

        [Test]
        public void NoDebugOnlyActionIsRebindableOrFeatured()
        {
            foreach (InputActionDefinition action in NebulaInputActions.All)
            {
                if (!action.DebugOnly) continue;
                Assert.IsFalse(action.Rebindable, $"{action.Id} is debug-only and rebindable");
                Assert.IsFalse(action.Featured, $"{action.Id} is debug-only and stated on the settings screen");
            }
        }

        // =====================================================================
        // The shipping documents
        // =====================================================================

        private static string DocPath(string name) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Distribution", "Docs", name));

        /// <summary>
        /// The Turkish manual states the keys in a table a player reads instead of
        /// playing. It is static text, so it can only be held to the catalog by a test
        /// that fails when they disagree — which is the whole reason this one exists:
        /// the manual shipped a paragraph describing the U double-fire, another
        /// describing four keys the reserved list was missing, and a third warning
        /// that rebinds are never saved. All three were true when they were written.
        /// </summary>
        [Test]
        public void ReadmeStatesTheKeysTheCatalogActuallyOwns()
        {
            string readme = DocPath("README_TR.md");
            Assert.IsTrue(File.Exists(readme), $"the shipped manual is missing: {readme}");
            string text = File.ReadAllText(readme);

            (string id, string cell)[] claims =
            {
                (NebulaInputActions.MenuProgression, "`U`"),
                (NebulaInputActions.MenuFleet, "`Y`"),
                (NebulaInputActions.MapToggle, "`M`"),
                (NebulaInputActions.ExtractionScan, "`R`"),
                (NebulaInputActions.ProcessingPrevSlot, "`[`"),
                (NebulaInputActions.QuickPackageConfirm, "`P`"),
                (NebulaInputActions.QuickPackageCancel, "`U`"),
                (NebulaInputActions.EvacuationEvacuate, "`V`"),
                (NebulaInputActions.PoiSensorRelay, "`Z`"),
            };

            foreach ((string id, string cell) in claims)
            {
                InputActionDefinition action = NebulaInputActions.Find(id);
                Assert.IsNotNull(action, id);
                StringAssert.Contains(cell, text,
                    $"the manual no longer names {action.Key} for {id}");
            }

            StringAssert.DoesNotContain("| `Tab` | HUD aç/kapat |", text,
                "the manual still lists Tab as a HUD toggle");
            StringAssert.DoesNotContain("**kaydedilmez**", text,
                "the manual still says rebinds are never saved");
            StringAssert.Contains("input-bindings.json", text,
                "the manual does not tell the player where their key layout is kept");
            StringAssert.Contains("Çözüldü", text,
                "the manual still presents the U collision as unresolved");
        }

        [Test]
        public void QuickStartStatesTheSameMenuKeysAsTheCatalog()
        {
            string quickStart = DocPath("QUICK-START_TR.md");
            Assert.IsTrue(File.Exists(quickStart), $"the shipped quick start is missing: {quickStart}");
            string text = File.ReadAllText(quickStart);

            StringAssert.Contains($"`{NebulaInputActions.Key(NebulaInputActions.MenuProgression)}`", text);
            StringAssert.Contains($"`{NebulaInputActions.Key(NebulaInputActions.MenuFleet)}`", text);
            StringAssert.Contains($"`{NebulaInputActions.Key(NebulaInputActions.ExtractionScan)}`", text);
        }

        // =====================================================================
        // The bracket keys: build mode and the processing panel, both open at once
        // =====================================================================

        /// <summary>
        /// Puts both sub-modes on the air and reports who each bracket goes to. The
        /// contexts are declared through the same public seam the two components use
        /// in <c>OnEnable</c>, so this exercises the real arbitration rather than a
        /// model of it.
        /// </summary>
        private static void WithBothSubModesLive(System.Action body)
        {
            try
            {
                NebulaInputRouter.SetContextSource(InputContext.BuildMode, () => true);
                NebulaInputRouter.SetContextSource(InputContext.Processing, () => true);
                body();
            }
            finally
            {
                NebulaInputRouter.ResetContexts();
            }
        }

        /// <summary>
        /// You can stand in build mode at a planet and press G, so both sub-modes read
        /// <c>[</c> and <c>]</c> on the same frame. One press, one owner — the panel,
        /// because it is the surface the player summoned and is reading.
        /// </summary>
        [Test]
        public void WithBothSubModesOpen_EachBracketGoesToExactlyOneOwner()
        {
            WithBothSubModesLive(() =>
            {
                Assert.AreEqual(NebulaInputActions.ProcessingPrevSlot,
                    NebulaInputRouter.OwnerOf(KeyCode.LeftBracket)?.Id,
                    "[ is not owned by the open processing panel");
                Assert.AreEqual(NebulaInputActions.ProcessingNextSlot,
                    NebulaInputRouter.OwnerOf(KeyCode.RightBracket)?.Id,
                    "] is not owned by the open processing panel");
            });
        }

        /// <summary>Close the panel and build mode has its defense node back.</summary>
        [Test]
        public void WithOnlyBuildModeOpen_TheBracketsCycleTheDefenseNode()
        {
            try
            {
                NebulaInputRouter.SetContextSource(InputContext.BuildMode, () => true);
                Assert.AreEqual(NebulaInputActions.ExtractionPrevDefenseNode,
                    NebulaInputRouter.OwnerOf(KeyCode.LeftBracket)?.Id);
                Assert.AreEqual(NebulaInputActions.ExtractionNextDefenseNode,
                    NebulaInputRouter.OwnerOf(KeyCode.RightBracket)?.Id);
            }
            finally { NebulaInputRouter.ResetContexts(); }
        }

        /// <summary>
        /// The ranking has to keep deciding it. Two live contexts at equal rank is an
        /// ambiguity the router refuses to resolve — it gives the key to nobody — so a
        /// sub-mode added later at the same rank would silently kill these presses
        /// rather than quietly stealing them. This is the test that would catch it.
        /// </summary>
        [Test]
        public void TheTwoBracketContextsAreRankedApart_SoNeitherPressFallsIntoATie()
        {
            Assert.AreNotEqual(
                NebulaInputActions.Rank(InputContext.Processing),
                NebulaInputActions.Rank(InputContext.BuildMode),
                "the processing panel and build mode share a rank; the brackets they " +
                "share would then be owned by nobody while both are open");

            Assert.Greater(
                NebulaInputActions.Rank(InputContext.Processing),
                NebulaInputActions.Rank(InputContext.BuildMode));
        }

        /// <summary>
        /// The collision scan's "arbitrated pairs cannot double-fire" exemption is only
        /// honest while arbitration is real. F is the case in hand: it still has three
        /// owners — extraction, logistics and POI — and all three now register with the
        /// router and read through it, so one press reaches exactly one of them.
        /// <para>
        /// This test used to assert the opposite, because that was the truth:
        /// P1-ECO-021 / P1-TEST-006 were three unarbitrated owners all reading F
        /// directly. Arbitration being claimed in the catalog while the router names no
        /// owner would be worse than the original bug — the exemption would hide a live
        /// double-fire — so the claim and the routing are asserted together.
        /// </para>
        /// </summary>
        [Test]
        public void EverySharedKeyOwnerIsArbitrated_AndTheRouterNamesExactlyOne()
        {
            // Screen contexts are excluded for the same reason GloballyReservedKeys
            // excludes them: a screen raises InputGate while it is open, so a screen
            // owner and a world owner can never be listening on the same frame. The
            // strategic map's F is the case here. Arbitration is about the owners that
            // really do compete.
            InputActionDefinition[] fOwners = NebulaInputActions.All
                .Where(a => a.Key == KeyCode.F && a.Press != InputPressType.Axis
                            && !NebulaInputActions.IsScreenContext(a.Context)).ToArray();
            Assert.Greater(fOwners.Length, 1, "this test needs F to still have several world owners");
            CollectionAssert.IsEmpty(fOwners.Where(a => !a.Arbitrated).ToArray(),
                "an F owner reads the key without going through the router — that is a live double-fire");

            Assert.AreEqual(0, NebulaInputActions.Collisions().Count(c => c.Key == KeyCode.F),
                "F is still reported as a collision even though every owner is arbitrated");

            // Arbitration is only worth anything if it resolves. Standing at a scanned
            // planet with a logistics centre and a POI all in range is the worst case
            // and a reachable one: all three F owners live at once. The router must
            // name exactly one of them — none would mean a rank tie, and a rank tie
            // means the key silently does nothing, which is not better than the
            // double-fire it replaced.
            try
            {
                NebulaInputRouter.SetContextSource(InputContext.Extraction, () => true);
                NebulaInputRouter.SetContextSource(InputContext.Logistics, () => true);
                NebulaInputRouter.SetContextSource(InputContext.Poi, () => true);

                InputActionDefinition owner = NebulaInputRouter.OwnerOf(KeyCode.F);
                Assert.IsNotNull(owner,
                    "the router names no owner for F while several claim it — a rank tie means the key does nothing at all");
                Assert.IsTrue(owner.Arbitrated, $"the router chose {owner.Id}, which is not arbitrated");

                foreach (InputActionDefinition other in fOwners)
                    if (other.Id != owner.Id)
                        Assert.IsFalse(NebulaInputRouter.Pressed(other.Id) || NebulaInputRouter.Held(other.Id),
                            $"{other.Id} still answers F while {owner.Id} owns it");
            }
            finally { NebulaInputRouter.ResetContexts(); }
        }
    }
}
