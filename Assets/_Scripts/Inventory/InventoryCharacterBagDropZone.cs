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
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		if (selectionManager == null || eventData.pointerDrag == null)
			return;

		if (m_BoundBagPanel != null && selectionManager.CharacterInventoryPanel != m_BoundBagPanel)
			return;

		if (!eventData.pointerDrag.TryGetComponent<InventoryGroundToCharacterDrag>(out var drag))
			return;

		if (selectionManager.TryAcceptDraggedGroundSlot(drag))
			drag.NotifyDropAccepted();
	}
	#endregion
}
