using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nebula.UI.Design;
using Nebula.UI.Shell;
using Nebula.Visuals;

namespace Nebula.UI.Bench
{
    /// <summary>
    /// What a drag source and a drop zone need from the page that owns them.
    /// <para>
    /// The handlers below carry no rules and no state of their own: they translate
    /// Unity's pointer events into four questions and hand every one of them to the
    /// page, which is the only thing that reads the bench and the hold. A handler
    /// that decided for itself whether a drop was legal would be a second validator,
    /// and two validators drift.
    /// </para>
    /// </summary>
    public interface INebulaBenchDragHost
    {
        /// <summary>
        /// Starts a drag from a cargo row, or refuses it. False means no ghost is
        /// shown and no drop will be accepted — an item nothing can process is not
        /// draggable at all rather than draggable and then rejected.
        /// </summary>
        bool BeginBenchDrag(string itemId);

        /// <summary>Moves the ghost. Pure presentation; nothing is committed while it moves.</summary>
        void MoveBenchDrag(Vector2 screenPosition, Camera eventCamera);

        /// <summary>
        /// Ends the gesture whether or not it landed on the bench. Always runs, so
        /// a pointer released over empty space cannot leave a ghost behind.
        /// </summary>
        void EndBenchDrag();

        /// <summary>The pointer entered or left the bench while a drag is live.</summary>
        void HoverBench(bool entering);

        /// <summary>The pointer was released over the bench.</summary>
        void DropOnBench();
    }

    /// <summary>
    /// A cargo row a player can pick up.
    /// <para>
    /// Unity's own <see cref="IBeginDragHandler"/>/<see cref="IDragHandler"/>/
    /// <see cref="IEndDragHandler"/> rather than a new input path: the EventSystem
    /// already owns the drag threshold, the pointer capture and the release, and it
    /// already knows about touch and about a second pointer. A hand-rolled
    /// mouse-down/mouse-up pair would have to reimplement all four and would still
    /// lose the drag when the pointer left the window.
    /// </para>
    /// </summary>
    public sealed class NebulaBenchDragSource : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private INebulaBenchDragHost host;

        /// <summary>The item this row stands for.</summary>
        public string ItemId { get; private set; }

        /// <summary>True while this row is the one being dragged.</summary>
        public bool Dragging { get; private set; }

        public static NebulaBenchDragSource Attach(GameObject target, INebulaBenchDragHost owner, string itemId)
        {
            var source = target.GetComponent<NebulaBenchDragSource>();
            if (source == null) source = target.AddComponent<NebulaBenchDragSource>();
            source.host = owner;
            source.ItemId = itemId;
            return source;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (host == null) return;
            Dragging = host.BeginBenchDrag(ItemId);
            if (Dragging) host.MoveBenchDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Dragging) host.MoveBenchDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!Dragging) return;
            Dragging = false;
            // Always, and after any OnDrop the EventSystem has already delivered.
            // Unity raises OnDrop first and OnEndDrag second, so the page has taken
            // the drop by the time the ghost is put away.
            host.EndBenchDrag();
        }

        /// <summary>
        /// A row that is switched off mid-gesture — the page was closed, the row was
        /// hidden — ends its own drag rather than leaving the page believing one is
        /// still live.
        /// </summary>
        private void OnDisable()
        {
            if (!Dragging) return;
            Dragging = false;
            host?.EndBenchDrag();
        }
    }

    /// <summary>
    /// The bench's drop target.
    /// <para>
    /// Enter and exit are answered while a drag is live so the zone can say
    /// <i>before</i> the release whether it would take the item — a drop zone that
    /// only speaks after the release makes the player undo something to find out.
    /// </para>
    /// </summary>
    public sealed class NebulaBenchDropZone : MonoBehaviour,
        IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private INebulaBenchDragHost host;

        public static NebulaBenchDropZone Attach(GameObject target, INebulaBenchDragHost owner)
        {
            var zone = target.GetComponent<NebulaBenchDropZone>();
            if (zone == null) zone = target.AddComponent<NebulaBenchDropZone>();
            zone.host = owner;
            return zone;
        }

        /// <summary>Only a drag this page started may be dropped here.</summary>
        private bool Ours(PointerEventData eventData)
        {
            if (eventData?.pointerDrag == null) return false;
            var source = eventData.pointerDrag.GetComponent<NebulaBenchDragSource>();
            return source != null && source.Dragging;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (host == null || !Ours(eventData)) return;
            host.DropOnBench();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (host != null && Ours(eventData)) host.HoverBench(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            host?.HoverBench(false);
        }

        private void OnDisable() => host?.HoverBench(false);
    }

    /// <summary>
    /// The thing under the cursor while a stack is being carried.
    /// <para>
    /// Exactly one of these exists per page and it is re-used for every drag, so a
    /// thousand drags create one object rather than a thousand. The alternative —
    /// instantiate on begin, destroy on end — leaks one ghost for every gesture
    /// that ends in a way the page did not anticipate, and "the pointer left the
    /// window" is one of those ways.
    /// </para>
    /// <para>
    /// It never blocks a raycast. A ghost that did would sit between the pointer and
    /// the drop zone it is being carried towards, and the drop would never land.
    /// </para>
    /// </summary>
    public sealed class NebulaDragGhost
    {
        private readonly RectTransform root;
        private readonly RectTransform parent;
        private readonly Image icon;
        private readonly NebulaUiFactory.LabelHandle nameLabel;
        private readonly NebulaUiFactory.LabelHandle amountLabel;

        /// <summary>True while the ghost is on screen.</summary>
        public bool Visible => root != null && root.gameObject.activeSelf;

        /// <summary>Test-facing: what the ghost currently says it is carrying.</summary>
        public string NameText => nameLabel.Value;

        /// <inheritdoc cref="NameText"/>
        public string AmountText => amountLabel.Value;

        /// <summary>The ghost's own rect, so a fixture can read where it went.</summary>
        public RectTransform Rect => root;

        public NebulaDragGhost(RectTransform host)
        {
            parent = host;
            Image panel = NebulaUiFactory.Panel(host, "Drag Ghost",
                NebulaUiAssetCatalog.Row(NebulaUiAssetCatalog.RowState.Selected));
            root = panel.rectTransform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.sizeDelta = new Vector2(240f, 44f);

            // The whole ghost, chrome and contents, is transparent to raycasts. The
            // factory already leaves panels and labels non-blocking; a CanvasGroup
            // states it for anything added later as well.
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            icon = NebulaUiFactory.Icon(root, "Icon", null, 20f);
            NebulaUiFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(20f, 20f));

            nameLabel = NebulaUiFactory.Label(root, "Name", "", NebulaUiDesignTokens.SizeCompact,
                NebulaUiDesignTokens.InkPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            NebulaUiFactory.Place(nameLabel.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(40f, 6f), new Vector2(190f, 16f));

            amountLabel = NebulaUiFactory.Label(root, "Amount", "", NebulaUiDesignTokens.SizeCaption,
                NebulaUiDesignTokens.InkSecondary, TextAnchor.MiddleLeft, FontStyle.Normal,
                NebulaUiDesignTokens.TrackingCaption);
            NebulaUiFactory.Place(amountLabel.Text.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(40f, -10f), new Vector2(190f, 14f));

            root.gameObject.SetActive(false);
        }

        /// <summary>Shows the real item, its real name and what is really available.</summary>
        public void Show(string itemId, int available)
        {
            // A gesture can outlive its page by a frame when a session tears down
            // mid-drag; a destroyed rect compares equal to null through Unity's own
            // operator, so this is a real check rather than a defensive one.
            if (root == null) return;
            Sprite sprite = NebulaIconResolver.Item(itemId);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            nameLabel.Value = NebulaBenchMessages.ItemName(itemId);
            amountLabel.Value = $"KULLANILABİLİR {available}";
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            root.SetAsLastSibling();
        }

        /// <summary>Follows the pointer, in the ghost parent's own local space.</summary>
        public void MoveTo(Vector2 screenPosition, Camera eventCamera)
        {
            if (root == null || parent == null || !root.gameObject.activeSelf) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, screenPosition, eventCamera, out Vector2 local))
                root.anchoredPosition = local + new Vector2(14f, -14f);
        }

        /// <summary>Puts it away. Idempotent — a second call is not an error.</summary>
        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }
    }
}
