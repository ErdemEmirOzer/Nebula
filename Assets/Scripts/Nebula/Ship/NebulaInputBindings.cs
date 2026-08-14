using UnityEngine;

namespace Nebula.Ship
{
    /// <summary>
    /// Single shared authority for every non-flight, non-POI keybinding that
    /// used to sit on an F-key (F1/F3/F5/F9). No runtime code should read
    /// <see cref="KeyCode"/> literals for these actions directly — everything
    /// routes through here so there is exactly one place that could ever
    /// introduce a collision. Legacy Input Manager only; no Input System
    /// dependency.
    /// </summary>
    public static class NebulaInputBindings
    {
        // There is deliberately no HUD-toggle key here any more.
        //
        // `HudToggleKey = Tab` sat in this file for four slices with no consumer at
        // all: no file under Assets/Scripts read it, the four HUD classes its summary
        // named (ShipHud/ExtractionHud/NavigationHud/HangarHud) no longer exist, and
        // TutorialKeyPrompts.HudToggle() — the one thing that would have printed it to
        // a player — was itself never called. It was still reserved against combat
        // rebinding and still documented, so the game refused a real key on behalf of
        // a feature that did nothing.
        //
        // It was deleted rather than wired up. HUD visibility already has exactly one
        // authority, NebulaUiShell.SetHudVisible (Assets/Scripts/Nebula/UI/Shell/
        // NebulaUiShell.cs:395), driven by the screen stack: the HUD hides while a
        // screen is open and comes back when the last one closes. A player-held toggle
        // would be a second authority over the same two canvases, and the two would
        // disagree the first time a screen opened over a hidden HUD — with the alert
        // strip and the vitals readout on the wrong side of the disagreement.

        /// <summary>SectorDebugHud open/close (was F3).</summary>
        public const KeyCode SectorDebugToggleKey = KeyCode.Alpha0;

        /// <summary>Chord key for manual save, held together with Ctrl (Windows/Linux) or Cmd (macOS) (was F5).</summary>
        public const KeyCode ManualSaveKey = KeyCode.Comma;

        /// <summary>Chord key for manual load, held together with Ctrl (Windows/Linux) or Cmd (macOS) (was F9).</summary>
        public const KeyCode ManualLoadKey = KeyCode.Period;

        // --- Pure, testable chord logic -----------------------------------------
        // Takes plain booleans rather than reading Input directly so EditMode
        // tests can exercise every combination without any input simulation.

        public static bool IsControlHeld(bool leftControl, bool rightControl) => leftControl || rightControl;

        public static bool IsCommandHeld(bool leftCommand, bool rightCommand) => leftCommand || rightCommand;

        /// <summary>Ctrl (either side) OR Cmd (either side) — the one shared modifier both save and load require.</summary>
        public static bool IsCommonModifierHeld(bool leftControl, bool rightControl, bool leftCommand, bool rightCommand) =>
            IsControlHeld(leftControl, rightControl) || IsCommandHeld(leftCommand, rightCommand);

        /// <summary>True only when the modifier is held AND the save key was pressed this frame — the comma alone must never fire it.</summary>
        public static bool IsManualSaveChord(bool commonModifierHeld, bool saveKeyPressedThisFrame) =>
            commonModifierHeld && saveKeyPressedThisFrame;

        /// <summary>True only when the modifier is held AND the load key was pressed this frame — the period alone must never fire it.</summary>
        public static bool IsManualLoadChord(bool commonModifierHeld, bool loadKeyPressedThisFrame) =>
            commonModifierHeld && loadKeyPressedThisFrame;

        // --- Runtime wrappers (read live Input state) ---------------------------

        public static bool ControlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        public static bool CommandHeld() => Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        public static bool CommonModifierHeld() => IsCommonModifierHeld(
            Input.GetKey(KeyCode.LeftControl), Input.GetKey(KeyCode.RightControl),
            Input.GetKey(KeyCode.LeftCommand), Input.GetKey(KeyCode.RightCommand));

        public static bool ManualSavePressed() => IsManualSaveChord(CommonModifierHeld(), Input.GetKeyDown(ManualSaveKey));

        public static bool ManualLoadPressed() => IsManualLoadChord(CommonModifierHeld(), Input.GetKeyDown(ManualLoadKey));
    }
}
