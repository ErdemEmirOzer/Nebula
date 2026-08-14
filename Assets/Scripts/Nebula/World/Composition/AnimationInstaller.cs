using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Campaign-scoped animation composition (TF-002 §2). Creates the one
    /// <see cref="NebulaAnimationDirector"/> a session has and the player
    /// ship's single animation presenter, handing each its dependencies
    /// directly. Nothing here searches the scene, and the director is
    /// reachable afterwards only through <see cref="NebulaContext"/>, whose
    /// set-once property is what makes a second director impossible.
    /// <para>
    /// Runs after lighting, so the animation quality tier can follow the
    /// lighting tier the player already chose.
    /// </para>
    /// </summary>
    public sealed class AnimationInstaller
    {
        public void WireAnimation(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            var root = new GameObject("Nebula Animation");
            lifetime.Track(SessionLifetime.CampaignSession, root);

            AnimationQualityProfile profile =
                AnimationQualityProfile.FromLightingQuality(context.LightingDirector.Profile.Quality);

            var director = root.AddComponent<NebulaAnimationDirector>();
            director.Initialize(profile);

            // ANI-02.1: the delivery-capsule proxies, built once. Capacity is
            // three because the Quick Asset Package is the largest thing that
            // sends at once (t=0/2/4); a fourth simultaneous build loses its
            // scenery and nothing else.
            GameObject capsuleVisual = NebulaVisualResolver.Load(NebulaVisualCatalog.BuildCapsule.ResourcePath);
            director.BindCapsuleProxies(CapsuleProxyPool.Create(root.transform, 3, capsuleVisual));

            // ANI-02.2: two proxies per item id is enough to show one batch
            // arriving while the previous one leaves; more slots running at
            // once simply share them.
            var itemRoot = new GameObject("Item Proxies");
            itemRoot.transform.SetParent(root.transform, false);
            director.BindItemProxies(new ItemProxyPool(itemRoot.transform, capacityPerItem: 2));

            // ANI-03.1: operation VFX for scans, repairs, recoveries and escort
            // orders. Four per sprite is the whole session's budget — a fleet of
            // twenty-four vehicles all working at once shares these and never
            // grows the pool.
            var operationRoot = new GameObject("Operation VFX");
            operationRoot.transform.SetParent(root.transform, false);
            director.BindSupportOperations(new SupportOperationPool(operationRoot.transform, capacityPerVfx: 4));

            context.SetAnimationDirector(director);

            var shipAnimation = context.Ship.gameObject.AddComponent<PlayerShipAnimationPresenter>();
            shipAnimation.Initialize(context.Ship, director);

            // ANI-02.1: the processing module poses from its real slot runtimes.
            // A ship without the module has no anchor and this is a no-op.
            var processingAnimation = context.Ship.gameObject.AddComponent<ProcessingAnimationPresenter>();
            processingAnimation.Initialize(
                context.Ship.GetComponent<Nebula.Extraction.ShipProcessing>(),
                context.Ship.GetComponent<Nebula.Ship.PlayerShipVisualController>(),
                director);
            // A restored session must not replay batches it already produced.
            processingAnimation.AdoptCurrentProgressSilently();

            // ANI-04.1: the player's mounted weapons follow the target the ship
            // has really acquired, and kick when a shot really goes off.
            var gimbals = context.Ship.gameObject.AddComponent<WeaponGimbalPresenter>();
            gimbals.Initialize(
                context.Ship.GetComponent<Nebula.Ship.ShipCombat>(),
                context.Ship.GetComponent<Nebula.Ship.ShipWeaponVisuals>(),
                director);

            // ANI-05.2.2: the hull's own two mechanisms — the hangar hatch and
            // the auxiliary thrusters. Both read; neither delays a launch or
            // produces torque.
            var shipMechanics = context.Ship.gameObject.AddComponent<ShipAttitudeAndHangarPresenter>();
            shipMechanics.Initialize(
                context.Ship.GetComponent<Nebula.Hangar.ShipHangar>(),
                context.Ship.GetComponent<Nebula.Ship.PlayerShipVisualController>(),
                director);

            // ANI-04: the one bridge from real combat events to animation. It
            // gets its two sources handed to it — streaming (step 8) is already
            // up by the time this runs — so nothing here searches the scene.
            var combatBridge = root.AddComponent<CombatAnimationBridge>();
            combatBridge.Initialize(director,
                context.Ship.GetComponent<Nebula.Ship.ShipCombat>(),
                context.Streaming.CombatEncounter);

            // ANI-04.3: the raid warning, and only the warning. Its anchor is
            // the planet the scheduler really named; the encounter director's
            // own transform is the fallback when no planet is named yet.
            Nebula.Combat.CombatEncounterDirector encounters = context.Streaming.CombatEncounter;
            var raid = root.AddComponent<RaidAnimationPresenter>();
            raid.Initialize(encounters, director, encounters != null ? encounters.transform : null);
        }
    }
}
