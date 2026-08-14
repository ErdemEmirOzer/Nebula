using System;
using System.Collections.Generic;
using System.Linq;

namespace Nebula.Content
{
    /// <summary>
    /// TF-003: thrown by <see cref="ContentCatalogValidator"/> (boot-time, before
    /// any world/ship/director is created) and by <see cref="RuntimeCatalog"/>'s
    /// GetRequired* accessors (a missing authoritative definition is always a
    /// programming/content error, never a case to silently fall back from).
    /// </summary>
    public sealed class ContentValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ContentValidationException(string singleError) : base(singleError)
        {
            Errors = new[] { singleError };
        }

        public ContentValidationException(IReadOnlyList<string> errors) : base(BuildMessage(errors))
        {
            Errors = errors;
        }

        private static string BuildMessage(IReadOnlyList<string> errors) =>
            $"Content validation failed with {errors.Count} error(s):\n" + string.Join("\n", errors.Select(e => "- " + e));
    }
}
