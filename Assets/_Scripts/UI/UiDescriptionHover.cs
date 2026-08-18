using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Показывает текстовую подсказку при наведении на UI-элемент (с задержкой 2 с).
/// </summary>
[DisallowMultipleComponent]
public sealed class UiDescriptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
	#region Constants
	private const float c_ShowDelaySeconds = 0.35f;
	#endregion

	#region Private Fields
	private string m_Title = string.Empty;
	private string m_Description = string.Empty;
	private RectTransform m_HoverRect;
	private Coroutine m_ShowDelayCoroutine;
	private Vector2 m_LastPointerPosition;
	#endregion

	#region Public Methods
	public void Configure(string _title, string _description, RectTransform _hoverRect = null)
	{
		m_Title = _title ?? string.Empty;
		m_Description = _description ?? string.Empty;
		m_HoverRect = _hoverRect != null ? _hoverRect : transform as RectTransform;

		if (UiDescriptionTooltip.Instance.IsVisibleForSource(this))
			TryShowTooltip(m_LastPointerPosition);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_HoverRect == null)
			m_HoverRect = transform as RectTransform;
	}

	private void LateUpdate()
	{
		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			return;

		bool overTarget = IsPointerOverTarget(pointerPosition);
		m_LastPointerPosition = pointerPosition;

		if (m_ShowDelayCoroutine != null)
		{
			if (!overTarget)
				CancelShowDelay();

			return;
		}

		if (!UiDescriptionTooltip.Instance.IsVisibleForSource(this))
			return;

		if (overTarget)
		{
			UiDescriptionTooltip.Instance.UpdateScreenPosition(pointerPosition);
			return;
		}

		UiDescriptionTooltip.Instance.HideIfSource(this);
	}
	#endregion

	#region Event Handlers
	public void OnPointerEnter(PointerEventData _eventData)
	{
		if (_eventData == null || _eventData.dragging)
			return;

		if (string.IsNullOrWhiteSpace(m_Title) && string.IsNullOrWhiteSpace(m_Description))
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

		if (m_ShowDelayCoroutine != null && !IsPointerOverTarget(pointerPosition))
			CancelShowDelay();
	}
	#endregion

	#region Private Methods
	private IEnumerator ShowAfterDelayCoroutine()
	{
		yield return new WaitForSeconds(c_ShowDelaySeconds);

		if (string.IsNullOrWhiteSpace(m_Title) && string.IsNullOrWhiteSpace(m_Description))
			yield break;

		if (!TryGetPointerScreenPosition(out Vector2 pointerPosition))
			pointerPosition = m_LastPointerPosition;

		if (!IsPointerOverTarget(pointerPosition))
			yield break;

		m_LastPointerPosition = pointerPosition;
		TryShowTooltip(pointerPosition);
		m_ShowDelayCoroutine = null;
	}

	private void TryShowTooltip(Vector2 _screenPosition)
	{
		if (string.IsNullOrWhiteSpace(m_Title) && string.IsNullOrWhiteSpace(m_Description))
			return;

		Canvas hostCanvas = GetComponentInParent<Canvas>();
		if (hostCanvas == null)
			return;

		UiDescriptionTooltip.Instance.Show(this, m_Title, m_Description, _screenPosition, hostCanvas);
	}

	private void CancelShowDelay()
	{
		if (m_ShowDelayCoroutine == null)
			return;

		StopCoroutine(m_ShowDelayCoroutine);
		m_ShowDelayCoroutine = null;
	}

	private bool IsPointerOverTarget(Vector2 _screenPosition)
	{
		if (m_HoverRect == null)
			return false;

		Canvas canvas = m_HoverRect.GetComponentInParent<Canvas>();
		Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
			? canvas.worldCamera
			: null;

		return RectTransformUtility.RectangleContainsScreenPoint(m_HoverRect, _screenPosition, eventCamera);
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
