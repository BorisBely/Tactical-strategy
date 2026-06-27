using TMPro;
using UnityEngine;

/// <summary>
/// Binds a <see cref="TextMeshProUGUI"/> to a <see cref="LocalizationManager"/> key and refreshes on language change.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class LocalizedTextMeshProUGUI : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private string m_LocalizationKey;
	[SerializeField] private TextMeshProUGUI m_Text;
	#endregion

	#region Public Methods
	public void SetLocalizationKey(string _localizationKey)
	{
		m_LocalizationKey = _localizationKey;
		RefreshText();
	}

	public bool TryGetLocalizationKey(out string _key)
	{
		_key = m_LocalizationKey;
		return !string.IsNullOrWhiteSpace(m_LocalizationKey);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Text == null)
			m_Text = GetComponent<TextMeshProUGUI>();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		RefreshText();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		RefreshText();
	}

	private void RefreshText()
	{
		if (m_Text == null || string.IsNullOrWhiteSpace(m_LocalizationKey))
			return;

		m_Text.text = LocalizationManager.Get(m_LocalizationKey);
	}
	#endregion

#if UNITY_EDITOR
	[ContextMenu("Preview Current Language")]
	private void ContextPreview()
	{
		Awake();
		RefreshText();
	}
#endif
}
