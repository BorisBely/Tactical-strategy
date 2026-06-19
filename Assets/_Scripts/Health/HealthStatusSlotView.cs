using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HealthStatusSlotView : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private TMP_Text m_StatusText;
	[SerializeField] private TMP_Text m_ConditionText;
	[SerializeField] private Image m_HealProgressImage;
	[SerializeField] private GameObject m_OccupiedRoot;
	[SerializeField] private GameObject m_EmptyRoot;
	[SerializeField] private Color m_ActiveInjuryColor = new Color(1f, 0.35f, 0.35f, 1f);
	[SerializeField] private Color m_StabilizedInjuryColor = new Color(0.35f, 0.95f, 0.45f, 1f);
	[SerializeField] private Color m_HealProgressColor = new Color(0.2f, 0.82f, 0.35f, 0.95f);
	[SerializeField] private Color m_EmptyTextColor = Color.white;
	#endregion

	#region Private Fields
	private HealthStatusEntryData m_Data;
	private bool m_HasEntry;
	private bool m_RuntimeSpawned;
	private RectTransform m_HealProgressRect;
	private Vector2 m_HealProgressAnchorMin;
	private Vector2 m_HealProgressAnchorMax;
	private Vector2 m_HealProgressSizeDelta;
	#endregion

	#region Public Properties
	public bool HasEntry => m_HasEntry;
	public bool IsRuntimeSpawned => m_RuntimeSpawned;
	public HealthStatusEntryData EntryData => m_Data;

	public bool HasTooltipContent =>
		m_HasEntry &&
		(!string.IsNullOrWhiteSpace(m_Data.GetLocalizedDescriptionText()) ||
		 !string.IsNullOrWhiteSpace(m_Data.GetLocalizedDebuffsText()));
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureReferences();
		HealthStatusSlotUiUtility.EnsureDescriptionHover(this);
	}

	private void Reset()
	{
		EnsureReferences();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Public Methods
	public void MarkRuntimeSpawned()
	{
		m_RuntimeSpawned = true;
	}

	public void SetEntry(HealthStatusEntryData _data)
	{
		m_Data = _data;
		m_HasEntry = !_data.IsEmpty;
		RefreshVisuals();
	}

	public void Clear()
	{
		m_Data = default;
		m_HasEntry = false;
		SetHealProgressVisible(false);
		RefreshVisuals();
	}

	public void SetHealProgressVisible(bool _visible)
	{
		if (m_HealProgressImage == null)
			return;

		m_HealProgressImage.gameObject.SetActive(_visible);
		if (!_visible)
			ApplyHealProgressRect01(0f);
	}

	public void SetHealProgress01(float _progress01)
	{
		if (m_HealProgressImage == null)
			return;

		ApplyHealProgressRect01(_progress01);
	}
	#endregion

	#region Private Methods
	private void EnsureReferences()
	{
		if (m_StatusText == null || m_ConditionText == null)
		{
			TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
			if (m_StatusText == null && texts.Length > 0)
				m_StatusText = texts[0];
			if (m_ConditionText == null && texts.Length > 1)
				m_ConditionText = texts[1];
		}

		if (m_HealProgressImage == null)
		{
			Transform progressTransform = transform.Find("HealthCellImage");
			if (progressTransform != null)
				m_HealProgressImage = progressTransform.GetComponent<Image>();
		}

		ConfigureHealProgressImage();
	}

	private void ConfigureHealProgressImage()
	{
		if (m_HealProgressImage == null)
			return;

		InventorySlotUiUtility.EnsureImageCanRenderSolidColor(m_HealProgressImage);

		m_HealProgressImage.color = m_HealProgressColor;
		m_HealProgressImage.raycastTarget = false;
		m_HealProgressImage.type = Image.Type.Simple;
		m_HealProgressImage.preserveAspect = false;

		m_HealProgressRect = m_HealProgressImage.rectTransform;
		m_HealProgressAnchorMin = m_HealProgressRect.anchorMin;
		m_HealProgressAnchorMax = m_HealProgressRect.anchorMax;
		m_HealProgressSizeDelta = m_HealProgressRect.sizeDelta;
		m_HealProgressRect.pivot = new Vector2(0f, m_HealProgressRect.pivot.y);

		ApplyHealProgressRect01(0f);
		m_HealProgressImage.gameObject.SetActive(false);
	}

	private void ApplyHealProgressRect01(float _progress01)
	{
		if (m_HealProgressRect == null)
			return;

		float progress = Mathf.Clamp01(_progress01);
		m_HealProgressRect.anchorMin = m_HealProgressAnchorMin;
		m_HealProgressRect.anchorMax = new Vector2(progress, m_HealProgressAnchorMax.y);
		m_HealProgressRect.sizeDelta = new Vector2(0f, m_HealProgressSizeDelta.y);
	}

	private void RefreshVisuals()
	{
		if (m_StatusText != null)
		{
			m_StatusText.text = m_HasEntry ? m_Data.GetLocalizedStatusText() : string.Empty;
			m_StatusText.color = ResolveTextColor();
		}

		if (m_ConditionText != null)
		{
			string conditionText = m_HasEntry ? m_Data.GetLocalizedConditionText() : string.Empty;
			m_ConditionText.text = conditionText;
			m_ConditionText.color = ResolveTextColor();
			m_ConditionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(conditionText));
		}

		if (m_OccupiedRoot != null)
			m_OccupiedRoot.SetActive(m_HasEntry);
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(!m_HasEntry);
	}

	private Color ResolveTextColor()
	{
		if (!m_HasEntry)
			return m_EmptyTextColor;

		return m_Data.IsStabilized
			? m_StabilizedInjuryColor
			: m_ActiveInjuryColor;
	}

	private void HandleLanguageChanged()
	{
		if (!isActiveAndEnabled)
			return;

		RefreshVisuals();
	}
	#endregion
}

[System.Serializable]
public struct HealthStatusEntryData
{
	public string StatusDisplayName;
	public string StatusLocalizationKey;
	public string ConditionDisplayName;
	public string ConditionLocalizationKey;
	public string DescriptionDisplayName;
	public string DescriptionLocalizationKey;
	public string[] DebuffLocalizationKeys;
	public string DebuffsDisplayText;
	public int SortPriority;
	public int InjuryIndex;
	public bool IsStabilized;
	public float AccumulatedLethalPressure;

	public bool IsEmpty => string.IsNullOrWhiteSpace(StatusDisplayName) && string.IsNullOrWhiteSpace(StatusLocalizationKey);

	public string GetLocalizedStatusText()
	{
		if (!string.IsNullOrWhiteSpace(StatusLocalizationKey))
			return LocalizationManager.Get(StatusLocalizationKey, StatusDisplayName);

		return StatusDisplayName;
	}

	public string GetLocalizedConditionText()
	{
		if (!string.IsNullOrWhiteSpace(ConditionLocalizationKey))
			return LocalizationManager.Get(ConditionLocalizationKey, ConditionDisplayName);

		return ConditionDisplayName;
	}

	public string GetLocalizedDescriptionText()
	{
		if (!string.IsNullOrWhiteSpace(DescriptionLocalizationKey))
			return LocalizationManager.Get(DescriptionLocalizationKey, DescriptionDisplayName);

		return DescriptionDisplayName;
	}

	public string GetLocalizedDebuffsText()
	{
		if (!string.IsNullOrWhiteSpace(DebuffsDisplayText))
			return DebuffsDisplayText;

		if (DebuffLocalizationKeys == null || DebuffLocalizationKeys.Length == 0)
			return string.Empty;

		string header = LocalizationManager.Get("health.tooltip.debuffs_header", "Дебафы:");
		var builder = new System.Text.StringBuilder();
		builder.AppendLine(header);

		for (int i = 0; i < DebuffLocalizationKeys.Length; i++)
		{
			if (string.IsNullOrWhiteSpace(DebuffLocalizationKeys[i]))
				continue;

			builder.Append("- ");
			builder.AppendLine(LocalizationManager.Get(DebuffLocalizationKeys[i]));
		}

		return builder.ToString().TrimEnd();
	}

	public static HealthStatusEntryData FromLocalizedKey(string _localizationKey, string _fallback = "",
		string _conditionLocalizationKey = null, string _conditionFallback = "")
	{
		return new HealthStatusEntryData
		{
			StatusDisplayName = string.IsNullOrWhiteSpace(_fallback) ? LocalizationManager.Get(_localizationKey) : _fallback,
			StatusLocalizationKey = _localizationKey,
			ConditionDisplayName = string.IsNullOrWhiteSpace(_conditionFallback)
				? (!string.IsNullOrWhiteSpace(_conditionLocalizationKey) ? LocalizationManager.Get(_conditionLocalizationKey) : string.Empty)
				: _conditionFallback,
			ConditionLocalizationKey = _conditionLocalizationKey
		};
	}

	public static HealthStatusEntryData FromDisplayName(string _displayName, string _conditionDisplayName = null)
	{
		return new HealthStatusEntryData
		{
			StatusDisplayName = _displayName,
			StatusLocalizationKey = null,
			ConditionDisplayName = _conditionDisplayName,
			ConditionLocalizationKey = null
		};
	}
}
