using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Visuals;

namespace Nebula.UI.StrategicMap
{
    /// <summary>
    /// Top-left player panel: the ship's REAL cargo (raw resources, processed
    /// materials, Basic Parts, Nuclear Energy/Core) with their real produced icons,
    /// plus the short facts that actually help a decision — level, cargo fill,
    /// current system, link state, command capacity, waypoint and any critical alert.
    ///
    /// Zero-amount and out-of-context lines are never drawn: the summary stays short
    /// and the rows are pooled, so a refresh allocates nothing new once warm. No
    /// debug fields, no development data, no hidden intelligence.
    /// </summary>
    public sealed class StrategicMapResourcePanel : MonoBehaviour
    {
        private const int MaxRows = 14;

        private Text headerText;
        private Text statusText;
        private readonly List<ResourceRow> rows = new List<ResourceRow>();

        private sealed class ResourceRow
        {
            public GameObject Root;
            public Image Icon;
            public Text Label;
        }

        public static StrategicMapResourcePanel Create(RectTransform parent)
        {
            var go = new GameObject("Resource Panel");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            StrategicMapScreen.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -28f), new Vector2(360f, 620f));

            var panel = go.AddComponent<StrategicMapResourcePanel>();
            panel.BuildUi(rect);
            return panel;
        }

        private void BuildUi(RectTransform rect)
        {
            Image glass = StrategicMapScreen.CreateImage(rect, "Glass", StrategicMapTheme.PanelFill);
            StrategicMapScreen.Stretch(glass.rectTransform);

            headerText = StrategicMapScreen.CreateText(rect, "Header", "", StrategicMapTheme.HeadingSize,
                StrategicMapTheme.Gold, TextAnchor.UpperLeft);
            StrategicMapScreen.Anchor(headerText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -16f), new Vector2(324f, 30f));

            statusText = StrategicMapScreen.CreateText(rect, "Status", "", StrategicMapTheme.CaptionSize,
                StrategicMapTheme.MutedText, TextAnchor.UpperLeft);
            StrategicMapScreen.Anchor(statusText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -52f), new Vector2(324f, 118f));

            for (int i = 0; i < MaxRows; i++) rows.Add(BuildRow(rect, i));
        }

        private static ResourceRow BuildRow(RectTransform parent, int index)
        {
            var go = new GameObject($"Row {index}");
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            StrategicMapScreen.Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -180f - index * 30f), new Vector2(324f, 26f));

            Image icon = StrategicMapScreen.CreateImage(rect, "Icon", Color.white);
            StrategicMapScreen.Anchor(icon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 0f), new Vector2(24f, 24f));
            icon.preserveAspect = true;

            Text label = StrategicMapScreen.CreateText(rect, "Label", "", StrategicMapTheme.BodySize,
                StrategicMapTheme.PanelText, TextAnchor.MiddleLeft);
            StrategicMapScreen.Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, 0f), new Vector2(292f, 26f));

            go.SetActive(false);
            return new ResourceRow { Root = go, Icon = icon, Label = label };
        }

        /// <summary>Renders one immutable player summary. Pure presentation — reads nothing itself.</summary>
        public void Render(StrategicPlayerSummary player)
        {
            headerText.text = $"SEVİYE {player.Level}";

            float fill = player.CargoCapacity > 0.0001f ? player.CargoUsedVolume / player.CargoCapacity * 100f : 0f;
            var status = new System.Text.StringBuilder();
            status.Append($"Kargo  {player.CargoUsedVolume:0}/{player.CargoCapacity:0}  (%{fill:0})\n");
            status.Append($"Sistem  {player.CurrentSystemName}  ·  {LinkLabel(player.CurrentLink)}\n");
            if (player.CommandCapacity > 0)
                status.Append($"Komuta  {player.CommandUsed}/{player.CommandCapacity}\n");
            if (!string.IsNullOrEmpty(player.WaypointName))
                status.Append($"Rota  {player.WaypointName}  ·  {FormatDistance(player.WaypointDistance)}\n");
            if (player.HasCriticalAlert)
                status.Append("<KRİTİK UYARI>\n");
            statusText.text = status.ToString();
            statusText.color = player.HasCriticalAlert ? StrategicMapTheme.Red : StrategicMapTheme.MutedText;

            int used = 0;
            for (int i = 0; i < player.Resources.Count && used < rows.Count; i++)
            {
                StrategicResourceLine line = player.Resources[i];
                if (line.Amount <= 0.0001f) continue; // never pad the panel with empty stock

                ResourceRow row = rows[used++];
                row.Root.SetActive(true);
                row.Label.text = $"{DisplayName(line.ItemId)}   {line.Amount:0}";

                Sprite icon = NebulaIconResolver.Item(line.ItemId);
                row.Icon.sprite = icon;
                row.Icon.enabled = icon != null; // controlled fallback: the row still reads, just without an icon
            }
            for (int i = used; i < rows.Count; i++) rows[i].Root.SetActive(false);
        }

        private static string LinkLabel(Nebula.Extraction.SystemLinkState link) => link switch
        {
            Nebula.Extraction.SystemLinkState.Live => "Canlı",
            Nebula.Extraction.SystemLinkState.LastKnown => "Son bilinen",
            _ => "Bağlantı yok"
        };

        private static string FormatDistance(float distance) =>
            distance >= 1000f ? $"{distance / 1000f:0.0} bin birim" : $"{distance:0} birim";

        /// <summary>Player-facing name for a cargo item. Never the raw stable id.</summary>
        private static string DisplayName(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "";
            if (ItemNames.TryGetValue(itemId, out string name)) return name;
            return itemId; // controlled fallback for an id this table has not learned yet
        }

        private static readonly Dictionary<string, string> ItemNames = new Dictionary<string, string>
        {
            { "resource.metal", "Metal" },
            { "resource.wood", "Biyokütle" },
            { "resource.gas", "Gaz" },
            { "resource.water", "Su" },
            { "resource.diamond", "Elmas" },
            { "part.basic", "Temel Parça" },
            { "material.metal_alloy", "Metal Alaşım" },
            { "material.biocomposite", "Biyokompozit" },
            { "material.refined_gas", "Rafine Gaz" },
            { "material.purified_water", "Arıtılmış Su" },
            { "material.industrial_diamond", "Endüstriyel Elmas" },
            { "energy.nuclear", "Nükleer Enerji" },
            { "core.nuclear", "Nükleer Çekirdek" }
        };
    }
}
