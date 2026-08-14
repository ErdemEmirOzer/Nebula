using UnityEngine;
using Nebula.Visuals;

namespace Nebula.World.Composition
{
    /// <summary>
    /// Campaign-scoped lighting composition (TF-002 §2). Creates the one
    /// <see cref="NebulaLightingDirector"/> a session has and hands it its
    /// dependencies directly — streaming for which system is Full, the camera
    /// for distance falloff. Nothing here searches the scene, and the director
    /// is reachable afterwards only through <see cref="NebulaContext"/>, whose
    /// set-once property is what makes a second director impossible.
    /// <para>
    /// Runs after streaming has booted the start system, so the first frame is
    /// already lit by the right star instead of coming up dark.
    /// </para>
    /// </summary>
    public sealed class LightingInstaller
    {
        public void WireLighting(NebulaContext context, NebulaSessionLifetime lifetime)
        {
            var root = new GameObject("Nebula Lighting");
            lifetime.Track(SessionLifetime.CampaignSession, root);

            LightingQualityProfile profile = LightingQualityProfile.FromQualitySettings();

            var director = root.AddComponent<NebulaLightingDirector>();
            director.Initialize(context.Streaming, context.MainCamera, profile);
            context.SetLightingDirector(director);

            // LGT-03: the ship's own lighting, including the single hull fill
            // that used to be spawned by ShipFactory outside any budget.
            var shipLighting = context.Ship.gameObject.AddComponent<ShipLightingPresenter>();
            shipLighting.Initialize(context.Ship, context.ModuleVisuals, profile);
            director.BindShipLighting(shipLighting);

            // LGT-05: combat flashes. A pure observer of events the combat
            // systems already raise, drawing every light from the director's
            // transient pool.
            var combatLighting = root.AddComponent<CombatLightingBridge>();
            combatLighting.Initialize(director, context.ShipCombat,
                context.Ship.GetComponent<Nebula.Ship.ShipWeaponVisuals>(), context.Streaming);
            director.BindCombatLighting(combatLighting);
        }
    }
}
