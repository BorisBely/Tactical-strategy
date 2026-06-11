using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Зона сброса из инвентаря пресета на панель доступного снаряжения (удаление из снимка пресета).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepAvailableEquipmentDropZone : MonoBehaviour, IDropHandler
{
	#region Serialized Fields
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void OnEnable()
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}
	#endregion

	#region Drop Handler
	public void OnDrop(PointerEventData eventData)
	{
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;

		if (m_Coordinator == null || eventData.pointerDrag == null)
			return;

		if (eventData.pointerDrag.TryGetComponent(out MissionPrepModificationSlotDrag modificationDrag))
		{
			if (m_Coordinator.TryEjectModificationSlotToAvailable(modificationDrag))
				modificationDrag.NotifyDropAccepted();
			return;
		}

		if (!eventData.pointerDrag.TryGetComponent(out MissionPrepPresetToAvailableDrag drag))
			return;

		if (!drag.HasResolvedSlot)
			return;

		if (!m_Coordinator.TryRemovePresetInventorySlot(drag.IsMainHandSlot, drag.IsHeadSlot, drag.BagIndex))
			return;

		drag.NotifyDropAccepted();
	}
	#endregion

	#region Public Methods
	public static void EnsureOnAvailablePanel(InventoryPanelView _availablePanel, MissionPrepLoadoutCoordinator _coordinator)
	{
		if (_availablePanel == null)
			return;

		EnsureDropZoneOnTransform(_availablePanel.transform, _coordinator);

		ScrollRect scrollRect = _availablePanel.GetComponent<ScrollRect>();
		if (scrollRect != null)
		{
			if (scrollRect.viewport != null)
				EnsureDropZoneOnTransform(scrollRect.viewport, _coordinator);

			if (scrollRect.content != null)
				EnsureDropZoneOnTransform(scrollRect.content, _coordinator);
		}

		Transform slotsContainer = _availablePanel.SlotsContainerTransform;
		if (slotsContainer != null)
			EnsureDropZoneOnTransform(slotsContainer, _coordinator);
	}
	#endregion

	#region Private Methods
	private static void EnsureDropZoneOnTransform(Transform _host, MissionPrepLoadoutCoordinator _coordinator)
	{
		if (_host == null)
			return;

		MissionPrepAvailableEquipmentDropZone zone = _host.GetComponent<MissionPrepAvailableEquipmentDropZone>();
		if (zone == null)
		{
			EnsureRaycastGraphic(_host);
			zone = _host.gameObject.AddComponent<MissionPrepAvailableEquipmentDropZone>();
		}

		if (_coordinator != null)
			zone.m_Coordinator = _coordinator;
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
