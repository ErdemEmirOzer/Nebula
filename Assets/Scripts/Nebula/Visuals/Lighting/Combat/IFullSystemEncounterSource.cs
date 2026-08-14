namespace Nebula.Visuals
{
    /// <summary>
    /// The one thing <see cref="CombatLightingBridge"/> needs from system
    /// streaming: the encounter director of whichever system is Full right
    /// now, or null when none is materialized.
    /// <para>
    /// Declared by the consumer, like <see cref="IFullSystemStarSource"/>, so
    /// the bridge depends on this single question rather than on the streaming
    /// director's whole surface — and so a test can drive a system change
    /// without materializing one.
    /// </para>
    /// </summary>
    public interface IFullSystemEncounterSource
    {
        Nebula.Combat.CombatEncounterDirector CombatEncounter { get; }
    }
}
