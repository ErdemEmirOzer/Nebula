using UnityEngine;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;

namespace Nebula.UI.Session
{
    /// <summary>
    /// KÜNYE — who and what this build is made of.
    /// <para>
    /// Everything on it is composed from values already in the build: the save
    /// schema the menu footer prints, the sector generation version, and the content
    /// revision. Nothing here is fetched, and nothing here is a placeholder — a
    /// credits panel that says "TODO" in a shipped build is worse than a menu entry
    /// that was never wired.
    /// </para>
    /// <para>
    /// The copy is Turkish and every block wraps. Turkish text runs roughly a fifth
    /// longer than the English it is translated from, and a fixed-width credits
    /// column is exactly where that overflows.
    /// </para>
    /// </summary>
    public sealed class NebulaCreditsScreen : MonoBehaviour, INebulaUiScreen
    {
        public string ScreenId => "session.credits";

        private NebulaUiShell shell;
        private RectTransform root;

        /// <summary>Test-facing: credit blocks drawn.</summary>
        public int BlockCount { get; private set; }

        public static NebulaCreditsScreen Create(NebulaUiShell shell)
        {
            var go = new GameObject("Credits Screen");
            var screen = go.AddComponent<NebulaCreditsScreen>();
            screen.Build(shell);
            return screen;
        }

        /// <summary>
        /// The panel's content, as headings and bodies. A table rather than a wall of
        /// Build() calls so a test can walk it and prove no entry is empty — an empty
        /// credit block renders as a heading floating over nothing.
        /// </summary>
        internal static (string heading, string body)[] Blocks() => new[]
        {
            ("NEBULA",
             "Mekanik alfa. Bir uzay madenciliği, lojistik ve hâkimiyet oyunu."),

            ("YAPIM",
             "Tasarım, kod ve arayüz: Nebula ekibi.\n" +
             "Görsel dil: Obsidian Aerospace."),

            ("BU SÜRÜM",
             $"Kayıt şeması: v{Extraction.SaveService.CurrentSchema}\n" +
             $"Sektör üretim sürümü: {Sectors.SectorManifestGenerator.GenerationVersion}\n" +
             $"İçerik revizyonu: {Content.ContentFingerprint.ContentRevision}"),

            ("TEŞEKKÜR",
             "Unity ve TextMeshPro ekiplerine, ve bu yapıyı kırmak için " +
             "saatlerini harcayan herkese teşekkür ederiz."),
        };

        private void Build(NebulaUiShell owningShell)
        {
            shell = owningShell;

            root = NebulaUiFactory.Group(shell.Root(UiLayer.Menu), "Credits Root");
            NebulaUiFactory.Fill(root);
            transform.SetParent(shell.Root(UiLayer.Menu), false);

            Image backdrop = NebulaUiFactory.Rect(root, "Backdrop", NebulaUiDesignTokens.SpaceVoid);
            NebulaUiFactory.Fill(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            NebulaSessionChrome.Header(root, "KÜNYE");
            NebulaSessionChrome.BackButton(root, () => shell.Back());

            Image panel = NebulaUiFactory.Panel(root, "Panel", NebulaUiAssetCatalog.PanelPrimary);
            RectTransform card = panel.rectTransform;
            NebulaUiFactory.Place(card, new Vector2(0f, 1f), new Vector2(88f, -102f),
                new Vector2(1000f, 780f));

            float y = -28f;
            foreach ((string heading, string body) in Blocks())
            {
                NebulaUiFactory.LabelHandle title = NebulaUiFactory.SectionTitle(card, heading, heading);
                NebulaUiFactory.Place(title.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(28f, y), new Vector2(940f, 18f));
                y -= 28f;

                // Wrapped and given real height. A single-line rect with Turkish copy
                // in it is the overflow this screen is most likely to produce.
                NebulaUiFactory.LabelHandle text = NebulaUiFactory.Label(card, heading + " Body", body,
                    NebulaUiDesignTokens.SizeCompact, NebulaUiDesignTokens.InkSecondary,
                    TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
                NebulaUiFactory.Place(text.Text.rectTransform, new Vector2(0f, 1f),
                    new Vector2(28f, y), new Vector2(940f, 96f));
                y -= 116f;

                BlockCount++;
            }
        }

        // ---- INebulaUiScreen -------------------------------------------------

        public void OnOpened(NebulaUiShell owningShell)
        {
            shell = owningShell;
            root.gameObject.SetActive(true);
        }

        public void OnClosed() => root.gameObject.SetActive(false);

        public void OnCovered() { }

        public void OnRevealed() { }

        /// <summary>Nothing nested; Back pops the screen.</summary>
        public bool HandleBack() => false;
    }
}
