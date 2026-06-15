using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Зона сброса на панель инвентаря партнёра (во время обмена — панель «земля»).
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryExchangePartnerBagDropZone : MonoBehaviour, IDropHandler
{
	#region Private Fields
	private InventoryPanelView m_BoundPartnerPanel;
	#endregion

	#region Public Methods
	public void BindPartnerPanel(InventoryPanelView _panel)
	{
		m_BoundPartnerPanel = _panel;
	}

	public static void EnsureOnPartnerPanel(InventoryPanelView _partnerPanel)
	{
		if (_partnerPanel == null)
			return;

		EnsureDropZoneOnTransform(_partnerPanel.transform, _partnerPanel);

		ScrollRect scrollRect = _partnerPanel.GetComponent<ScrollRect>();
		if (scrollRect != null)
		{
			if (scrollRect.viewport != null)
				EnsureDropZoneOnTransform(scrollRect.viewport, _partnerPanel);

			if (scrollRect.content != null)
				EnsureDropZoneOnTransform(scrollRect.content, _partnerPanel);
		}

		Transform slotsContainer = _partnerPanel.SlotsContainerTransform;
		if (slotsContainer != null)
			EnsureDropZoneOnTransform(slotsContainer, _partnerPanel);
	}
	#endregion

	#region Drop Handler
	public void OnDrop(PointerEventData eventData)
	{
		if (!InventoryExchangeController.Instance.IsActive || eventData?.pointerDrag == null)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		RtsUnitSelectionManager selectionManager = bindings != null ? bindings.SelectionManager : null;
		if (selectionManager == null)
			return;

		InventoryPanelView groundPanel = selectionManager.GroundPanel;
		if (groundPanel == null)
			return;

		InventoryPanelView resolved =
			m_BoundPartnerPanel != null ? m_BoundPartnerPanel : GetComponentInParent<InventoryPanelView>();
		if (resolved == null || resolved != groundPanel)
			return;

		Camera eventCamera = ResolveEventCamera(eventData);

		if (eventData.pointerDrag.TryGetComponent(out InventoryGroundToCharacterDrag groundDrag))
		{
			if (selectionManager.TryRouteGroundDragOnPartnerPanel(groundDrag, eventData.position, eventCamera))
				groundDrag.NotifyDropAccepted();
			return;
		}

		if (eventData.pointerDrag.TryGetComponent(out InventoryCharacterToGroundDrag characterDrag))
		{
			if (selectionManager.TryRouteCharacterDragOnPartnerPanel(characterDrag, eventData.position, eventCamera))
				characterDrag.NotifyDropAccepted();
		}
	}
	#endregion

	#region Private Methods
	private static void EnsureDropZoneOnTransform(Transform _host, InventoryPanelView _partnerPanel)
	{
		if (_host == null || _partnerPanel == null)
			return;

		InventoryExchangePartnerBagDropZone zone = _host.GetComponent<InventoryExchangePartnerBagDropZone>();
		if (zone == null)
		{
			EnsureRaycastGraphic(_host);
			zone = _host.gameObject.AddComponent<InventoryExchangePartnerBagDropZone>();
		}

		zone.BindPartnerPanel(_partnerPanel);
	}

	private static void EnsureRaycastGraphic(Transform _host)
	{
		if (_host.GetComponent<Graphic>() != null)
			return;

		Image image = _host.gameObject.AddComponent<Image>();
		image.color = new Color(1f, 1f, 1f, 0f);
		image.raycastTarget = true;
	}

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
