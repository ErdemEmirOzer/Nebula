using System;
using UnityEngine;
using Nebula.Combat;
using Nebula.Ship;
using Nebula.Tutorial;
using Nebula.UI.Controls;
using Nebula.UI.Design;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// Every player-facing string the tutorial's own surfaces use, and the one
    /// place a control name is written down.
    /// <para>
    /// The names matter as much as the captions. Four of these screens are
    /// procedural, so a test has no prefab to look at and finds a control by the
    /// name the factory gave it -- and a caption is not a name, because the
    /// caption is Turkish and changes with the copy. Constants here mean the
    /// screen and the fixture cannot drift into naming the same control two
    /// different things.
    /// </para>
    /// </summary>
    public static class TutorialUiCopy
    {
        // ---- control names (never shown; tests and the shell find these) -----

        public const string TutorialEntryName = "Tutorial";
        public const string FromBeginningName = "FromBeginning";
        public const string ChaptersName = "Chapters";
        public const string BackName = "Back";
        public const string StepRowNamePrefix = "Step ";
        public const string ChapterHeadingNamePrefix = "Chapter ";
        public const string MainMenuName = "MainMenu";
        public const string RestartName = "Restart";

        /// <summary>
        /// The confirmation control on the instruction card. It exists only while an
        /// <see cref="TutorialStepKind.InformationAcknowledgement"/> step is up, so a
        /// fixture that finds it on any other step has found a way to skip gameplay.
        /// </summary>
        public const string AcknowledgeName = "Acknowledge";

        // ---- captions ---------------------------------------------------------

        /// <summary>The main-menu entry. Turkish, like every other entry beside it.</summary>
        public const string TutorialEntry = "EĞİTİM";

        public const string ModeTitle = "EĞİTİM";
        public const string ModeSubtitle = "NASIL BAŞLAMAK İSTERSİNİZ?";
        public const string FromBeginning = "BAŞTAN BAŞLA";
        public const string Chapters = "BÖLÜMLER";
        public const string Back = "GERİ";

        public const string ChapterListTitle = "BÖLÜMLER";

        /// <summary>
        /// Said once, up front, because the alternative is a player hunting for a
        /// lock that is not there. Nothing in this list is gated on anything.
        /// </summary>
        public const string ChapterListNote = "HER ADIM HER ZAMAN AÇIK — İSTEDİĞİNİZ ADIMDAN BAŞLAYABİLİRSİNİZ";

        public const string EmptyCatalog = "EĞİTİM ADIMI TANIMLANMAMIŞ";

        /// <summary>
        /// A wiring failure, not a gate. Shown where it happens rather than left
        /// as a control that answers a click with nothing.
        /// </summary>
        public const string NoLauncher = "EĞİTİM BAŞLATICI BAĞLI DEĞİL";

        public const string SummaryTitle = "EĞİTİM ÖZETİ";
        public const string Completed = "EĞİTİM TAMAMLANDI";
        public const string SummaryToMainMenu = "ANA MENÜ";
        public const string SummaryRestart = "BAŞTAN BAŞLA";

        public const string ExitHeading = "EĞİTİMDEN ÇIK";
        public const string ExitQuestion = "Eğitimden çıkılsın mı?";

        /// <summary>Stated on the prompt, because leaving mid-step must not look like finishing it.</summary>
        public const string ExitNote = "BU ADIM TAMAMLANMIŞ SAYILMAZ";

        public const string ExitConfirm = "ÇIK";
        public const string ExitCancel = "DEVAM ET";

        public const string StepCompleteFeedback = "ADIM TAMAMLANDI";

        /// <summary>
        /// The confirmation a reading step ends on. It is a statement about the
        /// player's reading and never a gameplay command: the card only offers it
        /// when the step genuinely has no action available, and pressing it says "I
        /// have read this" rather than standing in for something the player would
        /// otherwise have done.
        /// </summary>
        public const string Acknowledge = "ANLADIM";

        /// <summary>What the card shows before a step has been handed to it.</summary>
        public const string NoStep = "—";

        // ---- composition ------------------------------------------------------

        /// <summary>
        /// "12 / 47". Global in both run modes: a standalone run of step 27 says
        /// 27, because the number is where the player is in the curriculum and not
        /// where they are in this sitting.
        /// </summary>
        public static string Counter(int globalNumber, int total)
        {
            if (globalNumber <= 0 || total <= 0) return $"{NoStep} / {NoStep}";
            return $"{globalNumber} / {total}";
        }

        /// <summary>"C · MADENCİLİK", or just the letter when the chapter is unknown.</summary>
        public static string ChapterTag(TutorialChapter chapter)
        {
            if (chapter == null) return string.Empty;
            string letter = string.IsNullOrEmpty(chapter.Letter) ? string.Empty : chapter.Letter;
            string title = string.IsNullOrEmpty(chapter.TitleTr) ? string.Empty : NebulaText.Upper(chapter.TitleTr);
            if (letter.Length == 0) return title;
            if (title.Length == 0) return letter;
            return $"{letter} · {title}";
        }

        /// <summary>The summary headline: complete is a different sentence, not a louder one.</summary>
        public static string SummaryHeadline(bool complete) => complete ? Completed : SummaryTitle;

        /// <summary>"7 / 47 ADIM TAMAMLANDI".</summary>
        public static string SummaryTally(int completedCount, int total)
        {
            int done = Mathf.Max(0, completedCount);
            int all = Mathf.Max(0, total);
            return $"{done} / {all} ADIM TAMAMLANDI";
        }

        /// <summary>Which door the player came through, said plainly.</summary>
        public static string SummaryMode(TutorialRunMode mode) =>
            mode == TutorialRunMode.Standalone ? "TEK ADIM · BÖLÜMLER" : "SIRALI · BAŞTAN BAŞLA";
    }

    /// <summary>
    /// Key prompts, resolved from the live binding every time they are asked for.
    /// <para>
    /// Nothing here holds a key name. A player who rebinds Fire to the right mouse
    /// button and then opens the tutorial has to be told "SAĞ FARE", and the only
    /// way that is true is to read the binding at the moment the prompt is drawn --
    /// which is why every method is a call rather than a constant, and why
    /// <see cref="TutorialStepDefinition.KeyHint"/> is a <c>Func</c> rather than a
    /// string.
    /// </para>
    /// <para>
    /// No F-key appears here and none may. F1-F12 are refused by
    /// <c>CombatBindingValidator</c> and the four system shortcuts moved off them
    /// years ago; a tutorial that told the player to press F1 would be teaching a
    /// binding the game will not accept.
    /// </para>
    /// </summary>
    /// <summary>
    /// The keys that confirm a reading step, in one place so the card, the prompt
    /// and any fixture cannot drift into naming different ones.
    /// </summary>
    public static class TutorialAcknowledgeInput
    {
        /// <summary>The key shown on the card.</summary>
        public const KeyCode PrimaryKey = KeyCode.Return;

        /// <summary>The numeric keypad's Enter, accepted silently because it is the same key to the player.</summary>
        public const KeyCode SecondaryKey = KeyCode.KeypadEnter;
    }

    public static class TutorialKeyPrompts
    {
        /// <summary>The key a combat action is bound to right now, as the player sees it.</summary>
        public static string Combat(CombatAction action) =>
            NebulaControlBindings.KeyLabel(NebulaControlBindings.KeyFor(action));

        /// <summary>The same key with the action's Turkish name after it: "SOL FARE  LAZER ATEŞLE".</summary>
        public static string CombatWithName(CombatAction action) =>
            $"{Combat(action)}   {NebulaText.Upper(NebulaControlBindings.ActionName(action))}";

        /// <summary>A key a step needs by <see cref="KeyCode"/> rather than by action.</summary>
        public static string Key(KeyCode key) => NebulaControlBindings.KeyLabel(key);

        /// <summary>A key with a caption after it, in the prompt's own voice.</summary>
        public static string Key(KeyCode key, string captionTr) =>
            string.IsNullOrEmpty(captionTr) ? Key(key) : $"{Key(key)}   {NebulaText.Upper(captionTr)}";

        // HudToggle() is gone with the binding it printed. It named Tab as the HUD
        // toggle, no runtime code ever read that key, and nothing ever called this
        // method either — so the only way it could have reached a player was as a
        // tutorial teaching a key that does nothing.

        public static string SectorDebugToggle() => Key(NebulaInputBindings.SectorDebugToggleKey);
        public static string ManualSave() => Key(NebulaInputBindings.ManualSaveKey);
        public static string ManualLoad() => Key(NebulaInputBindings.ManualLoadKey);

        /// <summary>Escape is fixed and is the one key the tutorial itself owns.</summary>
        public static string Exit() => $"{Key(KeyCode.Escape)}   {TutorialUiCopy.ExitHeading}";

        /// <summary>
        /// Enter confirms a reading step. Fixed, like Escape, and for the same
        /// reason: it belongs to the tutorial's own card rather than to the flight
        /// controls, so it is not on the rebindable list and cannot collide with
        /// one. Still resolved through the label resolver rather than written out,
        /// so the card never says a key name in a voice of its own.
        /// </summary>
        public static string Acknowledge() =>
            $"{Key(TutorialAcknowledgeInput.PrimaryKey)}   {TutorialUiCopy.Acknowledge}";

        /// <summary>
        /// The prompt a step wants shown, or <c>null</c> when the step is driven
        /// entirely through the interface and has no key to teach. A hook that
        /// throws is treated as no prompt rather than taken down the card with it.
        /// </summary>
        public static string For(TutorialStepDefinition step)
        {
            Func<string> hint = step?.KeyHint;
            if (hint == null) return null;

            string resolved;
            try { resolved = hint(); }
            catch (Exception) { return null; }

            return string.IsNullOrEmpty(resolved) ? null : resolved;
        }
    }
}
