using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Fade + лёгкий slide для корня окна инвентаря / Mission Prep.
/// </summary>
[DisallowMultipleComponent]
public sealed class InventoryUiWindowMotion : MonoBehaviour
{
	#region Constants
	private const float c_OpenDuration = 0.12f;
	private const float c_CloseDuration = 0.1f;
	private const float c_SlidePixels = 16f;
	#endregion

	#region Private Fields
	private CanvasGroup m_CanvasGroup;
	private RectTransform m_Rect;
	private Vector2 m_RestAnchoredPosition;
	private Coroutine m_Routine;
	private bool m_IsOpen;
	private bool m_RestCaptured;
	#endregion

	#region Public Properties
	public bool IsOpen => m_IsOpen;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureComponents();
		if (!gameObject.activeSelf)
		{
			m_IsOpen = false;
			m_CanvasGroup.alpha = 0f;
			m_CanvasGroup.blocksRaycasts = false;
			m_CanvasGroup.interactable = false;
		}
	}
	#endregion

	#region Public Methods
	public static InventoryUiWindowMotion Ensure(GameObject _root)
	{
		if (_root == null)
			return null;

		InventoryUiWindowMotion motion = _root.GetComponent<InventoryUiWindowMotion>();
		if (motion == null)
			motion = _root.AddComponent<InventoryUiWindowMotion>();
		motion.EnsureComponents();
		return motion;
	}

	public void SnapClosed()
	{
		StopRoutine();
		EnsureComponents();
		m_IsOpen = false;
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;
		RestorePosition();
		if (gameObject.activeSelf)
			gameObject.SetActive(false);
	}

	public void Open()
	{
		StopRoutine();
		EnsureComponents();
		CaptureRestPosition();
		m_IsOpen = true;
		if (!gameObject.activeSelf)
			gameObject.SetActive(true);

		m_Routine = StartCoroutine(CoOpen());
	}

	public void Close(Action _onClosed = null)
	{
		EnsureComponents();
		if (!gameObject.activeSelf)
		{
			m_IsOpen = false;
			_onClosed?.Invoke();
			return;
		}

		StopRoutine();
		m_IsOpen = false;
		m_CanvasGroup.blocksRaycasts = false;
		m_CanvasGroup.interactable = false;
		m_Routine = StartCoroutine(CoClose(_onClosed));
	}
	#endregion

	#region Private Methods
	private void EnsureComponents()
	{
		if (m_CanvasGroup == null)
		{
			m_CanvasGroup = GetComponent<CanvasGroup>();
			if (m_CanvasGroup == null)
				m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
		}

		if (m_Rect == null)
			m_Rect = transform as RectTransform;
	}

	private void CaptureRestPosition()
	{
		if (m_Rect == null || m_RestCaptured)
			return;

		m_RestAnchoredPosition = m_Rect.anchoredPosition;
		m_RestCaptured = true;
	}

	private void RestorePosition()
	{
		if (m_Rect != null && m_RestCaptured)
			m_Rect.anchoredPosition = m_RestAnchoredPosition;
	}

	private void StopRoutine()
	{
		if (m_Routine == null)
			return;

		StopCoroutine(m_Routine);
		m_Routine = null;
	}

	private IEnumerator CoOpen()
	{
		CaptureRestPosition();
		float duration = Mathf.Max(0.01f, c_OpenDuration);
		float t = 0f;
		m_CanvasGroup.alpha = 0f;
		m_CanvasGroup.blocksRaycasts = true;
		m_CanvasGroup.interactable = true;
		if (m_Rect != null)
			m_Rect.anchoredPosition = m_RestAnchoredPosition + new Vector2(0f, -c_SlidePixels);

		while (t < duration)
		{
			t += Time.unscaledDeltaTime;
			float u = Mathf.Clamp01(t / duration);
			float eased = 1f - (1f - u) * (1f - u);
			m_CanvasGroup.alpha = eased;
			if (m_Rect != null)
				m_Rect.anchoredPosition = Vector2.LerpUnclamped(
					m_RestAnchoredPosition + new Vector2(0f, -c_SlidePixels),
					m_RestAnchoredPosition,
					eased);
			yield return null;
		}

		m_CanvasGroup.alpha = 1f;
		RestorePosition();
		m_Routine = null;
	}

	private IEnumerator CoClose(Action _onClosed)
	{
		CaptureRestPosition();
		float duration = Mathf.Max(0.01f, c_CloseDuration);
		float startAlpha = m_CanvasGroup.alpha;
		Vector2 startPos = m_Rect != null ? m_Rect.anchoredPosition : m_RestAnchoredPosition;
		Vector2 endPos = m_RestAnchoredPosition + new Vector2(0f, -c_SlidePixels);
		float t = 0f;

		while (t < duration)
		{
			t += Time.unscaledDeltaTime;
			float u = Mathf.Clamp01(t / duration);
			float eased = u * u;
			m_CanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);
			if (m_Rect != null)
				m_Rect.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, eased);
			yield return null;
		}

		m_CanvasGroup.alpha = 0f;
		RestorePosition();
		gameObject.SetActive(false);
		m_Routine = null;
		_onClosed?.Invoke();
	}
	#endregion
}
