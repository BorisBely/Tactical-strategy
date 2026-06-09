using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row in the pre-mission unit list. Data binding is intentionally left for later — use inspector placeholders.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepUnitCellView : MonoBehaviour
{
	#region Events
	public event Action<MissionPrepUnitCellView> Clicked;
	#endregion

	#region Private Fields
	[SerializeField] private Button m_ClickArea;
	[SerializeField] private TextMeshProUGUI m_UnitRankText;
	[SerializeField] private TextMeshProUGUI m_UnitNameText;
	[SerializeField] private TextMeshProUGUI m_UnitPresetText;
	[SerializeField] private TextMeshProUGUI m_HealthStatusText;

	[Header("Выделение строки")]
	[SerializeField] private Graphic m_SelectionBackground;
	[SerializeField] private Color m_NormalBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
	[SerializeField] private Color m_SelectedBackgroundColor = new Color(0.28f, 0.45f, 0.65f, 1f);

	private bool m_InteractionEnabled = true;
	#endregion

	#region Public Properties
	public TextMeshProUGUI UnitRankText => m_UnitRankText;
	public TextMeshProUGUI UnitNameText => m_UnitNameText;
	public TextMeshProUGUI UnitPresetText => m_UnitPresetText;
	public TextMeshProUGUI HealthStatusText => m_HealthStatusText;
	public GameObject BoundUnitRoot { get; private set; }
	public bool IsSelected { get; private set; }
	public bool InteractionEnabled => m_InteractionEnabled;
	#endregion

	#region Public Methods
	public void BindToUnit(GameObject _unitRoot, string _displayName)
	{
		BoundUnitRoot = _unitRoot;

		if (m_UnitNameText != null)
			m_UnitNameText.text = _displayName ?? string.Empty;
	}

	public void SetPresetDisplayName(string _presetName)
	{
		if (m_UnitPresetText != null)
			m_UnitPresetText.text = _presetName ?? string.Empty;
	}

	public void SetRankDisplayName(string _rankName)
	{
		if (m_UnitRankText != null)
			m_UnitRankText.text = _rankName ?? string.Empty;
	}

	public void SetHealthStatusText(string _healthStatusText)
	{
		if (m_HealthStatusText != null)
			m_HealthStatusText.text = _healthStatusText ?? string.Empty;
	}

	public void SetSelected(bool _selected)
	{
		IsSelected = _selected;
		ApplySelectionVisual();
	}

	public void SetInteractionEnabled(bool _enabled)
	{
		m_InteractionEnabled = _enabled;
		if (m_ClickArea != null)
			m_ClickArea.interactable = _enabled;
	}

	public void ClearBinding()
	{
		SetSelected(false);
		BoundUnitRoot = null;
		if (m_UnitNameText != null)
			m_UnitNameText.text = string.Empty;
		SetPresetDisplayName(string.Empty);
		SetRankDisplayName(string.Empty);
		SetHealthStatusText(string.Empty);
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_SelectionBackground == null && m_ClickArea != null)
			m_SelectionBackground = m_ClickArea.GetComponent<Graphic>();

		if (m_HealthStatusText == null)
		{
			Transform healthTextTransform = transform.Find("Button/HealthText");
			if (healthTextTransform != null)
				healthTextTransform.TryGetComponent(out m_HealthStatusText);
		}

		ApplySelectionVisual();
	}

	private void OnEnable()
	{
		if (m_ClickArea != null)
			m_ClickArea.onClick.AddListener(HandleClicked);
	}

	private void OnDisable()
	{
		if (m_ClickArea != null)
			m_ClickArea.onClick.RemoveListener(HandleClicked);
	}
	#endregion

	#region Private Methods
	private void HandleClicked()
	{
		if (!m_InteractionEnabled)
			return;

		Clicked?.Invoke(this);
	}

	private void ApplySelectionVisual()
	{
		if (m_SelectionBackground == null)
			return;

		m_SelectionBackground.color = IsSelected ? m_SelectedBackgroundColor : m_NormalBackgroundColor;
	}
	#endregion
}
