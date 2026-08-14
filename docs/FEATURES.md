# Feature map

This map summarizes systems present in the current NEBULA source. “Implemented” here means the code path exists in the private-alpha project; it does not imply store-release readiness.

## Session and interface

- Main menu: continue, new campaign, tutorial, load, settings, accessibility, credits, exit
- Three save-slot summaries and campaign continuation
- Turkish-first HUD and menu shell
- Parent command menu with Resources, Fleet, Progression, Navigation, and other domain pages
- Context bar, vitals, navigation, alerts, planet context, notifications, and shared confirmation surface
- Keyboard/mouse input routing, rebinding storage, modal input suppression, and focus handling
- Accessibility settings and redundant status cues
- Local-only telemetry and session logs

## Flight and universe

- Manual thrust on forward/back, strafe, vertical, yaw/pitch, and roll axes
- Boost and deep-space cruise state machine
- Gravity sources and arrival assistance
- Seeded galaxies, sectors, systems, stars, planets, surface slots, and environment classes
- Reproducible system names, star classes, compositions, resources, and POI manifests
- Continuous physical interstellar travel without teleporting between systems
- Floating-origin rebasing for large-coordinate precision
- Strategic map, discovery knowledge, signal analysis, route planning, and frontier guarantees

## Streaming and simulation

- One Full system with complete gameplay objects
- At most one Warm neighbor prepared for arrival
- Mutable deltas for all other known systems
- Remote drills, structures, logistics, vehicles, raids, probes, and pending attacks
- Busy distant systems evaluated at 1 Hz; calm systems at 0.1 Hz with elapsed time carried forward
- No offline progress: simulation advances only with active game time

## Extraction and economy

- Resource families: Metal, Wood, Gas, Water, Diamond
- Planet scans and rarity-based deposits
- T1/T2 drills, local storage, transfer, damage, upgrade, and persistence
- Ship cargo with item-volume capacity
- Instant processing bench with recipes and schema-v15 migration from legacy timed bays
- Build-cost reservation, cancellation/refund rules, and upgrade jobs
- Command, defense, support, and maintenance structures
- Planet evacuation and reclaim

## Control and logistics

- Planet control and system dominance evaluation
- Orbital logistics center construction and upgrade
- Deposits, withdrawals, reserves, and storage estimates
- Collector routes with physical and remotely simulated states
- Maintenance resupply and system communication rules
- Quick asset packages and intervention policies

## Hangar and fleet

- Scout Mk I
- Collector Mk I / Mk II
- Engineer Mk I / Mk II
- Escort Mk I / Mk II
- Production jobs and build-cost ledgers
- Scout scans, collector transport, engineer repair/recovery, and escort protection
- Physical mission phases, docking, cancellation, recovery, and save/restore
- Same-system intervention groups and threat-aware retreat behavior

## Combat

- Cascading shield → armor → hull durability
- Laser, Plasma, and EM damage profiles
- Hardpoints, heat, energy, cooldowns, targeting, and weapon coordination
- Enemy roles and AI resolution
- Projectiles, collision damage, wreckage, salvage, and looter cargo
- Planetary defense coordination
- Raid scheduling, pressure/grace phases, pirate bases, and campaign outcomes
- Ship repair, engineer field work, safe zones, and emergency shutdown/recovery
- Remote pending attacks and reconnect summaries

## Exploration and POIs

- Deterministic anomaly, wreck, and facility manifests
- Visit ledger and mutable POI state
- Sensor relay, analysis, salvage, task, and facility interactions
- Frontier signal and knowledge progression

## Progression and modules

- Deterministic level 1–100 curve
- Idempotent XP awards keyed by stable event IDs
- T1–T5 ship modules and milestone validation
- Cargo, processing, navigation, combat, defense, and support upgrades
- Progression/module UI with cost, tier, effect, and readiness states

## Tutorial

Eleven ordered chapters:

1. Flight
2. Exploration
3. Extraction
4. Economy
5. Progression
6. Hangar
7. Structures
8. Combat
9. Raids
10. Dominance
11. Session management

Steps have stable IDs, global ordering, completion conditions, deep-entry support, and separate tutorial-world/session lifetime handling.

## Persistence and migration

- JSON envelope with schema, generation version, campaign identity, revision, timestamp, payload hash, and payload
- Current schema v15
- Main file plus three rotating backups per slot
- Temporary write and read-back validation before commit
- Corrupt-primary fallback and newer-schema rejection
- Explicit legacy migrations, including processing-state migration
- Autosave every 300 active seconds
- Isolated save roots for automated smoke/test runs

## Engineering and release tooling

- Fail-fast content catalog validation
- Explicit composition root and set-once dependency context
- Reverse-order lifetime disposal and partial-boot cleanup
- EditMode and PlayMode suites
- Smoke runners, performance recording, percentile math, and quality-gate reports
- macOS/Windows packaging, manifest, provenance, and verifier tools in the private project
