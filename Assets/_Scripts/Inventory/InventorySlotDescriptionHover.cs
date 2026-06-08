using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Показывает краткое описание предмета при наведении на ячейку инвентаря (с задержкой).
/// </summary>
[DisallowMultipleComponent]
public sealed class InventorySlotDescriptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
	#region Constants
	private const bool c_LogDebug = true;
	private const float c_ShowDelaySeconds = 2f;
	#endregion

	#region Private Fields
	private InventorySlotView m_Slot;
	private bool m_IsPointerInside;
	private Coroutine m_ShowDelayCoroutine;
	private Vector2 m_LastPointerPosition;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_Slot = GetComponentInParent<InventorySlotView>();
		Log($"Awake. hoverTarget='{name}', slot='{(m_Slot != null ? m_Slot.name : "null")}'");
	}

	private void LateUpdate()
	{
		if (m_Slot == null)
			return;

		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			return;

		bool overSlot = InventorySlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition);
		m_LastPointerPosition = pointerPosition;

		if (m_ShowDelayCoroutine != null)
		{
			if (!overSlot)
			{
				m_IsPointerInside = false;
				CancelShowDelay();
			}

			return;
		}

		if (!InventoryItemTooltip.Instance.IsVisibleForSource(m_Slot))
			return;

		if (overSlot)
		{
			m_IsPointerInside = true;
			InventoryItemTooltip.Instance.UpdateScreenPosition(pointerPosition);
			return;
		}

		m_IsPointerInside = false;
		Log($"Raycast left slot '{m_Slot.name}', hiding tooltip.");
		InventoryItemTooltip.Instance.HideIfSource(m_Slot);
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData _eventData)
	{
		if (_eventData == null)
		{
			Log("OnPointerEnter ignored: eventData is null.");
			return;
		}

		if (_eventData.dragging)
		{
			Log("OnPointerEnter ignored: pointer is dragging.");
			return;
		}

		if (m_Slot == null)
			m_Slot = GetComponentInParent<InventorySlotView>();

		if (m_Slot == null)
		{
			Log("OnPointerEnter ignored: InventorySlotView not found in parents.");
			return;
		}

		if (!m_Slot.HasItem)
		{
			Log($"OnPointerEnter ignored: slot '{m_Slot.name}' is empty.");
			return;
		}

		m_IsPointerInside = true;
		m_LastPointerPosition = _eventData.position;
		Log($"OnPointerEnter on '{m_Slot.name}', item='{ResolveItemLabel()}', delay={c_ShowDelaySeconds:0.0}s");

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
		Log($"OnPointerExit event on '{(m_Slot != null ? m_Slot.name : name)}' (raycast will confirm).");

		if (m_ShowDelayCoroutine != null &&
		    (m_Slot == null || !InventorySlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition)))
		{
			m_IsPointerInside = false;
			CancelShowDelay();
		}
	}
	#endregion

	#region Private Methods
	private IEnumerator ShowAfterDelayCoroutine()
	{
		Log($"Delay started ({c_ShowDelaySeconds:0.0}s) for '{(m_Slot != null ? m_Slot.name : name)}'.");
		yield return new WaitForSeconds(c_ShowDelaySeconds);

		if (m_Slot == null || !m_Slot.HasItem)
		{
			Log("Delay finished but slot is empty or missing.");
			yield break;
		}

		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			pointerPosition = m_LastPointerPosition;

		if (!InventorySlotUiUtility.IsScreenPointOverSlotRaycast(m_Slot, pointerPosition))
		{
			Log("Delay finished but raycast says pointer is outside the slot.");
			m_IsPointerInside = false;
			yield break;
		}

		m_IsPointerInside = true;
		m_LastPointerPosition = pointerPosition;
		Log($"Delay finished, requesting tooltip for '{ResolveItemLabel()}'.");
		InventoryItemTooltip.Instance.ShowForSlot(m_Slot, pointerPosition);
		m_ShowDelayCoroutine = null;
	}

	private void CancelShowDelay()
	{
		if (m_ShowDelayCoroutine == null)
			return;

		StopCoroutine(m_ShowDelayCoroutine);
		m_ShowDelayCoroutine = null;
		Log($"Delay cancelled on '{(m_Slot != null ? m_Slot.name : name)}'.");
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

	private string ResolveItemLabel()
	{
		if (m_Slot == null || !m_Slot.HasItem)
			return "<empty>";

		if (m_Slot.Data.Definition != null)
			return m_Slot.Data.Definition.GetLocalizedDisplayName();

		return m_Slot.Data.DisplayName;
	}

	private static void Log(string _message)
	{
		if (!c_LogDebug)
			return;

		Debug.Log($"[ItemTooltipHover] {_message}");
	}
	#endregion
}
