using System.Collections.Generic;
using UnityEngine;
using Nebula.Audio;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.UI.Accessibility;
using Nebula.UI.Controls;
using Nebula.UI.Design;
using Nebula.UI.Hud;
using Nebula.UI.Menu;
using Nebula.UI.Session;
using Nebula.UI.Shell;
using Nebula.Visuals;
using Nebula.World.Composition;

namespace Nebula.Tutorial.Steps
{
    /// <summary>
    /// Chapter K — Oturum ve Ayarlar. Curriculum steps K-01…K-12 (global 164–175).
    /// <para>
    /// Two rules shape every step in this file.
    /// </para>
    /// <para>
    /// <b>Nothing here may write a save.</b> The session page is always constructed
    /// with a null <c>NebulaContext</c>, which is what makes KAYDET refuse with
    /// production's own <c>"OTURUM YOK"</c>, and every screen that wants an
    /// <c>INebulaSessionLauncher</c> gets <see cref="TutorialRecordingLauncher"/> --
    /// never <c>NebulaSessionLauncher</c>, which mints a campaign id.
    /// </para>
    /// <para>
    /// <b>Nothing in this project persists a setting.</b> There is no
    /// <c>PlayerPrefs</c> call anywhere under <c>Assets/</c>, and every switch these
    /// steps touch is a process-wide static with only a <c>ResetToDefaults</c>. So
    /// each step captures its value in <c>Prepare</c> and restores it through
    /// <see cref="ITutorialStepScope.OnCleanup"/>; nothing else ever would, and a
    /// leaked change would follow the player into their campaign for the rest of
    /// the process. No step here introduces a <c>PlayerPrefs</c> write of its own.
    /// </para>
    /// </summary>
    public static class ChapterKSteps
    {
        public static IEnumerable<TutorialStepDefinition> Build()
        {
            yield return ReadTheSaveSlots();
            yield return ContinueVersusLoad();
            yield return ReturningToTheMainMenuAsksFirst();
            yield return GraphicsQuality();
            yield return AudioBuses();
            yield return InterfaceAndHudScale();
            yield return ReducedMotion();
            yield return FlashReduction();
            yield return HighContrast();
            yield return VisualCueForWarnings();
            yield return RebindACombatAction();
            yield return ReservedKeysCannotBeTaken();
        }

        // ---- K-01 -------------------------------------------------------------

        private static TutorialStepDefinition ReadTheSaveSlots() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-01", "Kayıt Yuvaları",
            "Komuta menüsünde Oturum sayfasını aç ve üç yuvanın ne dediğini oku — burada hiçbir şey kaydetmeyeceğiz.",
            "Kayıt yuvalarını, hiçbir şey yazmadan tanı.",
            () =>
            {
                NebulaParentMenuScreen menu = null;
                NebulaSessionPage page = null;

                return TutorialSteps.Poll(
                    complete: _ => menu != null && page != null
                        && menu.CurrentPage == NebulaUiAssetCatalog.Page.Session
                        && page.SlotCount == NebulaSaveSlotQuery.SlotCount,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        NebulaConfirmationDialog dialog = TutorialSettings.NewDialog(ctx, shell);

                        // A null session context is the structural guarantee: DoSave
                        // reads context?.ExtractionDirector, so with no context there
                        // is no director to write through, and KAYDET is refused with
                        // production's own "OTURUM YOK".
                        page = new NebulaSessionPage(null, dialog, null, shell);
                        menu = TutorialSettings.NewMenu(ctx, shell, page);
                    });
            },
            keyHint: () => NebulaControlBindings.KeyLabel(NebulaParentMenuScreen.ProgressionKey));

        // ---- K-02 -------------------------------------------------------------

        private static TutorialStepDefinition ContinueVersusLoad() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-02", "Devam Et ve Kayıt Yükle",
            "Ana menüde \"Devam Et\"in tek bir kaydı sürdürdüğünü, \"Kayıt Yükle\"nin ise yuvayı seçtirdiğini gör.",
            "Devam Et ile Kayıt Yükle farkını gör.",
            () =>
            {
                NebulaUiShell shell = null;
                NebulaMainMenuScreen mainMenu = null;
                bool sawLoadScreen = false;

                return TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (shell == null || mainMenu == null) return false;
                        if (shell.Top != null && shell.Top.ScreenId == "session.load") sawLoadScreen = true;
                        if (!sawLoadScreen) return false;

                        SaveSlotSummary recent = NebulaSaveSlotQuery.MostRecent();
                        return recent != null
                            ? mainMenu.ResumeSlot == recent.Slot
                            : mainMenu.ResumeSlot == -1 && !string.IsNullOrEmpty(mainMenu.ContinueBlockedReason);
                    },
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        ExclusiveModal.ResetForSession();

                        // Registered before the shell, so it runs after it: a load
                        // screen must not be destroyed while the shell stack still
                        // holds it and is about to call OnClosed on it.
                        var spawned = new List<GameObject>();
                        ctx.Scope.OnCleanup(() =>
                        {
                            for (int i = 0; i < spawned.Count; i++) TutorialAbk.DestroyNow(spawned[i]);
                        });

                        shell = TutorialAbk.NewShell(ctx);
                        var launcher = new TutorialRecordingLauncher();

                        // Never NebulaMainMenuHost: its Build() constructs a real
                        // NebulaSessionLauncher over the live bootstrap, whose
                        // StartNewCampaign/RestartCampaign mint a campaign id.
                        mainMenu = NebulaMainMenuScreen.Create(shell, launcher);
                        NebulaUiShell owningShell = shell;
                        mainMenu.LoadRequested += () =>
                        {
                            NebulaSaveSlotScreen load = NebulaSaveSlotScreen.Create(owningShell, launcher);
                            spawned.Add(load.gameObject);
                            owningShell.Push(load);
                        };
                        shell.Push(mainMenu);

                        NebulaMainMenuScreen ownedMenu = mainMenu;
                        ctx.Scope.OnCleanup(() =>
                        {
                            owningShell?.PopAll();
                            if (ownedMenu != null) TutorialAbk.DestroyNow(ownedMenu.gameObject);
                        });
                    },
                    reject: _ => mainMenu?.ContinueBlockedReason,
                    objectives: (_, report) =>
                    {
                        report.SetObjective(0, sawLoadScreen);
                        report.SetObjective(1, mainMenu != null &&
                            (mainMenu.ResumeSlot >= 0 || !string.IsNullOrEmpty(mainMenu.ContinueBlockedReason)));
                    });
            },
            checklistTr: new[] { "Kayıt Yükle ekranını aç", "Devam Et'in hangi yuvayı sürdüreceğini oku" });

        // ---- K-03 -------------------------------------------------------------

        private static TutorialStepDefinition ReturningToTheMainMenuAsksFirst() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-03", "Ana Menüye Dönüş",
            "Oturum sayfasında \"Ana Menüye Dön\"e bas ve çıkan onay sorusunu oku — sonra vazgeç.",
            "Geri dönülemez çıkışın onay kapısını gör.",
            () =>
            {
                NebulaUiShell shell = null;
                NebulaConfirmationDialog dialog = null;
                int cancelledAtStart = 0, confirmedAtStart = 0;
                bool asked = false, cancelled = false;

                return TutorialSteps.Poll(
                    complete: _ => asked && cancelled,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        shell = TutorialAbk.NewShell(ctx);
                        dialog = TutorialSettings.NewDialog(ctx, shell);
                        cancelledAtStart = dialog.CancelledCount;
                        confirmedAtStart = dialog.ConfirmedCount;

                        var page = new NebulaSessionPage(null, dialog, new TutorialRecordingLauncher(), shell);
                        NebulaParentMenuScreen menu = TutorialSettings.NewMenu(ctx, shell, page);
                        menu.Open(NebulaUiAssetCatalog.Page.Session);
                    },
                    objectives: (_, report) =>
                    {
                        if (dialog != null && shell != null)
                        {
                            if (!asked) asked = dialog.IsOpen && shell.StackDepth == 2;
                            else if (!cancelled)
                                cancelled = dialog.CancelledCount > cancelledAtStart
                                            && dialog.ConfirmedCount == confirmedAtStart;
                        }
                        report.SetObjective(0, asked);
                        report.SetObjective(1, cancelled);
                    });
            },
            checklistTr: new[] { "Onay penceresini aç", "Vazgeçip oyuna dön" });

        // ---- K-04 -------------------------------------------------------------

        private static TutorialStepDefinition GraphicsQuality() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-04", "Grafik Kalitesi",
            "Ayarlar sayfasında Kalite seviyesini bir kademe değiştir.",
            "Kalite ve dikey eşitlemeyi tanı.",
            () =>
            {
                int startLevel = -1;

                return TutorialSteps.Poll(
                    complete: _ => startLevel >= 0 && QualitySettings.GetQualityLevel() != startLevel,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        startLevel = QualitySettings.GetQualityLevel();
                        int startVSync = QualitySettings.vSyncCount;
                        ctx.Scope.OnCleanup(() =>
                        {
                            QualitySettings.SetQualityLevel(startLevel, true);
                            QualitySettings.vSyncCount = startVSync;
                        });
                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: false, pageContext: null);
                    },
                    // Real state, and the only honest thing to say on a build with a
                    // single quality level: the row clamps and never refuses.
                    reject: _ => QualitySettings.names.Length < 2
                        ? "BU YAPIDA TEK KALİTE SEVİYESİ VAR"
                        : null);
            });

        // ---- K-05 -------------------------------------------------------------

        private static TutorialStepDefinition AudioBuses() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-05", "Ses Seviyeleri",
            "Ana sesi ya da tek bir ses kanalını değiştir.",
            "Ana ses ve kanal seviyelerini tanı.",
            () =>
            {
                NebulaAudioService mixer = null;
                float masterAtStart = 1f;
                TutorialAudioBusSnapshot busesAtStart = default;

                return TutorialSteps.Poll(
                    complete: _ =>
                    {
                        if (mixer == null) return false;
                        if (Mathf.Abs(mixer.MasterVolume - masterAtStart) >= 0.049f) return true;
                        return busesAtStart.AnyDiffers(mixer);
                    },
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);

                        // A tutorial-owned mixer, not the campaign's: the tutorial
                        // composition deliberately never sets NebulaContext.AudioService,
                        // and this one dies with the step scope.
                        var host = new GameObject("Tutorial Audio Mixer");
                        ctx.Scope.Track(host);
                        mixer = host.AddComponent<NebulaAudioService>();

                        masterAtStart = mixer.MasterVolume;
                        busesAtStart = TutorialAudioBusSnapshot.Capture(mixer);
                        NebulaAudioService owned = mixer;
                        TutorialAudioBusSnapshot restore = busesAtStart;
                        float restoreMaster = masterAtStart;
                        ctx.Scope.OnCleanup(() =>
                        {
                            if (owned == null) return;
                            owned.SetMasterVolume(restoreMaster);
                            restore.RestoreTo(owned);
                        });

                        var pageContext = new NebulaContext();
                        pageContext.SetAudioService(mixer);
                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: false, pageContext: pageContext);
                    });
            });

        // ---- K-06 -------------------------------------------------------------

        private static TutorialStepDefinition InterfaceAndHudScale() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-06", "Arayüz ve HUD Ölçeği",
            "HUD ölçeğini büyüt ve göstergelerin gerçekten büyüdüğünü gör.",
            "Arayüz ve HUD ölçeğini ayarla.",
            () =>
            {
                float composedAtStart = float.NaN;

                return TutorialSteps.Poll(
                    // The composed number the HUD canvases actually render at, so a
                    // setting that moved without reaching a canvas cannot satisfy it.
                    complete: _ => !float.IsNaN(composedAtStart)
                        && !Mathf.Approximately(NebulaUiShell.ScaleFor(UiLayer.HudDynamic), composedAtStart),
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        float uiScale = NebulaUiShell.UiScale;
                        float hudScale = CombatAccessibilitySettings.HudScale;
                        ctx.Scope.OnCleanup(() =>
                        {
                            NebulaUiShell.UiScale = uiScale;
                            CombatAccessibilitySettings.HudScale = hudScale;
                        });

                        NebulaUiShell shell = TutorialAbk.NewShell(ctx);
                        NebulaHudContextBar.Create(shell); // a real HUD widget to grow
                        TutorialSettings.OpenSettingsPage(ctx, shell, accessibilityFrontDoor: false, pageContext: null);
                        composedAtStart = NebulaUiShell.ScaleFor(UiLayer.HudDynamic);
                    });
            });

        // ---- K-07 -------------------------------------------------------------

        private static TutorialStepDefinition ReducedMotion() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-07", "Hareketi Azalt ve Hareket Kapalı",
            "\"Hareketi Azalt\" ya da \"Hareket Kapalı\"yı aç ve süslemelerin durduğunu gör.",
            "Hareket erişilebilirlik anahtarlarını tanı.",
            () =>
            {
                float motionScaleAtStart = 1f;

                return TutorialSteps.Poll(
                    complete: _ => !Mathf.Approximately(AnimationAccessibility.MotionScale, motionScaleAtStart),
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        bool reduced = AnimationAccessibility.ReducedMotion;
                        bool off = AnimationAccessibility.MotionOff;
                        bool comfort = AnimationAccessibility.CameraComfort;
                        ctx.Scope.OnCleanup(() =>
                        {
                            AnimationAccessibility.ReducedMotion = reduced;
                            AnimationAccessibility.MotionOff = off;
                            AnimationAccessibility.CameraComfort = comfort;
                        });

                        // Cleared so the step is genuinely open, then measured.
                        AnimationAccessibility.ReducedMotion = false;
                        AnimationAccessibility.MotionOff = false;
                        motionScaleAtStart = AnimationAccessibility.MotionScale;

                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: true, pageContext: null);
                    });
            });

        // ---- K-08 -------------------------------------------------------------

        private static TutorialStepDefinition FlashReduction() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-08", "Parlama Azaltma ve Işık Duyarlılığı",
            "Parlama azaltmayı, gerekiyorsa ışık duyarlılığı güvenli modunu aç.",
            "Parlama ve ışık duyarlılığı anahtarlarını tanı.",
            () =>
            {
                float flashAtStart = 1f;

                return TutorialSteps.Poll(
                    complete: _ => LightingAccessibility.ScaleFlashIntensity(1f) < flashAtStart - 0.0001f,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        bool reduction = LightingAccessibility.FlashReduction;
                        bool photosensitive = LightingAccessibility.PhotosensitivitySafeMode;
                        ctx.Scope.OnCleanup(() =>
                        {
                            LightingAccessibility.FlashReduction = reduction;
                            LightingAccessibility.PhotosensitivitySafeMode = photosensitive;
                        });

                        LightingAccessibility.FlashReduction = false;
                        LightingAccessibility.PhotosensitivitySafeMode = false;
                        flashAtStart = LightingAccessibility.ScaleFlashIntensity(1f);

                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: true, pageContext: null);
                    });
            });

        // ---- K-09 -------------------------------------------------------------

        private static TutorialStepDefinition HighContrast() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-09", "Yüksek Kontrast",
            "Yüksek kontrastı aç; arayüz renklerinin değiştiğini gör.",
            "Yüksek kontrast paletini tanı.",
            () =>
            {
                Color inkAtStart = Color.clear;

                return TutorialSteps.Poll(
                    complete: _ => NebulaUiDesignTokens.HighContrast
                        && NebulaUiDesignTokens.InkPrimary != inkAtStart,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        bool contrast = LightingAccessibility.HighContrast;
                        ctx.Scope.OnCleanup(() => LightingAccessibility.HighContrast = contrast);

                        LightingAccessibility.HighContrast = false;
                        inkAtStart = NebulaUiDesignTokens.InkPrimary;

                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: true, pageContext: null);
                    });
            });

        // ---- K-10 -------------------------------------------------------------

        private static TutorialStepDefinition VisualCueForWarnings() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-10", "Uyarılar İçin Görsel İpucu",
            "Uyarı işaretlerini aç; kritik uyarıların renkten bağımsız bir şekil işareti kazandığını gör.",
            "Renkten bağımsız uyarı işaretini tanı.",
            () =>
            {
                return TutorialSteps.Poll(
                    // The derived cue, not the flag it came from.
                    complete: _ => UiStatusCue.For(UiStatusLevel.Critical).HasMark,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        bool cue = CombatAccessibilitySettings.AudioVisualCueForWarnings;
                        ctx.Scope.OnCleanup(() => CombatAccessibilitySettings.AudioVisualCueForWarnings = cue);

                        CombatAccessibilitySettings.AudioVisualCueForWarnings = false;
                        TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: true, pageContext: null);
                    });
            });

        // ---- K-11 -------------------------------------------------------------

        private static TutorialStepDefinition RebindACombatAction() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-11", "Savaş Tuşunu Ata",
            "\"Savaş Tuşlarını Ata\"yı aç, bir eylemi seç ve yeni bir tuşa bas.",
            "Bir savaş eylemini yeni bir tuşa ata.",
            () =>
            {
                NebulaSettingsPage page = null;
                TutorialBindingWatch watch = null;
                bool panelWasOpen = false;

                return TutorialSteps.Poll(
                    complete: _ => panelWasOpen && watch != null && watch.HasResult
                        && watch.LastResult == ControlRebindResult.Accepted
                        && watch.AnyBindingMoved(),
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        // The rebinding panel is only built past the accessibility
                        // cutoff, so this door has to be the full one.
                        page = TutorialSettings.OpenSettingsPage(ctx, accessibilityFrontDoor: false, pageContext: null);
                        watch = TutorialBindingWatch.Install(ctx, page);
                    },
                    // Production's own Turkish, from the real result enum.
                    reject: _ => watch != null && watch.HasResult
                                 && watch.LastResult != ControlRebindResult.Accepted
                        ? NebulaControlBindings.ResultText(watch.LastResult, watch.LastAction, watch.LastKey)
                        : null,
                    objectives: (_, report) =>
                    {
                        if (page != null && page.BindingPanelOpen) panelWasOpen = true;
                        report.SetObjective(0, panelWasOpen);
                        report.SetObjective(1, watch != null && watch.AnyBindingMoved());
                    });
            },
            checklistTr: new[] { "Atama panelini aç", "Yeni tuşu ata" });

        // ---- K-12 -------------------------------------------------------------

        private static TutorialStepDefinition ReservedKeysCannotBeTaken() => new TutorialStepDefinition(
            TutorialChapterId.Session, "K-12", "Ayrılmış Tuşlar",
            "Atama beklerken uçuş tuşlarından birine bas ve neden reddedildiğini oku.",
            "Ayrılmış tuşların neden alınamadığını gör.",
            () =>
            {
                TutorialBindingWatch watch = null;
                CombatAction armed = CombatAction.CombatBoost;

                return TutorialSteps.Poll(
                    complete: _ => watch != null && watch.HasResult
                        && watch.LastResult == ControlRebindResult.ReservedBySystem
                        && !watch.HasMoved(armed)
                        && watch.Section != null && watch.Section.Capturing == armed,
                    prepare: ctx =>
                    {
                        TutorialSettings.OpenMenuInput(ctx);
                        NebulaSettingsPage page = TutorialSettings.OpenSettingsPage(
                            ctx, accessibilityFrontDoor: false, pageContext: null);
                        watch = TutorialBindingWatch.Install(ctx, page);

                        page.OpenBindingPanel();
                        // Opens already armed: the step is about what happens to the
                        // press, not about arming the row.
                        page.Bindings.BeginCapture(armed);
                    },
                    reject: _ => watch != null && watch.HasResult
                        ? NebulaControlBindings.ResultText(watch.LastResult, watch.LastAction, watch.LastKey)
                        : null);
            });
    }

    /// <summary>
    /// An <see cref="INebulaSessionLauncher"/> that records and never acts.
    /// <para>
    /// The real <c>NebulaSessionLauncher</c> mints a campaign id on
    /// <c>StartNewCampaign</c>/<c>RestartCampaign</c> and disposes the world on
    /// <c>ReturnToMainMenu</c>. A tutorial screen that offered any of those for
    /// real would end the tutorial, or start a campaign the player never asked
    /// for, from inside a lesson about menus.
    /// </para>
    /// </summary>
    internal sealed class TutorialRecordingLauncher : INebulaSessionLauncher
    {
        internal int SessionsStarted { get; private set; }
        internal int RestartsRequested { get; private set; }
        internal int QuitsRequested { get; private set; }
        internal int ReturnsRequested { get; private set; }
        internal int LoadedSlot { get; private set; } = -1;

        public void StartNewCampaign() => SessionsStarted++;
        public void ContinueFromSlot(int slot) { SessionsStarted++; LoadedSlot = slot; }
        public void QuitGame() => QuitsRequested++;
        public void ReturnToMainMenu() => ReturnsRequested++;
        public void RestartCampaign() => RestartsRequested++;
    }

    /// <summary>Every audio bus volume at one moment, so a step can put them all back.</summary>
    internal readonly struct TutorialAudioBusSnapshot
    {
        private readonly float[] volumes;

        private TutorialAudioBusSnapshot(float[] captured) { volumes = captured; }

        internal static TutorialAudioBusSnapshot Capture(NebulaAudioService mixer)
        {
            var values = new float[Buses.Length];
            for (int i = 0; i < Buses.Length; i++) values[i] = mixer.GetBusVolume(Buses[i]);
            return new TutorialAudioBusSnapshot(values);
        }

        internal bool AnyDiffers(NebulaAudioService mixer)
        {
            if (volumes == null) return false;
            for (int i = 0; i < Buses.Length; i++)
                if (Mathf.Abs(mixer.GetBusVolume(Buses[i]) - volumes[i]) >= 0.049f) return true;
            return false;
        }

        internal void RestoreTo(NebulaAudioService mixer)
        {
            if (volumes == null) return;
            for (int i = 0; i < Buses.Length; i++) mixer.SetBusVolume(Buses[i], volumes[i]);
        }

        private static readonly NebulaAudioBus[] Buses =
        {
            NebulaAudioBus.UI, NebulaAudioBus.Ship, NebulaAudioBus.Combat,
            NebulaAudioBus.Industry, NebulaAudioBus.World, NebulaAudioBus.Ambience
        };
    }

    /// <summary>
    /// Watches the real rebinding command without becoming a second one.
    /// <para>
    /// <c>NebulaControlBindingSection.Apply</c> returns the real
    /// <see cref="ControlRebindResult"/> and stores it nowhere, so a refusal leaves
    /// no state at all behind -- which is correct for production and leaves a
    /// tutorial step with nothing honest to observe. This re-points the listener's
    /// captured callback at the section's own <c>Apply</c>, wrapped so the result it
    /// really returned is recorded. It reads no input and decides nothing: the key
    /// still arrives from the same source, and every rule about what may be bound
    /// still runs inside <c>Apply</c>.
    /// </para>
    /// </summary>
    internal sealed class TutorialBindingWatch
    {
        internal NebulaControlBindingSection Section { get; private set; }
        /// <summary>False until a real <c>Apply</c> has returned. The enum has no "nothing happened" member, and inventing one here would read as a refusal that never occurred.</summary>
        internal bool HasResult { get; private set; }

        internal ControlRebindResult LastResult { get; private set; }
        internal CombatAction LastAction { get; private set; }
        internal KeyCode LastKey { get; private set; }

        private readonly Dictionary<CombatAction, KeyCode> before = new Dictionary<CombatAction, KeyCode>();

        internal static TutorialBindingWatch Install(TutorialStepContext context, NebulaSettingsPage page)
        {
            var watch = new TutorialBindingWatch { Section = page?.Bindings };
            if (watch.Section == null) return watch;

            IReadOnlyList<CombatAction> actions = NebulaControlBindings.Actions;
            for (int i = 0; i < actions.Count; i++)
                watch.before[actions[i]] = NebulaControlBindings.KeyFor(actions[i]);

            NebulaControlBindingSection section = watch.Section;
            NebulaKeyCaptureListener listener = section.Listener;
            if (listener != null)
            {
                listener.Initialize(key =>
                {
                    CombatAction? armedAction = section.Capturing;
                    ControlRebindResult result = section.Apply(key);
                    watch.LastResult = result;
                    watch.HasResult = true;
                    watch.LastKey = key;
                    if (armedAction.HasValue) watch.LastAction = armedAction.Value;
                    return result;
                }, section.CancelCapture);

                context.Scope.OnCleanup(() =>
                {
                    if (listener != null) listener.Initialize(section.Apply, section.CancelCapture);
                });
            }

            var restore = new Dictionary<CombatAction, KeyCode>(watch.before);
            context.Scope.OnCleanup(() =>
            {
                section.CancelCapture();
                // A test may have substituted the key source; the player's keyboard
                // must always be what a tutorial run leaves behind.
                NebulaKeyCaptureListener.ResetSourceToKeyboard();
                foreach (KeyValuePair<CombatAction, KeyCode> entry in restore)
                    if (NebulaControlBindings.KeyFor(entry.Key) != entry.Value)
                        NebulaControlBindings.TryRebind(entry.Key, entry.Value);
            });

            return watch;
        }

        internal bool HasMoved(CombatAction action) =>
            before.TryGetValue(action, out KeyCode was) && NebulaControlBindings.KeyFor(action) != was;

        internal bool AnyBindingMoved()
        {
            foreach (KeyValuePair<CombatAction, KeyCode> entry in before)
                if (NebulaControlBindings.KeyFor(entry.Key) != entry.Value) return true;
            return false;
        }
    }

    /// <summary>Chapter K's shared surfaces: a menu, a dialog and a settings page, all torn down with the step.</summary>
    internal static class TutorialSettings
    {
        /// <summary>
        /// Lets the player press the menu key, and puts the gate back. The parent
        /// menu only reads its key while <c>InputGate.Suppressed</c> is false.
        /// </summary>
        internal static void OpenMenuInput(TutorialStepContext context)
        {
            bool suppressed = InputGate.Suppressed;
            InputGate.Suppressed = false;
            context.Scope.OnCleanup(() => InputGate.Suppressed = suppressed);
        }

        internal static NebulaConfirmationDialog NewDialog(TutorialStepContext context, NebulaUiShell shell)
        {
            NebulaConfirmationDialog dialog = NebulaConfirmationDialog.Create(shell);
            context.Scope.OnCleanup(() =>
            {
                if (dialog != null) TutorialAbk.DestroyNow(dialog.gameObject);
            });
            return dialog;
        }

        internal static NebulaParentMenuScreen NewMenu(TutorialStepContext context, NebulaUiShell shell,
            INebulaMenuPage page)
        {
            NebulaParentMenuScreen menu = NebulaParentMenuScreen.Create(shell);
            // An Overview page first, so the menu does not open already showing the
            // page the step is asking the player to reach.
            menu.AddPage(new NebulaOverviewPage(null));
            if (page != null) menu.AddPage(page);

            context.Scope.OnCleanup(() =>
            {
                if (menu == null) return;
                menu.Close();
                TutorialAbk.DestroyNow(menu.gameObject);
            });
            return menu;
        }

        internal static NebulaSettingsPage OpenSettingsPage(TutorialStepContext context,
            bool accessibilityFrontDoor, NebulaContext pageContext) =>
            OpenSettingsPage(context, TutorialAbk.NewShell(context), accessibilityFrontDoor, pageContext);

        internal static NebulaSettingsPage OpenSettingsPage(TutorialStepContext context, NebulaUiShell shell,
            bool accessibilityFrontDoor, NebulaContext pageContext)
        {
            var page = new NebulaSettingsPage(pageContext, accessibilityFrontDoor);
            NebulaParentMenuScreen menu = NewMenu(context, shell, page);
            menu.Open(NebulaUiAssetCatalog.Page.Settings);
            return page;
        }
    }
}
