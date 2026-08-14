using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Hud
{
    /// <summary>
    /// Ship durability, energy and hardpoints, bottom right.
    /// <para>
    /// This replaces the combat HUD's
    /// <c>"[HEX]K 85%  [PLATE]Z 60%  [HULL]G 22%"</c> line, whose bracket tags were
    /// markup the renderer never interpreted -- the player saw them literally.
    /// The tags were reaching for exactly the right idea, which is that Shield,
    /// Armor and Hull must be separable by something other than colour. Here that
    /// idea is real: each carries its own shape glyph, hexagon, plate and block,
    /// so a monochrome capture still reads correctly.
    /// </para>
    /// </summary>
    public sealed class NebulaHudVitals : MonoBehaviour
    {
        /// <summary>Hardpoints the HUD will show. Beyond this the loadout belongs on the Combat page.</summary>
        public const int MaxHardpoints = 5;

        private ShipCombat combat;
        private RectTransform root;

        private Reading shield, armor, hull, energy;
        private NebulaUiFactory.LabelHandle damagedBadge;
        private readonly List<HardpointWidget> hardpoints = new List<HardpointWidget>(MaxHardpoints);

        /// <summary>Test-facing: hardpoint widgets currently on screen.</summary>
        public int VisibleHardpoints { get; private set; }

        /// <summary>Test-facing: whether the damaged badge is up.</summary>
        public bool DamagedBadgeVisible => damagedBadge.Text != null && damagedBadge.Text.gameObject.activeSelf;

        // ANI-05.4.3: one animator per layer, so only the layer that actually
        // absorbed a hit reacts. A refused or zero-damage hit raises no event at
        // all and therefore flashes nothing — the whole point of the contract.
        private readonly UiValueAnimator shieldDelta = new UiValueAnimator();
        private readonly UiValueAnimator armorDelta = new UiValueAnimator();
        private readonly UiValueAnimator hullDelta = new UiValueAnimator();

        /// <summary>Test-facing: real shield hits actually shown.</summary>
        public int ShieldReactionsShown => shieldDelta.HighlightsShown;

        /// <summary>Test-facing: real armor hits actually shown.</summary>
        public int ArmorReactionsShown => armorDelta.HighlightsShown;

        /// <summary>Test-facing: real hull hits actually shown.</summary>
        public int HullReactionsShown => hullDelta.HighlightsShown;

        private ShipCombat damageSource;
        private bool damageSubscribed;

        /// <summary>
        /// The resolver names the layer that really absorbed the hit, so only that
        /// layer's widget reacts.
        /// </summary>
        private void OnDamageApplied(ShipDamageLayer layer, bool destroyed, bool critical)
        {
            switch (layer)
            {
                case ShipDamageLayer.Shield: shieldDelta.RaiseDelta(1f); break;
                case ShipDamageLayer.Armor: armorDelta.RaiseDelta(1f); break;
                case ShipDamageLayer.Hull: hullDelta.RaiseDelta(1f); break;
            }
        }

        /// <summary>
        /// Subscribes once the combat reference is really there. Create assigns it
        /// after AddComponent has already fired OnEnable, so hooking here is what
        /// makes the connection reliable.
        /// </summary>
        private void EnsureDamageSubscribed()
        {
            if (damageSubscribed || combat == null) return;
            damageSource = combat;
            damageSubscribed = true;
            damageSource.DamageApplied += OnDamageApplied;
        }

        private void OnDisable()
        {
            if (!damageSubscribed || damageSource == null) return;
            damageSubscribed = false;
            damageSource.DamageApplied -= OnDamageApplied;
        }

        private struct Reading
        {
            public NebulaUiFactory.BarHandle Bar;
            public NebulaUiFactory.LabelHandle Value;
            public Image Glyph;
        }

        private sealed class HardpointWidget
        {
            public RectTransform Root;
            public NebulaUiFactory.LabelHandle Label;
            public NebulaUiFactory.BarHandle Heat;
        }

        public static NebulaHudVitals Create(NebulaUiShell shell, ShipCombat shipCombat)
        {
            var go = new GameObject("HUD Vitals");
            var vitals = go.AddComponent<NebulaHudVitals>();
            vitals.combat = shipCombat;
            vitals.Build(shell);
            return vitals;
        }

        private void Build(NebulaUiShell shell)
        {
            Image panel = NebulaUiFactory.Panel(shell.Root(UiLayer.HudDynamic), "Vitals",
                NebulaUiAssetCatalog.PanelPrimary);
            root = panel.rectTransform;
            NebulaUiFactory.Place(root, new Vector2(1f, 0f), new Vector2(-32f, 26f), new Vector2(392f, 232f));
            // The controller lives on the layer root, never inside the visual it
            // toggles: a widget parented into its own panel is switched off
            // together with it, its Update stops, and it can never decide to
            // come back. Hiding the panel must not disable the thing that shows it.
            transform.SetParent(shell.Root(UiLayer.HudDynamic), false);

            damagedBadge = NebulaUiFactory.Label(root, "Damaged", "HASARLI",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(damagedBadge.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-18f, -14f), new Vector2(140f, 16f));
            damagedBadge.Text.gameObject.SetActive(false);

            shield = BuildReading("Shield", "KALKAN", -16f, UiStatusLevel.Nominal,
                NebulaUiAssetCatalog.ShapeGlyph.Shield);
            armor = BuildReading("Armor", "ZIRH", -54f, UiStatusLevel.Warning,
                NebulaUiAssetCatalog.ShapeGlyph.Armor);
            hull = BuildReading("Hull", "GÖVDE", -92f, UiStatusLevel.Critical,
                NebulaUiAssetCatalog.ShapeGlyph.Hull);
            energy = BuildReading("Energy", "ENERJİ", -130f, UiStatusLevel.Nominal,
                NebulaUiAssetCatalog.ShapeGlyph.Energy);

            BuildHardpoints();
            root.gameObject.SetActive(false);
        }

        private Reading BuildReading(string name, string label, float y, UiStatusLevel level,
            NebulaUiAssetCatalog.ShapeGlyph glyph)
        {
            Color color = NebulaUiDesignTokens.Status(level);
            var row = NebulaUiFactory.Group(root, name);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(18f, y - 30f);
            row.offsetMax = new Vector2(-18f, y);

            var reading = new Reading();

            // The shape glyph is the colour-blind safety channel. It is not
            // decoration and must never be dropped for space.
            reading.Glyph = NebulaUiFactory.Icon(row, "Glyph", NebulaUiAssetCatalog.Glyph(glyph), 11f);
            NebulaUiFactory.Place(reading.Glyph.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, -1f), new Vector2(11f, 11f));
            reading.Glyph.color = color;

            NebulaUiFactory.Place(NebulaUiFactory.Label(row, "Label", label,
                    NebulaUiDesignTokens.SizeCaption, color, TextAnchor.UpperLeft, FontStyle.Bold,
                    NebulaUiDesignTokens.TrackingCaption)
                .Text.rectTransform, new Vector2(0f, 1f), new Vector2(18f, 0f), new Vector2(200f, 14f));

            reading.Value = NebulaUiFactory.Value(row, "Value", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(reading.Value.Text.rectTransform, new Vector2(1f, 1f),
                Vector2.zero, new Vector2(150f, 14f));

            reading.Bar = NebulaUiFactory.Bar(row, "Bar", color);
            RectTransform bar = reading.Bar.Track.rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -18f - NebulaUiDesignTokens.BarHeight);
            bar.offsetMax = new Vector2(0f, -18f);
            return reading;
        }

        private void BuildHardpoints()
        {
            var strip = NebulaUiFactory.Group(root, "Hardpoints");
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(18f, 14f);
            strip.offsetMax = new Vector2(-18f, 54f);

            for (int i = 0; i < MaxHardpoints; i++)
            {
                var widget = new HardpointWidget();
                widget.Root = NebulaUiFactory.Group(strip, "Hardpoint " + i);
                widget.Root.anchorMin = new Vector2(i / (float)MaxHardpoints, 0f);
                widget.Root.anchorMax = new Vector2((i + 1) / (float)MaxHardpoints, 1f);
                widget.Root.offsetMin = new Vector2(2f, 0f);
                widget.Root.offsetMax = new Vector2(-2f, 0f);

                widget.Label = NebulaUiFactory.Label(widget.Root, "Label", "",
                    NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.UpperCenter, FontStyle.Bold);
                NebulaUiFactory.Fill(widget.Label.Text.rectTransform);

                widget.Heat = NebulaUiFactory.Bar(widget.Root, "Heat", NebulaUiDesignTokens.InkMuted);
                RectTransform heat = widget.Heat.Track.rectTransform;
                heat.anchorMin = new Vector2(0f, 0f);
                heat.anchorMax = new Vector2(1f, 0f);
                heat.pivot = new Vector2(0.5f, 0f);
                heat.offsetMin = Vector2.zero;
                heat.offsetMax = new Vector2(0f, 3f);

                widget.Root.gameObject.SetActive(false);
                hardpoints.Add(widget);
            }
        }

        private void Update()
        {
            EnsureDamageSubscribed();

            if (combat?.State == null)
            {
                if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
                return;
            }

            ShipCombatState state = combat.State;

            // Always, whenever there is a ship. Gating this on combat was a
            // regression: CombatHud drew the player's shield, armour and hull every
            // frame, and a space game that only shows your durability once you are
            // already being shot at has told you too late. The cluster stays quiet
            // by being small and unlit at full health, not by disappearing.
            bool relevant = true;
            if (root.gameObject.activeSelf != relevant) root.gameObject.SetActive(relevant);
            if (!relevant) return;

            // The printed numbers are always the real ones; only the bar eases, and
            // a load adopts the current fraction silently so a restored save never
            // animates up from zero.
            Durability durability = state.Durability;
            Set(shield, durability.Shield, durability.ShieldMax, shieldDelta);
            Set(armor, durability.Armor, durability.ArmorMax, armorDelta);
            Set(hull, durability.Hull, durability.HullMax, hullDelta);
            Set(energy, state.Energy, state.EnergyCapacity, null);

            if (damagedBadge.Text.gameObject.activeSelf != state.Damaged)
                damagedBadge.Text.gameObject.SetActive(state.Damaged);

            RefreshHardpoints();
        }

        private static void Set(Reading reading, float current, float max, UiValueAnimator animator)
        {
            float fraction = max > 0f ? current / max : 0f;
            reading.Bar.SetFraction(animator != null
                ? animator.Tick(fraction, Time.unscaledDeltaTime)
                : fraction);
            reading.Value.Value = $"{current:0} / {max:0}";
        }

        private void RefreshHardpoints()
        {
            int shown = 0;
            HardpointState[] states = combat.Hardpoints;

            for (int i = 0; i < states.Length && shown < MaxHardpoints; i++)
            {
                HardpointState hardpoint = states[i];
                if (hardpoint.IsEmpty) continue;

                HardpointWidget widget = hardpoints[shown];
                if (!widget.Root.gameObject.activeSelf) widget.Root.gameObject.SetActive(true);

                // Overheated is a state the player must act on, so it is named in
                // words as well as coloured -- the old HUD wrote "[ORANGE]" instead.
                bool overheated = hardpoint.Overheated;
                // Upper-cased like every other label. The weapon family name comes
                // from the catalog in mixed case, and "Laser T1" sitting under
                // "KALKAN" and "ZIRH" was the one word on the HUD that looked like
                // a different piece of software.
                string weapon = NebulaText.Upper($"{hardpoint.Weapon} T{hardpoint.Tier}");
                widget.Label.Value = overheated ? $"{weapon}\nAŞIRI ISI" : weapon;
                widget.Label.Text.color = overheated
                    ? NebulaUiDesignTokens.Status(UiStatusLevel.Critical)
                    : NebulaUiDesignTokens.InkSecondary;

                widget.Heat.SetFraction(hardpoint.Heat / 100f);
                widget.Heat.SetColor(overheated
                    ? NebulaUiDesignTokens.Status(UiStatusLevel.Critical)
                    : NebulaUiDesignTokens.Status(UiStatusLevel.Caution));
                shown++;
            }

            for (int i = shown; i < hardpoints.Count; i++)
                if (hardpoints[i].Root.gameObject.activeSelf) hardpoints[i].Root.gameObject.SetActive(false);

            VisibleHardpoints = shown;
        }
    }
}
