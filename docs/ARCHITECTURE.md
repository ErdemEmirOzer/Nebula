# Architecture

NEBULA separates campaign composition, immutable universe descriptions, mutable campaign deltas, materialized Unity objects, and presentation. This keeps the same gameplay state authoritative whether a system is visible, warm, or simulated remotely.

## Composition root

`NebulaBootstrap.StartSession` is the single owner of boot order.

```mermaid
flowchart TD
    A["Validate static content catalog"] --> B["Validate bootstrap config"]
    B --> C["Resolve seed once"]
    C --> D["WorldInstaller.PrepareSession"]
    D --> E["Environment foundation"]
    E --> F["Player + camera"]
    F --> G["Camera/environment bindings"]
    G --> H["Start Full-system streaming"]
    H --> I["Gameplay + save + logistics"]
    I --> J["Navigation"]
    J --> K["Telemetry"]
    K --> L["Lighting"]
    L --> M["Animation"]
    M --> N["Presentation"]
    N --> O["Audio"]
    O --> P["Floating origin"]
    P --> Q["Session active"]

    A -. failure .-> U["DisposePartialBoot"]
    B -. failure .-> U
    D -. failure .-> U
    N -. failure .-> U
```

Every installer receives the same typed context and lifetime tracker. Dependencies are never discovered by installer-to-installer scene searches.

## Typed context

`NebulaContext` is a non-generic, named dependency bag. Each property:

- has a concrete type and descriptive name;
- can be set exactly once;
- throws if read before wiring;
- throws on a second write;
- represents only the current campaign session;
- is not serialized and is not a second gameplay-state authority.

```mermaid
classDiagram
    class NebulaBootstrap {
        +NebulaContext Context
        +StartSession(config)
        +Dispose()
    }
    class NebulaContext {
        +ResolvedSeed
        +Streaming
        +Ship
        +ExtractionDirector
        +NavigationDirector
        +Presentation
        +TelemetryBridge
        +AudioService
    }
    class NebulaSessionLifetime {
        +Track(lifetime, root)
        +Dispose()
        +DisposePartialBoot()
    }
    NebulaBootstrap --> NebulaContext : builds
    NebulaBootstrap --> NebulaSessionLifetime : owns
    NebulaSessionLifetime --> NebulaContext : tears down session objects
```

## Lifetime model

| Lifetime | Owner | Examples |
| --- | --- | --- |
| Application | scene/application | scene adapter and inspector config |
| Campaign session | `NebulaSessionLifetime` | seed, ship, directors, repositories, save/navigation |
| Materialized system | `SystemStreamingDirector` | Full/Warm stars, planets, structures, POIs, encounters |
| Transient UI | `NebulaSessionLifetime` / presentation | HUD, screens, modals |

Disposal runs transient UI first, campaign roots second, then runtime-created materials/meshes/textures and process-wide static state. Calls are idempotent.

## World truth model

```mermaid
flowchart LR
    Inputs["Seed + generation version + coordinate"] --> Manifest["Immutable manifest"]
    Manifest --> Full["Full materialized system"]
    Manifest --> Warm["Warm materialized system"]
    Delta["Mutable SystemDelta"] --> Full
    Delta --> Warm
    Full -->|capture before demotion/save| Delta
    Warm -->|promote| Full
    Full -->|demote| Sparse["Data-only remote simulation"]
    Sparse --> Delta
    Delta --> Save["SavePayload"]
```

### Immutable manifests

`SectorManifest`, `SystemManifest`, `PlanetManifest`, and `PoiManifest` describe generated content. They are reproducible and therefore are not frozen into each save.

### Mutable deltas

`SystemDelta` stores changes layered over a manifest: scans, drills, structures, dominance, planet snapshots, vehicles, logistics, routes, POIs, combat state, pressure, intervention groups, pending attacks, and reconnect summaries.

Ship-owned state remains with the ship save rather than moving between system deltas.

## Streaming state machine

```mermaid
stateDiagram-v2
    [*] --> Sparse
    Sparse --> Warm: nearest non-Full system within 300,000 units
    Warm --> Full: ship enters navigation radius
    Full --> Warm: another system becomes Full
    Warm --> Sparse: beyond 400,000 units / no longer candidate
    Full --> Sparse: full demotion captures delta
```

Only one system is Full and at most one additional system is Warm. Other known systems with mutable work are data-only and scheduled remotely.

## Save and restore

```mermaid
sequenceDiagram
    participant G as Gameplay directors
    participant S as SaveService
    participant T as Temp file
    participant B as Backups
    participant M as Main slot
    G->>S: Capture DTO-only SavePayload
    S->>T: Write schema-v15 envelope
    S->>T: Read back + validate hash/JSON
    S->>B: Rotate bak1 → bak2 → bak3
    S->>M: Copy previous main to bak1
    S->>M: Commit validated temp
    Note over S,M: Load tries main, then three backups
```

Newer schemas are rejected. Older supported payloads are migrated after load. Save/test tooling can redirect its data root so automated runs never overwrite player saves.

## Domain boundaries

| Namespace | Responsibility |
| --- | --- |
| `World`, `Sectors`, `Simulation` | universe, gravity, manifests, materialization, streaming |
| `Ship`, `Navigation` | flight, input, cruise, routes, discovery |
| `Extraction`, `Logistics` | resources, drills, structures, control, transfers |
| `Hangar`, `Remote` | vehicles, missions, routes, distant simulation |
| `Combat` | durability, weapons, encounters, raids, repair, salvage |
| `Progression`, `Modules` | XP curve, milestones, ship upgrades |
| `Poi`, `Tutorial` | exploration content and guided curriculum |
| `UI`, `Visuals`, `Audio` | presentation-only concerns |
| `Content` | validated static definitions and runtime catalog |
| `Telemetry`, `Diagnostics`, `Platform` | local observation, gates, paths, migrations |

## Testability

Pure types handle progression math, hash-derived generation, remote scheduling, economic rules, damage profiles, and many state transitions. Unity-facing installers and presentation are covered separately in PlayMode. This split allows deterministic rule tests without scene discovery or frame timing wherever a Unity object is unnecessary.
