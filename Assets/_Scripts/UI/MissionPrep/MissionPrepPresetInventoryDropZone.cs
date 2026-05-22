using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Зона сброса с панели доступного снаряжения в инвентарь активного пресета (снимок, не CharacterInventory).
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepPresetInventoryDropZone : MonoBehaviour, IDropHandler
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

		if (!eventData.pointerDrag.TryGetComponent(out MissionPrepAvailableToPresetDrag drag))
			return;

		m_Coordinator.TryAcceptAvailableDrag(drag);
	}
	#endregion

	#region Public Methods
	public static void EnsureOnPresetPanel(InventoryPanelView _presetPanel, MissionPrepLoadoutCoordinator _coordinator)
	{
		if (_presetPanel == null)
			return;

		EnsureDropZoneOnTransform(_presetPanel.transform, _coordinator);

		ScrollRect scrollRect = _presetPanel.GetComponent<ScrollRect>();
		if (scrollRect != null)
		{
			if (scrollRect.viewport != null)
				EnsureDropZoneOnTransform(scrollRect.viewport, _coordinator);

			if (scrollRect.content != null)
				EnsureDropZoneOnTransform(scrollRect.content, _coordinator);
		}

		Transform slotsContainer = _presetPanel.SlotsContainerTransform;
		if (slotsContainer != null)
			EnsureDropZoneOnTransform(slotsContainer, _coordinator);
	}
	#endregion

	#region Private Methods
	private static void EnsureDropZoneOnTransform(Transform _host, MissionPrepLoadoutCoordinator _coordinator)
	{
		if (_host == null)
			return;

		MissionPrepPresetInventoryDropZone zone = _host.GetComponent<MissionPrepPresetInventoryDropZone>();
		if (zone == null)
		{
			EnsureRaycastGraphic(_host);
			zone = _host.gameObject.AddComponent<MissionPrepPresetInventoryDropZone>();
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
