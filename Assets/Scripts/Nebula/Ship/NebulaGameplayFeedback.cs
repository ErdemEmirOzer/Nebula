using UnityEngine;
using Nebula.Combat;

namespace Nebula.Ship
{
    public enum GameplayFeedbackSource
    {
        Navigation, Save, Extraction, Processing, QuickPackage, Evacuation, Poi, Logistics, Combat
    }

    public enum GameplayFeedbackTone { Status, Success, Rejection }

    /// <summary>A typed, presentation-only envelope. It owns no gameplay decision.</summary>
    public readonly struct GameplayFeedbackMessage
    {
        public readonly GameplayFeedbackSource Source;
        public readonly GameplayFeedbackTone Tone;
        public readonly string Text;
        public readonly float ExpiresAtUnscaledTime;

        public GameplayFeedbackMessage(GameplayFeedbackSource source, GameplayFeedbackTone tone,
            string text, float expiresAtUnscaledTime)
        {
            Source = source;
            Tone = tone;
            Text = text;
            ExpiresAtUnscaledTime = expiresAtUnscaledTime;
        }

        public bool IsVisible => !string.IsNullOrEmpty(Text) && Time.unscaledTime < ExpiresAtUnscaledTime;
    }

    /// <summary>
    /// One process-wide presentation channel for typed gameplay outcomes. Producers
    /// still own checks and mutations; this only carries their answer to the HUD.
    /// </summary>
    public static class NebulaGameplayFeedback
    {
        public const float DefaultSeconds = 3.5f;
        private static GameplayFeedbackMessage latest;

        public static GameplayFeedbackMessage Latest => latest;

        /// <summary>
        /// Carries a producer's already-player-facing sentence to the HUD, unchanged.
        /// <para>
        /// It used to run every string through a 33-entry English→Turkish lookup on the
        /// way past. That table was a second place where the game decided what the
        /// player reads: a producer could be corrected and still be overruled here, and
        /// a producer the table had never heard of shipped its English straight to the
        /// screen while looking translated. Every one of the 33 phrases is now authored
        /// in Turkish at the producer, so the table translated nothing and has been
        /// removed. A phrase reaches the player exactly as the system that refused
        /// wrote it, and there is one place to correct it (P1-ECO-018).
        /// </para>
        /// </summary>
        public static void Publish(GameplayFeedbackSource source, string text,
            GameplayFeedbackTone tone = GameplayFeedbackTone.Status, float seconds = DefaultSeconds)
        {
            latest = new GameplayFeedbackMessage(source, tone, text,
                Time.unscaledTime + Mathf.Max(0.1f, seconds));
        }

        public static void Clear() => latest = default;

        /// <summary>
        /// The one conversion from a repair refusal to what the player reads.
        /// <para>
        /// <c>None</c> is not a refusal and has no sentence. Anything else the table
        /// has not been taught still answers in Turkish rather than returning null —
        /// a null here published an empty message, which is a silent reject wearing
        /// the costume of a reported one — and never leaks an enum name.
        /// </para>
        /// </summary>
        public static string RepairText(RepairRejectReason reason) => reason switch
        {
            RepairRejectReason.None => null,
            RepairRejectReason.InCombat => "MUHAREBE SIRASINDA ONARIM BAŞLATILAMAZ",
            RepairRejectReason.TooSoonAfterDamage => "SON HASARDAN SONRA GÜVENLİ SÜREYİ BEKLEYİN",
            RepairRejectReason.TooFast => "ONARIM İÇİN GEMİYİ YAVAŞLATIN",
            RepairRejectReason.EmergencyShutdown => "ACİL KAPANMA SÜRERKEN ONARIM BAŞLATILAMAZ",
            RepairRejectReason.AlreadyActive => "ONARIM ZATEN SÜRÜYOR",
            RepairRejectReason.AlreadyAtCap => "GEMİ GÜVENLİ ONARIM SINIRINDA",
            RepairRejectReason.InsufficientResources => "ONARIM İÇİN KAYNAK YETERSİZ",
            _ => "ONARIM ŞU AN BAŞLATILAMIYOR"
        };

        /// <summary>
        /// The one conversion from a defense-coordination refusal to what the player
        /// reads (P1-COMBAT-008). Alpha7/Alpha8 used to answer an impossible order with
        /// nothing at all.
        /// </summary>
        public static string DefenseCoordinationText(DefenseCoordinationRejectReason reason) => reason switch
        {
            DefenseCoordinationRejectReason.None => null,
            DefenseCoordinationRejectReason.NoEncounterDirector => "SAVUNMA KOORDİNASYONU BU SİSTEMDE ETKİN DEĞİL",
            DefenseCoordinationRejectReason.NoCommandPlanetInRange => "MENZİLDE ETKİN KOMUTA MERKEZİ OLAN GEZEGEN YOK",
            DefenseCoordinationRejectReason.NoLiveTarget => "ODAK İÇİN CANLI BİR HEDEF SEÇİN",
            _ => "SAVUNMA KOORDİNASYONU ŞU AN VERİLEMİYOR"
        };
    }
}
