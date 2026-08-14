using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.UI.Accessibility;
using Nebula.UI.Design;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.Tests.EditMode
{
    /// <summary>
    /// GENEL DENETİM PART 4 / AJAN 1 — erişilebilirliğin oyuncuya ulaşması.
    /// <para>
    /// İki bulgu. <b>P1-UI-007</b>: <see cref="UiStatusCueStrip"/> kendi özetinde
    /// "Görsel İpucu anahtarının gösterildiği yüzey" diyordu ve <b>hiçbir üretim
    /// çağıranı yoktu</b> — ona yapılan her referans bir testti, yani oyuncunun beş
    /// işareti öğrenmesi gereken panel oyunda mevcut değildi. <b>P1-UI-010</b>:
    /// ana menüde ayrı bir ERİŞİLEBİLİRLİK kapısı vardı, oturum içinde yoktu;
    /// satırlar AYARLAR'ın ortasında, grafik ve ses kontrollerinin arasındaydı.
    /// </para>
    /// </summary>
    public class AuditPart4AccessibilityReachTests
    {
        private NebulaUiShell shell;
        private bool savedCues, savedContrast, savedMotionOff, savedSafeMode;
        private float savedUiScale;

        [SetUp]
        public void BuildAShell()
        {
            savedCues = CombatAccessibilitySettings.AudioVisualCueForWarnings;
            savedContrast = LightingAccessibility.HighContrast;
            savedMotionOff = AnimationAccessibility.MotionOff;
            savedSafeMode = LightingAccessibility.PhotosensitivitySafeMode;
            savedUiScale = NebulaUiShell.UiScale;

            NebulaAccessibilitySettingsStore.WritesEnabled = false;
            shell = NebulaUiShell.Create();
        }

        [TearDown]
        public void TearItDown()
        {
            if (shell != null) { shell.Shutdown(); Object.DestroyImmediate(shell.gameObject); }
            CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCues;
            LightingAccessibility.HighContrast = savedContrast;
            AnimationAccessibility.MotionOff = savedMotionOff;
            LightingAccessibility.PhotosensitivitySafeMode = savedSafeMode;
            NebulaUiShell.UiScale = savedUiScale;
            NebulaAccessibilitySettingsStore.WritesEnabled = true;
            Nebula.World.Composition.NebulaSessionLifetime.ResetProcessWideState();
        }

        private RectTransform Host()
        {
            var go = new GameObject("Part4 Host", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(shell.Root(UiLayer.Menu), false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(236f, 62f);   // the parent menu's content host
            rect.offsetMax = new Vector2(0f, -78f);
            return rect;
        }

        private static T Find<T>(Transform root, string name) where T : Component
        {
            foreach (T found in root.GetComponentsInChildren<T>(true))
                if (found.gameObject.name == name) return found;
            return null;
        }

        // =====================================================================
        // P1-UI-007 — the status legend exists in the game
        // =====================================================================

        /// <summary>
        /// Erişilebilirlik sayfası durum işaretleri şeridini gerçekten kuruyor, ve
        /// şerit Görsel İpucu anahtarını yanıtlıyor.
        /// </summary>
        [Test]
        public void TheAccessibilityPage_BuildsTheStatusCueLegend_AndTheSwitchChangesIt()
        {
            var page = new NebulaSettingsPage(null, accessibilityFrontDoor: true);
            page.Build(Host());

            RectTransform legend = Find<RectTransform>(shell.Root(UiLayer.Menu),
                NebulaSettingsPage.StatusCueLegendName);
            Assert.IsNotNull(legend,
                "the accessibility page does not build the status legend, so UiStatusCueStrip still "
                + "has no production call site and the player never sees the five marks");

            CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            page.Refresh();
            int on = page.VisibleStatusCueMarks;
            Assert.Greater(on, 0,
                "the legend draws no marks with Visual Cue on, so the panel that demonstrates the "
                + "switch demonstrates nothing");

            CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
            page.Refresh();
            Assert.AreEqual(0, page.VisibleStatusCueMarks,
                "turning Visual Cue off left the marks on screen — the switch does not reach the "
                + "one surface built to show it");

            CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            page.Refresh();
            Assert.AreEqual(on, page.VisibleStatusCueMarks,
                "the marks did not come back when the switch went back on");
        }

        /// <summary>
        /// Ve hiçbir durum yalnızca renkle anlatılmıyor: her temsilî okuma bir şekil
        /// ve bir sözcük de taşıyor, ve iki farklı durum aynı şekli kullanmıyor.
        /// </summary>
        [Test]
        public void NoStatusIsToldByColourAlone_AndNoTwoStatesShareAMark()
        {
            CombatAccessibilitySettings.AudioVisualCueForWarnings = true;

            var shapes = new List<UiCueShape>();
            foreach (UiStatusCueStrip.Entry entry in UiStatusCueStrip.RepresentativeStates())
            {
                Assert.IsTrue(entry.Cue.HasMark,
                    $"{entry.Label} is drawn with colour and no shape");
                Assert.IsNotEmpty(entry.Cue.Marker ?? string.Empty,
                    $"{entry.Label} carries a shape but no word");
                Assert.IsFalse(shapes.Contains(entry.Cue.Shape),
                    $"{entry.Label} reuses the {entry.Cue.Shape} mark another state already has");
                shapes.Add(entry.Cue.Shape);
            }
        }

        /// <summary>
        /// Yüksek Kontrast ve Işık Duyarlılığı Güvenli Modu açıkken şerit hâlâ üç
        /// kanalı da taşıyor. Bir erişilebilirlik ayarı bir başkasının kanalını
        /// söndürüyorsa, en çok ihtiyacı olan oyuncu en az bilgiyi alır.
        /// </summary>
        [Test]
        public void TheLegendKeepsAllThreeChannels_UnderHighContrastAndPhotosensitivitySafeMode()
        {
            CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
            LightingAccessibility.HighContrast = true;
            LightingAccessibility.PhotosensitivitySafeMode = true;
            AnimationAccessibility.MotionOff = true;

            var seen = new List<Color>();
            foreach (UiStatusCueStrip.Entry entry in UiStatusCueStrip.RepresentativeStates())
            {
                Assert.IsTrue(entry.Cue.HasMark, $"{entry.Label} lost its shape in High Contrast");
                Assert.IsNotEmpty(entry.Cue.Marker ?? string.Empty,
                    $"{entry.Label} lost its word in High Contrast");

                Color colour = entry.Cue.Color;
                Assert.Greater(colour.a, 0.5f,
                    $"{entry.Label} is drawn nearly transparent in High Contrast");
                seen.Add(colour);
            }

            Assert.Greater(seen.Distinct().Count(), 1,
                "every status collapsed to one colour in High Contrast; the colour channel is gone "
                + "and only the shape and the word are left");
        }

        // =====================================================================
        // P1-UI-010 — the in-session accessibility front door
        // =====================================================================

        /// <summary>
        /// Oturum içindeki AYARLAR sayfası ERİŞİLEBİLİRLİK'i kendi ekranı olarak
        /// açan gerçek bir kontrol taşıyor, ve o ekran erişilebilirlik kipindeki
        /// aynı sayfayı barındırıyor.
        /// </summary>
        [Test]
        public void TheInSessionSettingsPage_OpensAccessibilityAsItsOwnScreen()
        {
            var page = new NebulaSettingsPage(null, accessibilityFrontDoor: false, owningShell: shell);
            page.Build(Host());

            Button door = Find<Button>(shell.Root(UiLayer.Menu), NebulaSettingsPage.OpenAccessibilityControlName);
            Assert.IsNotNull(door,
                "the in-session settings page has no ERİŞİLEBİLİRLİK control at all — the front door "
                + "exists only in the main menu");
            Assert.IsTrue(door.interactable,
                "the accessibility control is on screen and cannot be clicked");

            int before = shell.StackDepth;
            door.onClick.Invoke();

            Assert.Greater(shell.StackDepth, before,
                "clicking ERİŞİLEBİLİRLİK opened nothing");

            NebulaMenuPageScreen opened = null;
            foreach (NebulaMenuPageScreen screen in
                     Object.FindObjectsOfType<NebulaMenuPageScreen>(true))
                if (screen.ScreenId.EndsWith("accessibility")) opened = screen;

            Assert.IsNotNull(opened,
                "the screen that opened is not the accessibility front door");
            var hosted = opened.Page as NebulaSettingsPage;
            Assert.IsNotNull(hosted, "the accessibility screen is not hosting the settings page");
            Assert.AreEqual("Erişilebilirlik", hosted.Title,
                "the accessibility screen is hosting the full settings page, not the front door");
        }

        /// <summary>
        /// Kapı, oturum dışında bir kabuk olmadan kurulduğunda kendini kapalı
        /// gösteriyor ve nedenini söylüyor — sessizce hiçbir şey yapan bir kontrol
        /// bırakılmıyor.
        /// </summary>
        [Test]
        public void WithNoShell_TheAccessibilityControlSaysWhyItCannotOpen()
        {
            var page = new NebulaSettingsPage(null, accessibilityFrontDoor: false);
            page.Build(Host());

            Button door = Find<Button>(shell.Root(UiLayer.Menu), NebulaSettingsPage.OpenAccessibilityControlName);
            Assert.IsNotNull(door, "the control is missing entirely rather than disabled with a reason");
            Assert.IsFalse(door.interactable,
                "the accessibility control offers itself with no shell to push a screen onto, so a "
                + "click is a silent no-op");
            Assert.IsFalse(page.OpenAccessibilityPage(),
                "OpenAccessibilityPage claimed to open a screen without a shell");
        }

        // =====================================================================
        // Türkçe uzun metin — %140 arayüz ölçeğinde kırpılma yok
        // =====================================================================

        /// <summary>
        /// Erişilebilirlik sayfasındaki her tek satırlık etiket, azami arayüz
        /// ölçeğinde bile kendi dikdörtgenine sığıyor.
        /// <para>
        /// Türkçe etiketler İngilizce muadillerinden uzundur ("Işık Duyarlılığı
        /// Güvenli Modu"), ve ölçek büyüdükçe referans çözünürlük küçülür — yani
        /// %140 en dar durumdur, en geniş değil. Kırpılan bir erişilebilirlik satırı
        /// tam olarak okunamayan satırdır.
        /// </para>
        /// </summary>
        [Test]
        public void EveryAccessibilityLabel_FitsItsRect_AtMaximumUiScale()
        {
            NebulaUiShell.UiScale = NebulaUiShell.MaxUiScale;

            var page = new NebulaSettingsPage(null, accessibilityFrontDoor: true);
            page.Build(Host());
            page.Refresh();
            Canvas.ForceUpdateCanvases();

            var offenders = new List<string>();
            int measured = 0;
            foreach (TextMeshProUGUI text in
                     shell.Root(UiLayer.Menu).GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                if (text.enableWordWrapping) continue;
                measured++;

                if (text.preferredWidth > text.rectTransform.rect.width + 1f)
                    offenders.Add($"{text.name} \"{text.text}\" needs {text.preferredWidth:0}px "
                                  + $"and has {text.rectTransform.rect.width:0}px");
                if (text.rectTransform.rect.height + 1f < text.fontSize)
                    offenders.Add($"{text.name} is {text.fontSize}pt in a "
                                  + $"{text.rectTransform.rect.height:0}px rect");
            }

            Assert.Greater(measured, 5,
                "almost no label was measured, so this proves nothing about the layout");
            Assert.IsEmpty(offenders,
                "these accessibility labels are cut off at 140% interface scale: "
                + string.Join("; ", offenders));
        }

        /// <summary>
        /// Ve hiçbir oyuncu-yüzü metin bir enum ya da sınıf adı taşımıyor.
        /// <para>
        /// Aranan şey bu kod tabanının kendi tip adlarıdır: bir ekranda
        /// "QuickAssetPackage" görmek, oyuncuya iç yapıyı göstermektir.
        /// </para>
        /// </summary>
        [Test]
        public void NoLabelOnTheseScreens_ShowsATechnicalName()
        {
            var page = new NebulaSettingsPage(null, accessibilityFrontDoor: true);
            page.Build(Host());
            page.Refresh();

            string[] technical =
            {
                "QuickAssetPackage", "MaintenancePlatform", "SensorArray", "InputContext",
                "KeyCode", "CombatAction", "UiStatusCue", "NebulaSettingsPage", "Mouse0", "Alpha",
            };

            var offenders = new List<string>();
            foreach (TextMeshProUGUI text in
                     shell.Root(UiLayer.Menu).GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                foreach (string name in technical)
                    if (text.text.Contains(name)) offenders.Add($"{text.name}: \"{text.text}\"");
            }

            Assert.IsEmpty(offenders,
                "these labels show a type name from this codebase to the player: "
                + string.Join("; ", offenders));
        }

        // =====================================================================
        // P1-UI-008 — modal input ownership
        // =====================================================================

        /// <summary>
        /// Harita tuşu router'dan okunuyor, yani bir ekran açıkken presi hiç
        /// almıyor; ve ekran kapanınca aynı karede geri geliyor.
        /// <para>
        /// Eskiden <c>Input.GetKeyDown</c> ile doğrudan okunuyordu: press açık menü
        /// altında da alınıyor, sonra <c>ExclusiveModal</c> tarafından hiçbir şey
        /// söylenmeden reddediliyordu. Oyuncu M'ye basıyor, hiçbir şey olmuyor,
        /// hiçbir gerekçe görünmüyor.
        /// </para>
        /// </summary>
        [Test]
        public void TheMapKey_IsSuppressedWhileAScreenOwnsInput_AndComesBackTheFrameItCloses()
        {
            string map = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Nebula",
                "UI", "StrategicMap", "StrategicMapScreen.cs"));
            StringAssert.Contains(
                "NebulaInputRouter.Pressed(NebulaInputActions.MapToggle)", map,
                "the map key is not read through the router, so a screen cannot suppress it");

            var pressed = new HashSet<KeyCode>();
            NebulaInputRouter.Source = key => pressed.Contains(key);
            try
            {
                KeyCode key = NebulaInputActions.Key(NebulaInputActions.MapToggle);
                pressed.Add(key);

                InputGate.Suppressed = false;
                Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.MapToggle),
                    "the map key does not reach the map even with nothing open");

                InputGate.Suppressed = true;
                Assert.IsFalse(NebulaInputRouter.Pressed(NebulaInputActions.MapToggle),
                    "the map key still reaches the map while a screen owns input");

                InputGate.Suppressed = false;
                Assert.IsTrue(NebulaInputRouter.Pressed(NebulaInputActions.MapToggle),
                    "the map key did not come back on the frame the gate dropped");
            }
            finally
            {
                NebulaInputRouter.ResetSourceToKeyboard();
                InputGate.Suppressed = false;
            }
        }

        /// <summary>
        /// Ve haritanın açılmayı reddettiği tek durumda ret <b>görünür</b>: tek
        /// geri bildirim kanalına yazılıyor, sessizce yutulmuyor.
        /// </summary>
        [Test]
        public void ARefusedMapOpen_PublishesAVisibleReason()
        {
            NebulaGameplayFeedback.Clear();
            var other = new object();
            Assert.IsTrue(ExclusiveModal.TryAcquire(other), "baseline: the modal could be taken");
            try
            {
                var go = new GameObject("part4-map");
                var screen = go.AddComponent<Nebula.UI.StrategicMap.StrategicMapScreen>();
                try
                {
                    Assert.IsFalse(screen.ToggleFromMapKey(),
                        "the map opened over another modal owner");

                    GameplayFeedbackMessage latest = NebulaGameplayFeedback.Latest;
                    Assert.AreEqual(GameplayFeedbackTone.Rejection, latest.Tone,
                        "a refused map open said nothing at all");
                    Assert.AreEqual(Nebula.UI.StrategicMap.StrategicMapScreen.ModalBlockedText, latest.Text,
                        "the refusal reason is not the one wording the map declares");
                    Assert.IsTrue(latest.IsVisible, "the refusal was published already expired");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                ExclusiveModal.Release(other);
                NebulaGameplayFeedback.Clear();
            }
        }

        // =====================================================================
        // P1-UI-004 — the accessibility settings survive a restart
        // =====================================================================

        /// <summary>
        /// Every accessibility setting comes back after a restart, and Photosensitivity
        /// Safe Mode in particular.
        /// <para>
        /// The ledger closed this row on source evidence alone — the store existed and
        /// the page called it — which is a promise, not a guarantee. It is the one row
        /// here whose failure lands on a specific person: a photosensitive player who
        /// switches the mode on, quits, and is shown full flashing on the next launch
        /// because the setting silently defaulted back to off.
        /// </para>
        /// <para>
        /// Round-tripped through the store's own Save/Load against a sandbox directory,
        /// so the operator's real settings file is neither read nor written.
        /// </para>
        /// </summary>
        [Test]
        public void EveryAccessibilitySetting_SurvivesASaveAndLoad()
        {
            string sandbox = Path.Combine(Path.GetTempPath(), "nebula-a11y-" + Path.GetRandomFileName());
            Directory.CreateDirectory(sandbox);

            bool savedReduced = AnimationAccessibility.ReducedMotion;
            bool savedMotionOff = AnimationAccessibility.MotionOff;
            bool savedComfort = AnimationAccessibility.CameraComfort;
            bool savedFlash = LightingAccessibility.FlashReduction;
            bool savedSafe = LightingAccessibility.PhotosensitivitySafeMode;
            bool savedContrast = LightingAccessibility.HighContrast;
            bool savedCue = CombatAccessibilitySettings.AudioVisualCueForWarnings;
            float savedHud = CombatAccessibilitySettings.HudScale;
            float savedUi = NebulaUiShell.UiScale;
            string savedDirectory = NebulaAccessibilitySettingsStore.DirectoryOverride;

            try
            {
                NebulaAccessibilitySettingsStore.DirectoryOverride = sandbox;
                NebulaAccessibilitySettingsStore.WritesEnabled = true;

                AnimationAccessibility.ReducedMotion = true;
                AnimationAccessibility.MotionOff = true;
                AnimationAccessibility.CameraComfort = true;
                LightingAccessibility.FlashReduction = true;
                LightingAccessibility.PhotosensitivitySafeMode = true;
                LightingAccessibility.HighContrast = true;
                CombatAccessibilitySettings.AudioVisualCueForWarnings = true;
                CombatAccessibilitySettings.HudScale =
                    CombatAccessibilitySettings.ClampHudScale(savedHud + 0.1f);
                NebulaUiShell.UiScale = NebulaUiShell.MaxUiScale;

                Assert.IsTrue(NebulaAccessibilitySettingsStore.Save(),
                    $"the store refused to write: {NebulaAccessibilitySettingsStore.LastStatus}");
                float writtenHud = CombatAccessibilitySettings.HudScale;

                // The next launch, as far as these values are concerned.
                AnimationAccessibility.ReducedMotion = false;
                AnimationAccessibility.MotionOff = false;
                AnimationAccessibility.CameraComfort = false;
                LightingAccessibility.FlashReduction = false;
                LightingAccessibility.PhotosensitivitySafeMode = false;
                LightingAccessibility.HighContrast = false;
                CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
                CombatAccessibilitySettings.HudScale = CombatAccessibilitySettings.MinHudScale;
                NebulaUiShell.UiScale = 1f;

                Assert.IsTrue(NebulaAccessibilitySettingsStore.Load(),
                    $"the store refused to read back: {NebulaAccessibilitySettingsStore.LastStatus}");

                Assert.IsTrue(LightingAccessibility.PhotosensitivitySafeMode,
                    "Photosensitivity Safe Mode came back off — the player who needs it most "
                    + "has to switch it on again every single launch");
                Assert.IsTrue(AnimationAccessibility.ReducedMotion, "Reduced Motion did not survive");
                Assert.IsTrue(AnimationAccessibility.MotionOff, "Motion Off did not survive");
                Assert.IsTrue(AnimationAccessibility.CameraComfort, "Camera Comfort did not survive");
                Assert.IsTrue(LightingAccessibility.FlashReduction, "Flash Reduction did not survive");
                Assert.IsTrue(LightingAccessibility.HighContrast, "High Contrast did not survive");
                Assert.IsTrue(CombatAccessibilitySettings.AudioVisualCueForWarnings,
                    "the warning visual cue did not survive");
                Assert.AreEqual(writtenHud, CombatAccessibilitySettings.HudScale, 0.001f,
                    "the HUD scale did not survive");
                Assert.AreEqual(NebulaUiShell.MaxUiScale, NebulaUiShell.UiScale, 0.001f,
                    "the interface scale did not survive");
            }
            finally
            {
                NebulaAccessibilitySettingsStore.DirectoryOverride = savedDirectory;
                AnimationAccessibility.ReducedMotion = savedReduced;
                AnimationAccessibility.MotionOff = savedMotionOff;
                AnimationAccessibility.CameraComfort = savedComfort;
                LightingAccessibility.FlashReduction = savedFlash;
                LightingAccessibility.PhotosensitivitySafeMode = savedSafe;
                LightingAccessibility.HighContrast = savedContrast;
                CombatAccessibilitySettings.AudioVisualCueForWarnings = savedCue;
                CombatAccessibilitySettings.HudScale = savedHud;
                NebulaUiShell.UiScale = savedUi;
                try { Directory.Delete(sandbox, true); } catch { /* best effort */ }
            }
        }
    }
}
