using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class MissionPrepModificationOutsideClick : MonoBehaviour
{
	#region Private Fields
	private MissionPrepLoadoutCoordinator m_Coordinator;
	private Coroutine m_DeferredCollapse;
	#endregion

	#region Public Methods
	public static void EnsureOn(MissionPrepLoadoutCoordinator _coordinator)
	{
		if (_coordinator == null)
			return;

		if (!_coordinator.TryGetComponent(out MissionPrepModificationOutsideClick handler))
			handler = _coordinator.gameObject.AddComponent<MissionPrepModificationOutsideClick>();

		handler.Bind(_coordinator);
	}

	public void Bind(MissionPrepLoadoutCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
	}
	#endregion

	#region Unity Lifecycle
	private void OnDisable()
	{
		CancelDeferredCollapse();
	}

	private void Update()
	{
		if (!Application.isPlaying || m_Coordinator == null)
			return;

		Mouse mouse = Mouse.current;
		if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
			return;

		if (!m_Coordinator.HasExpandedEmptyModificationSlots())
			return;

		if (m_Coordinator.ShouldSuppressOutsideClickCollapse())
			return;

		if (MissionPrepModificationDragContext.HasActiveModificationItem)
			return;

		if (ShouldIgnoreOutsideClick(mouse.position.ReadValue()))
			return;

		ScheduleDeferredCollapse();
	}
	#endregion

	#region Private Methods
	private void ScheduleDeferredCollapse()
	{
		if (m_DeferredCollapse != null)
			return;

		m_DeferredCollapse = StartCoroutine(CollapseAfterCurrentPointerFrame());
	}

	private void CancelDeferredCollapse()
	{
		if (m_DeferredCollapse == null)
			return;

		StopCoroutine(m_DeferredCollapse);
		m_DeferredCollapse = null;
	}

	private IEnumerator CollapseAfterCurrentPointerFrame()
	{
		yield return null;
		m_DeferredCollapse = null;

		if (!Application.isPlaying || m_Coordinator == null)
			yield break;

		if (!m_Coordinator.HasExpandedEmptyModificationSlots())
			yield break;

		if (m_Coordinator.ShouldSuppressOutsideClickCollapse())
			yield break;

		if (MissionPrepModificationDragContext.HasActiveModificationItem)
			yield break;

		m_Coordinator.CollapseEmptyModificationSlots();
	}
	private bool ShouldIgnoreOutsideClick(Vector2 _screenPosition)
	{
		if (EventSystem.current == null)
			return false;

		var results = new List<RaycastResult>();
		var pointerData = new PointerEventData(EventSystem.current)
		{
			position = _screenPosition
		};
		EventSystem.current.RaycastAll(pointerData, results);

		for (int i = 0; i < results.Count; i++)
		{
			GameObject hit = results[i].gameObject;
			if (hit == null)
				continue;

			if (hit.GetComponentInParent<MissionPrepModificationSlotView>() != null)
				return true;

			InventorySlotView slot = hit.GetComponentInParent<InventorySlotView>();
			if (slot == null || !slot.HasItem)
				continue;

			if (ItemModificationUtility.IsModificationItem(slot.Data))
				return true;

			if (ItemModificationUtility.IsModifiableWeapon(slot.Data.Definition))
				return true;
		}

		return false;
	}
	#endregion
}
