using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Зона сброса на панель инвентаря персонажа: земля → сумка/экипировка, снятие оружия в сумку.
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
		if (selectionManager == null || eventData?.pointerDrag == null)
			return;

		if (m_BoundBagPanel != null && selectionManager.CharacterInventoryPanel != m_BoundBagPanel)
			return;

		Camera eventCamera = ResolveEventCamera(eventData);

		if (eventData.pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
		{
			if (selectionManager.TryRouteGroundDragOnCharacterPanel(groundDrag, eventData.position, eventCamera))
				groundDrag.NotifyDropAccepted();
			return;
		}

		if (eventData.pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
		{
			if (selectionManager.TryRouteCharacterDragOnCharacterPanel(characterDrag, eventData.position, eventCamera))
				characterDrag.NotifyDropAccepted();
		}
	}
	#endregion

	#region Private Methods
	private static Camera ResolveEventCamera(PointerEventData eventData)
	{
		if (eventData == null)
			return null;

		if (eventData.pressEventCamera != null)
			return eventData.pressEventCamera;

		GameObject pointerEnter = eventData.pointerEnter;
		if (pointerEnter == null)
			return null;

		Canvas canvas = pointerEnter.GetComponentInParent<Canvas>();
		if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
			return null;

		return canvas.worldCamera;
	}
	#endregion
}
