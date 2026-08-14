using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Menu
{
    /// <summary>
    /// Settings and accessibility.
    /// <para>
    /// Every control here binds to a setting that already existed and already
    /// worked -- <see cref="AnimationAccessibility"/>, <see cref="LightingAccessibility"/>,
    /// <see cref="CombatAccessibilitySettings"/>. The game has honoured Reduced
    /// Motion, Motion Off, Flash Reduction and HUD scale for some time; there was
    /// simply no screen through which a player could reach them. This page is that
    /// screen, not a new settings system.
    /// </para>
    /// <para>
    /// Reads and writes live values directly, so a change is visible in the
    /// interface behind the menu on the same frame rather than on apply.
    /// </para>
    /// </summary>
    public sealed class NebulaSettingsPage : INebulaMenuPage
    {
        public NebulaUiAssetCatalog.Page Page => NebulaUiAssetCatalog.Page.Settings;
        public string Title => accessibilityOnly ? "Erişilebilirlik" : "Ayarlar";

        /// <summary>
        /// Whether this instance is the accessibility front door rather than the
        /// full settings page.
        /// <para>
        /// The approved main menu has ERİŞİLEBİLİRLİK as its own entry and the
        /// runtime menu did not: the rows existed, but only as one section partway
        /// down AYARLAR, so a player who needs Photosensitivity Safe Mode had to
        /// find it inside a page of graphics and audio controls. That is exactly
        /// the player least able to go looking.
        /// </para>
        /// <para>
        /// Built as a mode on this page rather than a second page, because a
        /// separate accessibility screen would be a second settings authority that
        /// drifts: the same <c>AddToggle</c> calls, over the same statics, in the
        /// same order. What changes is which sections are laid out, and nothing
        /// about what a row does.
        /// </para>
        /// </summary>
        private readonly bool accessibilityOnly;

        /// <summary>Settings raise no attention; the rail badge stays empty.</summary>
        public int AttentionCount => 0;

        private readonly List<Row> rows = new List<Row>();

        private abstract class Row
        {
            public NebulaUiFactory.LabelHandle Value;
            public abstract void Refresh();
        }

        private sealed class ToggleRow : Row
        {
            public Func<bool> Get;
            public Action<bool> Set;
            public override void Refresh() => Value.Value = Get() ? "AÇIK" : "KAPALI";
            public void Flip() => Set(!Get());
        }

        /// <summary>
        /// A row that states something rather than changing it. The bindings are
        /// compile-time constants, so a remap control would be a lie -- what this
        /// page can honestly offer is the list, in one place, which is where
        /// ShipHud's pinned hint string went.
        /// </summary>
        private sealed class ReadingRow : Row
        {
            public string Text;
            public override void Refresh() => Value.Value = Text;
        }

        /// <summary>
        /// The status legend, as a row of the page so it repaints with everything
        /// else. It is the surface the Visual Cue switch is demonstrated on — see
        /// <see cref="Accessibility.UiStatusCueStrip"/>, which until now had no
        /// production call site at all and was built only from a test.
        /// </summary>
        private sealed class CueLegendRow : Row
        {
            public Accessibility.UiStatusCueStrip.Handle Legend;
            public override void Refresh() => Accessibility.UiStatusCueStrip.Repaint(Legend);
        }

        private sealed class ScaleRow : Row
        {
            public Func<float> Get;
            public Action<float> Set;
            public float Step, Min, Max;

            /// <summary>Optional: how the value reads. A quality level is not a percentage.</summary>
            public Func<float, string> Format;

            public override void Refresh() =>
                Value.Value = Format != null ? Format(Get()) : $"%{Get() * 100f:0}";

            public void Nudge(int direction) =>
                Set(Mathf.Clamp(Get() + direction * Step, Min, Max));
        }

        /// <summary>Test-facing: how many settings this page exposes.</summary>
        public int RowCount => rows.Count;

        /// <summary>The name of the panel the rebinding rows live in.</summary>
        public const string BindingPanelName = "Bindings";

        /// <summary>The name of the control that opens the rebinding panel.</summary>
        public const string OpenBindingsControlName = "OpenBindings";

        private readonly Controls.NebulaControlBindingSection bindings =
            new Controls.NebulaControlBindingSection();

        private RectTransform bindingPanel;

        /// <summary>The rebinding section, so a test can read what it is showing.</summary>
        public Controls.NebulaControlBindingSection Bindings => bindings;

        /// <summary>Test-facing: whether the rebinding panel is open.</summary>
        public bool BindingPanelOpen => bindingPanel != null && bindingPanel.gameObject.activeSelf;

        private readonly World.Composition.NebulaContext context;

        /// <summary>
        /// The shell this page can push a screen onto, or <c>null</c> when it has
        /// none. Same shape as <see cref="NebulaMapPage"/>, which takes a shell for
        /// the same reason: a page that hands off to a screen needs the stack.
        /// </summary>
        private readonly Shell.NebulaUiShell shell;

        /// <summary>The name of the control that opens the accessibility page in-session.</summary>
        public const string OpenAccessibilityControlName = "OpenAccessibility";

        private Session.NebulaMenuPageScreen accessibilityScreen;

        public NebulaSettingsPage(World.Composition.NebulaContext sessionContext = null,
            bool accessibilityFrontDoor = false, Shell.NebulaUiShell owningShell = null)
        {
            context = sessionContext;
            accessibilityOnly = accessibilityFrontDoor;
            shell = owningShell;
        }

        /// <summary>
        /// Opens ERİŞİLEBİLİRLİK as its own screen from inside a session.
        /// <para>
        /// The front door had one and the campaign did not. In-session the rows
        /// existed only as one section partway down AYARLAR, under interface scale and
        /// motion, beside graphics and audio — so a player who has just been hurt by
        /// a flash has to read past nine unrelated controls to reach Photosensitivity
        /// Safe Mode. That is the player least able to go looking, which is the whole
        /// argument for a front door.
        /// </para>
        /// <para>
        /// It pushes the same <see cref="NebulaSettingsPage"/> in the same
        /// accessibility mode the main menu builds, through the same
        /// <see cref="Session.NebulaMenuPageScreen"/> host — not a second accessibility
        /// surface that would drift out of step with the first. Returns true when the
        /// screen really opened.
        /// </para>
        /// </summary>
        public bool OpenAccessibilityPage()
        {
            if (shell == null) return false;
            if (accessibilityScreen == null)
                accessibilityScreen = Session.NebulaMenuPageScreen.Create(shell,
                    new NebulaSettingsPage(context, accessibilityFrontDoor: true), "accessibility",
                    breadcrumbRoot: "KOMUTA");
            return shell.Push(accessibilityScreen);
        }

        public void Build(RectTransform host)
        {
            // Two columns. One ran off the bottom -- the last three control rows
            // were simply unreachable -- while the right half of the page sat empty.
            // The split moved once since: graphics is on the first column now, so the
            // second has room for the audio buses and the rebinding opener together.
            RectTransform column = ScrollingColumn(host, "Settings Column",
                new Vector2(0f, 0f), new Vector2(0.5f, 1f),
                new Vector2(32f, 24f), new Vector2(-16f, -24f));

            RectTransform second = ScrollingColumn(host, "Settings Column 2",
                new Vector2(0.5f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 24f), new Vector2(-32f, -24f));

            float y = 0f;
            Section(column, "ARAYÜZ", ref y);

            // Scale is an accessibility control before it is a preference -- it is
            // the answer to "I cannot read this" -- so both doors carry it.
            AddScale(column, "Arayüz Ölçeği", ref y,
                () => NebulaUiShell.UiScale, value => { NebulaUiShell.UiScale = value; NebulaAccessibilitySettingsStore.Save(); },
                0.1f, NebulaUiShell.MinUiScale, NebulaUiShell.MaxUiScale);

            AddScale(column, "HUD Ölçeği", ref y,
                () => CombatAccessibilitySettings.HudScale,
                value => { CombatAccessibilitySettings.HudScale =
                    CombatAccessibilitySettings.ClampHudScale(value); NebulaAccessibilitySettingsStore.Save(); },
                0.05f, CombatAccessibilitySettings.MinHudScale, CombatAccessibilitySettings.MaxHudScale);

            Section(column, "HAREKET", ref y);

            AddToggle(column, "Hareketi Azalt", ref y,
                () => AnimationAccessibility.ReducedMotion,
                value => { AnimationAccessibility.ReducedMotion = value; NebulaAccessibilitySettingsStore.Save(); });

            AddToggle(column, "Hareket Kapalı", ref y,
                () => AnimationAccessibility.MotionOff,
                value => { AnimationAccessibility.MotionOff = value; NebulaAccessibilitySettingsStore.Save(); });

            AddToggle(column, "Kamera Konforu", ref y,
                () => AnimationAccessibility.CameraComfort,
                value => { AnimationAccessibility.CameraComfort = value; NebulaAccessibilitySettingsStore.Save(); });

            Section(column, "ERİŞİLEBİLİRLİK", ref y);

            // This row was bound to CombatAccessibilitySettings.ReduceFlash, which
            // nothing in Assets/Scripts reads -- grep returns the declaration, the
            // reset, and this page. The switch the engine actually honours is
            // LightingAccessibility.FlashReduction: LightingUiPalette.Apply,
            // UiAnimationProfiles.MaxRepeatHz, CombatAnimationBridge and
            // CelestialAnimationProfiles all branch on it. So the toggle moved,
            // flipped its label, and changed nothing on screen. Now it reaches the
            // setting whose name it carries.
            AddToggle(column, "Parlama Azaltma", ref y,
                () => LightingAccessibility.FlashReduction,
                value => { LightingAccessibility.FlashReduction = value; NebulaAccessibilitySettingsStore.Save(); });

            // The strongest guarantee the codebase makes -- no rapid,
            // high-amplitude flashing, overriding Flash Reduction downward and
            // stopping every repeating emphasis dead -- and it had no control at
            // all. Four runtime systems branch on it and the player could not
            // reach any of them.
            AddToggle(column, "Işık Duyarlılığı Güvenli Modu", ref y,
                () => LightingAccessibility.PhotosensitivitySafeMode,
                value => { LightingAccessibility.PhotosensitivitySafeMode = value; NebulaAccessibilitySettingsStore.Save(); });

            AddToggle(column, "Uyarılar İçin Görsel İpucu", ref y,
                () => CombatAccessibilitySettings.AudioVisualCueForWarnings,
                value => { CombatAccessibilitySettings.AudioVisualCueForWarnings = value; NebulaAccessibilitySettingsStore.Save(); });

            AddToggle(column, "Yüksek Kontrast", ref y,
                () => LightingAccessibility.HighContrast,
                value => { LightingAccessibility.HighContrast = value; NebulaAccessibilitySettingsStore.Save(); });

            // The accessibility door stops here: interface scale, motion and the
            // four accessibility switches, and none of the graphics, audio or
            // rebinding rows. Stopping is the entire difference between the two
            // pages -- every row above was built by the same call either way.
            float y2 = 0f;
            if (accessibilityOnly)
            {
                BuildStatusCueLegend(second, ref y2);
                FitColumn(column, y);
                FitColumn(second, y2);
                Refresh();
                return;
            }

            // The in-session door to the page above. Only on the full page: the
            // accessibility page must not offer a control that reopens itself.
            NebulaUiFactory.ButtonHandle door = NebulaUiFactory.ButtonControl(column,
                OpenAccessibilityControlName, "ERİŞİLEBİLİRLİK SAYFASI",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => OpenAccessibilityPage());
            NebulaUiFactory.Place(door.Button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(300f, NebulaUiDesignTokens.MinimumHitTarget));
            door.SetAvailable(shell != null, "BU EKRANDAN AÇILAMIYOR");
            y -= NebulaUiDesignTokens.MinimumHitTarget + NebulaUiDesignTokens.S2;

            // Graphics moved off the second column and onto the first. The second
            // one already ran 26px past the page with a session's six audio buses on
            // it, and the controls section has grown a rebinding opener since.
            BuildGraphics(column, ref y);

            BuildAudio(second, ref y2);
            BuildControls(second, ref y2);

            // Now that the rows have been laid out, each column knows how tall it
            // really is. Told, rather than left stretched: a stretched column reports
            // the height of the frame no matter what is in it, which is exactly how a
            // row below the fold reads as "contained" right up until a player looks.
            FitColumn(column, y);
            FitColumn(second, y2);

            // The panel is not part of either column and must not scroll with one:
            // it is centred over the whole page.
            BuildBindingPanel(host);

            Refresh();
        }

        /// <summary>
        /// One settings column, in a window that scrolls when the page is shorter
        /// than its rows.
        /// <para>
        /// The interface scale is the reason this is needed. Under
        /// <c>ScaleWithScreenSize</c> the canvas is <see cref="NebulaUiShell.ReferenceResolution"/>
        /// divided by that scale (<c>NebulaUiShell.cs:178-182</c>), so at 140% a
        /// 1920x1080 screen lays the interface out in 1371x771 units rather than
        /// 1920x1080 — the page loses a third of its height, and the last rows of both
        /// columns, the rebinding opener among them, fall below the bottom edge.
        /// Scale is the accessibility answer to "I cannot read this", so shrinking the
        /// rows to fit would remove the thing the setting was turned up for; and a
        /// control below the fold is unreachable, not merely ugly.
        /// </para>
        /// <para>
        /// The same shape, and the same components, the main menu already uses for the
        /// same defect (<c>NebulaMainMenuScreen.cs:350-395</c>) — including
        /// <see cref="Session.NebulaMenuScrollFollow"/>, because a ScrollRect does not
        /// follow keyboard or pad focus on its own and a selection scrolled out of
        /// sight is a menu the player is operating blind. When the page is tall enough
        /// the content is shorter than the window and nothing scrolls at all, so the
        /// ordinary case renders exactly as it did before.
        /// </para>
        /// </summary>
        private static RectTransform ScrollingColumn(RectTransform host, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform frame = NebulaUiFactory.Group(host, name + " Window");
            frame.anchorMin = anchorMin;
            frame.anchorMax = anchorMax;
            frame.offsetMin = offsetMin;
            frame.offsetMax = offsetMax;

            // Invisible, and a raycast target on purpose: a ScrollRect only sees a
            // wheel event that landed on a graphic, so without this the wheel would
            // work over a row and do nothing in the gaps between them.
            Image wheelTarget = NebulaUiFactory.Rect(frame, "Wheel Target", new Color(0f, 0f, 0f, 0f));
            NebulaUiFactory.Fill(wheelTarget.rectTransform);
            wheelTarget.raycastTarget = true;

            RectTransform viewport = NebulaUiFactory.Group(frame, "Viewport");
            NebulaUiFactory.Fill(viewport);
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();

            // Keeps the column's own name, so every row's path and every lookup that
            // names it is what it was before the window appeared.
            RectTransform content = NebulaUiFactory.Group(viewport, name);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var scroller = frame.gameObject.AddComponent<ScrollRect>();
            scroller.viewport = viewport;
            scroller.content = content;
            scroller.horizontal = false;
            scroller.vertical = true;
            scroller.movementType = ScrollRect.MovementType.Clamped;
            // No inertia: a list that keeps sliding after the player stopped is
            // decorative motion, which is what Reduced Motion and Motion Off exist to
            // remove. Never introduced rather than switched off twice.
            scroller.inertia = false;
            scroller.scrollSensitivity = 60f;

            Session.NebulaMenuScrollFollow.Attach(frame, scroller, content);
            return content;
        }

        /// <summary>
        /// Gives a built column the height its rows actually need. <paramref name="y"/>
        /// is the running offset the row builders left behind, and it runs downward, so
        /// the height is its negation plus the bottom margin.
        /// </summary>
        private static void FitColumn(RectTransform content, float y)
        {
            if (content == null) return;
            content.sizeDelta = new Vector2(0f, Mathf.Max(0f, -y) + NebulaUiDesignTokens.S4);
        }

        /// <summary>
        /// The rebinding surface, as a second level inside this page rather than a
        /// second screen.
        /// <para>
        /// Nine rows with a value and two controls each do not fit in a settings
        /// column beside the audio buses, and pushing a whole screen for them would
        /// put a modal over a modal. The fleet page already solved this shape with
        /// its launch-target picker: open in place, and consume Back to close —
        /// which is exactly what <see cref="INebulaMenuPage.HandleBack"/> exists for.
        /// </para>
        /// </summary>
        private void BuildBindingPanel(RectTransform host)
        {
            Image panel = NebulaUiFactory.Panel(host, BindingPanelName, NebulaUiAssetCatalog.PanelElevated);
            bindingPanel = panel.rectTransform;
            bindingPanel.anchorMin = new Vector2(0.5f, 0.5f);
            bindingPanel.anchorMax = new Vector2(0.5f, 0.5f);
            bindingPanel.pivot = new Vector2(0.5f, 0.5f);
            bindingPanel.anchoredPosition = Vector2.zero;
            bindingPanel.sizeDelta = new Vector2(900f, 640f);

            NebulaUiFactory.Place(
                NebulaUiFactory.SectionTitle(bindingPanel, "Title", "TUŞ ATAMA").Text.rectTransform,
                new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(560f, 18f));

            NebulaUiFactory.ButtonHandle close = NebulaUiFactory.ButtonControl(bindingPanel,
                "CloseBindings", "KAPAT", NebulaUiAssetCatalog.ButtonKind.Secondary, CloseBindingPanel);
            NebulaUiFactory.Place(close.Button.GetComponent<RectTransform>(), new Vector2(1f, 1f),
                new Vector2(-24f, -14f), new Vector2(140f, NebulaUiDesignTokens.MinimumHitTarget));

            RectTransform inner = NebulaUiFactory.Group(bindingPanel, "Rows");
            inner.anchorMin = new Vector2(0f, 1f);
            inner.anchorMax = new Vector2(1f, 1f);
            inner.pivot = new Vector2(0.5f, 1f);
            inner.offsetMin = new Vector2(24f, -600f);
            inner.offsetMax = new Vector2(-24f, -56f);

            float y = 0f;
            bindings.Build(inner, ref y);

            bindingPanel.gameObject.SetActive(false);
        }

        /// <summary>Opens the rebinding panel. Idempotent.</summary>
        public void OpenBindingPanel()
        {
            if (bindingPanel == null) return;
            bindingPanel.gameObject.SetActive(true);
            bindings.Refresh();
        }

        /// <summary>Closes it, abandoning any capture that was still armed.</summary>
        public void CloseBindingPanel()
        {
            if (bindingPanel == null) return;
            bindings.CancelCapture();
            bindingPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Volume, per bus. The service already mixes master x bus x clip, so these
        /// are the two the player actually has an opinion about; a row per bus is
        /// what lets someone keep combat loud and industry quiet.
        /// </summary>
        private void BuildAudio(RectTransform column, ref float y)
        {
            if (context == null) return;

            // Resolved per read, never at build time: presentation is composed
            // before audio, and asking for the service here threw during
            // composition and took the entire session down with it.
            Section(column, "SES", ref y);

            AddScale(column, "Ana Ses", ref y,
                () => Mixer()?.MasterVolume ?? 0f,
                value => Mixer()?.SetMasterVolume(value), 0.05f, 0f, 1f);

            foreach (Audio.NebulaAudioBus bus in System.Enum.GetValues(typeof(Audio.NebulaAudioBus)))
            {
                Audio.NebulaAudioBus captured = bus;
                AddScale(column, BusName(bus), ref y,
                    () => Mixer()?.GetBusVolume(captured) ?? 0f,
                    value => Mixer()?.SetBusVolume(captured, value), 0.05f, 0f, 1f);
            }
        }

        private Audio.NebulaAudioService Mixer() => context?.AudioServiceOrNull;

        private static string BusName(Audio.NebulaAudioBus bus) => bus switch
        {
            Audio.NebulaAudioBus.UI => "Arayüz",
            Audio.NebulaAudioBus.Ship => "Gemi",
            Audio.NebulaAudioBus.Combat => "Savaş",
            Audio.NebulaAudioBus.Industry => "Sanayi",
            Audio.NebulaAudioBus.World => "Dünya",
            _ => "Ortam"
        };

        /// <summary>
        /// Quality and vertical sync, which are Unity's own and take effect at once.
        /// Resolution is deliberately absent: it belongs to the launcher, and a
        /// half-working resolution picker is worse than none.
        /// </summary>
        private void BuildGraphics(RectTransform column, ref float y)
        {
            Section(column, "GRAFİK", ref y);

            AddScale(column, "Kalite", ref y,
                () => QualitySettings.GetQualityLevel(),
                value => QualitySettings.SetQualityLevel(
                    Mathf.Clamp(Mathf.RoundToInt(value), 0, QualitySettings.names.Length - 1), true),
                1f, 0f, Mathf.Max(0, QualitySettings.names.Length - 1),
                // The level's own name, not "%500": a quality level is an index into
                // Unity's list, and printing it as a percentage was nonsense.
                level =>
                {
                    string[] names = QualitySettings.names;
                    int index = Mathf.Clamp(Mathf.RoundToInt(level), 0, names.Length - 1);
                    return names.Length == 0 ? "—" : NebulaText.Upper(names[index]);
                });

            AddToggle(column, "Dikey Eşitleme", ref y,
                () => QualitySettings.vSyncCount > 0,
                value => QualitySettings.vSyncCount = value ? 1 : 0);
        }

        /// <summary>
        /// What the rebinding surface is currently warning about, forwarded so a test
        /// can read it from the page. The warning itself lives on the rebinding panel
        /// rather than in this column: that is where a player who could have caused a
        /// conflict is standing, and this column has no room — it already ran past the
        /// bottom of the page once.
        /// </summary>
        public string ConflictText => bindings.ConflictText;

        /// <summary>
        /// The bindings, read from the action catalog.
        /// <para>
        /// L-03 moved ShipHud's 137-character pinned hint string here, and this page
        /// then re-stated eight keys as its own <c>KeyCode.ToString()</c> calls beside
        /// the Turkish label it invented for each. That was a fourth copy of the
        /// binding table — after the owners, the reserved-key array and the shipping
        /// documents — and it was already wrong in one place: it labelled the build-mode
        /// key "Yörünge Sabitle", which is only half of what B does.
        /// </para>
        /// <para>
        /// Now the rows come from <see cref="NebulaInputActions"/>: its label, its key,
        /// its own decision about which rows a player wants stated. Debug-only actions
        /// are filtered on <c>Debug.isDebugBuild</c>, so a release screen cannot even
        /// name a key whose panel that build never constructs.
        /// </para>
        /// </summary>
        private void BuildControls(RectTransform column, ref float y)
        {
            Section(column, "KONTROLLER", ref y);

            foreach (InputActionDefinition action in NebulaInputActions.PlayerVisible(Debug.isDebugBuild))
            {
                if (!action.Featured || action.Rebindable) continue;
                AddReading(column, action.LabelTr, ref y,
                    Controls.NebulaControlBindings.KeyLabel(action.Key));
            }

            // The nine combat actions are the ones that are genuinely reassignable:
            // CombatBindings has carried TrySetBinding, CurrentBindings and
            // ResetToDefaults since the combat slice, and nothing in the interface
            // reached any of them. Everything above this line is still a reading,
            // because those keys really are fixed owners.
            NebulaUiFactory.ButtonHandle open = NebulaUiFactory.ButtonControl(column,
                OpenBindingsControlName, "SAVAŞ TUŞLARINI ATA",
                NebulaUiAssetCatalog.ButtonKind.Secondary, OpenBindingPanel);
            NebulaUiFactory.Place(open.Button.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(280f, NebulaUiDesignTokens.MinimumHitTarget));
            y -= NebulaUiDesignTokens.MinimumHitTarget + NebulaUiDesignTokens.S2;
        }


        /// <summary>The name the status legend's root carries, so a test can find it on screen.</summary>
        public const string StatusCueLegendName = "Status Cue Legend";

        /// <summary>Test-facing: how many marks the legend is currently drawing.</summary>
        public int VisibleStatusCueMarks => legend?.Legend?.VisibleMarks ?? 0;

        private CueLegendRow legend;

        /// <summary>
        /// Puts the status legend on the accessibility page.
        /// <para>
        /// <see cref="Accessibility.UiStatusCueStrip"/> documented itself as the
        /// surface the Visual Cue switch is demonstrated on and had <b>no production
        /// caller at all</b> — every reference to it was a test, so the panel the
        /// player was supposed to learn the five marks from did not exist in the game.
        /// This is that caller, on the page whose switch it demonstrates, so flipping
        /// the switch changes something the player is looking at.
        /// </para>
        /// </summary>
        /// <summary>
        /// <paramref name="y"/> is carried in and out rather than started at zero
        /// here: the column it lands in now sizes itself from that running offset, and
        /// a section that consumed height without saying so would be sized away to
        /// nothing behind the window's mask.
        /// </summary>
        private void BuildStatusCueLegend(RectTransform host, ref float y)
        {
            Section(host, "DURUM İŞARETLERİ", ref y);

            NebulaUiFactory.LabelHandle note = NebulaUiFactory.Label(host, "CueNote",
                "Her durum renk, şekil ve sözcükle birlikte gösterilir. "
                + "Görsel ipucu kapatılırsa yalnızca şekil ve sözcük kalkar; renk ve metin aynı kalır.",
                NebulaUiDesignTokens.SizeCaption, NebulaUiDesignTokens.InkMuted,
                TextAnchor.UpperLeft, FontStyle.Normal, 0f, wrap: true);
            NebulaUiFactory.Place(note.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, y), new Vector2(470f, 44f));
            y -= 52f;

            Accessibility.UiStatusCueStrip.Handle handle =
                Accessibility.UiStatusCueStrip.Build(host, StatusCueLegendName);
            handle.Root.anchorMin = handle.Root.anchorMax = new Vector2(0f, 1f);
            handle.Root.pivot = new Vector2(0f, 1f);
            handle.Root.anchoredPosition = new Vector2(0f, y);

            y -= handle.Root.sizeDelta.y + NebulaUiDesignTokens.S2;

            legend = new CueLegendRow { Legend = handle };
            rows.Add(legend);
        }

        private static void Section(RectTransform parent, string title, ref float y)
        {
            NebulaUiFactory.LabelHandle heading = NebulaUiFactory.SectionTitle(parent, title, title);
            NebulaUiFactory.Place(heading.Text.rectTransform, new Vector2(0f, 1f),
                new Vector2(0f, y - 6f), new Vector2(400f, 18f));
            y -= 34f;
        }

        private void AddToggle(RectTransform parent, string label, ref float y,
            Func<bool> get, Action<bool> set)
        {
            var row = new ToggleRow { Get = get, Set = set };
            BuildRow(parent, label, ref y, row, () => row.Flip());
            rows.Add(row);
        }

        private void AddReading(RectTransform parent, string label, ref float y, string reading)
        {
            var row = new ReadingRow { Text = reading };
            BuildRow(parent, label, ref y, row, null);
            rows.Add(row);
        }

        private void AddScale(RectTransform parent, string label, ref float y,
            Func<float> get, Action<float> set, float step, float min, float max,
            Func<float, string> format = null)
        {
            var row = new ScaleRow { Get = get, Set = set, Step = step, Min = min, Max = max, Format = format };
            RectTransform host = BuildRow(parent, label, ref y, row, null);

            // MinimumHitTarget, not 30. The design system states 32 as the floor
            // in either axis and this page was the one place that undercut it --
            // and it undercuts it worst exactly where it matters, because at
            // 1280x720 the scaler takes a 30px control down to 20 real pixels.
            NebulaUiFactory.ButtonHandle down = NebulaUiFactory.ButtonControl(host, "Down", "−",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => { row.Nudge(-1); row.Refresh(); });
            NebulaUiFactory.Place(down.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-118f, 0f), new Vector2(36f, NebulaUiDesignTokens.MinimumHitTarget));

            NebulaUiFactory.ButtonHandle up = NebulaUiFactory.ButtonControl(host, "Up", "+",
                NebulaUiAssetCatalog.ButtonKind.Secondary, () => { row.Nudge(1); row.Refresh(); });
            NebulaUiFactory.Place(up.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                new Vector2(-18f, 0f), new Vector2(36f, NebulaUiDesignTokens.MinimumHitTarget));

            rows.Add(row);
        }

        /// <summary>
        /// Repaints the whole page after a control was used, not just the row that was
        /// clicked.
        /// <para>
        /// Several of these switches change something outside their own row: High
        /// Contrast moves the whole status palette, and Visual Cue strips the mark
        /// channel off every cue on the page including the legend. Refreshing only the
        /// clicked row left the legend showing the marks it happened to be built with,
        /// so the one panel that exists to demonstrate the switch was the one panel
        /// that did not answer it.
        /// </para>
        /// </summary>
        private RectTransform BuildRow(RectTransform parent, string label, ref float y,
            Row row, UnityEngine.Events.UnityAction onActivate)
        {
            Image panel = NebulaUiFactory.Panel(parent, label,
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Default));
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, NebulaUiDesignTokens.RowHeight);

            NebulaUiFactory.Place(NebulaUiFactory.Label(rect, "Label", label,
                    NebulaUiDesignTokens.SizeBody, NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft)
                .Text.rectTransform, new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(360f, 20f));

            row.Value = NebulaUiFactory.Value(rect, "Value", "", NebulaUiDesignTokens.SizeCompact);
            NebulaUiFactory.Place(row.Value.Text.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(-70f, 0f), new Vector2(140f, 18f));

            if (onActivate != null)
            {
                NebulaUiFactory.ButtonHandle toggle = NebulaUiFactory.ButtonControl(rect, "Toggle", "DEĞİŞTİR",
                    NebulaUiAssetCatalog.ButtonKind.Secondary, () => { onActivate(); Refresh(); });
                NebulaUiFactory.Place(toggle.Button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f),
                    new Vector2(-18f, 0f), new Vector2(96f, NebulaUiDesignTokens.MinimumHitTarget));
                NebulaUiFactory.Place(row.Value.Text.rectTransform, new Vector2(1f, 0.5f),
                    new Vector2(-124f, 0f), new Vector2(140f, 18f));
            }

            y -= NebulaUiDesignTokens.RowHeight + NebulaUiDesignTokens.S2;
            return rect;
        }

        public void Refresh()
        {
            for (int i = 0; i < rows.Count; i++) rows[i].Refresh();

            // Read back from the binding service on every refresh, not only when
            // this page changed something: a binding altered anywhere else -- a
            // load, a restart, another surface -- must not leave a stale key on
            // screen claiming to be the one the game reads.
            if (BindingPanelOpen) bindings.Refresh();
        }

        /// <summary>
        /// Back unwinds exactly one level per press, and only falls through to the
        /// shell when nothing is open here.
        /// <para>
        /// An armed capture is a level of its own. It used to share Escape with the
        /// shell — <c>NebulaKeyCaptureListener</c> read the key itself and cancelled,
        /// and the shell read the same press and popped — so one Escape abandoned the
        /// capture <i>and</i> shut the whole rebinding panel. The listener no longer
        /// sees Escape at all; this is where the press lands, and it stops at the
        /// innermost thing that is open.
        /// </para>
        /// </summary>
        public bool HandleBack()
        {
            if (!BindingPanelOpen) return false;

            if (bindings.CapturingActionId != null)
            {
                bindings.CancelCapture();
                return true;
            }

            CloseBindingPanel();
            return true;
        }
    }
}
