using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Empty-state для панели доступного снаряжения Mission Prep.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepAvailableEquipmentHintsUi : MonoBehaviour
{
	#region Constants
	private const string c_HelperObjectName = "PrepAvailableHelperText";
	private const string c_EmptyObjectName = "PrepAvailableEmptyState";
	private const string c_EmptyKey = "mission_prep.equipment.empty_category";
	private const string c_EmptyFallback = "Нет предметов в категории";
	#endregion

	#region Serialized Fields
	[SerializeField] private TMP_Text m_EmptyStateText;
	#endregion

	#region Public Methods
	public static MissionPrepAvailableEquipmentHintsUi EnsureOnPanelRoot(Transform _panelRoot)
	{
		if (_panelRoot == null)
			return null;

		MissionPrepAvailableEquipmentHintsUi hints =
			_panelRoot.GetComponent<MissionPrepAvailableEquipmentHintsUi>();
		if (hints == null)
			hints = _panelRoot.gameObject.AddComponent<MissionPrepAvailableEquipmentHintsUi>();

		hints.EnsureUi();
		return hints;
	}

	public void SetEmptyVisible(bool _visible)
	{
		EnsureUi();
		if (m_EmptyStateText != null)
			m_EmptyStateText.gameObject.SetActive(_visible);
	}

	public void RefreshLocalizedText()
	{
		EnsureUi();
		if (m_EmptyStateText != null)
			m_EmptyStateText.text = LocalizationManager.Get(c_EmptyKey, c_EmptyFallback);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureUi();
		RefreshLocalizedText();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		RefreshLocalizedText();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		RefreshLocalizedText();
	}

	private void EnsureUi()
	{
		DestroyLeftoverHelperLabel();

		if (m_EmptyStateText == null)
		{
			m_EmptyStateText = EnsureLabel(
				c_EmptyObjectName,
				new Vector2(0f, -40f),
				new Vector2(360f, 48f),
				15f,
				new Color(0.65f, 0.65f, 0.65f, 0.95f));
			m_EmptyStateText.gameObject.SetActive(false);
		}
	}

	private TMP_Text EnsureLabel(
		string _objectName,
		Vector2 _anchoredPosition,
		Vector2 _size,
		float _fontSize,
		Color _color)
	{
		Transform existing = transform.Find(_objectName);
		GameObject go = existing != null
			? existing.gameObject
			: new GameObject(_objectName, typeof(RectTransform));
		if (existing == null)
			go.transform.SetParent(transform, false);

		RectTransform rt = go.transform as RectTransform;
		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = _anchoredPosition;
		rt.sizeDelta = _size;

		TMP_Text text = go.GetComponent<TextMeshProUGUI>();
		if (text == null)
			text = go.AddComponent<TextMeshProUGUI>();
		text.fontSize = _fontSize;
		text.color = _color;
		text.alignment = TextAlignmentOptions.Center;
		text.textWrappingMode = TextWrappingModes.Normal;
		text.overflowMode = TextOverflowModes.Ellipsis;
		text.raycastTarget = false;

		Graphic graphic = text;
		graphic.raycastTarget = false;
		return text;
	}

	private void DestroyLeftoverHelperLabel()
	{
		Transform leftover = transform.Find(c_HelperObjectName);
		if (leftover != null)
			Destroy(leftover.gameObject);
	}
	#endregion
}
