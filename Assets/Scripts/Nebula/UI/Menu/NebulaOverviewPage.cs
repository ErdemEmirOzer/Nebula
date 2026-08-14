using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Logistics;
using Nebula.Progression;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// The page that answers "where am I and what needs me".
    /// <para>
    /// Nothing showed this together before: durability lived in the combat HUD,
    /// level in the progression HUD, cargo in the extraction text wall, fleet in a
    /// one-line strip, and attention in a fourth panel. Reading them as one page is
    /// the point of a command menu.
    /// </para>
    /// <para>
    /// Strictly read-only. Every value is projected from state that already has an
    /// owner; this page never writes, never caches a gameplay object across a
    /// refresh, and tolerates a null context so it can be built before or without a
    /// session.
    /// </para>
    /// </summary>
    public sealed class NebulaOverviewPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Overview;
        public string Title => "Genel Bakış";

        private readonly NebulaContext context;

        private NebulaUiFactory.BarHandle shieldBar, armorBar, hullBar, energyBar, xpBar, cargoBar;
        private NebulaUiFactory.LabelHandle shieldValue, armorValue, hullValue, energyValue;
        private NebulaUiFactory.LabelHandle levelValue, cargoValue, fleetValue, dominanceValue;
        private NebulaUiFactory.LabelHandle damagedBadge;

        /// <summary>Test-facing: attention items this page would badge on the rail.</summary>
        public int AttentionCount { get; private set; }

        public NebulaOverviewPage(NebulaContext sessionContext)
        {
            context = sessionContext;
        }

        public void Build(RectTransform host)
        {
            RectTransform column1 = Column(host, 0);
            RectTransform column2 = Column(host, 1);

            BuildIntegrityCard(column1);
            BuildProgressCard(column1);
            BuildFleetCard(column2);
            BuildDominanceCard(column2);
        }

        private static RectTransform Column(RectTransform host, int index)
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
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, height);

            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.SectionTitle(rect, "Title", NebulaText.Upper(title));
            NebulaUiFactory.Place(heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -18f), new Vector2(320f, 18f));
            return rect;
        }

        // ---- cards -----------------------------------------------------------

        private void BuildIntegrityCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Integrity", 0f, 220f, "Gemi Bütünlüğü");

            damagedBadge = NebulaUiFactory.Label(card, "Damaged", "",
                NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                TextAnchor.MiddleRight, FontStyle.Bold, NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(damagedBadge.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(-20f, -18f), new Vector2(160f, 18f));

            shieldBar = Reading(card, "Shield", "KALKAN", -52f,
                NebulaUiDesignTokens.Status(UiStatusLevel.Nominal),
                NebulaUiAssetCatalog.ShapeGlyph.Shield, out shieldValue);
            armorBar = Reading(card, "Armor", "ZIRH", -92f,
                NebulaUiDesignTokens.Status(UiStatusLevel.Warning),
                NebulaUiAssetCatalog.ShapeGlyph.Armor, out armorValue);
            hullBar = Reading(card, "Hull", "GÖVDE", -132f,
                NebulaUiDesignTokens.Status(UiStatusLevel.Critical),
                NebulaUiAssetCatalog.ShapeGlyph.Hull, out hullValue);
            energyBar = Reading(card, "Energy", "ENERJİ", -172f,
                NebulaUiDesignTokens.Status(UiStatusLevel.Nominal),
                NebulaUiAssetCatalog.ShapeGlyph.Energy, out energyValue);
        }

        /// <summary>
        /// One labelled bar. The shape glyph is what keeps Shield/Armor/Hull apart
        /// for a player who cannot separate the three colours, so it is not optional.
        /// </summary>
        private static NebulaUiFactory.BarHandle Reading(RectTransform card, string name, string label,
            float y, Color color, NebulaUiAssetCatalog.ShapeGlyph glyph,
            out NebulaUiFactory.LabelHandle value)
        {
            var row = NebulaUiFactory.Group(card, name);
            NebulaUiFactory.Place(row, new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(0f, 32f));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.offsetMin = new Vector2(20f, y - 32f);
            row.offsetMax = new Vector2(-20f, y);

            Image shape = NebulaUiFactory.Icon(row, "Glyph", NebulaUiAssetCatalog.Glyph(glyph), 11f);
            NebulaUiFactory.Place(shape.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -2f),
                new Vector2(11f, 11f));
            shape.color = color;

            NebulaUiFactory.LabelHandle caption = NebulaUiFactory.Label(row, "Label", label,
                NebulaUiDesignTokens.SizeCaption, color, TextAnchor.UpperLeft, FontStyle.Bold,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(caption.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(18f, 0f), new Vector2(200f, 14f));

            value = NebulaUiFactory.Value(row, "Value", "—", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(value.Text.rectTransform, new Vector2(1f, 1f),
                new Vector2(0f, 0f), new Vector2(160f, 14f));

            NebulaUiFactory.BarHandle bar = NebulaUiFactory.Bar(row, "Bar", color);
            RectTransform barRect = bar.Track.rectTransform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.offsetMin = new Vector2(0f, -NebulaUiDesignTokens.BarHeight - 18f);
            barRect.offsetMax = new Vector2(0f, -18f);
            return bar;
        }

        private void BuildProgressCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Progress", -236f, 150f, "Seviye ve Kargo");

            levelValue = NebulaUiFactory.Label(card, "Level", "—", NebulaUiDesignTokens.SizeValue,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.UpperLeft);
            NebulaUiFactory.Place(levelValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -46f), new Vector2(300f, 24f));

            xpBar = NebulaUiFactory.Bar(card, "Xp", NebulaUiDesignTokens.ActionPrimary);
            RectTransform xpRect = xpBar.Track.rectTransform;
            xpRect.anchorMin = new Vector2(0f, 1f);
            xpRect.anchorMax = new Vector2(1f, 1f);
            xpRect.pivot = new Vector2(0.5f, 1f);
            xpRect.offsetMin = new Vector2(20f, -84f);
            xpRect.offsetMax = new Vector2(-20f, -84f + NebulaUiDesignTokens.BarHeight);

            cargoValue = NebulaUiFactory.Label(card, "Cargo", "—", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft);
            NebulaUiFactory.Place(cargoValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -100f), new Vector2(300f, 18f));

            cargoBar = NebulaUiFactory.Bar(card, "CargoBar", NebulaUiDesignTokens.ActionPrimary);
            RectTransform cargoRect = cargoBar.Track.rectTransform;
            cargoRect.anchorMin = new Vector2(0f, 1f);
            cargoRect.anchorMax = new Vector2(1f, 1f);
            cargoRect.pivot = new Vector2(0.5f, 1f);
            cargoRect.offsetMin = new Vector2(20f, -128f);
            cargoRect.offsetMax = new Vector2(-20f, -128f + NebulaUiDesignTokens.BarHeight);
        }

        private void BuildFleetCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Fleet", 0f, 120f, "Filo");
            fleetValue = NebulaUiFactory.Label(card, "Value", "—", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(fleetValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -46f), new Vector2(340f, 60f));
        }

        private void BuildDominanceCard(RectTransform parent)
        {
            RectTransform card = Card(parent, "Dominance", -136f, 150f, "Hâkimiyet");
            dominanceValue = NebulaUiFactory.Label(card, "Value", "—", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(dominanceValue.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(20f, -46f), new Vector2(340f, 90f));
        }

        // ---- refresh ---------------------------------------------------------

        public void Refresh()
        {
            AttentionCount = 0;
            RefreshIntegrity();
            RefreshProgress();
            RefreshFleet();
            RefreshDominance();
        }

        private void RefreshIntegrity()
        {
            ShipCombat combat = context?.Ship != null ? context.Ship.GetComponent<ShipCombat>() : null;
            ShipCombatState state = combat?.State;
            if (state == null)
            {
                shieldValue.Value = armorValue.Value = hullValue.Value = energyValue.Value = "—";
                damagedBadge.Text.gameObject.SetActive(false);
                return;
            }

            Durability durability = state.Durability;
            SetReading(shieldBar, shieldValue, durability.Shield, durability.ShieldMax);
            SetReading(armorBar, armorValue, durability.Armor, durability.ArmorMax);
            SetReading(hullBar, hullValue, durability.Hull, durability.HullMax);
            SetReading(energyBar, energyValue, state.Energy, state.EnergyCapacity);

            bool damaged = state.Damaged;
            if (damagedBadge.Text.gameObject.activeSelf != damaged)
                damagedBadge.Text.gameObject.SetActive(damaged);
            if (damaged)
            {
                damagedBadge.Value = "HASARLI";
                AttentionCount++;
            }
        }

        private static void SetReading(NebulaUiFactory.BarHandle bar,
            NebulaUiFactory.LabelHandle value, float current, float max)
        {
            bar.SetFraction(max > 0f ? current / max : 0f);
            value.Value = $"{current:0} / {max:0}";
        }

        private void RefreshProgress()
        {
            ProgressionState progression = ProgressionService.State;
            if (progression.IsMaxLevel)
            {
                levelValue.Value = $"SEVİYE {progression.Level} · MAKS";
                xpBar.SetFraction(1f);
            }
            else
            {
                long into = ProgressionMath.XpIntoLevel(progression.TotalXp);
                long need = ProgressionMath.XpForLevel(progression.Level);
                levelValue.Value = $"SEVİYE {progression.Level}   {into:N0} / {need:N0} XP";
                xpBar.SetFraction(need > 0 ? (float)into / need : 0f);
            }

            ShipExtraction extraction = context?.Ship != null
                ? context.Ship.GetComponent<ShipExtraction>() : null;
            LogisticsCargo cargo = extraction?.Cargo;
            if (cargo == null)
            {
                cargoValue.Value = "KARGO —";
                cargoBar.SetFraction(0f);
                return;
            }

            cargoValue.Value = $"KARGO {cargo.UsedVolume:0} / {cargo.Capacity:0}";
            cargoBar.SetFraction(cargo.Capacity > 0f ? cargo.UsedVolume / cargo.Capacity : 0f);
            if (cargo.OverCapacity) AttentionCount++;
        }

        private void RefreshFleet()
        {
            Hangar.ShipHangar hangar = context?.Hangar;
            if (hangar?.Vehicles == null)
            {
                fleetValue.Value = "ARAÇ YOK";
                return;
            }

            int active = 0, waiting = 0;
            IReadOnlyList<Hangar.VehicleAgent> vehicles = hangar.Vehicles;
            for (int i = 0; i < vehicles.Count; i++)
            {
                Hangar.VehicleAgent vehicle = vehicles[i];
                if (vehicle == null) continue;
                if (vehicle.Mission.Phase != Hangar.VehiclePhase.InHangar) active++;
                if (vehicle.Mission.Phase == Hangar.VehiclePhase.Waiting) waiting++;
            }

            fleetValue.Value = waiting > 0
                ? $"{active} GÖREVDE / {vehicles.Count} TOPLAM\n{waiting} ARAÇ BEKLİYOR"
                : $"{active} GÖREVDE / {vehicles.Count} TOPLAM";
            AttentionCount += waiting;
        }

        private void RefreshDominance()
        {
            int controlled = 0, contested = 0, lost = 0;
            foreach (KeyValuePair<string, PlanetDominance> entry in ExtractionState.AllDominance)
            {
                switch (entry.Value.State)
                {
                    case DominanceState.Controlled: controlled++; break;
                    case DominanceState.Contested: contested++; break;
                    case DominanceState.Lost: lost++; break;
                }
            }

            var text = new System.Text.StringBuilder(120);
            text.Append($"{controlled} KONTROL EDİLEN GEZEGEN");
            if (contested > 0) text.Append($"\n{contested} ÇEKİŞMELİ");
            if (lost > 0) text.Append($"\n{lost} KAYBEDİLDİ");
            dominanceValue.Value = text.ToString();

            AttentionCount += contested + lost;
        }

        /// <summary>Overview owns no nested state, so Back always falls through to the shell.</summary>
        public bool HandleBack() => false;
    }
}
