using UnityEngine;
using Nebula.Content;

namespace Nebula.World.Composition
{
    /// <summary>
    /// The single owner of composition order (TF-002 §2/§8). Every step
    /// below runs in exactly this order, every time, whether this is the
    /// scene's first boot or a <c>RestartCampaign</c> from
    /// <see cref="SolarSystemBootstrap"/> — installers never discover each
    /// other via <c>Find</c>; every dependency flows through the typed
    /// <see cref="NebulaContext"/> this method builds up step by step:
    ///
    /// 0. TF-003 content validate + build (<see cref="ContentCatalogValidator"/>/<see cref="RuntimeCatalogBuilder"/>) — before config validate even, since it depends on nothing but the static content catalogs
    /// 1. Config validate (<see cref="NebulaBootstrapConfig.Validate"/>)
    /// 2. Seed resolve (0 =&gt; a fresh random seed, exactly once this session)
    /// 3+5. Campaign state/repository reset + start manifest (<see cref="WorldInstaller.PrepareSession"/>)
    /// 4. Environment/world foundation (<see cref="EnvironmentInstaller.BuildWorldFoundation"/>)
    /// 6+7. Player, camera, modules/hangar (<see cref="PlayerInstaller"/>)
    /// 6(tail). Starfield/skybox binding, now that the camera exists (<see cref="EnvironmentInstaller.BuildCameraPresentationBindings"/>)
    /// 8. Start-system Full streaming boot (<see cref="WorldInstaller.StartStreaming"/>)
    /// 9. Extraction/gameplay/logistics/save wiring (<see cref="GameplayInstaller"/>)
    /// 10. Navigation wiring (<see cref="NavigationInstaller"/>)
    /// 10.7. Lighting (<see cref="LightingInstaller"/>) — needs the Full system's star and the camera, so it follows streaming
    /// 10.8. Animation (<see cref="AnimationInstaller"/>) — visual only; takes its quality tier from lighting, so it follows it
    /// 11. Presentation (<see cref="PresentationInstaller"/>)
    /// 12. Floating-origin final binding
    /// 13. Session active (nothing further — new-game/restore already ran inside steps 3–10 exactly as the original monolith did)
    /// </summary>
    public sealed class NebulaBootstrap
    {
        public NebulaContext Context { get; private set; }
        private NebulaSessionLifetime lifetime;

        private NebulaBootstrap() { }

        /// <summary>
        /// <paramref name="contentCatalogOverride"/> is null in every real
        /// (production) call — <see cref="SolarSystemBootstrap"/> never
        /// passes one, so the real static C# catalogs
        /// (<see cref="CSharpContentCatalog"/>) are always the source. The
        /// parameter exists only so a PlayMode test can inject a
        /// deliberately-broken <see cref="IContentCatalog"/> and assert boot
        /// fails fast with no partial world/player/director — never a
        /// runtime service-locator seam.
        /// </summary>
        public static NebulaBootstrap StartSession(NebulaBootstrapConfig config, IContentCatalog contentCatalogOverride = null)
        {
            // Step 0 — before even config validation: pure content, no world/Random/GameObject touched yet.
            // A content error must fail exactly as fast as a config error, before a single
            // world/ship/director object exists (this literal ordering IS the "fail-fast, no
            // partial world/player/director" guarantee — there is nothing yet to unwind).
            IContentCatalog contentSource = contentCatalogOverride ?? new CSharpContentCatalog();
            System.Collections.Generic.List<string> contentErrors = ContentCatalogValidator.Validate(contentSource);
            if (contentErrors.Count > 0) throw new ContentValidationException(contentErrors);
            RuntimeCatalog runtimeCatalog = RuntimeCatalogBuilder.Build(contentSource);

            // Step 1 — before ANY world mutation: no static reset, no Random.InitState, nothing.
            config.Validate();

            var bootstrap = new NebulaBootstrap
            {
                lifetime = new NebulaSessionLifetime(),
                Context = new NebulaContext()
            };
            NebulaContext context = bootstrap.Context;

            try
            {
                context.SetRuntimeCatalog(runtimeCatalog);

                // Step 2 — resolved exactly once, per session, never mutating config.Seed itself.
                int resolvedSeed = config.Seed == 0 ? System.Environment.TickCount : config.Seed;
                Random.InitState(resolvedSeed);
                context.SetConfig(config);
                context.SetResolvedSeed(resolvedSeed);

                var world = new WorldInstaller();
                var environment = new EnvironmentInstaller();
                var player = new PlayerInstaller();
                var gameplay = new GameplayInstaller();
                var navigation = new NavigationInstaller();
                var telemetry = new TelemetryInstaller();
                var audio = new AudioInstaller();
                var lighting = new LightingInstaller();
                var animation = new AnimationInstaller();
                var presentation = new PresentationInstaller();

                world.PrepareSession(resolvedSeed, context);                                           // 3+5
                environment.BuildWorldFoundation(config, context, bootstrap.lifetime);                 // 4
                player.CreatePlayerAndCamera(context, bootstrap.lifetime);                              // 6+7
                environment.BuildCameraPresentationBindings(config, context, bootstrap.lifetime);       // 6 (tail)
                world.StartStreaming(context, bootstrap.lifetime, config.ShowOrbitRings, config.ShowPlanetTrails); // 8
                gameplay.WireGameplay(context, bootstrap.lifetime, resolvedSeed);                        // 9
                navigation.WireNavigation(context, bootstrap.lifetime);                                  // 10
                telemetry.WireTelemetry(context, bootstrap.lifetime);                                    // 10.5 (VS-017A.1)
                lighting.WireLighting(context, bootstrap.lifetime);                                       // 10.7 (LGT-01: after streaming booted Full, before presentation)
                animation.WireAnimation(context, bootstrap.lifetime);                                     // 10.8 (ANI-01: follows the lighting tier, so it comes after it)
                presentation.CreatePresentation(context, bootstrap.lifetime);                            // 11
                audio.WireAudio(context, bootstrap.lifetime);                                            // 11.5 (presentation audio)

                var floatingOriginGO = new GameObject("Floating Origin");                                // 12
                bootstrap.lifetime.Track(SessionLifetime.CampaignSession, floatingOriginGO);
                var floatingOrigin = floatingOriginGO.AddComponent<FloatingOrigin>();
                floatingOrigin.Initialize(context.Ship.transform);
                context.SetFloatingOrigin(floatingOrigin);
                // 13 — session active; nothing further to do here.
            }
            catch
            {
                // A partial boot never leaves half-built state behind: unwind
                // whatever was tracked so far, in reverse creation order.
                bootstrap.lifetime.DisposePartialBoot();
                throw;
            }

            return bootstrap;
        }

        /// <summary>Idempotent — a second call is a no-op. Safe to call unconditionally before starting a new session (see <see cref="SolarSystemBootstrap.RestartCampaign"/>).</summary>
        public void Dispose() => lifetime.Dispose();

        public bool IsDisposed => lifetime.IsDisposed;
    }
}
