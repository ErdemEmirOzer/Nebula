#!/usr/bin/env python3
"""One-time converter: HTML universe snapshot -> flat, JsonUtility-friendly Unity asset.

The HTML embeds its exact runtime snapshot as arrays-of-mixed-types, which Unity's
JsonUtility cannot read. This flattens it into named fields and pre-computes the
per-planet extraction values the HTML derives at load time, so the runtime reader
stays trivial and allocation-free.

Run:  python3 Tools/convert-map-snapshot.py
Out:  Assets/Resources/StrategicMap/universe-snapshot.json
"""
import json, math, os

SRC = "MapDesign/nebula-live-universe-map.html"
OUT = "Assets/Resources/StrategicMap/universe-snapshot.json"

RESOURCE_FAMILIES = ["Metal", "Wood", "Gas", "Water", "Diamond"]
RARITY_TIERS = ["Common", "Uncommon", "Rare", "VeryRare", "Legendary"]
RARITY_MULTIPLIERS = [1.0, 1.25, 1.6, 2.1, 3.0]
FAMILY_EFFICIENCIES = [1.0, 0.75, 0.5, 0.25, 0.25]

MASK64 = (1 << 64) - 1

def fnv1a64(text: str) -> int:
    h = 14695981039346656037
    for ch in text:
        h ^= ord(ch)
        h = (h * 1099511628211) & MASK64
    return h

def hash_to_unit(text: str) -> float:
    return (fnv1a64(text) >> 11) / 9007199254740992.0

def extraction(seed: int, system_index: int, planet_index: int):
    world_id = f"sys{system_index:03d}/pl{planet_index:02d}"
    family_index = int(hash_to_unit(f"{seed}:{world_id}/family") * 5)
    roll = hash_to_unit(f"{seed}:{world_id}/rarity")
    rarity_index = 0 if roll < 0.5 else 1 if roll < 0.8 else 2 if roll < 0.95 else 3 if roll < 0.99 else 4
    recommended = system_index == 0 and planet_index == 0
    if recommended:
        rarity_index = min(rarity_index, 1)
    ppm = 8.0 * RARITY_MULTIPLIERS[rarity_index] * FAMILY_EFFICIENCIES[family_index]
    return {
        "worldId": world_id,
        "resourceFamily": RESOURCE_FAMILIES[family_index],
        "rarity": RARITY_TIERS[rarity_index],
        "t1Yield": round(FAMILY_EFFICIENCIES[family_index] * 100),
        "productionPerMinute": ppm,
        "localStorage": math.ceil(ppm * 10 / 5) * 5,
        "recommendedFirstSetup": recommended,
    }

def read_snapshot(path: str):
    s = open(path, encoding="utf-8").read()
    start = s.index("/* NEBULA_EXACT_SNAPSHOT_START */") + len("/* NEBULA_EXACT_SNAPSHOT_START */")
    i = s.index("{", start)
    depth = 0
    for j in range(i, len(s)):
        if s[j] == "{":
            depth += 1
        elif s[j] == "}":
            depth -= 1
            if depth == 0:
                return json.loads(s[i:j + 1])
    raise ValueError("unbalanced snapshot object")

def main():
    raw = read_snapshot(SRC)
    seed = raw["seed"]

    galaxies = [{
        "id": g[0], "name": g[1],
        "centerX": g[2], "centerY": g[3], "centerZ": g[4],
        "radius": g[5], "particles": g[6], "blackHoleRadius": g[7],
        "whiteness": g[8], "phase": g[9],
    } for g in raw["galaxies"]]

    systems, planets = [], []
    for si, row in enumerate(raw["systems"]):
        systems.append({
            "id": row[0], "name": row[1], "galaxyId": row[2],
            "x": row[3], "y": row[4], "z": row[5],
            "starRadius": row[6], "planetCount": row[7],
            "firstOrbitRadius": row[8], "outermostOrbit": row[9],
            "maxInclination": row[10], "preset": row[11], "origin": row[12],
            "lightweight": row[13] == 1, "asteroidCount": row[16],
            "planetFrom": len(planets), "planetTo": len(planets) + len(row[15]),
        })
        for pi, p in enumerate(row[15]):
            e = extraction(seed, si, pi)
            planets.append({
                "id": p[0], "name": p[1], "systemId": row[0],
                "radius": p[2], "orbit": p[3],
                "inclinationDegrees": p[4], "ascendingNodeDegrees": p[5],
                "phase": p[6], "axialTilt": p[7], "spinSpeed": p[8], "speed": p[9],
                "materialIndex": p[10], "trailHue": p[11],
                "initialX": p[12], "initialY": p[13], "initialZ": p[14],
                **e,
            })

    clusters = [{"x": c[0], "y": c[1], "z": c[2], "radius": c[3], "galaxyIndex": c[4]}
                for c in raw.get("clusters", [])]

    def marker(rows):
        # Derelict and anomaly rows carry no galaxy column -- the reference reads them with
        # vectorFromRow(row, 2) and sets galaxyId to null. Reading from 3 shifts the position
        # one place, which silently turns the z coordinate into the row's next field
        # (tumbleSpeed for a derelict, outerSize for an anomaly) and flings the marker
        # thousands of units out of the universe.
        return [{"id": r[0], "name": r[1], "galaxyId": "", "x": r[2], "y": r[3], "z": r[4]} for r in rows]

    out = {
        "version": raw["v"], "seed": seed,
        "shipX": raw["ship"][0], "shipY": raw["ship"][1], "shipZ": raw["ship"][2],
        "starfieldStarCount": raw["source"]["starfieldStarCount"],
        "haloStarsPerGalaxy": raw["source"]["haloStarsPerGalaxy"],
        "clustersPerGalaxy": raw["source"]["clustersPerGalaxy"],
        "bridgeStarCount": raw["source"]["bridgeStarCount"],
        "nebulaCount": raw["source"]["nebulaCount"],
        "totalAsteroids": raw["q"]["beltAsteroids"],
        "galaxies": galaxies, "systems": systems, "planets": planets, "clusters": clusters,
        "derelicts": marker(raw.get("derelicts", [])),
        "anomalies": marker(raw.get("anomalies", [])),
    }

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as fh:
        json.dump(out, fh, separators=(",", ":"))

    print(f"galaxies {len(galaxies)}  systems {len(systems)}  planets {len(planets)}")
    print(f"derelicts {len(out['derelicts'])}  anomalies {len(out['anomalies'])}  "
          f"clusters {len(clusters)}  asteroids {out['totalAsteroids']}")
    print(f"wrote {OUT}  ({os.path.getsize(OUT)} bytes)")

if __name__ == "__main__":
    main()
