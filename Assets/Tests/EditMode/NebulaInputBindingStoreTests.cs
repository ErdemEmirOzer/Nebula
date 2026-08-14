using System.IO;
using NUnit.Framework;
using UnityEngine;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Controls;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// Rebind persistence: the store, its file, and every way that file can be wrong.
    /// <para>
    /// A player could reassign nine combat keys and lose all nine on the next launch.
    /// There was no user-settings authority anywhere to reuse — no <c>PlayerPrefs</c>
    /// call exists under <c>Assets/</c>, and the only file writers are the campaign
    /// save, telemetry and the session log — so this is a new store, and these tests
    /// are what stop it becoming a second save system: the campaign schema must not
    /// move, a bad file must cost the player nothing but their layout, and a file that
    /// asks for something the validator would refuse must be refused exactly as if a
    /// player had typed it.
    /// </para>
    /// </summary>
    public class NebulaInputBindingStoreTests
    {
        private string sandbox;

        [SetUp]
        public void UseASandboxDirectory()
        {
            sandbox = Path.Combine(Path.GetTempPath(), "nebula-input-settings-" + Path.GetRandomFileName());
            Directory.CreateDirectory(sandbox);
            NebulaInputBindingStore.DirectoryOverride = sandbox;
            NebulaInputBindingStore.WritesEnabled = true;
            CombatBindings.ResetToDefaults();

            // The contextual rows and the router's statics too, not just the combat
            // service. These outlive a fixture, so a rebind or a registered context an
            // earlier fixture in the same Unity process left behind was still in force
            // here — which is what made a batched run disagree with the same tests run
            // alone. The real public reset contract, never reflection.
            NebulaInputActions.ResetRebindableToDefaults();
            NebulaInputRouter.ResetContexts();
            NebulaInputRouter.ResetSourceToKeyboard();
        }

        [TearDown]
        public void PutTheWorldBack()
        {
            NebulaInputBindingStore.DirectoryOverride = null;
            NebulaInputBindingStore.WritesEnabled = true;
            CombatBindings.ResetToDefaults();
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { /* best effort */ }
        }

        private void WriteRaw(string json)
        {
            Directory.CreateDirectory(sandbox);
            File.WriteAllText(Path.Combine(sandbox, "input-bindings.json"), json);
        }

        // =====================================================================
        // Round trip
        // =====================================================================

        [Test]
        public void ARebindSurvivesAWriteAndAReadBack()
        {
            Assert.IsTrue(CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9));
            Assert.IsTrue(NebulaInputBindingStore.Save());

            // The process forgetting everything is what a restart is.
            CombatBindings.ResetToDefaults();
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey, "the fixture did not actually forget");

            // Read from the catalog rather than pinned at nine: the store persists every
            // rebindable row, and P1-ECO-006 made the contextual extraction/logistics/POI
            // rows rebindable too. A literal here would have to be edited every time the
            // catalog grows, and would say nothing about whether the store agreed with it.
            int persisted = 0;
            foreach (InputActionDefinition action in NebulaInputActions.All)
                if (action.Persisted) persisted++;
            Assert.AreEqual(persisted, NebulaInputBindingStore.Load(),
                "the store must load back exactly the rows it persists");
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void TheFileNamesActionsByIdAndCarriesItsVersion()
        {
            NebulaInputBindingStore.Save();
            string json = File.ReadAllText(NebulaInputBindingStore.FilePath);

            StringAssert.Contains($"\"settingsVersion\": {NebulaInputBindingStore.CurrentVersion}", json);
            StringAssert.Contains(NebulaInputActions.CombatRepair, json);
            StringAssert.Contains("Alpha6", json);
        }

        [Test]
        public void TheStoreWritesOutsideTheCampaignSaveDirectory()
        {
            NebulaInputBindingStore.DirectoryOverride = null;
            StringAssert.DoesNotContain(Path.Combine("nebula", "saves"), NebulaInputBindingStore.Directory);
            StringAssert.Contains("settings", NebulaInputBindingStore.Directory);
        }

        /// <summary>The campaign schema is not this feature's to move, and this is the line that says so.</summary>
        [Test]
        public void TheCampaignSaveSchemaIsUntouched()
        {
            Assert.AreEqual(15, SaveService.CurrentSchema);
            Assert.AreEqual(0, new SaveEnvelope().schemaVersion); // P2-SAVE-201: absent schema is unknown, never v13
        }

        // =====================================================================
        // Bad files
        // =====================================================================

        [Test]
        public void ACorruptFileLeavesEveryDefaultStanding()
        {
            WriteRaw("{ this is not json at all ");
            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            Assert.AreEqual(KeyCode.Mouse0, CombatBindings.FireLaserKey);
        }

        [Test]
        public void AnEmptyFileLeavesEveryDefaultStanding()
        {
            WriteRaw("");
            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void AFileFromAnotherVersionIsDeclinedWhole_NotGuessedAt()
        {
            WriteRaw("{\"settingsVersion\":99,\"bindings\":[" +
                     "{\"actionId\":\"" + NebulaInputActions.CombatBoost + "\",\"key\":\"Alpha9\"}]}");

            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            StringAssert.Contains("not readable", NebulaInputBindingStore.LastStatus);

            // Declined, not deleted: a future build may know how to migrate it.
            Assert.IsTrue(NebulaInputBindingStore.Exists());
        }

        [Test]
        public void NoFileAtAllIsNotAnError()
        {
            Assert.IsFalse(NebulaInputBindingStore.Exists());
            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void AnUnknownActionIdIsIgnoredAndTheRestSurvive()
        {
            WriteRaw("{\"settingsVersion\":1,\"bindings\":[" +
                     "{\"actionId\":\"combat.this_action_never_existed\",\"key\":\"Alpha9\"}," +
                     "{\"actionId\":\"" + NebulaInputActions.CombatBoost + "\",\"key\":\"Alpha9\"}]}");

            Assert.AreEqual(1, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey);
            StringAssert.Contains("ignored 1", NebulaInputBindingStore.LastStatus);
        }

        [Test]
        public void AnUnparseableKeyNameIsIgnoredAndTheRestSurvive()
        {
            WriteRaw("{\"settingsVersion\":1,\"bindings\":[" +
                     "{\"actionId\":\"" + NebulaInputActions.CombatRepair + "\",\"key\":\"NotAKeyCode\"}," +
                     "{\"actionId\":\"" + NebulaInputActions.CombatBoost + "\",\"key\":\"Alpha9\"}]}");

            Assert.AreEqual(1, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey, "the bad entry moved a binding anyway");
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey);
        }

        /// <summary>
        /// A settings file is not a licence to enter a state the settings screen would
        /// have refused. Every entry goes through the same validator the player's own
        /// key press does.
        /// </summary>
        [Test]
        public void AFileCannotGiveTwoActionsTheSameKey()
        {
            WriteRaw("{\"settingsVersion\":1,\"bindings\":[" +
                     "{\"actionId\":\"" + NebulaInputActions.CombatBoost + "\",\"key\":\"Alpha9\"}," +
                     "{\"actionId\":\"" + NebulaInputActions.CombatRepair + "\",\"key\":\"Alpha9\"}]}");

            Assert.AreEqual(1, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey);
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey, "two actions ended up on one key");
        }

        [Test]
        public void AFileCannotHandACombatActionAKeyAnotherSystemOwns()
        {
            WriteRaw("{\"settingsVersion\":1,\"bindings\":[" +
                     "{\"actionId\":\"" + NebulaInputActions.CombatRepair + "\",\"key\":\"LeftBracket\"}]}");

            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey);
        }

        [Test]
        public void AFileCannotBindAFunctionKey()
        {
            WriteRaw("{\"settingsVersion\":1,\"bindings\":[" +
                     "{\"actionId\":\"" + NebulaInputActions.CombatRepair + "\",\"key\":\"F5\"}]}");

            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha6, CombatBindings.RepairKey);
        }

        // =====================================================================
        // Restore defaults
        // =====================================================================

        [Test]
        public void RestoreDefaultsForgetsTheSavedLayoutEntirely()
        {
            NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9);
            Assert.IsTrue(NebulaInputBindingStore.Exists(), "an accepted rebind was never written");

            NebulaControlBindings.ResetToDefaults();

            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
            Assert.IsFalse(NebulaInputBindingStore.Exists(),
                "the saved layout outlived restore-defaults and would come back on the next launch");

            CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9);
            NebulaInputBindingStore.Load();
            Assert.AreEqual(KeyCode.Alpha9, CombatBindings.CombatBoostKey,
                "loading with no file must change nothing at all");
        }

        [Test]
        public void AnAcceptedRebindIsWrittenAtOnce_WithNoApplyStep()
        {
            Assert.IsFalse(NebulaInputBindingStore.Exists());
            Assert.AreEqual(ControlRebindResult.Accepted,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.Alpha9));
            Assert.IsTrue(NebulaInputBindingStore.Exists());
        }

        [Test]
        public void ARefusedRebindWritesNothing()
        {
            Assert.AreEqual(ControlRebindResult.ReservedBySystem,
                NebulaControlBindings.TryRebind(CombatAction.CombatBoost, KeyCode.W));
            Assert.IsFalse(NebulaInputBindingStore.Exists(),
                "a refusal reached the disk, so the file no longer describes the game");
        }

        // =====================================================================
        // The write itself
        // =====================================================================

        [Test]
        public void DisablingTheWriteIsVisible_SoTheRestartTestCanBeProvedToFail()
        {
            NebulaInputBindingStore.WritesEnabled = false;
            CombatBindings.TrySetBinding(CombatAction.CombatBoost, KeyCode.Alpha9);

            Assert.IsFalse(NebulaInputBindingStore.Save());
            Assert.IsFalse(NebulaInputBindingStore.Exists());

            CombatBindings.ResetToDefaults();
            Assert.AreEqual(0, NebulaInputBindingStore.Load());
            Assert.AreEqual(KeyCode.Alpha3, CombatBindings.CombatBoostKey);
        }

        [Test]
        public void TheWriteLeavesNoTemporaryFileBehind()
        {
            NebulaInputBindingStore.Save();
            Assert.IsFalse(File.Exists(Path.Combine(sandbox, "input-bindings.tmp.json")),
                "the temp file survived a successful commit");
        }
    }

    /// <summary>
    /// A cancel with no cargo behind it.
    /// <para>
    /// Found by the routing tests rather than reasoned about:
    /// <c>ShipQuickAssetPackage.Cancel</c> has always resolved the cargo as
    /// <c>logistics != null ? logistics.Cargo : null</c> and then handed that null to
    /// <c>ReleaseUnsentReservation</c>, which dereferenced it unconditionally. The
    /// throw left the transaction neither cancelled nor running — the worst of the
    /// three states — so the guard is not politeness, it is the difference between
    /// cancelling and corrupting.
    /// </para>
    /// </summary>
    public class QuickAssetPackageCancelWithoutCargoTests
    {
        [Test]
        public void ReleasingAnUnsentReservationWithNoCargo_IsANoOpRatherThanAThrow()
        {
            var transaction = new QuickAssetPackageTransaction
            {
                TransactionId = "no-cargo",
                PlanetWorldId = "planet",
                CommandMaterialId = ProcessingCatalog.MaterialIds[0],
                LaserMaterialId = ProcessingCatalog.MaterialIds[0],
            };

            Assert.DoesNotThrow(() => QuickAssetPackageService.ReleaseUnsentReservation(transaction, null));
            Assert.DoesNotThrow(() => QuickAssetPackageService.ReleaseUnsentReservation(null, null));
        }

        [Test]
        public void ARealCargoStillHasItsReservationReleased()
        {
            var cargo = new LogisticsCargo(1000f);
            cargo.Add(ProcessingCatalog.BasicPartId, 100f);
            var transaction = new QuickAssetPackageTransaction { TransactionId = "with-cargo" };

            Assert.IsTrue(cargo.TryReserve(QuickAssetPackageService.OwnerId("with-cargo"), ProcessingCatalog.BasicPartId, 10f),
                "the fixture could not reserve, so releasing proves nothing");
            Assert.Less(cargo.GetAvailable(ProcessingCatalog.BasicPartId), 100f);

            QuickAssetPackageService.ReleaseUnsentReservation(transaction, cargo);
            Assert.AreEqual(100f, cargo.GetAvailable(ProcessingCatalog.BasicPartId), 0.001f,
                "the guard turned a real release into a no-op as well");
        }
    }
}
