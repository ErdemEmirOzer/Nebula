<p align="center">
  <img src="docs/brand/nebula-banner.png" width="640" alt="NEBULA emblem and wordmark" />
</p>

<p align="center"><strong>Single-player space exploration, extraction, logistics and territorial control · Unity 2022.3 LTS · C#</strong></p>
<p align="center"><em>One ship, a deterministic universe, and every planet you claim held by what you built on it.</em></p>

<p align="center">
  <img src="docs/images/hero.jpg" width="100%" alt="NEBULA key art: a heavy starship leaving an orbital city above a cratered moon, with a black hole and a spiral galaxy behind" />
</p>
<p align="center"><sub>Key art</sub></p>

NEBULA is a single-player space game built in Unity and C#. You fly one ship by hand across a deterministic universe of four galaxies, scan planets, deploy drills, process what they produce, raise command posts and defenses, and hold planets against raids. A support fleet of scouts, collectors, engineers and escorts carries the work further than one ship can.

This README is the developer entry point. It covers what the game does today, where each system lives in the code, and how the project is built, tested and kept safe. Deeper material is in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/ALGORITHMS.md`](docs/ALGORITHMS.md) and [`docs/FEATURES.md`](docs/FEATURES.md).

> **Status (16 Oct 2026):** private alpha, version 0.1.0 (build 1).
> - The macOS release build is universal (Apple silicon and Intel) and passes its release smoke run.
> - A Windows x64 player is built from the same pipeline but has never been run on Windows.
> - This repository is a source snapshot for portfolio review. Playable builds and production assets whose provenance is not yet fully documented are not included; see [Public snapshot boundary](#public-snapshot-boundary).

| | |
| --- | --- |
| **Genre** | Single-player space exploration, extraction, logistics and territorial control |
| **Engine** | Unity 2022.3.22f1 LTS · built-in render pipeline · C# |
| **Platform** | macOS universal release builds; Windows x64 build path, not yet run on Windows |
| **World** | Deterministic universe of 4 galaxies · systems revealed through 4 knowledge levels · 3–8 planets per system |
| **Economy** | 5 resource families · 5 rarities · instant recipe processing · cargo and orbital logistics |
| **Fleet** | Scout, Collector, Engineer and Escort vehicles with Mk upgrades and physical missions |
| **Combat** | Shield / armour / hull · Laser, Plasma and EM hardpoints · raids, pirate bases, emergency shutdown |
| **Progression** | Character levels 1–100 · 12 ship modules, tiers T1–T5 |
| **Tutorial** | 11 chapters, 176 scripted steps |
| **Saves** | 3 slots × 3 rotating backups · schema v15 with migrations and integrity checks · 5-minute autosave |
| **Language** | Turkish-first interface |
| **Codebase** | ≈ 124k lines of runtime and editor C# · ≈ 118k lines of tests · 3,857 test declarations |

---

## Contents

1. [Screenshots](#screenshots)
2. [How a session plays](#how-a-session-plays)
3. [Features and where they live](#features-and-where-they-live)
4. [Tech stack](#tech-stack)
5. [Architecture](#architecture)
6. [Deterministic world model](#deterministic-world-model)
7. [Build footprint](#build-footprint)
8. [Engineering profile](#engineering-profile)
9. [Test strategy](#test-strategy)
10. [Build, release and data safety](#build-release-and-data-safety)
11. [Repository layout](#repository-layout)
12. [Public snapshot boundary](#public-snapshot-boundary)
13. [Not shipped and known limitations](#not-shipped-and-known-limitations)
14. [Further reading](#further-reading)

---

## Screenshots

### Gameplay

<p align="center">
  <img src="docs/images/gameplay-station.jpg" width="100%" alt="The player ship at the start of a campaign, the system's star and its corona ahead, with the flight HUD" />
</p>
<p align="center"><sub>The start of a campaign in Albion: the star's corona ahead, flight HUD on</sub></p>

<table>
  <tr>
    <td width="50%"><img src="docs/images/gameplay-star.jpg" alt="The ship facing the star with a metal and an ocean world on their orbits" /><br /><sub>Facing the star: a metal world and an ocean world on their orbits</sub></td>
    <td width="50%"><img src="docs/images/gameplay-system.jpg" alt="The Albion system from the ship: star, planets and their orbit trails" /><br /><sub>The Albion system from the ship: five planets and their orbit trails</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/gameplay-blackhole-aetheria.jpg" alt="The ship at the core of the Aetheria galaxy, its black hole ahead, with the flight HUD" /><br /><sub>At the core of Aetheria: the galaxy's black hole ahead, HUD on</sub></td>
    <td width="50%"><img src="docs/images/gameplay-blackhole-nyxara.jpg" alt="The ship at the core of the Nyxara galaxy, its black hole ahead, with the flight HUD" /><br /><sub>At the core of Nyxara, another galaxy's black hole</sub></td>
  </tr>
</table>

### The world

<p align="center">
  <img src="docs/images/world-ringed-eclipse.jpg" width="100%" alt="A ringed gas giant passing in front of the star" />
</p>
<p align="center"><sub>A ringed gas giant crossing its star</sub></p>

<table>
  <tr>
    <td width="50%"><img src="docs/images/world-ringed-giant.jpg" alt="The ringed gas giant lit by its star" /><br /><sub>The same giant, lit, with its neighbours further out</sub></td>
    <td width="50%"><img src="docs/images/world-ocean.jpg" alt="An ocean world" /><br /><sub>An ocean world: the Water resource family</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/world-forest.jpg" alt="A forest world" /><br /><sub>A forest world: the Wood resource family</sub></td>
    <td width="50%"><img src="docs/images/world-system.jpg" alt="The whole system low across the orbital plane" /><br /><sub>The whole system, low across the orbital plane</sub></td>
  </tr>
</table>

### Beyond the home system

Four galaxies, each with a black hole at its core, and 128 star systems strung along their arms. For these frames the ship was moved through the game's own world model, floating origin and system streaming included, and each place was rendered by the game camera.

<p align="center">
  <img src="docs/images/deep-galaxies-aetheria-elysia.jpg" width="100%" alt="The Aetheria galaxy from outside, the Elysia galaxy's disc above it, both cores glowing" />
</p>
<p align="center"><sub>Aetheria from outside, with Elysia's disc above it; each core is a black hole</sub></p>

<table>
  <tr>
    <td width="50%"><img src="docs/images/core-aetheria.jpg" alt="Aetheria's black hole edge-on: event horizon, photon ring and accretion disc" /><br /><sub>Aetheria's core edge-on: event horizon, photon ring and accretion disc</sub></td>
    <td width="50%"><img src="docs/images/core-elysia.jpg" alt="Elysia's black hole seen from above its accretion disc" /><br /><sub>Elysia's core, from above the disc</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/core-nyxara.jpg" alt="Nyxara's black hole edge-on inside the galaxy's dust" /><br /><sub>Nyxara's core, inside the galaxy's dust</sub></td>
    <td width="50%"><img src="docs/images/core-thalassa.jpg" alt="Thalassa's black hole edge-on" /><br /><sub>Thalassa's core</sub></td>
  </tr>
  <tr>
    <td colspan="2"><img src="docs/images/deep-core-approach.jpg" alt="The player ship approaching Aetheria's black hole" /><br /><sub>The ship on approach to Aetheria's core</sub></td>
  </tr>
</table>

<table>
  <tr>
    <td width="50%"><img src="docs/images/system-red-dwarf-nyxara-core.jpg" alt="A red dwarf system on Nyxara's inner arm, the spiral and its core behind it" /><br /><sub>A red dwarf on Nyxara's inner arm, the galaxy's core behind it</sub></td>
    <td width="50%"><img src="docs/images/system-red-dwarf-galaxies.jpg" alt="A red dwarf system in Thalassa with neighbouring galaxies beyond" /><br /><sub>A red dwarf in Thalassa, neighbouring galaxies beyond</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/system-blue-giant-world.jpg" alt="A blue world of a blue giant system, galaxies edge-on behind it" /><br /><sub>A blue giant's world, galaxies edge-on behind it</sub></td>
    <td width="50%"><img src="docs/images/system-white-ringed.jpg" alt="A ringed giant in a white star's system" /><br /><sub>A ringed giant around a white star</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/system-ringed-crescent.jpg" alt="A ringed giant in crescent against its yellow star" /><br /><sub>A ringed giant in crescent against its yellow star</sub></td>
    <td width="50%"><img src="docs/images/system-elysia-forest.jpg" alt="A forest world in an Elysia system" /><br /><sub>A forest world in Elysia</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/deep-galaxy-from-home.jpg" alt="Looking across Aetheria's arms from above the home system towards the core" /><br /><sub>From above the home arm, across Aetheria to its core</sub></td>
    <td width="50%"><img src="docs/images/deep-galaxy-nyxara-top.jpg" alt="The Nyxara galaxy from above" /><br /><sub>Nyxara from above</sub></td>
  </tr>
</table>

### Strategic map

The map runs on live campaign data. Systems the player has not earned stay hidden; a signal gives a direction, a located system gets a position, and a visited one shows its planets.

<p align="center">
  <img src="docs/images/map-universe-diagonal.jpg" width="100%" alt="The whole universe on the strategic map, four galaxies from a diagonal angle" />
</p>
<p align="center"><sub>The whole universe: four galaxies, from a diagonal angle</sub></p>

<table>
  <tr>
    <td width="50%"><img src="docs/images/map-universe-side.jpg" alt="The universe from the side, galaxies edge-on" /><br /><sub>From the side: the galaxies edge-on</sub></td>
    <td width="50%"><img src="docs/images/map-universe-low.jpg" alt="The universe from a low angle over the map grid" /><br /><sub>Low over the map grid</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/map-universe-top.jpg" alt="The universe overview from above" /><br /><sub>Overview from above, as the map opens</sub></td>
    <td width="50%"><img src="docs/images/map-system.jpg" alt="The Albion system on the map with its inspector" /><br /><sub>System level: Albion, its five planets and the inspector</sub></td>
  </tr>
</table>

### Command menu

Every page of the in-game command menu, from the same campaign.

<table>
  <tr>
    <td width="50%"><img src="docs/images/page-overview.jpg" alt="Overview page" /><br /><sub>Overview: ship vitals, level, cargo, fleet and planetary control</sub></td>
    <td width="50%"><img src="docs/images/page-resources.jpg" alt="Resources page" /><br /><sub>Resources: the hold, the processing bench and its recipes</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-progression.jpg" alt="Progression page" /><br /><sub>Progression: level 18 and the twelve ship modules</sub></td>
    <td width="50%"><img src="docs/images/page-fleet.jpg" alt="Fleet page" /><br /><sub>Fleet: five vehicles, production and missions</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-planets.jpg" alt="Planets page" /><br /><sub>Planets: a controlled world and its control timeline</sub></td>
    <td width="50%"><img src="docs/images/page-discovery.jpg" alt="Discovery page" /><br /><sub>Discovery: known systems by knowledge level</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-combat.jpg" alt="Combat page" /><br /><sub>Combat: hardpoints, weapon production and threats</sub></td>
    <td width="50%"><img src="docs/images/page-map.jpg" alt="Map page" /><br /><sub>Map: route state and the way into the strategic map</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-settings.jpg" alt="Settings page" /><br /><sub>Settings: interface, motion, accessibility and key bindings</sub></td>
    <td width="50%"><img src="docs/images/page-session.jpg" alt="Session page" /><br /><sub>Session: three save slots, restart and exit</sub></td>
  </tr>
</table>

Inside the pages: the panels and dialogs those pages open.

<table>
  <tr>
    <td width="50%"><img src="docs/images/page-fleet-build.jpg" alt="Fleet page with the vehicle build picker open" /><br /><sub>Fleet: the build picker, four vehicle classes in seven marks, with their costs</sub></td>
    <td width="50%"><img src="docs/images/page-fleet-logistics.jpg" alt="Fleet page with the logistics centre panel open" /><br /><sub>Fleet: the logistics centre panel, before one is built in this system</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-resources-bench-add.jpg" alt="Resources page with the add-to-bench quantity dialog" /><br /><sub>Resources: moving Metal from the hold to the processing bench</sub></td>
    <td width="50%"><img src="docs/images/page-resources-bench-recipes.jpg" alt="Resources page with Metal on the bench and its conversion recipes" /><br /><sub>Resources: the loaded bench and the recipes it unlocks</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/page-settings-bindings.jpg" alt="Settings page with the key binding panel open" /><br /><sub>Settings: key binding across nine action groups</sub></td>
    <td width="50%"><img src="docs/images/page-session-load-confirm.jpg" alt="Session page asking for confirmation before loading a slot" /><br /><sub>Session: loading a slot over unsaved progress asks first</sub></td>
  </tr>
</table>

### Front end

<table>
  <tr>
    <td width="50%"><img src="docs/images/ui-main-menu.jpg" alt="Main menu with a campaign to continue" /><br /><sub>Main menu, with a campaign to continue</sub></td>
    <td width="50%"><img src="docs/images/ui-load.jpg" alt="Load screen with three save slots" /><br /><sub>Load: three save slots</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/ui-load-confirm.jpg" alt="Confirmation before loading over unsaved progress" /><br /><sub>The shared confirmation surface, before loading over unsaved progress</sub></td>
    <td width="50%"><img src="docs/images/ui-tutorial-chapters.jpg" alt="Tutorial chapter list" /><br /><sub>Tutorial: eleven chapters</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/ui-settings.jpg" alt="Settings from the main menu" /><br /><sub>Settings</sub></td>
    <td width="50%"><img src="docs/images/ui-accessibility.jpg" alt="Accessibility settings and status cues" /><br /><sub>Accessibility, with the status-cue legend</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/ui-tutorial.jpg" alt="Tutorial start screen" /><br /><sub>Tutorial: start from the beginning or pick a chapter</sub></td>
    <td width="50%"><img src="docs/images/ui-credits.jpg" alt="Credits" /><br /><sub>Credits</sub></td>
  </tr>
</table>

### In motion

<table>
  <tr>
    <td width="50%"><img src="docs/images/motion-ship-turn.gif" alt="Animated: the ship turning across its star system with the HUD" /><br /><sub>Turning across the system, HUD on</sub></td>
    <td width="50%"><img src="docs/images/motion-universe-orbit.gif" alt="Animated: an orbit of the universe on the strategic map" /><br /><sub>An orbit of the universe on the strategic map</sub></td>
  </tr>
  <tr>
    <td width="50%"><img src="docs/images/motion-corona.gif" alt="Animated: the star's live corona over the starting position" /><br /><sub>The star's live corona over the starting position</sub></td>
    <td width="50%"><img src="docs/images/motion-blackhole.gif" alt="Animated: a slow orbit around Aetheria's black hole" /><br /><sub>A slow orbit around Aetheria's core</sub></td>
  </tr>
</table>

<sub>Gameplay, map, menu and front-end images were captured from the macOS release player (0.1.0, build 1), loading a mid-campaign save. That save was built in the Unity Editor through the game's own services: scans, drill and structure builds ticked through their lifecycles, module tiers, vehicles and discovered systems. The two black-hole gameplay frames load saves of the same campaign made at those cores. World and deep-space images and the corona, black-hole and universe animations are rendered in the Editor through the game camera and the map camera, with no interface and with orbit guides off, the player's own <code>O</code> toggle.</sub>

---

## How a session plays

```mermaid
flowchart LR
    Fly["Fly by hand<br/>six-axis flight · boost · cruise"] --> Scan["Scan a planet<br/>family · rarity"]
    Scan --> Drill["Deploy a drill<br/>capsule → construction → active"]
    Drill --> Process["Process<br/>raw → parts and materials"]
    Process --> Build["Build on the surface<br/>command post · defenses · support"]
    Build --> Control["Hold the planet<br/>120 s stabilisation"]
    Control --> Fleet["Fleet and logistics<br/>scouts · collectors · routes"]
    Fleet --> Explore["Signals and analysis<br/>new systems on the map"]
    Explore --> Fly
    Control -->|raids · pirate bases| Defend["Defend<br/>shield · armour · hull"]
    Defend --> Control
    Process --> Modules["Ship modules<br/>T1 → T5"]
    Modules --> Fly
```

- **Flight.** The ship flies on six axes with roll, boost and a deep-space cruise for long distances. A floating origin keeps precision across a universe millions of units wide. Heading lock and an arrival assist help with long approaches.
- **Scan and drill.** Holding the scan key reveals a planet's resource family (Metal, Wood, Gas, Water, Diamond) and rarity. The first drill comes from a one-use pioneer kit; later drills cost Basic Building Parts. A drill drops as a capsule, constructs, then fills its own storage until the ship collects it.
- **Process.** On the Resources page, raw material goes onto a bench and converts instantly: two raw units into one Basic Building Part, or three into a family-specific material such as Metal Alloy or Refined Gas.
- **Build and hold.** A command post and at least one active defense on a scanned planet with a working drill start a 120-second stabilisation. Planets move through Unowned, Developing, Controlled, Contested and Lost.
- **Fleet.** Scouts, Collectors, Engineers and Escorts are produced in the hangar, upgraded through Mk tiers and sent on physical missions; logistics centres run collector routes inside a system.
- **Explore.** Signals point toward unknown systems. Analysis locates them and a visit reveals their planets. The strategic map shows only what the campaign has earned.

---

## Features and where they live

Code pointers are relative to `Assets/Scripts/Nebula/`.

### Flight and navigation

- Manual six-axis flight, roll, boost and camera: `Ship/ShipController.cs`, `Ship/ChaseCamera.cs`
- Deep-space cruise and its state machine: `Ship/ShipCruise.cs`
- Heading lock and arrival assist: `Navigation/HeadingLockState.cs`, `Navigation/ArrivalAssist.cs`
- Floating origin for large-distance precision: `World/FloatingOrigin.cs`
- System knowledge levels (Unknown → Signal → Located → Visited): `Navigation/SystemKnowledgeService.cs`, `World/NavigationDirector.cs`

### World generation and streaming

- Seeded galaxies, spiral arms and the star field that both the world and the map read: `World/GalaxySpiral.cs`, `World/GalaxyStarField.cs`, `World/GalaxyGenerator.cs`
- Sector and system manifests, generation version 3: `Sectors/SectorManifestGenerator.cs`, `World/StarSystemRegistry.cs`
- Full, Warm and sparse system streaming, with a 1 Hz tick for distant systems: `Sectors/SystemStreamingDirector.cs`, `Sectors/SystemMaterializer.cs`
- Remote simulation of economy, raids, probes and pending attacks in systems the player is not in: `Remote/`

### Extraction, processing and planetary control

- Scanning, build mode, drills and their upgrades: `Extraction/ShipExtraction.cs`, `Extraction/DrillStructure.cs`, `Extraction/DrillRuntime.cs`
- Resource families, rarities and recipes: `Extraction/ExtractionCatalog.cs`, `Extraction/ProcessingCatalog.cs`
- Command post, defenses and support structures: `Extraction/CommandBuildService.cs`, `Extraction/SurfaceStructure.cs`
- Planetary control and its five states: `Extraction/PlanetDominance.cs`
- Quick asset packages, evacuation and reclaim: `Extraction/QuickAssetPackage.cs`, `Extraction/PlanetEvacuation.cs`

### Fleet and logistics

- Vehicle classes, Mk tiers and production: `Hangar/VehicleCatalog.cs`, `Hangar/ShipHangar.cs`
- Orbital logistics centres and transfers: `Logistics/LogisticsCenterCatalog.cs`, `Logistics/ShipLogisticsCenter.cs`

### Combat and threats

- Shield, armour and hull with Laser, Plasma and EM hardpoints and per-hardpoint heat: `Ship/ShipCombat.cs`, `Combat/`
- One shared enemy state machine for every hostile; only data differs: `Combat/EnemyAiResolver.cs`
- Encounters, raids, pirate bases, wreckage and emergency shutdown: `Combat/CombatEncounterDirector.cs`, `Combat/CombatCampaignDirector.cs`, `Combat/EmergencyShutdownState.cs`
- Rebindable combat keys with collision checks: `Ship/CombatBindings.cs`, `Ship/CombatBindingValidator.cs`

### Strategic map

- Universe → galaxy → system, built from the same snapshot the world uses: `UI/StrategicMap/StrategicMapScreen.cs`, `UI/StrategicMap/Preview/StrategicMapUniverseRenderer.cs`
- Layers for grid, routes, discovery points, fleet, logistics, threat and ambient detail, all display-only

### Progression

- Levels 1–100 and development points: `Progression/ProgressionService.cs`, `Progression/ProgressionMath.cs`
- Twelve ship modules across industry, exploration, defense and nuclear branches, tiers T1–T5: `Modules/ModuleCatalog.cs`

### Tutorial

- Eleven chapters (A–K) and 176 steps, each step a scripted condition on real gameplay state: `Tutorial/TutorialStepCatalog.cs`, `Tutorial/Steps/`

### Saves

- Versioned envelope, schema v15, with payload hash, migrations and three rotating backups per slot: `Extraction/SaveModel.cs`
- Autosave every five active minutes and on real state transitions: `Extraction/ExtractionDirector.cs`
- One-time import of data written under the project's earlier identity; it copies and never moves or deletes: `Platform/NebulaLegacyDataMigrator.cs`

### Interface and accessibility

- One interface shell with a screen stack and focus handling: `UI/Shell/NebulaUiShell.cs`
- Command menu with ten pages: `UI/Menu/NebulaParentMenuScreen.cs`
- Keyboard and mouse input routing, rebinding and persistence: `Ship/NebulaInputActions.cs`, `Ship/NebulaInputRouter.cs`, `Ship/NebulaInputBindingStore.cs`
- Interface scale 80–140%, reduced motion, motion off, flash reduction and high contrast; status cues pair colour with a symbol and text

### Telemetry and diagnostics

- Local-only telemetry and session logs; nothing leaves the machine: `Telemetry/TelemetryService.cs`
- A release visibility probe and a smoke runner for automated release checks: `Diagnostics/`

---

## Tech stack

| | |
| --- | --- |
| Engine | Unity **2022.3.22f1** (LTS), pinned |
| Rendering | Built-in render pipeline; 12 hand-written shaders for the strategic map, planets and effects |
| Input | Legacy Input Manager, keyboard and mouse, runtime rebinding |
| Packages | glTFast for model import, TextMesh Pro, uGUI, Unity Test Framework |
| Language | C#: ≈ 124k lines of runtime and editor code, ≈ 118k lines of tests |
| Tooling | Python 3 and zsh for the PlayMode gate, smoke harness and font pipeline (`Tools/`) |
| Platforms | macOS universal (Apple silicon + Intel); Windows x64 build path |
| Network | None. The game runs fully offline. |

---

## Architecture

NEBULA has one explicit composition root. `NebulaBootstrap` validates content and configuration, resolves the campaign seed, then runs installers in a fixed order. Every dependency is written once into a typed, set-once `NebulaContext`, and a partial boot unwinds through `NebulaSessionLifetime` instead of leaving a half-built world behind.

```mermaid
flowchart LR
    subgraph Boot["Boot"]
        Entry["SolarSystemBootstrap"] --> Bootstrap["NebulaBootstrap<br/>validate · seed"]
        Bootstrap --> Installers["Ordered installers"]
    end

    subgraph World["World"]
        Registry["StarSystemRegistry<br/>manifests · knowledge"]
        Streaming["SystemStreamingDirector<br/>Full · Warm · sparse 1 Hz"]
        Remote["Remote simulation<br/>economy · raids · probes"]
        Origin["FloatingOrigin"]
    end

    subgraph Play["Gameplay"]
        Ship["ShipController · ShipCruise"]
        Extraction["ExtractionDirector<br/>drills · structures · control"]
        Hangar["ShipHangar · logistics"]
        Combat["ShipCombat · EnemyAiResolver"]
        Progress["ProgressionService · modules"]
    end

    subgraph Pres["Presentation"]
        Shell["NebulaUiShell"]
        Menu["Command menu · 10 pages"]
        Map["StrategicMapScreen"]
        Hud["HUD · alerts · tutorial"]
    end

    Installers --> Context["NebulaContext<br/>typed · set-once"]
    Context --> World
    Context --> Play
    Context --> Pres
    Registry --> Streaming --> Remote
    Ship --> Extraction --> Progress
    Extraction --> Hangar
    Combat --> Extraction
    Extraction -->|SaveNow| Save["SaveService<br/>schema v15 · 3 slots × 3 backups"]
    Context --> Lifetime["NebulaSessionLifetime"]
    Lifetime -->|reverse-order teardown| Dispose["Clean session disposal"]
```

Principles that run through the code (details in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)):

- **Generated data never changes.** A system's manifest is regenerated from the seed and its coordinates on demand. Only the campaign's changes to it, the system delta, are saved.
- **One owner per concern.** One composition root, one context, one session lifetime, one writer of each save slot. A pure resolver decides and a MonoBehaviour adapter only gathers positions and applies the result, so the decisions are testable in EditMode without a scene.
- **Reservations, not deductions.** Every build, upgrade and production job reserves its cost first and consumes it on completion, and a restored job re-establishes its own reservation. A general audit traced the costliest bug family in the project to restore paths that skipped this step; all of them are fixed and covered.
- **Never trust a test that cannot fail.** Graphics leaves run on a real device, key capture fixtures fail rather than skip without one, and new gates are mutation-verified before they count.

---

## Deterministic world model

Immutable generated data is separated from mutable campaign history. A system manifest can always be regenerated from the campaign seed and coordinates; only its delta needs to be saved.

```mermaid
flowchart LR
    Seed["Campaign seed"] --> Hash["FNV-1a + mixed hash channels"]
    Hash --> Sector["SectorManifest"]
    Sector --> System["SystemManifest"]
    System --> Materialize["Full or Warm materialization"]
    Delta["SystemDelta: scans, structures, fleets, combat"] --> Materialize
    Materialize --> Capture["Capture mutations"]
    Capture --> Delta
    Delta --> Save["Versioned save envelope"]
    Save --> Restore["Validate, migrate, restore"]
    Restore --> Delta
```

See [Algorithms](docs/ALGORITHMS.md) for generation, LRU caching, streaming, sparse ticks, floating origin, progression, combat and save integrity.

---

## Build footprint

The macOS release player went from **3.47 GiB to 613 MiB**.

| | Release player | Measured |
| --- | ---: | --- |
| Before | 3,728,163,448 B | 10 Aug 2026 |
| After | 642,595,538 B | 16 Oct 2026, the current release build |

- **The cause.** 85% of the old player was a single file of texture data. The 245 model files of the visual library embedded about 926 texture instances that resolved to only about 62 distinct images: every level-of-detail copy re-embedded the full-resolution textures of its base model. Unity then imported all of them uncompressed.
- **The fix.** Textures are de-duplicated on a composite key (decoded pixels, size, colour space, material role, alpha need), compressed with BC7, and the 127 wrapper prefabs are rebound onto the shared materials. De-duplication alone saved nothing until the rebinding landed; extracting the models without it made the build larger.

---

## Engineering profile

Counted from the source at commit `2825ecd` (16 Oct 2026).

| Area | Snapshot |
| --- | ---: |
| C# total | 891 files · 242,171 lines |
| Gameplay runtime (`Assets/Scripts`) | 491 files · 114,577 lines in 23 domain folders |
| Editor tooling (`Assets/Editor`) | 25 files · 9,745 lines |
| Test code (`Assets/Tests`) | 375 files · 117,849 lines |
| Test declarations | 3,857 (2,976 EditMode · 881 PlayMode) |
| Assembly definitions | 6 (1 runtime · 3 editor · 2 test) |
| Shaders | 12, hand-written |
| Save schema | v15 · world generation version 3 · content revision 3 |
| macOS release player | 642,595,538 B, universal |
| History | 307 commits, 22 Jul → 16 Oct 2026 |

---

## Test strategy

Unity Test Framework with NUnit. Counts are static attribute counts (`[Test]`, `[UnityTest]`, `[TestCase]`) at `2825ecd`.

| Suite | Assembly | Mode | Files | Declarations |
| --- | --- | --- | ---: | ---: |
| Rules, content, saves, composition | `Nebula.Tests.EditMode` | EditMode | 260 | 2,976 |
| Sessions, streaming, UI, captures | `Nebula.Tests.PlayMode` | PlayMode | 115 | 881 |
| **Total** | 2 assemblies | | **375** | **3,857** |

- **EditMode** covers pure rules and contracts: generation determinism, manifests and LRU caching, reservations, drills and structure lifecycles, planetary control, combat state machines and heat, progression math, module gates, save migration across every schema, content validation and input-binding collisions.
- **PlayMode** boots the real scene: sessions begun and ended, streaming promotion and demotion round trips, save/load through the real director, the tutorial, the strategic map, accessibility settings, and capture fixtures that photograph real screens.
- **The PlayMode gate.** `Tools/Testing/playmode_gate.py` runs the suite in four lanes (ReleaseRegression, Graphics, Soak, Diagnostic) at leaf level across disposable APFS-cloned mirrors. Results only aggregate when two hashes agree: one of the C# source and one of everything a player can see (`Assets`, `ProjectSettings`, `Packages`). A crash-survivable journal lets a broken run resume. The gate's own 59 self-tests are mutation-verified.
- **The first whole-suite run** (13 Aug 2026) found 79 failures that per-fixture runs could never see, mostly state leaking between fixtures. About half were faults in the tests themselves; the rest were product defects. They were worked down before the gate's lanes were closed on 14 Aug 2026.
- **A general audit** recorded 104 defects in one ledger, each with a root cause, a fix strategy and its regression fixtures. 102 are closed as fixed, one of them in test infrastructure, and 2 were withdrawn.

---

## Build, release and data safety

- **One build authority.** `NebulaBuildAutomation` (in `Assets/Editor/Nebula/`) produces every player: macOS universal and Windows x64 release targets, development and release standalones. It hardens each release build (no development flags, no profiler connection, no debug-symbol copies) and writes a build report.
- **Shaders the code asks for by name.** `NebulaRequiredShaders` registers every shader the game resolves with `Shader.Find` in Graphics Settings before a release build, and refuses to build if one is missing. It was added after a release candidate rendered every procedural material magenta; its smoke run had passed because it only opened the main menu.
- **The release smoke run.** `Tools/Smoke/smoke_macos.sh` starts the real release player against an isolated data root, runs a visibility probe and scans the player log. It snapshots the player's real data folders before and after and fails unless they are byte-identical.
- **Isolation that cannot be switched on by accident.** The player only isolates its data when it is given an absolute path under a temporary root, with no symbolic links on the way, carrying a marker file a harness created. In the Editor, every persistent writer falls back to a throwaway scratch folder instead of the player's folder, so a test run cannot reach a real campaign.

---

## Repository layout

```text
Assets/
├── Scripts/Nebula/   # runtime gameplay and presentation code
├── Editor/Nebula/    # build, import, catalog and scene tooling
└── Tests/            # EditMode and PlayMode suites
Packages/             # Unity package manifest and lock file
ProjectSettings/      # engine version and sanitized configuration snapshot
Tools/                # PlayMode gate, smoke harness and font tooling
docs/                 # architecture, algorithms, features, brand, images
```

## Public snapshot boundary

This is intentionally not a runnable or redistributable game package. The private working project contains binary media whose public-distribution provenance has not yet been fully resolved. Apart from the brand exports and the images used by this page, those files, generated Unity directories, release builds, raw audits and internal planning are excluded here.

The source is published for portfolio review only. No open-source licence or reuse grant is provided; see [NOTICE](NOTICE.md) and [third-party notices](THIRD_PARTY_NOTICES.md).

## Not shipped and known limitations

- Private alpha, not a store release.
- Turkish-only player-facing text.
- The Windows player has been verified structurally but never run on Windows; its package carries a marker that says so.
- macOS builds are ad-hoc signed and not notarized, so the first launch goes through Gatekeeper's **Open** confirmation.
- Keyboard and mouse only; no gamepad support.
- No performance budget has been measured against target hardware yet.
- No external playtest or long-session soak on a player machine yet.
- Final audio, music, lighting and animation polish is deferred, and some star-energy and nuclear-chain design decisions are still open.
- Asset licence and provenance closure is a release gate that is still open.
- This snapshot cannot build without the excluded assets.

## Further reading

- [Architecture](docs/ARCHITECTURE.md): composition root, ownership and lifetime rules
- [Algorithms](docs/ALGORITHMS.md): generation, streaming, floating origin, combat and save integrity
- [Feature map](docs/FEATURES.md): the domain-by-domain breakdown
- [Brand guide](docs/BRAND.md): emblem, wordmark and key art

---

<sub>Copyright © 2026 Erdem Emir Özer. All rights reserved. Images are captured from the current build of the game. No reuse rights are granted; see [NOTICE](NOTICE.md).</sub>
