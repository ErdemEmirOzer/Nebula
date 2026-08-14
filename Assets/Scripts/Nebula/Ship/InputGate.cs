namespace Nebula.Ship
{
    /// <summary>
    /// Global gate checked by flight, extraction, processing, save-key and
    /// flight-HUD-toggle systems. A single full-screen modal (the ship
    /// progression screen) sets this true while it owns input/focus, so every
    /// other system can suppress itself with one flag instead of each modal
    /// having to know about every other system.
    /// </summary>
    public static class InputGate
    {
        public static bool Suppressed { get; set; }
    }
}
