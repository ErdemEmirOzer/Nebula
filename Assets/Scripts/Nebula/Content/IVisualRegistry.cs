namespace Nebula.Content
{
    /// <summary>TF-003: read-only lookup surface for visual bindings — never a gameplay authority (see <see cref="VisualBinding"/>).</summary>
    public interface IVisualRegistry
    {
        bool TryGetBinding(string contentId, out VisualBinding binding);
    }
}
