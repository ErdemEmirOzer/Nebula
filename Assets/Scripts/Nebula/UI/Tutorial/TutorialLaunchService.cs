using System;
using Nebula.Tutorial;

namespace Nebula.UI.Tutorial
{
    /// <summary>
    /// Where the main menu finds an <see cref="ITutorialLauncher"/>.
    /// <para>
    /// The menu cannot construct one. Building a launcher means knowing how the
    /// tutorial scene is loaded and how its lifecycle is composed, and a menu that
    /// knew either of those would be a menu that could start a run by being drawn --
    /// the exact thing <c>INebulaSessionLauncher</c> exists to prevent for the
    /// campaign. So the composition root registers a factory here and the menu only
    /// ever asks.
    /// </para>
    /// <para>
    /// A factory rather than an instance, because a launcher is bound to a
    /// bootstrap that outlives no scene load: handing the menu a cached instance
    /// from a previous boot is how a front door ends up driving a lifecycle that has
    /// already been torn down.
    /// </para>
    /// <para>
    /// Nothing registered is a legitimate state, not an error. The tutorial's
    /// surfaces all render without a launcher and say plainly, where it happens,
    /// that a run cannot be started -- which is far better than a menu entry that
    /// answers a click with silence.
    /// </para>
    /// </summary>
    public static class TutorialLaunchService
    {
        private static Func<ITutorialLauncher> factory;

        /// <summary>Registers the factory the menu will ask. Replaces any previous one.</summary>
        public static void Register(Func<ITutorialLauncher> launcherFactory) => factory = launcherFactory;

        /// <summary>Forgets the factory. Called on teardown so a dead bootstrap is never reachable.</summary>
        public static void Clear() => factory = null;

        /// <summary>True when a run could be started right now.</summary>
        public static bool IsAvailable => factory != null;

        /// <summary>
        /// A launcher, or <c>null</c> when none is registered. A factory that throws
        /// is treated as none rather than taken up through the menu with it -- the
        /// front door must survive a broken tutorial installer.
        /// </summary>
        public static ITutorialLauncher Resolve()
        {
            Func<ITutorialLauncher> current = factory;
            if (current == null) return null;

            try { return current(); }
            catch (Exception) { return null; }
        }
    }
}
