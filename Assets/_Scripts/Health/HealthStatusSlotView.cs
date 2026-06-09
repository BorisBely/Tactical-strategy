using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HealthStatusSlotView : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private TMP_Text m_StatusText;
	[SerializeField] private TMP_Text m_ConditionText;
	[SerializeField] private GameObject m_OccupiedRoot;
	[SerializeField] private GameObject m_EmptyRoot;
	#endregion

	#region Private Fields
	private HealthStatusEntryData m_Data;
	private bool m_HasEntry;
	private bool m_RuntimeSpawned;
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
		RefreshVisuals();
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
	}

	private void RefreshVisuals()
	{
		if (m_StatusText != null)
			m_StatusText.text = m_HasEntry ? m_Data.GetLocalizedStatusText() : string.Empty;

		if (m_ConditionText != null)
		{
			string conditionText = m_HasEntry ? m_Data.GetLocalizedConditionText() : string.Empty;
			m_ConditionText.text = conditionText;
			m_ConditionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(conditionText));
		}

		if (m_OccupiedRoot != null)
			m_OccupiedRoot.SetActive(m_HasEntry);
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(!m_HasEntry);
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
