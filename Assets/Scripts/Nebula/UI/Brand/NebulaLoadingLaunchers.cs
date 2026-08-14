using System;
using Nebula.Tutorial;
using Nebula.UI.Session;
using Nebula.UI.Shell;

namespace Nebula.UI.Brand
{
    /// <summary>
    /// Wraps a session launcher so every transition that actually loads something
    /// shows the loading surface.
    /// <para>
    /// A decorator rather than calls inside <see cref="NebulaSessionLauncher"/>: that
    /// class turns menu intent into session lifecycle and has no business knowing
    /// what the interface draws while it works. It also has no shell to draw on —
    /// this does, because the host that owns both is what builds it.
    /// </para>
    /// <para>
    /// Only the three that load are wrapped. <see cref="QuitGame"/> is not an
    /// operation to wait through, and <see cref="ReturnToMainMenu"/> tears the
    /// session down rather than building one — it clears the surface instead, so a
    /// transition out of a campaign cannot strand an overlay from the way in.
    /// </para>
    /// </summary>
    public sealed class NebulaLoadingSessionLauncher : INebulaSessionLauncher
    {
        private readonly INebulaSessionLauncher inner;
        private readonly Func<NebulaUiShell> shell;

        /// <summary>Captions, in the language the front door speaks.</summary>
        public const string NewCampaignCaption = "SEFER HAZIRLANIYOR";
        public const string ContinueCaption = "KAYIT YÜKLENİYOR";
        public const string RestartCaption = "SEFER YENİDEN BAŞLATILIYOR";

        /// <summary>
        /// The shell is fetched through a delegate, not held. The host's shell is
        /// destroyed the moment a session starts, and a decorator holding the dead
        /// one would try to parent the next overlay under it.
        /// </summary>
        public NebulaLoadingSessionLauncher(INebulaSessionLauncher inner, Func<NebulaUiShell> shell)
        {
            this.inner = inner;
            this.shell = shell;
        }

        private NebulaUiShell Shell => shell == null ? null : shell();

        public void StartNewCampaign() =>
            NebulaLoadingPresenter.Run(Shell, NewCampaignCaption, () => inner?.StartNewCampaign());

        public void ContinueFromSlot(int slot) =>
            NebulaLoadingPresenter.Run(Shell, ContinueCaption, () => inner?.ContinueFromSlot(slot));

        public void RestartCampaign() =>
            NebulaLoadingPresenter.Run(Shell, RestartCaption, () => inner?.RestartCampaign());

        public void QuitGame() => inner?.QuitGame();

        public void ReturnToMainMenu()
        {
            NebulaLoadingPresenter.ForceClear();
            inner?.ReturnToMainMenu();
        }
    }

    /// <summary>
    /// The same for the tutorial's launcher, whose two entry points additively load
    /// a scene.
    /// <para>
    /// The run tears the menu's shell down as part of starting, which takes this
    /// overlay with it — that is correct and needs no special handling: the overlay
    /// clears its own static handle when it is destroyed, and the presenter's
    /// <c>finally</c> then finds nothing to hide.
    /// </para>
    /// </summary>
    public sealed class NebulaLoadingTutorialLauncher : ITutorialLauncher
    {
        private readonly ITutorialLauncher inner;
        private readonly Func<NebulaUiShell> shell;

        public const string TutorialCaption = "EĞİTİM HAZIRLANIYOR";

        public NebulaLoadingTutorialLauncher(ITutorialLauncher inner, Func<NebulaUiShell> shell)
        {
            this.inner = inner;
            this.shell = shell;
        }

        private NebulaUiShell Shell => shell == null ? null : shell();

        /// <summary>Forwarded, so nothing that listens to the real launcher is cut off.</summary>
        public event Action<TutorialRunMode, bool> RunEnded
        {
            add { if (inner != null) inner.RunEnded += value; }
            remove { if (inner != null) inner.RunEnded -= value; }
        }

        public void StartFromBeginning() =>
            NebulaLoadingPresenter.Run(Shell, TutorialCaption, () => inner?.StartFromBeginning());

        public void StartSingleStep(string stepId) =>
            NebulaLoadingPresenter.Run(Shell, TutorialCaption, () => inner?.StartSingleStep(stepId));

        public void ExitToMainMenu()
        {
            NebulaLoadingPresenter.ForceClear();
            inner?.ExitToMainMenu();
        }

        /// <summary>Test-facing: the launcher underneath, for a fixture that needs it.</summary>
        public ITutorialLauncher Inner => inner;
    }
}
