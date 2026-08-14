using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// Loadout, repair and the standing threat picture.
    /// <para>
    /// The combat HUD had to carry all of this at once, in the middle of a fight,
    /// as prose. Most of it is not fight-time information: how many hardpoints the
    /// hull form allows, which weapon sits in each, how far the pressure meter has
    /// climbed, whether the pirate base is still protected. That belongs on a page
    /// the player reads between fights, so the HUD can be about the fight.
    /// </para>
    /// <para>Production commands stay with <see cref="ShipCombat"/>; this page only presents that public seam.</para>
    /// </summary>
    public sealed class NebulaCombatPage : INebulaMenuPage, INebulaFocusablePage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Combat;
        public string Title => "Savaş";

        /// <summary>Every slot the hull form could ever open, drawn whether filled or not.</summary>
        public const int MaxHardpointRows = 5;

        private readonly NebulaContext context;

        private NebulaUiFactory.LabelHandle loadoutSummary;
        private readonly List<HardpointRow> hardpoints = new List<HardpointRow>(MaxHardpointRows);

        private NebulaUiFactory.LabelHandle repairState, repairDetail;
        private NebulaUiFactory.BarHandle repairBar;

        private NebulaUiFactory.LabelHandle pressureValue, raidValue, pirateValue;
        private NebulaUiFactory.BarHandle pressureBar;
        private NebulaUiFactory.LabelHandle productionState, productionCost;
        private NebulaUiFactory.BarHandle productionBar;
        private NebulaUiFactory.ButtonHandle laserProduction, plasmaProduction, emProduction, cancelProduction;

        public const string LaserProductionControlName = "Produce Laser";
        public const string PlasmaProductionControlName = "Produce Plasma";
        public const string EmProductionControlName = "Produce EM";

        /// <summary>Damaged ship, overheated weapon, or a live raid.</summary>
        public int AttentionCount { get; private set; }

        /// <summary>
        /// Test-facing: why this family's production control is refusing, or
        /// <c>null</c> while it can be pressed. Read from the control itself, so what
        /// this returns is exactly the sentence the player gets from its tooltip —
        /// the same shape <c>NebulaResourcesPage.BenchAddBlockedReason</c> uses.
        /// </summary>
        public string ProductionBlockedReason(WeaponFamily family) => family switch
        {
            WeaponFamily.Laser => laserProduction?.DisabledReason,
            WeaponFamily.Plasma => plasmaProduction?.DisabledReason,
            WeaponFamily.Em => emProduction?.DisabledReason,
            _ => null
        };

        /// <summary>Test-facing: hardpoint rows drawn as occupied.</summary>
        public int OccupiedHardpoints { get; private set; }

        private sealed class HardpointRow
        {
            public RectTransform Root;
            public Image Background;
            public NebulaUiFactory.LabelHandle Name;
            public NebulaUiFactory.LabelHandle State;
            public NebulaUiFactory.BarHandle Heat;

            /// <summary>Frees this slot for a new weapon. See <see cref="ReleaseControlName"/>.</summary>
            public NebulaUiFactory.ButtonHandle Release;
        }

        /// <summary>The per-slot release control's object name, suffixed with the slot index.</summary>
        public const string ReleaseControlName = "Release Hardpoint ";

        public NebulaCombatPage(NebulaContext sessionContext)
        {
            context = sessionContext;
        }

        /// <summary>The page host, kept so the focus chain has a place to sweep when its named controls are all disabled.</summary>
        private RectTransform root;

        public void Build(RectTransform host)
        {
            root = host;
            RectTransform left = Half(host, 0);
            RectTransform right = Half(host, 1);

            BuildLoadout(left);
            BuildProduction(left);
            BuildRepair(right);
            BuildThreat(right);
        }

        private void BuildProduction(RectTransform parent)
        {
            RectTransform card = Card(parent, "Weapon Production", -352f, 238f, "SİLAH ÜRETİMİ");

            productionState = NebulaUiFactory.Label(card, "State", "",
                NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                TextAnchor.MiddleLeft, FontStyle.Bold, 0f, wrap: true);
            NebulaUiFactory.Place(productionState.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -46f), new Vector2(430f, 34f));

            productionCost = NebulaUiFactory.Value(card, "Cost", "", NebulaUiDesignTokens.SizeCaption);
            NebulaUiFactory.Place(productionCost.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -46f), new Vector2(300f, 18f));

            productionBar = NebulaUiFactory.Bar(card, "Progress", NebulaUiDesignTokens.Status(UiStatusLevel.Working), striped: true);
            RectTransform bar = productionBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(20f, -88f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-20f, -88f);

            // Measured from the card's left edge, left to right. These three were laid
            // out at -20/-170/-320 against a bottom-LEFT anchor, which is the Cancel
            // button's spacing copied without its bottom-RIGHT anchor: negative offsets
            // from the left edge put Plasma and EM outside the card entirely, so the
            // player could produce a Laser and had no reachable control for the other
            // two families at all. Caught by CombatPageControls_StayInsideThePage.
            laserProduction = ProductionButton(card, LaserProductionControlName, "LAZER T1", 20f, WeaponFamily.Laser);
            plasmaProduction = ProductionButton(card, PlasmaProductionControlName, "PLAZMA T1", 170f, WeaponFamily.Plasma);
            emProduction = ProductionButton(card, EmProductionControlName, "EM T1", 320f, WeaponFamily.Em);

            cancelProduction = NebulaUiFactory.ButtonControl(card, "Cancel Weapon Production", "İPTAL",
                NebulaUiAssetCatalog.ButtonKind.Tertiary, CancelProduction);
            NebulaUiFactory.Place(cancelProduction.Button.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(-20f, 16f), new Vector2(116f, NebulaUiDesignTokens.MinimumHitTarget));
        }

        /// <summary>
        /// One family's production control.
        /// <para>
        /// It asks for a focus stop that outlives being disabled, because this control
        /// spends most of the early campaign refused — no empty active hardpoint, the
        /// family locked, the parts missing, a job already running, a fight open — and
        /// each of those refusals is a sentence the player has to be able to read.
        /// Unity's directional search skips a control that is not interactable, so
        /// without the stand-in the whole of that explanation was reachable with a
        /// mouse and with nothing else.
        /// </para>
        /// </summary>
        private NebulaUiFactory.ButtonHandle ProductionButton(RectTransform card, string name, string caption,
            float x, WeaponFamily family)
        {
            NebulaUiFactory.ButtonHandle button = NebulaUiFactory.ButtonControl(card, name, caption,
                family == WeaponFamily.Laser ? NebulaUiAssetCatalog.ButtonKind.Primary : NebulaUiAssetCatalog.ButtonKind.Secondary,
                () => StartProduction(family))
                .KeepRefusalReachableByFocus();
            NebulaUiFactory.Place(button.Button.GetComponent<RectTransform>(), new Vector2(0f, 0f),
                new Vector2(x, 16f), new Vector2(138f, NebulaUiDesignTokens.MinimumHitTarget));
            return button;
        }

        private void StartProduction(WeaponFamily family)
        {
            ShipCombat combat = context?.Ship != null ? context.Ship.GetComponent<ShipCombat>() : null;
            if (combat == null)
            {
                // A missing session is a wiring failure, not a gameplay rejection.
                // Reporting "not enough resources" would send the player looking for
                // a problem that is not theirs.
                productionState.Value = "OTURUM YOK — ÜRETİM BAŞLATILAMADI";
                Refresh();
                return;
            }
            WeaponProductionRejectReason reason = combat.TryStartProduction(family, 1);
            productionState.Value = reason == WeaponProductionRejectReason.None
                ? $"{WeaponName(family)} T1 ÜRETİMİ BAŞLADI"
                : WeaponProductionRejectText(reason);
            Refresh();
        }

        /// <summary>
        /// Empties one slot, and says what happened either way. A refusal is printed in
        /// the same place a success is, in the player's language — never the enum name.
        /// </summary>
        private void ReleaseHardpoint(int index)
        {
            ShipCombat combat = context?.Ship != null ? context.Ship.GetComponent<ShipCombat>() : null;
            if (combat == null)
            {
                productionState.Value = "OTURUM YOK — YUVA BOŞALTILAMADI";
                Refresh();
                return;
            }

            HardpointReleaseRejectReason reason = combat.TryReleaseHardpoint(index);
            productionState.Value = reason == HardpointReleaseRejectReason.None
                ? "SİLAH SÖKÜLDÜ — YUVA BOŞ"
                : HardpointReleaseRejectText(reason);
            Refresh();
        }

        /// <summary>Turkish for every way a release can be refused. Never the enum's own name.</summary>
        internal static string HardpointReleaseRejectText(HardpointReleaseRejectReason reason) => reason switch
        {
            HardpointReleaseRejectReason.CombatActive => "MUHAREBE SIRASINDA SİLAH SÖKÜLEMEZ",
            HardpointReleaseRejectReason.QueueBusy => "ÜRETİM SÜRERKEN SİLAH SÖKÜLEMEZ",
            HardpointReleaseRejectReason.NoSuchHardpoint => "BU YUVA GÖVDE FORMUNDA AÇIK DEĞİL",
            HardpointReleaseRejectReason.AlreadyEmpty => "YUVA ZATEN BOŞ",
            _ => "YUVA BOŞALTILAMADI"
        };

        private void CancelProduction()
        {
            ShipCombat combat = context?.Ship != null ? context.Ship.GetComponent<ShipCombat>() : null;
            if (combat?.ActiveProductionJob == null) return;
            combat.CancelProduction();
            productionState.Value = "ÜRETİM İPTAL EDİLDİ · %75 İADE";
            Refresh();
        }

        private static RectTransform Half(RectTransform host, int index)
        {
            var column = NebulaUiFactory.Group(host, "Column " + index);
            column.anchorMin = new Vector2(index * 0.5f, 0f);
            column.anchorMax = new Vector2((index + 1) * 0.5f, 1f);
            column.offsetMin = new Vector2(index == 0 ? 32f : 10f, 24f);
            column.offsetMax = new Vector2(index == 0 ? -10f : -32f, -24f);
            return column;
        }

        private static RectTransform Card(RectTransform parent, string name, float y, float height, string title)
        {
            Image panel = NebulaUiFactory.Panel(parent, name, NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            card.anchorMin = new Vector2(0f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, y);
            card.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.Place(NebulaUiFactory.SectionTitle(card, "Title", title).Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(340f, 18f));
            return card;
        }

        private void BuildLoadout(RectTransform parent)
        {
            RectTransform card = Card(parent, "Loadout", 0f, 340f, "SİLAH YUVALARI");

            loadoutSummary = NebulaUiFactory.Value(card, "Summary", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(loadoutSummary.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -18f), new Vector2(300f, 16f));

            for (int i = 0; i < MaxHardpointRows; i++)
            {
                var row = new HardpointRow();
                Image panel = NebulaUiFactory.Panel(card, "Hardpoint " + i,
                    NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Empty));
                row.Root = panel.rectTransform;
                row.Background = panel;
                row.Root.anchorMin = new Vector2(0f, 1f);
                row.Root.anchorMax = new Vector2(1f, 1f);
                row.Root.pivot = new Vector2(0.5f, 1f);
                row.Root.anchoredPosition = new Vector2(0f, -46f - i * 56f);
                row.Root.sizeDelta = new Vector2(-32f, 50f);

                row.Name = NebulaUiFactory.Label(row.Root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                    NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
                NebulaUiFactory.Place(row.Name.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(14f, -10f), new Vector2(260f, 18f));

                row.State = NebulaUiFactory.Label(row.Root, "State", "", NebulaUiDesignTokens.SizeCaption,
                    NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleRight, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingCaption);
                NebulaUiFactory.Place(row.State.Text.rectTransform, new Vector2(1f, 1f),
                    new Vector2(-134f, -10f), new Vector2(160f, 16f));

                // The control that makes weapon production reachable at all.
                //
                // Hull Form I opens one hardpoint and the campaign starts with the free
                // T1 Laser already in it, so with no way to empty a slot every
                // production request answered NoHardpointAvailable forever — three
                // buttons on this very card, permanently dead, refusing with a sentence
                // that sounded like a temporary condition. Releasing goes through
                // ShipCombat.TryReleaseHardpoint, which is WeaponLoadoutService's rule
                // and not a second one; it carries no refund because the weapon is
                // simply gone, and its replacement is paid for in full by the ordinary
                // production job that follows.
                int slot = i;
                row.Release = NebulaUiFactory.ButtonControl(row.Root, ReleaseControlName + i,
                    "SÖK", NebulaUiAssetCatalog.ButtonKind.Tertiary, () => ReleaseHardpoint(slot));
                NebulaUiFactory.Place(row.Release.Button.GetComponent<RectTransform>(),
                    new Vector2(1f, 1f), new Vector2(-14f, -6f),
                    new Vector2(104f, NebulaUiDesignTokens.MinimumHitTarget));

                row.Heat = NebulaUiFactory.Bar(row.Root, "Heat", NebulaUiDesignTokens.Status(UiStatusLevel.Caution));
                RectTransform bar = row.Heat.Track.rectTransform;
                bar.anchorMin = new Vector2(0f, 0f);
                bar.anchorMax = new Vector2(1f, 0f);
                bar.pivot = new Vector2(0.5f, 0f);
                bar.offsetMin = new Vector2(14f, 10f);
                bar.offsetMax = new Vector2(-14f, 10f + NebulaUiDesignTokens.BarHeight);

                hardpoints.Add(row);
            }
        }

        private void BuildRepair(RectTransform parent)
        {
            RectTransform card = Card(parent, "Repair", 0f, 116f, "ONARIM");

            repairState = NebulaUiFactory.Label(card, "State", "", NebulaUiDesignTokens.SizeBody,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft);
            NebulaUiFactory.Place(repairState.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -42f), new Vector2(360f, 20f));

            repairDetail = NebulaUiFactory.Value(card, "Detail", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(repairDetail.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -42f), new Vector2(240f, 20f));

            repairBar = NebulaUiFactory.Bar(card, "Bar", NebulaUiDesignTokens.Status(UiStatusLevel.Working), striped: true);
            RectTransform bar = repairBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(20f, -84f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-20f, -84f);
        }

        private void BuildThreat(RectTransform parent)
        {
            RectTransform card = Card(parent, "Threat", -128f, 190f, "TEHDİT");

            pressureValue = NebulaUiFactory.Value(card, "Pressure", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(pressureValue.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -18f), new Vector2(300f, 16f));

            pressureBar = NebulaUiFactory.Bar(card, "PressureBar", NebulaUiDesignTokens.Status(UiStatusLevel.Warning));
            RectTransform bar = pressureBar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(20f, -50f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(-20f, -50f);

            raidValue = NebulaUiFactory.Label(card, "Raid", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(raidValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -78f), new Vector2(400f, 40f));

            pirateValue = NebulaUiFactory.Label(card, "Pirate", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkMuted, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(pirateValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -140f), new Vector2(400f, 16f));
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;
            ShipCombat combat = context?.Ship != null ? context.Ship.GetComponent<ShipCombat>() : null;

            RefreshLoadout(combat);
            RefreshProduction(combat);
            RefreshRepair(combat);
            RefreshThreat(combat);
        }

        /// <summary>
        /// One family's production control. <paramref name="busy"/> is answered first so
        /// a running job reads as "the queue is taken" rather than as whatever the family
        /// would refuse for underneath it, which is the more useful sentence while the
        /// player is watching a progress bar.
        /// </summary>
        private static void ApplyProductionAvailability(
            NebulaUiFactory.ButtonHandle control, ShipCombat combat, WeaponFamily family, bool busy)
        {
            if (busy) { control.SetAvailable(false, "ÜRETİM YUVASI DOLU"); return; }
            if (combat == null) { control.SetAvailable(false, "GEMİ HAZIR DEĞİL"); return; }

            WeaponProductionRejectReason reason = combat.CheckProduction(family, 1);
            control.SetAvailable(reason == WeaponProductionRejectReason.None,
                reason == WeaponProductionRejectReason.None ? null : WeaponProductionRejectText(reason));
        }

        private void RefreshProduction(ShipCombat combat)
        {
            WeaponProductionJob job = combat?.ActiveProductionJob;
            bool busy = job != null;
            // Availability is the seam's own answer, per family, not just "is the queue
            // free". Asking only `!busy` left all three buttons live while the ship had
            // no empty active hardpoint, no parts, the family locked or combat running:
            // the player clicked, the click was refused, and the button offered itself
            // again next frame. Now the control is off and wears the reason it would
            // have published — and because CheckProduction is what TryStartProduction
            // itself runs, an enabled button can no longer be refused.
            ApplyProductionAvailability(laserProduction, combat, WeaponFamily.Laser, busy);
            ApplyProductionAvailability(plasmaProduction, combat, WeaponFamily.Plasma, busy);
            ApplyProductionAvailability(emProduction, combat, WeaponFamily.Em, busy);
            cancelProduction.SetAvailable(busy, "ETKİN ÜRETİM YOK");

            if (!busy)
            {
                if (string.IsNullOrEmpty(productionState.Value)) productionState.Value = "ÜRETİM YUVASI HAZIR";
                productionCost.Value = "T1 MALİYETİ DÜĞMEDE";
                productionBar.SetFraction(0f);
                return;
            }

            WeaponTierCost cost = WeaponCatalog.CostForTier(job.Family, job.TargetTier);
            productionState.Value = $"{WeaponName(job.Family)} T{job.TargetTier} ÜRETİLİYOR";
            productionCost.Value = $"{cost.Parts} PARÇA + {cost.Material} {ProcessingCatalog.ItemDisplayName(cost.MaterialId)}";
            productionBar.SetFraction(job.Progress01);
        }

        internal static string WeaponProductionRejectText(WeaponProductionRejectReason reason) => reason switch
        {
            WeaponProductionRejectReason.QueueBusy => "ÜRETİM YUVASI DOLU",
            WeaponProductionRejectReason.TargetTierInvalid => "BU SİLAH KADEMESİ KULLANILAMIYOR",
            WeaponProductionRejectReason.FamilyLocked => "SİLAH KOORDİNASYONU KADEMESİ YETERSİZ",
            WeaponProductionRejectReason.InsufficientResources => "YETERSİZ PARÇA VEYA MALZEME",
            WeaponProductionRejectReason.CombatActive => "MUHAREBE SIRASINDA SİLAH ÜRETİLEMEZ",
            WeaponProductionRejectReason.NoHardpointAvailable => "BOŞ ETKİN SİLAH YUVASI YOK",
            _ => "ÜRETİM BAŞLATILAMADI"
        };

        /// <summary>
        /// Puts focus on something the player can actually use.
        /// <para>
        /// This used to select the Laser production button and, if that button was not
        /// interactable, <b>return having selected nothing</b> — which is precisely the
        /// state the page is in while a production job runs, because a busy queue
        /// disables all three family buttons at once. Opening Savaş mid-production
        /// therefore left the EventSystem with no selection, and Unity's navigation
        /// walks outwards from the current selection: with none, the arrow keys and
        /// the stick did nothing whatsoever and the page was keyboard-dead.
        /// </para>
        /// <para>
        /// The answer is a chain rather than a single candidate. Cancel is second on
        /// purpose — while production is busy it is the one control that <i>is</i>
        /// enabled, and it is also the only thing the player can meaningfully do on
        /// this page at that moment. The final sweep is what makes the guarantee
        /// unconditional: if any interactable Selectable exists under this page, one of
        /// them ends up focused.
        /// </para>
        /// </summary>
        public void FocusFirstControl()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;

            if (TryFocus(events, laserProduction)) return;
            if (TryFocus(events, cancelProduction)) return;
            if (TryFocus(events, plasmaProduction)) return;
            if (TryFocus(events, emProduction)) return;

            // Last resort: anything at all the player can reach. A page that offers a
            // control the chain above does not name must still be navigable.
            if (root == null) return;
            foreach (Selectable candidate in root.GetComponentsInChildren<Selectable>(false))
            {
                if (candidate == null || !candidate.IsInteractable()) continue;
                if (!candidate.gameObject.activeInHierarchy) continue;
                // A stand-in explains a refusal; it does not do anything. Opening the
                // page onto one would hand the pad a first stop where Submit is dead,
                // which is the very state this chain exists to avoid. It stays
                // reachable by moving there — it is simply never where focus starts.
                if (NebulaUiFactory.IsReasonFocus(candidate)) continue;
                events.SetSelectedGameObject(candidate.gameObject);
                return;
            }
        }

        private static bool TryFocus(EventSystem events, NebulaUiFactory.ButtonHandle handle)
        {
            Button button = handle?.Button;
            if (button == null || !button.interactable || !button.gameObject.activeInHierarchy) return false;
            events.SetSelectedGameObject(button.gameObject);
            return true;
        }

        private void RefreshLoadout(ShipCombat combat)
        {
            HardpointState[] slots = combat?.Hardpoints;
            int active = combat?.ActiveHardpointCount ?? 0;
            loadoutSummary.Value = slots == null ? "—" : $"{active} / {MaxHardpointRows} AKTİF YUVA";

            int occupied = 0;
            for (int i = 0; i < hardpoints.Count; i++)
            {
                HardpointRow row = hardpoints[i];
                HardpointState state = slots != null && i < slots.Length ? slots[i] : null;

                // Availability from the service's own answer, and the reason from the
                // same one. A control that decides for itself when it may be pressed
                // eventually offers something the seam refuses.
                HardpointReleaseRejectReason release = combat != null
                    ? combat.CheckReleaseHardpoint(i)
                    : HardpointReleaseRejectReason.NoSuchHardpoint;
                row.Release.SetAvailable(release == HardpointReleaseRejectReason.None,
                    HardpointReleaseRejectText(release));

                // A slot the hull form has not opened yet is locked, not empty. The
                // difference matters: one is waiting for a weapon, the other for a
                // level, and only one of them is the player's next move.
                if (state == null || state.IsEmpty)
                {
                    bool locked = i >= active;
                    row.Name.Value = locked ? "KİLİTLİ YUVA" : "BOŞ YUVA";
                    row.Name.Text.color = NebulaUiDesignTokens.DisabledInk;
                    row.State.Value = locked ? "GÖVDE FORMU YETERSİZ" : "SİLAH TAKILMADI";
                    row.State.Text.color = NebulaUiDesignTokens.InkMuted;
                    row.Background.sprite = NebulaUiAssetCatalog.Row(
                        locked ? NebulaUiAssetCatalog.RowState.Disabled : NebulaUiAssetCatalog.RowState.Empty);
                    row.Heat.SetFraction(0f);
                    continue;
                }

                occupied++;
                row.Name.Value = $"{WeaponName(state.Weapon)} T{state.Tier}";
                row.Name.Text.color = NebulaUiDesignTokens.InkPrimary;

                if (state.Overheated)
                {
                    row.State.Value = "AŞIRI ISI";
                    row.State.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
                    row.Background.sprite = NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Critical);
                    row.Heat.SetColor(NebulaUiDesignTokens.Status(UiStatusLevel.Critical));
                    AttentionCount++;
                }
                else
                {
                    row.State.Value = $"ISI %{state.Heat:0}";
                    row.State.Text.color = NebulaUiDesignTokens.InkSecondary;
                    row.Background.sprite = NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default);
                    row.Heat.SetColor(NebulaUiDesignTokens.Status(UiStatusLevel.Caution));
                }

                row.Heat.SetFraction(state.Heat / 100f);
            }

            OccupiedHardpoints = occupied;
        }

        private void RefreshRepair(ShipCombat combat)
        {
            ShipCombatState state = combat?.State;
            RepairJobState repair = combat?.Repair;

            if (state == null || repair == null)
            {
                repairState.Value = "—";
                repairDetail.Value = string.Empty;
                repairBar.SetFraction(0f);
                return;
            }

            if (!state.Damaged)
            {
                repairState.Value = "GEMİ SAĞLAM";
                repairState.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Nominal);
                repairDetail.Value = string.Empty;
                repairBar.SetFraction(0f);
                return;
            }

            AttentionCount++;

            if (!repair.Active)
            {
                repairState.Value = "HASARLI";
                repairState.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
                repairDetail.Value = NebulaGameplayFeedback.RepairText(combat.LastRepairRejectReason)
                    ?? "ONARIM BAŞLATILMADI";
                repairBar.SetFraction(0f);
                return;
            }

            // Paused is its own state, not a slower version of running: it will not
            // finish on its own, and the player has to do something about it.
            repairState.Value = repair.Paused ? "ONARIM DURAKLADI" : "ONARILIYOR";
            repairState.Text.color = NebulaUiDesignTokens.Status(
                repair.Paused ? UiStatusLevel.Warning : UiStatusLevel.Working);
            repairDetail.Value = $"%{repair.PackageProgress01 * 100f:0}";
            repairBar.SetFraction(repair.PackageProgress01);
        }

        private void RefreshThreat(ShipCombat combat)
        {
            ThreatPressureState pressure = combat?.Director?.Pressure;
            if (pressure == null)
            {
                pressureValue.Value = "—";
                pressureBar.SetFraction(0f);
                raidValue.Value = "AKIN YOK";
                pirateValue.Value = string.Empty;
                return;
            }

            if (pressure.InGrace)
            {
                pressureValue.Value = $"DOKUNULMAZLIK {pressure.GraceRemainingActiveMinutes:0.0} DK";
                pressureBar.SetFraction(0f);
            }
            else
            {
                pressureValue.Value = $"BASKI {pressure.Pressure:0} / {ThreatPressureState.MaxPressure:0}";
                pressureBar.SetFraction(pressure.Pressure / ThreatPressureState.MaxPressure);
            }

            if (pressure.Phase == RaidPhase.None)
            {
                raidValue.Value = "AKIN YOK";
                raidValue.Text.color = NebulaUiDesignTokens.InkMuted;
            }
            else
            {
                raidValue.Value = $"{Hud.NebulaHudAlerts.StageName(pressure.Stage)} AKINI · " +
                                  $"{PhaseName(pressure.Phase)}";
                raidValue.Text.color = NebulaUiDesignTokens.Status(UiStatusLevel.Critical);
                AttentionCount++;
            }

            PirateBaseRuntime pirateBase = combat?.Director?.PirateBase;
            pirateValue.Value = pirateBase == null
                ? string.Empty
                : pirateBase.Resolved ? "KORSAN ÜSSÜ: TEMİZLENDİ" : "KORSAN ÜSSÜ: KORUMALI";
        }

        // ---- copy ------------------------------------------------------------

        internal static string WeaponName(WeaponFamily? family)
        {
            if (!family.HasValue) return "BOŞ";
            switch (family.Value)
            {
                case WeaponFamily.Laser: return "LAZER";
                case WeaponFamily.Plasma: return "PLAZMA";
                case WeaponFamily.Em: return "EM";
                default: return "SİLAH";
            }
        }

        internal static string PhaseName(RaidPhase phase)
        {
            switch (phase)
            {
                case RaidPhase.None: return "YOK";
                case RaidPhase.Scheduled: return "PLANLANDI";
                case RaidPhase.Warning: return "UYARI";
                case RaidPhase.Spawning: return "GELİYOR";
                case RaidPhase.Active: return "SÜRÜYOR";
                case RaidPhase.RetreatCleanup: return "ÇEKİLİYOR";
                case RaidPhase.Resolved: return "PÜSKÜRTÜLDÜ";
                default: return "—";
            }
        }

        /// <summary>No nested state, so Back falls through to the shell.</summary>
        public bool HandleBack() => false;
    }
}
