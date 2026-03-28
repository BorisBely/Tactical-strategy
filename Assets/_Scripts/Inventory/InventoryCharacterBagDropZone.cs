using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Зона сброса с «земли» в инвентарь. Живёт на Canvas; координатор берётся из <see cref="InventoryScreenBindings"/>.
/// Панель персонажа ссылается на этот компонент в <see cref="InventoryPanelView"/>.
/// </summary>
[DisallowMultipleComponent]
public class InventoryCharacterBagDropZone : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private InventoryPanelView m_BoundBagPanel;
	#endregion

	#region Public Methods
	public void BindBagPanel(InventoryPanelView _panel)
	{
		m_BoundBagPanel = _panel;
	}
	#endregion

	#region Drop Handler
	public void OnDrop(PointerEventData eventData)
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		PlayerInventoryCoordinator coordinator = bindings != null ? bindings.Coordinator : null;
		if (coordinator == null || eventData.pointerDrag == null)
			return;

		if (m_BoundBagPanel != null && coordinator.CharacterInventoryPanel != m_BoundBagPanel)
			return;

		if (!eventData.pointerDrag.TryGetComponent<InventoryGroundToCharacterDrag>(out var drag))
			return;

		if (coordinator.TryAcceptDraggedGroundSlot(drag))
			drag.NotifyDropAccepted();
	}
	#endregion
}
