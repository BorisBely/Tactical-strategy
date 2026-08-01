using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space progress bar над юнитом (посадка в машину, погрузка и т.п.).
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitWorldActionProgressBar : MonoBehaviour
{
	#region Constants
	private const int c_SortingOrder = 31505;
	#endregion

	#region Serialized Fields
	[SerializeField, Min(0.5f)] private float m_HeightMeters = 2.5f;
	[SerializeField] private Color m_BackgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
	[SerializeField] private Color m_FillColor = new Color(0.22f, 0.78f, 0.34f, 1f);
	#endregion

	#region Private Fields
	private GameObject m_Root;
	private Image m_BackgroundImage;
	private Image m_FillImage;
	private RectTransform m_FillRect;
	private Vector2 m_FillAnchorMin;
	private Vector2 m_FillAnchorMax;
	private Transform m_CachedCameraTransform;
	private bool m_IsVisible;
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
	#endregion

	#region Unity Lifecycle
	private void LateUpdate()
	{
		UpdateBillboard();
	}

	private void OnDisable()
	{
		Hide();
	}
	#endregion

	#region Public Methods
	public void Show()
	{
		EnsureUi();
		m_IsVisible = true;
		if (m_Root != null)
		{
			m_Root.SetActive(true);
			SetProgress(0f);
		}
	}

	public void SetProgress(float _normalized)
	{
		if (m_FillRect == null)
			return;

		float progress = Mathf.Clamp01(_normalized);
		m_FillRect.anchorMin = m_FillAnchorMin;
		m_FillRect.anchorMax = new Vector2(progress, m_FillAnchorMax.y);
		m_FillRect.offsetMin = Vector2.zero;
		m_FillRect.offsetMax = Vector2.zero;
	}

	public void Hide()
	{
		m_IsVisible = false;
		if (m_Root != null)
			m_Root.SetActive(false);
	}

	public static UnitWorldActionProgressBar GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;

		if (!_unitObject.TryGetComponent(out UnitWorldActionProgressBar bar))
			bar = _unitObject.AddComponent<UnitWorldActionProgressBar>();
		return bar;
	}
	#endregion

	#region Private Methods
	private void EnsureUi()
	{
		if (m_Root != null)
			return;

		m_Root = new GameObject("ActionProgressBar", typeof(RectTransform));
		RectTransform rootRt = m_Root.GetComponent<RectTransform>();
		rootRt.SetParent(transform, false);
		rootRt.sizeDelta = new Vector2(1.4f, 0.16f);

		Canvas canvas = m_Root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.WorldSpace;
		canvas.sortingOrder = c_SortingOrder;

		if (m_Root.TryGetComponent(out GraphicRaycaster raycaster))
			Destroy(raycaster);

		GameObject bgGo = new GameObject("Background", typeof(RectTransform));
		RectTransform bgRt = bgGo.GetComponent<RectTransform>();
		bgRt.SetParent(m_Root.transform, false);
		bgRt.anchorMin = Vector2.zero;
		bgRt.anchorMax = Vector2.one;
		bgRt.offsetMin = Vector2.zero;
		bgRt.offsetMax = Vector2.zero;

		m_BackgroundImage = bgGo.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(m_BackgroundImage);
		m_BackgroundImage.color = m_BackgroundColor;
		m_BackgroundImage.raycastTarget = false;

		GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
		m_FillRect = fillGo.GetComponent<RectTransform>();
		m_FillRect.SetParent(bgGo.transform, false);
		m_FillAnchorMin = Vector2.zero;
		m_FillAnchorMax = Vector2.one;
		m_FillRect.anchorMin = m_FillAnchorMin;
		m_FillRect.anchorMax = m_FillAnchorMax;
		m_FillRect.offsetMin = new Vector2(0.015f, 0.015f);
		m_FillRect.offsetMax = new Vector2(-0.015f, -0.015f);
		m_FillRect.pivot = new Vector2(0f, 0.5f);

		m_FillImage = fillGo.AddComponent<Image>();
		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(m_FillImage);
		m_FillImage.color = m_FillColor;
		m_FillImage.raycastTarget = false;

		m_Root.SetActive(false);
	}

	private void UpdateBillboard()
	{
		if (!m_IsVisible || m_Root == null || !m_Root.activeSelf)
			return;

		if (m_CachedCameraTransform == null)
		{
			Camera cam = Camera.main;
			if (cam == null)
				return;
			m_CachedCameraTransform = cam.transform;
		}

		Transform barTransform = m_Root.transform;
		barTransform.position = transform.position + Vector3.up * m_HeightMeters;
		barTransform.rotation = m_CachedCameraTransform.rotation;
	}
	#endregion
}
