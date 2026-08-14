using UnityEngine;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Campaign-scoped audio composition. Loads the one prebuilt clip catalog,
    /// creates one playback authority and one presentation-only gameplay
    /// observer. Missing catalog is safe: the game continues silently.
    /// </summary>
    public sealed class AudioInstaller
    {
        public void WireAudio(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            var root = new GameObject("Nebula Audio");
            lifetime.Track(SessionLifetime.CampaignSession, root);

            var service = root.AddComponent<Audio.NebulaAudioService>();
            var catalog = Resources.Load<Audio.NebulaAudioCatalog>("Audio/NebulaAudioCatalog");
            service.Initialize(catalog);
            context.SetAudioService(service);

            var bridge = root.AddComponent<Audio.NebulaAudioGameplayBridge>();
            bridge.Initialize(context, service);
            context.SetAudioBridge(bridge);

            var uiFeedback = root.AddComponent<Audio.NebulaUiAudioFeedback>();
            uiFeedback.Initialize(service);
        }
    }
}
