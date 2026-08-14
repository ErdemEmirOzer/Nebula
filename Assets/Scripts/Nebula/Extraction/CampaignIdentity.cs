namespace Nebula.Extraction
{
    /// <summary>
    /// VS-017A.2 §A2: the single owner of a campaign's persistent identity.
    /// A campaign id is minted exactly once — when a genuinely new campaign
    /// starts (scene boot / <c>RestartCampaign</c>) — written into every
    /// <see cref="SaveEnvelope"/> from then on, and adopted back verbatim on
    /// load, so a normal load is never mistaken for a new campaign.
    ///
    /// Gameplay/save owns this; telemetry only ever consumes it (never mints
    /// one, never carries a previous campaign's id into a new campaign).
    /// The legacy hardcoded <c>"campaign-1"</c> is treated as "no real id":
    /// it is upgraded to a real unique id exactly once, and that id is then
    /// stable across every later save/load — never regenerated per load.
    /// </summary>
    public static class CampaignIdentity
    {
        /// <summary>The pre-VS-017A.2 hardcoded placeholder — never a real campaign identity.</summary>
        public const string LegacyPlaceholderId = "campaign-1";

        public static string Current { get; private set; }

        /// <summary>
        /// Raised whenever the active campaign identity changes — <c>isNewCampaign</c> is true
        /// only for a genuinely minted new campaign, false for one adopted from a save. Consumers
        /// (telemetry) use the id to re-scope themselves and the flag to decide whether a real
        /// "new game" transition just happened.
        /// </summary>
        public static event System.Action<string, bool> IdentityChanged;

        /// <summary>TF-002 convention: clears any subscriber left over from a prior campaign session.</summary>
        public static void ResetSubscribersForSession() => IdentityChanged = null;

        private static bool pendingNewCampaignAnnouncement;

        /// <summary>Mints a brand-new unique identity for a genuinely new campaign and announces it. Called by composition on scene boot / RestartCampaign — never by a load.</summary>
        public static string StartNewCampaign()
        {
            Current = System.Guid.NewGuid().ToString("N");
            pendingNewCampaignAnnouncement = true;
            IdentityChanged?.Invoke(Current, true);
            return Current;
        }

        /// <summary>
        /// One-shot handshake for a consumer that is composed AFTER the campaign was minted
        /// (the telemetry bridge is wired several installer steps later): returns true exactly
        /// once per genuinely-new campaign, and never for a campaign adopted from a save. This
        /// is what keeps "new_game" tied to the real campaign-creation transition rather than
        /// to bridge initialization, scene boot, load, or re-materialization.
        /// </summary>
        public static bool ConsumePendingNewCampaign()
        {
            if (!pendingNewCampaignAnnouncement) return false;
            pendingNewCampaignAnnouncement = false;
            return true;
        }

        /// <summary>
        /// Adopts the identity a save envelope carried. An empty/missing id, or the
        /// legacy <see cref="LegacyPlaceholderId"/>, is upgraded to one real unique id
        /// here — that upgraded id becomes <see cref="Current"/> and is written by the
        /// next save, so it stays stable from then on instead of being re-minted on
        /// every load. Always raises <see cref="IdentityChanged"/> with isNewCampaign=false:
        /// adopting a save is the opposite of starting a new campaign.
        /// </summary>
        public static string AdoptFromSave(string savedCampaignId)
        {
            Current = IsRealId(savedCampaignId) ? savedCampaignId : System.Guid.NewGuid().ToString("N");
            pendingNewCampaignAnnouncement = false; // adopting a save is never a new campaign
            IdentityChanged?.Invoke(Current, false);
            return Current;
        }

        /// <summary>True only for a genuine minted identity — never for null/empty or the legacy placeholder.</summary>
        public static bool IsRealId(string campaignId) =>
            !string.IsNullOrEmpty(campaignId) && campaignId != LegacyPlaceholderId;

        /// <summary>The id a save should be written with. Falls back to minting one if a save somehow happens before any campaign was started (never expected in normal composition, but a save must never write an empty identity).</summary>
        public static string ForSave() => IsRealId(Current) ? Current : StartNewCampaign();

        /// <summary>Test-only: clears the current identity so a fresh assertion starts from a known-empty state.</summary>
        public static void ClearForTests() { Current = null; pendingNewCampaignAnnouncement = false; }
    }
}
