using UnityEngine;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Composition step 10: creates the <see cref="NavigationDirector"/>,
    /// wires its <see cref="BootstrapNavigationWorldProvider"/> (still the
    /// only <see cref="INavigationWorldProvider"/> implementation — no new
    /// global navigation registry/singleton/locator is introduced here),
    /// starts a fresh campaign's knowledge state, and connects the existing
    /// save/extraction/streaming links exactly as before.
    /// </summary>
    public sealed class NavigationInstaller
    {
        public void WireNavigation(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            var navigationDirectorGO = new GameObject("Navigation Director");
            lifetime.Track(SessionLifetime.CampaignSession, navigationDirectorGO);
            var navigationDirector = navigationDirectorGO.AddComponent<NavigationDirector>();

            var navigationWorldProvider = new BootstrapNavigationWorldProvider();
            navigationDirector.Initialize(context.Ship, navigationWorldProvider);
            navigationDirector.InitializeNewCampaign();

            Extraction.ExtractionDirector extractionDirector = context.ExtractionDirector;
            navigationDirector.RequestSave = () => extractionDirector.SaveNow(null);
            extractionDirector.Navigation = navigationDirector;
            context.Streaming.Navigation = navigationDirector;

            context.SetNavigationDirector(navigationDirector);
            context.SetNavigationWorldProvider(navigationWorldProvider);
        }
    }
}
