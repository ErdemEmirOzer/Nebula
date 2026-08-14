using UnityEngine;
using Nebula.Combat;
using Nebula.Extraction;
using Nebula.Ship;

namespace Nebula.Visuals
{
    /// <summary>
    /// How urgent something is, independent of what it is. Every HUD reading in
    /// the game collapses onto one of these, so a player learns four states
    /// once instead of a colour code per panel.
    /// </summary>
    public enum UiStatusLevel
    {
        /// <summary>Nothing to say. The reading is inside its normal band.</summary>
        Nominal,

        /// <summary>Worth noticing but not yet acting on.</summary>
        Caution,

        /// <summary>Needs action now.</summary>
        Warning,

        /// <summary>Failing or failed.</summary>
        Critical,

        /// <summary>Working on it — repair, construction, upgrade.</summary>
        Working,

        /// <summary>Off, unavailable, or out of contact.</summary>
        Inactive
    }

    /// <summary>
    /// The single colour table every HUD reads, derived from the same palette
    /// the world lighting uses: friendly cyan-white, industrial amber,
    /// engineering turquoise, and one restrained red for genuine failure.
    /// <para>
    /// Colour is never the only carrier. This class hands back a colour for a
    /// state a HUD has already decided; the text and icon that state also
    /// drives stay exactly where they were. Nothing here blinks, pulses or
    /// tweens — a status that has to move to be noticed is a status some
    /// players cannot use.
    /// </para>
    /// <para>
    /// Pure and allocation-free: every method is a lookup over readonly
    /// colours, safe to call from an existing per-frame HUD refresh without
    /// creating a material or a garbage colour object.
    /// </para>
    /// </summary>
    public static class LightingUiPalette
    {
        // ---- the six state colours ------------------------------------------

        /// <summary>Everything is fine. Cold friendly white-cyan, the same family the ship's own systems use.</summary>
        public static readonly Color Nominal = new Color(0.72f, 0.90f, 1f);

        /// <summary>Worth a look. A quiet amber, clearly below Warning.</summary>
        public static readonly Color Caution = new Color(1f, 0.82f, 0.45f);

        /// <summary>Act now. The industrial amber the world's structures already warn with.</summary>
        public static readonly Color Warning = ShipLightingProfiles.Industrial;

        /// <summary>Failing. The one red in the interface, kept restrained so it still means something.</summary>
        public static readonly Color Critical = ShipLightingProfiles.Warning;

        /// <summary>Repair, construction, upgrade — the engineering turquoise from the ship and maintenance platforms.</summary>
        public static readonly Color Working = ShipLightingProfiles.Engineering;

        /// <summary>Off or out of contact. Readable, deliberately dull.</summary>
        public static readonly Color Inactive = new Color(0.55f, 0.58f, 0.62f);

        /// <summary>How far <see cref="UiStatusLevel.Inactive"/> and dimmed states sit below full strength.</summary>
        public const float InactiveDim = 0.75f;

        // ---- the same six states, authored again for High Contrast ------------
        // Not the six above with a multiplier on them. A single scalar is a tint:
        // it lifts every state by the same amount, so the two that were hardest to
        // tell apart stay exactly as hard to tell apart and only the screen gets
        // brighter. These are picked per state against the high-contrast surfaces,
        // for two things at once -- each one's own ratio against the panel it lands
        // on, and its separation from its five neighbours. Critical stays the
        // reddest thing in the interface and Inactive stays the dullest, because
        // those two facts are what a player reads the panel by.
        //
        // WCAG 2.1, worst case over all six panel surfaces. The authored palette
        // first, then these on the high-contrast surfaces:
        //   Nominal 10.7 -> 13.4   Caution  10.0 -> 12.6   Warning  7.5 -> 9.5
        //   Critical 4.5 -> 6.6    Working  10.9 -> 13.1   Inactive 2.8 -> 6.8
        // Every one of them clears 4.5:1, so a status may be written as text and
        // not only drawn as a mark.
        //
        // They are also the palette Photosensitivity Safe Mode draws from, on the
        // ordinary surfaces, where they read 11.3 / 10.6 / 8.0 / 5.6 / 11.0 / 5.8.
        // Critical is the one that matters there: it goes from 1.53:1 to 5.60:1,
        // because the flash it used to rely on is gone.

        public static readonly Color NominalHighContrast = Rgb(0xBFEBFF);
        public static readonly Color CautionHighContrast = Rgb(0xFFD98A);
        public static readonly Color WarningHighContrast = Rgb(0xFFB25C);
        public static readonly Color CriticalHighContrast = Rgb(0xFF7A63);
        public static readonly Color WorkingHighContrast = Rgb(0x88F5E2);
        public static readonly Color InactiveHighContrast = Rgb(0x9AA6B2);

        /// <summary>The authored colour for a status level, before accessibility folds in.</summary>
        public static Color Base(UiStatusLevel level, bool highContrast)
        {
            switch (level)
            {
                case UiStatusLevel.Nominal: return highContrast ? NominalHighContrast : Nominal;
                case UiStatusLevel.Caution: return highContrast ? CautionHighContrast : Caution;
                case UiStatusLevel.Warning: return highContrast ? WarningHighContrast : Warning;
                case UiStatusLevel.Critical: return highContrast ? CriticalHighContrast : Critical;
                case UiStatusLevel.Working: return highContrast ? WorkingHighContrast : Working;
                case UiStatusLevel.Inactive:
                    return highContrast ? InactiveHighContrast : Inactive * InactiveDim;
                default: return highContrast ? NominalHighContrast : Nominal;
            }
        }

        /// <summary>True for the levels that exist to alarm.</summary>
        public static bool IsAlarm(UiStatusLevel level) =>
            level == UiStatusLevel.Caution || level == UiStatusLevel.Warning
            || level == UiStatusLevel.Critical;

        /// <summary>
        /// True when the stronger palette is in force, because colour is working
        /// on its own.
        /// <para>
        /// Two settings put it there and they arrive from opposite directions.
        /// High Contrast is a player saying the normal palette is not enough.
        /// Photosensitivity Safe Mode is the player keeping a promise the game
        /// made — no repeating emphasis at all — and the moment that promise is
        /// kept, whatever the emphasis was carrying has nothing to carry it. A
        /// critical reading that was legible because it pulsed is not legible; it
        /// was only ever loud.
        /// </para>
        /// </summary>
        public static bool UsesStrongPalette =>
            LightingAccessibility.HighContrast || LightingAccessibility.PhotosensitivitySafeMode;

        /// <summary>The colour for a status level, after accessibility.</summary>
        public static Color Resolve(UiStatusLevel level) => Base(level, UsesStrongPalette);

        /// <summary>
        /// The colour for a status level at a given alpha. HUDs draw their text
        /// and fills at their own opacity, so this exists to stop each one
        /// re-declaring a colour constant just to change the alpha — the hue
        /// and the accessibility response still come from the one table.
        /// </summary>
        public static Color Resolve(UiStatusLevel level, float alpha)
        {
            Color color = Resolve(level);
            color.a = alpha;
            return color;
        }

        /// <summary>
        /// Folds the accessibility settings into a UI colour a caller supplies
        /// itself, rather than one from the table above.
        /// <para>
        /// This method used to dim alarm colours by the flash limit, and that was
        /// backwards in a way that took a test measuring the outcome to see. A
        /// flash limit is about a transient pop of light; the resting colour of a
        /// status readout is not a flash. Running it through
        /// <c>ScaleFlashIntensity</c> meant the harder a photosensitive player
        /// asked the game to protect them, the darker their critical readouts
        /// became — Safe Mode put Critical at <b>1.53:1</b> against a row, roughly
        /// a third of the WCAG AA floor, so the one mode that exists to make the
        /// game safe for photosensitive players was the mode in which a critical
        /// state was hardest to see. The flash limit now lands where it belongs,
        /// on the amplitude and rate of <c>UiEmphasisPulse</c>, which is the only
        /// thing in the interface that repeats.
        /// </para>
        /// <para>
        /// The scene brightness trim is gone from here for the same reason: a HUD
        /// is not lit by the world, and a setting that dims lights has no business
        /// dimming the sentence explaining why a module is locked.
        /// </para>
        /// <para>
        /// What is left is the one real fold. When colour is working alone — see
        /// <see cref="UsesStrongPalette"/> — an alarm colour is reinforced toward
        /// white rather than toward grey, so the hue that tells a colour-blind
        /// player which state this is survives exactly.
        /// </para>
        /// </summary>
        public static Color Apply(Color color, bool isAlarm)
        {
            if (!isAlarm || !UsesStrongPalette) return color;
            return Reinforce(color);
        }

        /// <summary>How far <see cref="Reinforce"/> lifts a colour toward white.</summary>
        public const float ReinforceTowardWhite = 0.22f;

        /// <summary>
        /// Raises a colour's luminance without moving its hue off its own line —
        /// the same relationship the authored high-contrast values already hold to
        /// their normal counterparts, applied to a colour that has no authored
        /// counterpart.
        /// </summary>
        public static Color Reinforce(Color color) => new Color(
            Mathf.Lerp(color.r, 1f, ReinforceTowardWhite),
            Mathf.Lerp(color.g, 1f, ReinforceTowardWhite),
            Mathf.Lerp(color.b, 1f, ReinforceTowardWhite),
            color.a);

        /// <summary>0xRRGGBB to an opaque UI colour.</summary>
        private static Color Rgb(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);

        // ---- thresholds ----------------------------------------------------------

        /// <summary>Below this fraction a durability layer reads as Caution.</summary>
        public const float LayerCautionFraction = 0.6f;

        /// <summary>Below this it reads as Warning.</summary>
        public const float LayerWarningFraction = 0.35f;

        /// <summary>Below this it reads as Critical.</summary>
        public const float LayerCriticalFraction = 0.15f;

        /// <summary>Storage at or above this fraction reads as Warning; full reads as Critical.</summary>
        public const float StorageWarningFraction = 0.8f;

        /// <summary>Heat at or above this fraction of the overheat point reads as Warning.</summary>
        public const float HeatWarningFraction = 0.75f;

        // ---- readings ----------------------------------------------------------------

        /// <summary>Shield, armour or hull as a fraction of its maximum.</summary>
        public static UiStatusLevel DurabilityLayer(float current, float max)
        {
            if (max <= 0f) return UiStatusLevel.Inactive;
            float fraction = Mathf.Clamp01(current / max);

            if (fraction <= LayerCriticalFraction) return UiStatusLevel.Critical;
            if (fraction <= LayerWarningFraction) return UiStatusLevel.Warning;
            if (fraction <= LayerCautionFraction) return UiStatusLevel.Caution;
            return UiStatusLevel.Nominal;
        }

        /// <summary>Combat energy: the same bands as a durability layer, because it fails the same way.</summary>
        public static UiStatusLevel Energy(float current, float max) => DurabilityLayer(current, max);

        /// <summary>Hardpoint heat. Rises toward Critical rather than falling, so the bands invert.</summary>
        public static UiStatusLevel Heat(float current, float overheatAt)
        {
            if (overheatAt <= 0f) return UiStatusLevel.Inactive;
            float fraction = Mathf.Clamp01(current / overheatAt);

            if (fraction >= 1f) return UiStatusLevel.Critical;
            if (fraction >= HeatWarningFraction) return UiStatusLevel.Warning;
            if (fraction >= 0.5f) return UiStatusLevel.Caution;
            return UiStatusLevel.Nominal;
        }

        /// <summary>Drill or Logistics Center fill. 80% warns, completely full is critical — nothing is moving.</summary>
        public static UiStatusLevel Storage(float stored, float capacity)
        {
            if (capacity <= 0f) return UiStatusLevel.Inactive;
            float fraction = Mathf.Clamp01(stored / capacity);

            if (fraction >= 0.999f) return UiStatusLevel.Critical;
            if (fraction >= StorageWarningFraction) return UiStatusLevel.Warning;
            return UiStatusLevel.Nominal;
        }

        /// <summary>The ship's own condition, from the states LGT-03 already resolves.</summary>
        public static UiStatusLevel Ship(ShipLightingState state)
        {
            switch (state)
            {
                case ShipLightingState.Damaged: return UiStatusLevel.Warning;
                case ShipLightingState.Disabled: return UiStatusLevel.Critical;
                case ShipLightingState.Repair: return UiStatusLevel.Working;
                default: return UiStatusLevel.Nominal;
            }
        }

        /// <summary>A structure or drill lifecycle as a HUD status.</summary>
        public static UiStatusLevel Structure(DrillLifecycle state)
        {
            switch (state)
            {
                case DrillLifecycle.Planned: return UiStatusLevel.Inactive;
                case DrillLifecycle.CapsuleInTransit:
                case DrillLifecycle.Constructing: return UiStatusLevel.Working;
                case DrillLifecycle.Active: return UiStatusLevel.Nominal;
                case DrillLifecycle.Damaged: return UiStatusLevel.Warning;
                case DrillLifecycle.Disabled: return UiStatusLevel.Critical;
                case DrillLifecycle.Dismantled: return UiStatusLevel.Inactive;
                default: return UiStatusLevel.Inactive;
            }
        }

        /// <summary>Raid phase. Scheduled shows nothing — the player has not been told yet.</summary>
        public static UiStatusLevel Raid(RaidPhase phase)
        {
            switch (phase)
            {
                case RaidPhase.None:
                case RaidPhase.Scheduled:
                case RaidPhase.Resolved: return UiStatusLevel.Inactive;
                case RaidPhase.Warning: return UiStatusLevel.Warning;
                case RaidPhase.Spawning:
                case RaidPhase.Active: return UiStatusLevel.Critical;
                case RaidPhase.RetreatCleanup: return UiStatusLevel.Caution;
                default: return UiStatusLevel.Inactive;
            }
        }

        /// <summary>Planet or system control.</summary>
        public static UiStatusLevel Dominance(DominanceState state)
        {
            switch (state)
            {
                case DominanceState.Unowned: return UiStatusLevel.Inactive;
                case DominanceState.Developing: return UiStatusLevel.Working;
                case DominanceState.Controlled: return UiStatusLevel.Nominal;
                case DominanceState.Contested: return UiStatusLevel.Warning;
                case DominanceState.Lost: return UiStatusLevel.Critical;
                default: return UiStatusLevel.Inactive;
            }
        }

        /// <summary>
        /// Attention-card priority. The card's own text and icon still say what
        /// it is; this only decides how loudly it is coloured.
        /// </summary>
        public static UiStatusLevel Attention(int priority)
        {
            if (priority >= 3) return UiStatusLevel.Critical;
            if (priority == 2) return UiStatusLevel.Warning;
            if (priority == 1) return UiStatusLevel.Caution;
            return UiStatusLevel.Nominal;
        }

        /// <summary>
        /// A module upgrade, read from the real
        /// <see cref="Nebula.Modules.UpgradeRejectReason"/> the progression
        /// service already produces. No new progression state is invented here:
        /// this only decides how each existing answer is coloured.
        /// <para>
        /// The distinction that matters is between "you cannot do this yet"
        /// and "this is wrong". A missing milestone is a caution, missing
        /// resources are a warning, and only a genuinely broken request —
        /// an unknown module, a skipped tier, something unimplemented — is
        /// critical.
        /// </para>
        /// </summary>
        public static UiStatusLevel Progression(Nebula.Modules.UpgradeRejectReason reason, bool upgradeActive = false)
        {
            // Work in progress outranks everything: the player already
            // committed, so the panel should read as busy rather than as barred.
            if (upgradeActive) return UiStatusLevel.Working;

            switch (reason)
            {
                case Nebula.Modules.UpgradeRejectReason.None: return UiStatusLevel.Nominal;
                // Finished, not blocked — a maxed module is a success state.
                case Nebula.Modules.UpgradeRejectReason.AlreadyMaxTier: return UiStatusLevel.Nominal;
                // Not yet reachable: quiet, so a locked branch does not shout.
                case Nebula.Modules.UpgradeRejectReason.LevelTooLow: return UiStatusLevel.Inactive;
                // Reachable, but something is still missing.
                case Nebula.Modules.UpgradeRejectReason.MilestoneNotMet:
                case Nebula.Modules.UpgradeRejectReason.ScreenPaused: return UiStatusLevel.Caution;
                // Reachable and understood — the player simply cannot pay yet.
                case Nebula.Modules.UpgradeRejectReason.InsufficientPoints:
                case Nebula.Modules.UpgradeRejectReason.InsufficientResources: return UiStatusLevel.Warning;
                // Another upgrade already holds the queue.
                case Nebula.Modules.UpgradeRejectReason.QueueBusy: return UiStatusLevel.Working;
                // Genuinely wrong requests.
                case Nebula.Modules.UpgradeRejectReason.UnknownModule:
                case Nebula.Modules.UpgradeRejectReason.TierSkip:
                case Nebula.Modules.UpgradeRejectReason.NotImplemented: return UiStatusLevel.Critical;
                default: return UiStatusLevel.Inactive;
            }
        }

        /// <summary>
        /// Communication link. Last-known data is a caution, not a failure —
        /// the reading is real, just stale.
        /// </summary>
        public static UiStatusLevel CommunicationLink(bool live, bool hasLastKnown)
        {
            if (live) return UiStatusLevel.Nominal;
            if (hasLastKnown) return UiStatusLevel.Caution;
            return UiStatusLevel.Inactive;
        }
    }
}
