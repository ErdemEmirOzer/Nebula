namespace Nebula.Content
{
    /// <summary>
    /// TF-003: the single authority for what makes a content id "stable" —
    /// never null/empty, never leading/trailing whitespace, never uppercase.
    /// Existing valid ids (e.g. "resource.metal", "vehicle.scout.mk1",
    /// "station.system_logistics") already satisfy this; nothing about their
    /// format changes here, this only makes the rule explicit and checkable.
    /// </summary>
    public static class ContentIdRules
    {
        public static bool IsValidFormat(string id, out string reason)
        {
            if (string.IsNullOrEmpty(id)) { reason = "id is null or empty"; return false; }
            if (id != id.Trim()) { reason = "id has leading or trailing whitespace"; return false; }
            foreach (char c in id)
            {
                if (char.IsUpper(c)) { reason = "id contains an uppercase character"; return false; }
            }
            reason = null;
            return true;
        }

        public static bool IsValidFormat(string id) => IsValidFormat(id, out _);
    }
}
