using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Зона сброса из рюкзака на панель «земля». Вешается на <b>Viewport</b> скролла или на ячейку; ссылка с <see cref="InventoryPanelView"/> опциональна.
/// </summary>
[DisallowMultipleComponent]
public class InventoryGroundDropZone : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private InventoryPanelView m_BoundGroundPanel;
	#endregion

	#region Public Methods
	public void BindGroundPanel(InventoryPanelView _panel)
	{
		m_BoundGroundPanel = _panel;
	}
	#endregion

	#region Drop Handler
	public void OnDrop(PointerEventData eventData)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		PlayerInventoryCoordinator coordinator = bindings != null ? bindings.Coordinator : null;
		if (coordinator == null || eventData.pointerDrag == null)
			return;

		InventoryPanelView groundPanel = coordinator.GroundPanel;
		if (groundPanel == null)
			return;

		InventoryPanelView resolved =
			m_BoundGroundPanel != null ? m_BoundGroundPanel : GetComponentInParent<InventoryPanelView>();
		if (resolved == null || resolved != groundPanel)
			return;

		if (!eventData.pointerDrag.TryGetComponent<InventoryCharacterToGroundDrag>(out var drag))
			return;

		if (coordinator.TryAcceptDraggedCharacterSlot(drag))
			drag.NotifyDropAccepted();
	}
	#endregion
}
