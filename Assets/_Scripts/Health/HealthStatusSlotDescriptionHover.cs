using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HealthStatusSlotDescriptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
	#region Constants
	private const float c_ShowDelaySeconds = 0.35f;
	#endregion

	#region Private Fields
	private HealthStatusSlotView m_Slot;
	private Coroutine m_ShowDelayCoroutine;
	private Vector2 m_LastPointerPosition;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Slot = GetComponentInParent<HealthStatusSlotView>();
	}

	private void LateUpdate()
	{
		if (m_Slot == null)
			return;

		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			return;

		bool overSlot = HealthStatusSlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition);
		m_LastPointerPosition = pointerPosition;

		if (m_ShowDelayCoroutine != null)
		{
			if (!overSlot)
				CancelShowDelay();

			return;
		}

		if (!HealthStatusTooltip.Instance.IsVisibleForSource(m_Slot))
			return;

		if (overSlot)
		{
			HealthStatusTooltip.Instance.UpdateScreenPosition(pointerPosition);
			return;
		}

		HealthStatusTooltip.Instance.HideIfSource(m_Slot);
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData _eventData)
	{
		if (_eventData == null || _eventData.dragging)
			return;

		if (m_Slot == null)
			m_Slot = GetComponentInParent<HealthStatusSlotView>();

		if (m_Slot == null || !m_Slot.HasEntry || !m_Slot.HasTooltipContent)
			return;

		m_LastPointerPosition = _eventData.position;
		CancelShowDelay();
		m_ShowDelayCoroutine = StartCoroutine(ShowAfterDelayCoroutine());
	}

	public void OnPointerMove(PointerEventData _eventData)
	{
		if (_eventData == null)
			return;

		m_LastPointerPosition = _eventData.position;
	}

	public void OnPointerExit(PointerEventData _eventData)
	{
		Vector2 pointerPosition = _eventData != null ? _eventData.position : m_LastPointerPosition;

		if (m_ShowDelayCoroutine != null &&
		    (m_Slot == null || !HealthStatusSlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition)))
			CancelShowDelay();
	}
	#endregion

	#region Private Methods
	private IEnumerator ShowAfterDelayCoroutine()
	{
		yield return new WaitForSeconds(c_ShowDelaySeconds);

		if (m_Slot == null || !m_Slot.HasEntry || !m_Slot.HasTooltipContent)
			yield break;

		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			pointerPosition = m_LastPointerPosition;

		if (!HealthStatusSlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition))
			yield break;

		m_LastPointerPosition = pointerPosition;
		HealthStatusTooltip.Instance.ShowForSlot(m_Slot, pointerPosition);
		m_ShowDelayCoroutine = null;
	}

	private void CancelShowDelay()
	{
		if (m_ShowDelayCoroutine == null)
			return;

		StopCoroutine(m_ShowDelayCoroutine);
		m_ShowDelayCoroutine = null;
	}

	private static bool TryGetPointerScreenPosition(out Vector2 _screenPosition)
	{
		Mouse mouse = Mouse.current;
		if (mouse != null)
		{
			_screenPosition = mouse.position.ReadValue();
			return true;
		}

		_screenPosition = Input.mousePosition;
		return true;
	}
	#endregion
}
