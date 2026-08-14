using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Extraction
{
    /// <summary>
    /// What the one Command Center action would do right now.
    /// <para>
    /// The action is the verb, not the verdict: <see cref="BuildT1"/> stays
    /// <see cref="BuildT1"/> when the hold is short, and the refusal rides
    /// alongside it in <see cref="CommandBuildAssessment.BuildReject"/>. A
    /// button that changes its own caption depending on whether it is usable
    /// tells the player what they cannot do instead of what they are trying
    /// to do.
    /// </para>
    /// </summary>
    public enum CommandActionKind
    {
        /// <summary>No planet is targeted at all.</summary>
        None,
        BuildT1,
        UpgradeT2,
        /// <summary>A capsule is in flight or construction is running.</summary>
        Building,
        /// <summary>A T1→T2 job is already in flight; the structure keeps working.</summary>
        Upgrading,
        /// <summary>Active T2 — nothing left this slice.</summary>
        MaxTier,
        /// <summary>The node is taken by something that is not a Command Center: no verb exists.</summary>
        Rejected
    }

    /// <summary>
    /// Why a fresh Command <b>T1</b> cannot be placed. Deliberately narrow, and
    /// deliberately not a second copy of <see cref="StructureUpgradeRejectReason"/>
    /// — every T1→T2 refusal still comes from that enum, resolved by
    /// <see cref="StructureUpgradeService.CheckStart"/>.
    /// </summary>
    public enum CommandBuildRejectReason
    {
        None,
        NoTarget,
        NoProfile,
        NotScanned,
        OutOfRange,
        EvacuationLocked,
        NodeOccupied,
        NoCargo,
        InsufficientResources,
        /// <summary>A Command Center is already on its way here or being built.</summary>
        AlreadyBuilding
    }

    /// <summary>
    /// One line of a cost, with the three numbers that are not the same number.
    /// <para>
    /// <see cref="Stored"/> is what is physically in the hold; <see cref="Reserved"/>
    /// is what other jobs have already spoken for; <see cref="Available"/> is the
    /// only one a new build may spend. Showing stock alone is what made
    /// "insufficient resources" look like a lie to a player staring at a full hold.
    /// </para>
    /// </summary>
    public readonly struct CommandCostLine
    {
        public readonly string ItemId;
        public readonly float Required;
        public readonly float Stored;
        public readonly float Reserved;
        public readonly float Available;

        public CommandCostLine(string itemId, float required, float stored, float reserved, float available)
        {
            ItemId = itemId; Required = required; Stored = stored; Reserved = reserved; Available = available;
        }

        public bool Met => Available >= Required - 0.0001f;
    }

    /// <summary>
    /// Everything the evaluation needs, as plain values. Built in exactly one
    /// production place — <see cref="ShipExtraction.AssessCommandAction"/> — so the
    /// interface can never assemble a context that says something the world does not.
    /// </summary>
    public readonly struct CommandBuildContext
    {
        public readonly bool HasTarget;
        public readonly string WorldId;
        public readonly PlanetProfile Profile;
        public readonly float Altitude;
        public readonly float RangeLimit;
        public readonly bool EvacuationLocked;
        public readonly bool NodeOccupied;
        public readonly SurfaceStructure Existing;
        public readonly LogisticsCargo Cargo;
        public readonly int CharacterLevel;
        public readonly int WeaponCoordinationTier;
        public readonly bool HasAnyControlledPlanet;
        /// <summary>The real 3–8s capsule flight for the ship's current distance.</summary>
        public readonly float DeliverySeconds;

        public CommandBuildContext(string worldId, PlanetProfile profile, float altitude, float rangeLimit,
            bool evacuationLocked, bool nodeOccupied, SurfaceStructure existing, LogisticsCargo cargo,
            int characterLevel, int weaponCoordinationTier, bool hasAnyControlledPlanet, float deliverySeconds)
        {
            HasTarget = true;
            WorldId = worldId; Profile = profile; Altitude = altitude; RangeLimit = rangeLimit;
            EvacuationLocked = evacuationLocked; NodeOccupied = nodeOccupied; Existing = existing; Cargo = cargo;
            CharacterLevel = characterLevel; WeaponCoordinationTier = weaponCoordinationTier;
            HasAnyControlledPlanet = hasAnyControlledPlanet; DeliverySeconds = deliverySeconds;
        }
    }

    /// <summary>
    /// The read-only answer to "what would the Command action do, and may it".
    /// Produced by <see cref="CommandBuildService.Evaluate"/> and by nothing else.
    /// </summary>
    public sealed class CommandBuildAssessment
    {
        public CommandActionKind Action = CommandActionKind.None;
        public string WorldId;

        public bool HasProfile;
        public bool Scanned;
        public bool InRange;
        public float Altitude;
        public float RangeLimit;
        public bool EvacuationLocked;

        /// <summary>The Command node itself is taken (by this structure, or by something else).</summary>
        public bool NodeOccupied;
        public bool HasStructure;
        public int Tier;
        public DrillLifecycle State;
        /// <summary>0–1 through whatever is currently running (construction or upgrade).</summary>
        public float Progress01;
        public bool Upgrading;

        /// <summary>Capsule flight seconds for a fresh build; 0 for an upgrade (nothing is delivered).</summary>
        public float DeliverySeconds;
        /// <summary>Catalog build/upgrade seconds for whatever <see cref="Action"/> would start.</summary>
        public float WorkSeconds;

        /// <summary>
        /// The planet-family material this build needs — <c>null</c> until the
        /// planet is scanned. Withholding it is the information gate, not a
        /// display choice: the family is exactly what a scan is for.
        /// </summary>
        public string MaterialItemId;

        /// <summary>True when a cost line is being withheld because the planet is unscanned.</summary>
        public bool MaterialHidden;

        public IReadOnlyList<CommandCostLine> Cost = System.Array.Empty<CommandCostLine>();

        public CommandBuildRejectReason BuildReject = CommandBuildRejectReason.None;
        public StructureUpgradeRejectReason UpgradeReject = StructureUpgradeRejectReason.None;

        /// <summary>
        /// True only when pressing the control right now would really change the
        /// world.
        /// <para>
        /// Both refusal channels have to be clear, not just the one belonging to
        /// the verb. Presence refusals — unscanned, out of range, evacuation —
        /// land in <see cref="BuildReject"/> whatever the verb is, so an upgrade
        /// that only consulted <see cref="UpgradeReject"/> reported itself
        /// available the moment the planet orbited out from under the ship: a live
        /// button, its reason suppressed because the control believed it was
        /// usable, and a T2 upgrade that would really have started from out of
        /// range.
        /// </para>
        /// </summary>
        public bool CanAct =>
            BuildReject == CommandBuildRejectReason.None
            && (Action == CommandActionKind.BuildT1
                || (Action == CommandActionKind.UpgradeT2
                    && UpgradeReject == StructureUpgradeRejectReason.None));
    }

    /// <summary>What actually happened when the one mutating call was made.</summary>
    public readonly struct CommandActionOutcome
    {
        public readonly bool Succeeded;
        public readonly CommandActionKind Attempted;
        public readonly CommandBuildRejectReason BuildReject;
        public readonly StructureUpgradeRejectReason UpgradeReject;

        public CommandActionOutcome(bool succeeded, CommandActionKind attempted,
            CommandBuildRejectReason buildReject, StructureUpgradeRejectReason upgradeReject)
        {
            Succeeded = succeeded; Attempted = attempted;
            BuildReject = buildReject; UpgradeReject = upgradeReject;
        }

        /// <summary>
        /// Whether anything happened worth telling the player about.
        /// <para>
        /// Read from the result, not from the verb. Keying it off
        /// <see cref="Attempted"/> looked equivalent and was not: a press with no
        /// planet under the ship carries <see cref="CommandActionKind.None"/> as
        /// its verb, so the one case where the player most needs an answer was
        /// the one case that produced silence.
        /// </para>
        /// </summary>
        public bool HasValue => Succeeded
                               || BuildReject != CommandBuildRejectReason.None
                               || UpgradeReject != StructureUpgradeRejectReason.None;
    }

    /// <summary>
    /// The single, pure evaluation behind the Command Center control — the read
    /// half of the seam whose write half is
    /// <see cref="ShipExtraction.TryBuildOrUpgradeCommand"/>.
    /// <para>
    /// It reserves nothing, spends nothing and writes nothing. That is the whole
    /// point: a panel refreshing five times a second must be able to ask "could I
    /// build here, and what would it cost" without the asking becoming a
    /// transaction.
    /// </para>
    /// <para>
    /// Every number it reports is read from the existing authorities —
    /// <see cref="SurfaceStructureCatalog"/> for the T1 recipe and build time,
    /// <see cref="PlanetaryDefenseCatalog"/> for the T2 tier, <see cref="ProcessingCatalog"/>
    /// for the family material, <see cref="LogisticsCargo"/> for stock and
    /// reservations, and <see cref="StructureUpgradeService"/> for every T2 gate.
    /// Nothing here re-types a cost, a duration or a requirement.
    /// </para>
    /// </summary>
    public static class CommandBuildService
    {
        /// <summary>The family → refined-material mapping, straight from the processing catalog. Never a second table.</summary>
        public static string MaterialIdFor(ResourceFamily family) => ProcessingCatalog.MaterialIds[(int)family];

        /// <summary>The T1 recipe, from the catalog. Parts first, then the planet's own family material.</summary>
        public static List<(string itemId, float amount)> BuildT1Cost(ResourceFamily family)
        {
            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            return new List<(string, float)>
            {
                (ProcessingCatalog.BasicPartId, def.PartsCost),
                (MaterialIdFor(family), def.MaterialCost)
            };
        }

        public static CommandBuildAssessment Evaluate(in CommandBuildContext context)
        {
            var result = new CommandBuildAssessment();

            if (!context.HasTarget)
            {
                result.BuildReject = CommandBuildRejectReason.NoTarget;
                return result;
            }

            result.WorldId = context.WorldId;
            result.Altitude = context.Altitude;
            result.RangeLimit = context.RangeLimit;
            result.InRange = context.Altitude <= context.RangeLimit;
            result.EvacuationLocked = context.EvacuationLocked;
            result.NodeOccupied = context.NodeOccupied;
            result.HasProfile = context.Profile != null;
            result.Scanned = context.Profile != null && context.Profile.Scanned;

            if (context.Profile == null)
            {
                result.Action = CommandActionKind.Rejected;
                result.BuildReject = CommandBuildRejectReason.NoProfile;
                return result;
            }

            SurfaceStructure existing = context.Existing;
            if (existing != null && existing.Runtime != null)
            {
                DescribeExisting(result, existing, context.Profile);
                ResolveExistingAction(result, existing, context);
            }
            else
            {
                ResolveFreshBuild(result, context);
            }

            // Physical-presence gates outrank every economic one: a player who is
            // not there, or has not looked, is told that first, whatever else is
            // also missing.
            ApplyPresenceGates(result, context);
            return result;
        }

        // --- existing structure ----------------------------------------------

        private static void DescribeExisting(CommandBuildAssessment result, SurfaceStructure existing, PlanetProfile profile)
        {
            result.HasStructure = true;
            result.Tier = existing.Runtime.Tier;
            result.State = existing.Runtime.State;
            result.Upgrading = existing.ActiveUpgradeJob != null;
            result.Progress01 = ProgressOf(existing, profile);
        }

        /// <summary>
        /// How far through whatever is running. Construction is measured against
        /// the environment-scaled catalog duration the runtime itself was built
        /// with, so a Hostile planet's longer build does not read as stalled.
        /// </summary>
        private static float ProgressOf(SurfaceStructure existing, PlanetProfile profile)
        {
            if (existing.ActiveUpgradeJob != null) return existing.ActiveUpgradeJob.Progress01;

            switch (existing.Runtime.State)
            {
                case DrillLifecycle.CapsuleInTransit:
                    return 0f;
                case DrillLifecycle.Constructing:
                case DrillLifecycle.Damaged:
                    float total = Sectors.EnvironmentDurationResolver.ScaleDuration(
                        SurfaceStructureCatalog.Command.BuildSeconds, profile.Environment);
                    return total > 0f ? Mathf.Clamp01(1f - existing.Runtime.BuildRemaining / total) : 1f;
                default:
                    return 1f;
            }
        }

        private static void ResolveExistingAction(CommandBuildAssessment result, SurfaceStructure existing,
            in CommandBuildContext context)
        {
            if (existing.ActiveUpgradeJob != null)
            {
                result.Action = CommandActionKind.Upgrading;
                result.UpgradeReject = StructureUpgradeRejectReason.AlreadyUpgrading;
                result.WorkSeconds = existing.ActiveUpgradeJob.TotalSeconds;
                return;
            }

            if (existing.Runtime.State != DrillLifecycle.Active && !existing.Runtime.IsDisabled)
            {
                result.Action = CommandActionKind.Building;
                result.BuildReject = CommandBuildRejectReason.AlreadyBuilding;
                result.WorkSeconds = SurfaceStructureCatalog.Command.BuildSeconds;
                return;
            }

            StructureTierDef next = PlanetaryDefenseCatalog.Get(SurfaceStructureType.Command, existing.Runtime.Tier + 1);
            if (next == null)
            {
                result.Action = CommandActionKind.MaxTier;
                result.UpgradeReject = StructureUpgradeRejectReason.MaxTier;
                return;
            }

            result.Action = CommandActionKind.UpgradeT2;
            result.WorkSeconds = next.BuildSeconds;
            result.Cost = CostLines(next.FullCost(), context.Cargo);
            result.UpgradeReject = StructureUpgradeService.CheckStart(existing, context.CharacterLevel,
                existing.Runtime.Tier, context.WeaponCoordinationTier, context.HasAnyControlledPlanet,
                context.Cargo, out _);

            if (context.Cargo == null) result.BuildReject = CommandBuildRejectReason.NoCargo;
        }

        // --- fresh T1 ----------------------------------------------------------

        private static void ResolveFreshBuild(CommandBuildAssessment result, in CommandBuildContext context)
        {
            if (context.NodeOccupied)
            {
                // Something that is not a Command Center is standing on the Command
                // node. There is no verb here at all, so no caption pretends there is.
                result.Action = CommandActionKind.Rejected;
                result.BuildReject = CommandBuildRejectReason.NodeOccupied;
                return;
            }

            SurfaceStructureDef def = SurfaceStructureCatalog.Command;
            result.Action = CommandActionKind.BuildT1;
            result.WorkSeconds = def.BuildSeconds;
            result.DeliverySeconds = context.DeliverySeconds;

            // The information gate. Before a scan the family is unknown to the
            // player, so the family material line is withheld entirely rather than
            // shown greyed — a greyed-out "Bio-Composite" answers the question the
            // scan exists to ask.
            if (!result.Scanned)
            {
                result.MaterialHidden = true;
                result.Cost = CostLines(
                    new List<(string, float)> { (ProcessingCatalog.BasicPartId, def.PartsCost) }, context.Cargo);
                result.BuildReject = CommandBuildRejectReason.NotScanned;
                return;
            }

            result.MaterialItemId = MaterialIdFor(context.Profile.Family);
            List<(string itemId, float amount)> cost = BuildT1Cost(context.Profile.Family);
            result.Cost = CostLines(cost, context.Cargo);

            if (context.Cargo == null)
            {
                result.BuildReject = CommandBuildRejectReason.NoCargo;
                return;
            }

            foreach (var (itemId, amount) in cost)
            {
                if (context.Cargo.GetAvailable(itemId) < amount - 0.0001f)
                {
                    result.BuildReject = CommandBuildRejectReason.InsufficientResources;
                    return;
                }
            }
        }

        // --- shared ------------------------------------------------------------

        /// <summary>
        /// Evacuation, scan and range, applied after the verb is known so the
        /// caption still says what the player is trying to do while the reason
        /// says why they cannot. Never overwrites a refusal that is already
        /// physical rather than economic.
        /// </summary>
        private static void ApplyPresenceGates(CommandBuildAssessment result, in CommandBuildContext context)
        {
            if (result.Action == CommandActionKind.Building
                || result.Action == CommandActionKind.Upgrading
                || result.Action == CommandActionKind.MaxTier) return;

            if (context.EvacuationLocked)
            {
                result.BuildReject = CommandBuildRejectReason.EvacuationLocked;
                result.UpgradeReject = StructureUpgradeRejectReason.None;
                return;
            }

            if (!result.Scanned)
            {
                result.BuildReject = CommandBuildRejectReason.NotScanned;
                result.UpgradeReject = StructureUpgradeRejectReason.None;
                return;
            }

            if (!result.InRange)
            {
                result.BuildReject = CommandBuildRejectReason.OutOfRange;
                result.UpgradeReject = StructureUpgradeRejectReason.None;
            }
        }

        private static CommandCostLine[] CostLines(List<(string itemId, float amount)> cost, LogisticsCargo cargo)
        {
            var lines = new CommandCostLine[cost.Count];
            for (int i = 0; i < cost.Count; i++)
            {
                string itemId = cost[i].itemId;
                float stored = cargo != null ? cargo.GetStored(itemId) : 0f;
                float reserved = cargo != null ? cargo.GetReserved(itemId) : 0f;
                float available = cargo != null ? cargo.GetAvailable(itemId) : 0f;
                lines[i] = new CommandCostLine(itemId, cost[i].amount, stored, reserved, available);
            }
            return lines;
        }
    }

    /// <summary>
    /// The one place a Command Center action becomes words a player reads.
    /// <para>
    /// Both callers of the seam — the K key and the panel's button — resolve
    /// their text here, so the two cannot drift, and no raw English gameplay
    /// string reaches the screen. It lives beside the typed reasons rather than
    /// in the interface because <see cref="ShipExtraction"/> writes its own
    /// transient feedback through it, and a text table the domain cannot reach
    /// is a table the domain works around.
    /// </para>
    /// </summary>
    public static class CommandBuildText
    {
        public const string StructureName = "KOMUTA MERKEZİ";

        /// <summary>
        /// The player-facing Turkish name of a cost item.
        /// <para>
        /// <see cref="ProcessingCatalog.ItemDisplayName"/> answers in English —
        /// it is the content catalog's own name for the thing, and the reserve
        /// panel above shows those names as-is. A build card that tells a Wood
        /// planet's owner to find "Bio-Composite" while the processing screen
        /// calls it Biyokompozit is asking them to translate mid-task.
        /// </para>
        /// <para>
        /// Keyed off <see cref="ProcessingCatalog.MaterialIds"/> by index rather
        /// than by a second literal id list, so a catalog that gains or reorders
        /// a material cannot leave this silently pointing at the wrong name.
        /// </para>
        /// </summary>
        public static string ItemName(string itemId)
        {
            if (itemId == ProcessingCatalog.BasicPartId) return "TEMEL PARÇA";
            for (int i = 0; i < ProcessingCatalog.MaterialIds.Length; i++)
                if (ProcessingCatalog.MaterialIds[i] == itemId) return MaterialNamesTr[i];
            return ProcessingCatalog.ItemDisplayName(itemId).ToUpperInvariant();
        }

        /// <summary>In <see cref="ProcessingCatalog.MaterialIds"/> order: metal, wood, gas, water, diamond.</summary>
        private static readonly string[] MaterialNamesTr =
        {
            "METAL ALAŞIM", "BİYOKOMPOZİT", "RAFİNE GAZ", "ARITILMIŞ SU", "ENDÜSTRİYEL ELMAS"
        };

        public static string BuildRejectText(CommandBuildRejectReason reason) => reason switch
        {
            CommandBuildRejectReason.None => null,
            CommandBuildRejectReason.NoTarget => "MENZİLDE GEZEGEN YOK",
            CommandBuildRejectReason.NoProfile => "GEZEGEN VERİSİ OKUNAMADI",
            CommandBuildRejectReason.NotScanned => "ÖNCE GEZEGENİ TARA",
            CommandBuildRejectReason.OutOfRange => "GEZEGENE YAKLAŞ",
            CommandBuildRejectReason.EvacuationLocked => "TAHLİYE SÜRÜYOR — İNŞA KAPALI",
            CommandBuildRejectReason.NodeOccupied => "KOMUTA YUVASI DOLU",
            CommandBuildRejectReason.NoCargo => "KARGO AMBARI OKUNAMIYOR",
            CommandBuildRejectReason.InsufficientResources => "KAYNAK YETERSİZ — KAYNAKLAR SAYFASINDA ÜRET",
            CommandBuildRejectReason.AlreadyBuilding => "İNŞA ZATEN SÜRÜYOR",
            _ => "ŞU AN KURULAMIYOR"
        };

        /// <summary>
        /// The T2 refusals, translated from the existing shared enum. The tier
        /// numbers are read from the catalog rather than written out, so a
        /// retuned gate cannot leave a stale sentence behind.
        /// </summary>
        public static string UpgradeRejectText(StructureUpgradeRejectReason reason)
        {
            StructureTierDef t2 = PlanetaryDefenseCatalog.CommandT2;
            return reason switch
            {
                StructureUpgradeRejectReason.None => null,
                StructureUpgradeRejectReason.AlreadyUpgrading => "YÜKSELTME ZATEN SÜRÜYOR",
                StructureUpgradeRejectReason.NotActive => "YAPI HENÜZ AKTİF DEĞİL",
                StructureUpgradeRejectReason.MaxTier => "AZAMİ SEVİYEDE",
                StructureUpgradeRejectReason.LevelTooLow => $"SEVİYE {t2.RequiredCharacterLevel} GEREKLİ",
                StructureUpgradeRejectReason.CommandTierNotMet => $"KOMUTA T{t2.RequiredCommandTier} GEREKLİ",
                StructureUpgradeRejectReason.WeaponCoordinationTierNotMet =>
                    $"SİLAH KOORDİNASYONU T{t2.RequiredWeaponCoordinationTier} GEREKLİ",
                StructureUpgradeRejectReason.NoControlledPlanet => "EN AZ BİR KONTROL EDİLEN GEZEGEN GEREKLİ",
                StructureUpgradeRejectReason.InsufficientResources => "KAYNAK YETERSİZ — KAYNAKLAR SAYFASINDA ÜRET",
                _ => "YÜKSELTİLEMİYOR"
            };
        }

        /// <summary>The reason the assessment's own control is blocked, or <c>null</c> when it is not.</summary>
        public static string BlockedReason(CommandBuildAssessment assessment)
        {
            if (assessment == null) return BuildRejectText(CommandBuildRejectReason.NoTarget);
            if (assessment.BuildReject != CommandBuildRejectReason.None)
                return BuildRejectText(assessment.BuildReject);
            if (assessment.UpgradeReject != StructureUpgradeRejectReason.None)
                return UpgradeRejectText(assessment.UpgradeReject);
            return null;
        }

        /// <summary>The primary control's caption — the verb, never the verdict.</summary>
        public static string Caption(CommandBuildAssessment assessment) =>
            assessment == null ? "KUR · T1" : assessment.Action switch
            {
                CommandActionKind.UpgradeT2 => "YÜKSELT · T2",
                CommandActionKind.Building => "İNŞA SÜRÜYOR",
                CommandActionKind.Upgrading => "İNŞA SÜRÜYOR",
                CommandActionKind.MaxTier => "AZAMİ SEVİYE",
                _ => "KUR · T1"
            };

        /// <summary>The status line: what is physically on the ground right now.</summary>
        public static string Status(CommandBuildAssessment assessment)
        {
            if (assessment == null || !assessment.HasStructure) return "İNŞA EDİLMEDİ";
            if (assessment.State == DrillLifecycle.Disabled) return "DEVRE DIŞI";
            if (assessment.Upgrading) return $"YÜKSELTİLİYOR · %{assessment.Progress01 * 100f:0}";

            switch (assessment.State)
            {
                case DrillLifecycle.Planned:
                case DrillLifecycle.CapsuleInTransit: return "KAPSÜL YOLDA";
                case DrillLifecycle.Constructing: return $"İNŞA EDİLİYOR · %{assessment.Progress01 * 100f:0}";
                case DrillLifecycle.Damaged: return "HASARLI · İNŞA DURDU";
                default: return $"AKTİF · T{assessment.Tier}";
            }
        }

        public static string NodeStatus(CommandBuildAssessment assessment) =>
            assessment != null && assessment.NodeOccupied ? "DOLU" : "BOŞ";

        /// <summary>The line shown after the control is used — success or refusal, never both, never silence.</summary>
        public static string OutcomeText(in CommandActionOutcome outcome)
        {
            if (!outcome.HasValue) return null;
            if (outcome.Succeeded)
                return outcome.Attempted == CommandActionKind.UpgradeT2
                    ? "KOMUTA MERKEZİ T2 YÜKSELTMESİ BAŞLADI"
                    : "KOMUTA MERKEZİ KAPSÜLÜ YOLA ÇIKTI";

            if (outcome.BuildReject != CommandBuildRejectReason.None)
                return BuildRejectText(outcome.BuildReject);
            if (outcome.UpgradeReject != StructureUpgradeRejectReason.None)
                return UpgradeRejectText(outcome.UpgradeReject);
            return "İŞLEM REDDEDİLDİ";
        }
    }
}
