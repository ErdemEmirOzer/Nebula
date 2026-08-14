using System.Collections.Generic;
using UnityEngine;

namespace Nebula.Remote
{
    /// <summary>
    /// VS-016A: a headless, non-MonoBehaviour equivalent of a live
    /// <see cref="Combat.EnemyAgent"/> — same shared pure state
    /// (<see cref="Combat.EnemyAiState"/>, <see cref="Extraction.Durability"/>,
    /// <see cref="Combat.EnemyDef"/>) and the same shared resolvers
    /// (<see cref="Combat.EnemyAiResolver"/>, <see cref="Combat.DamageResolver"/>)
    /// a live enemy already uses — never a second stat/rule authority, only a
    /// second (deterministic, dt-driven, frame-rate-independent) adapter around
    /// the exact same rules. Field set mirrors <see cref="Extraction.EnemyDto"/>
    /// exactly so capture/restore never needs a lossy conversion.
    /// </summary>
    public class RemoteEnemyRuntime
    {
        public string Id { get; }
        public string DefId { get; }
        public Combat.EnemyDef Def { get; }
        public Combat.EnemyAiState Ai { get; }
        public Extraction.Durability Durability { get; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }

        public bool Destroyed => Durability.Disabled;

        public readonly List<(string itemId, float amount)> LooterCargo = new List<(string, float)>();
        public string StructureTargetId { get; set; }
        public float DisruptRemaining { get; set; }
        public float DisruptImmunityRemaining { get; set; }
        public float TelegraphRemaining { get; set; }

        public RemoteEnemyRuntime(string id, string defId, Combat.EnemyDef def, Combat.EnemyAiState ai,
            Extraction.Durability durability, Vector3 position, Vector3 velocity)
        {
            Id = id; DefId = defId; Def = def; Ai = ai; Durability = durability; Position = position; Velocity = velocity;
        }

        public static RemoteEnemyRuntime Create(string id, Combat.EnemyDef def, Vector3 position)
        {
            var durability = new Extraction.Durability(def.ShieldMax, def.ArmorMax, def.HullMax, 1f);
            return new RemoteEnemyRuntime(id, def.Id, def, new Combat.EnemyAiState(def), durability, position, Vector3.zero);
        }

        public void ApplyDamage(float amount, Combat.DamageProfile profile) => Combat.DamageResolver.Apply(Durability, amount, profile);

        public bool IsDisrupted => DisruptRemaining > 0f;

        /// <summary>Mirrors EnemyAgent.TryDisrupt's immunity-gate exactly -- an enemy just recovering from a disrupt cannot be immediately re-disrupted.</summary>
        public bool TryDisrupt(float disruptSeconds)
        {
            if (DisruptImmunityRemaining > 0f) return false;
            DisruptRemaining = Mathf.Max(DisruptRemaining, disruptSeconds);
            return true;
        }

        /// <summary>Per-second disrupt/immunity/telegraph countdown -- pure dt bookkeeping, same shape as EnemyAgent's own per-frame countdowns just called once per simulated second instead of every real frame.</summary>
        public void TickTimers(float dt)
        {
            if (DisruptRemaining > 0f)
            {
                DisruptRemaining = Mathf.Max(0f, DisruptRemaining - dt);
                if (DisruptRemaining <= 0f) DisruptImmunityRemaining = Def.DisruptImmunitySeconds;
            }
            else if (DisruptImmunityRemaining > 0f)
            {
                DisruptImmunityRemaining = Mathf.Max(0f, DisruptImmunityRemaining - dt);
            }
            if (TelegraphRemaining > 0f) TelegraphRemaining = Mathf.Max(0f, TelegraphRemaining - dt);
        }
    }
}
