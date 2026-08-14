using UnityEngine;

namespace Nebula.Visuals
{
    /// <summary>
    /// The only thing <see cref="NebulaLightingDirector"/> needs to know about
    /// system streaming: which system is Full right now, and where its star
    /// is. Declared here, by the consumer, rather than in
    /// <c>Nebula.Sectors</c> — the director depends on this narrow question,
    /// not on the streaming director's whole surface, and a test can answer it
    /// without materializing a system.
    /// </summary>
    public interface IFullSystemStarSource
    {
        /// <summary>Id of the system currently materialized Full, or null when none is.</summary>
        string FullSystemId { get; }

        /// <summary>The star transform for a system, or null if that system is not materialized.</summary>
        Transform TryGetStarTransform(string systemId);
    }
}
