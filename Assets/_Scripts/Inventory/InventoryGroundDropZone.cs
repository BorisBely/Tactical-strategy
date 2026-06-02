using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Зона сброса из рюкзака на панель «земля». Вешается на <b>Viewport</b> скролла или на ячейку; ссылка с <see cref="InventoryPanelView"/> опциональна.
/// Также принимает сброс установленных модулей оружия (<see cref="RuntimeModificationSlotDrag"/>).
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

	/// <summary>Повесить зону drop на корень панели, viewport/content скролла и контейнер ячеек.</summary>
	public static void EnsureOnGroundPanel(InventoryPanelView _groundPanel)
	{
		if (_groundPanel == null)
			return;

		EnsureDropZoneOnTransform(_groundPanel.transform, _groundPanel);

		ScrollRect scrollRect = _groundPanel.GetComponent<ScrollRect>();
		if (scrollRect != null)
		{
			if (scrollRect.viewport != null)
				EnsureDropZoneOnTransform(scrollRect.viewport, _groundPanel);

			if (scrollRect.content != null)
				EnsureDropZoneOnTransform(scrollRect.content, _groundPanel);
		}

		Transform slotsContainer = _groundPanel.SlotsContainerTransform;
		if (slotsContainer != null)
			EnsureDropZoneOnTransform(slotsContainer, _groundPanel);
	}
	#endregion

	#region Drop Handler
	public void OnDrop(PointerEventData eventData)
	{
		if (eventData.pointerDrag == null)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		RuntimeInventoryModificationCoordinator modificationCoordinator = RuntimeInventoryModificationCoordinator.Instance;

		InventoryPanelView groundPanel = selectionManager != null ? selectionManager.GroundPanel : null;
		if (groundPanel == null)
			return;

		InventoryPanelView resolved =
			m_BoundGroundPanel != null ? m_BoundGroundPanel : GetComponentInParent<InventoryPanelView>();
		if (resolved == null || resolved != groundPanel)
			return;

		if (eventData.pointerDrag.TryGetComponent(out RuntimeModificationSlotDrag modificationDrag))
		{
			if (modificationCoordinator != null &&
			    modificationCoordinator.TryEjectModificationSlotToGround(modificationDrag))
				modificationDrag.NotifyDropAccepted();
			return;
		}

		if (selectionManager == null)
			return;

		if (!eventData.pointerDrag.TryGetComponent<InventoryCharacterToGroundDrag>(out var drag))
			return;

		if (selectionManager.TryAcceptDraggedCharacterSlot(drag))
			drag.NotifyDropAccepted();
	}
	#endregion

	#region Private Methods
	private static void EnsureDropZoneOnTransform(Transform _host, InventoryPanelView _groundPanel)
	{
		if (_host == null || _groundPanel == null)
			return;

		InventoryGroundDropZone zone = _host.GetComponent<InventoryGroundDropZone>();
		if (zone == null)
		{
			EnsureRaycastGraphic(_host);
			zone = _host.gameObject.AddComponent<InventoryGroundDropZone>();
		}

		zone.BindGroundPanel(_groundPanel);
	}

	private static void EnsureRaycastGraphic(Transform _host)
	{
		if (_host.GetComponent<Graphic>() != null)
			return;

		Image image = _host.gameObject.AddComponent<Image>();
		image.color = new Color(1f, 1f, 1f, 0f);
		image.raycastTarget = true;
	}
	#endregion
}
