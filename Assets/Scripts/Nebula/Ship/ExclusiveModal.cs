namespace Nebula.Ship
{
    /// <summary>
    /// Single-owner gate for full-screen modals that each already raise
    /// <see cref="InputGate"/> on their own (the UI shell's screen stack, the
    /// strategic map). Prevents a second modal from opening while one is already
    /// open, without either modal needing to know the other exists — a general
    /// mechanism, not hardcoded to any pair of screens.
    /// <para>
    /// None of them stops the simulation: a modal takes the controls, not the
    /// clock. The world keeps running behind an open screen.
    /// </para>
    /// </summary>
    public static class ExclusiveModal
    {
        public static object Owner { get; private set; }

        public static bool TryAcquire(object owner)
        {
            if (Owner != null && Owner != owner) return false;
            Owner = owner;
            return true;
        }

        public static void Release(object owner)
        {
            if (Owner == owner) Owner = null;
        }

        public static bool IsHeldByOther(object owner) => Owner != null && Owner != owner;

        /// <summary>
        /// TF-002: session teardown safety net. A modal that is destroyed while
        /// still open (e.g. scene/session dispose) has no other path to call
        /// <see cref="Release"/>, which would otherwise leave <see cref="Owner"/>
        /// pointing at a destroyed object and permanently block every future
        /// modal. Call exactly once per session dispose.
        /// </summary>
        public static void ResetForSession() => Owner = null;
    }
}
