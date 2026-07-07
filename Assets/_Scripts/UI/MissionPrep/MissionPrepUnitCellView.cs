using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
	[SerializeField] private TextMeshProUGUI m_ArmorStatusText;

	[Header("Выделение строки")]
	[SerializeField] private Graphic m_SelectionBackground;
	[SerializeField] private Color m_NormalBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.85f);
	[SerializeField] private Color m_HoverBackgroundColor = new Color(0.38f, 0.38f, 0.38f, 0.95f);
	[SerializeField] private Color m_SelectedBackgroundColor = new Color(0.28f, 0.45f, 0.65f, 1f);

	private bool m_IsHovered;
	private bool m_InteractionEnabled = true;
	#endregion

	#region Public Properties
	public TextMeshProUGUI UnitRankText => m_UnitRankText;
	public TextMeshProUGUI UnitNameText => m_UnitNameText;
	public TextMeshProUGUI UnitPresetText => m_UnitPresetText;
	public TextMeshProUGUI HealthStatusText => m_HealthStatusText;
	public TextMeshProUGUI ArmorStatusText => m_ArmorStatusText;
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

	public void SetArmorStatusText(string _armorStatusText)
	{
		if (m_ArmorStatusText == null)
			return;

		string status = _armorStatusText ?? string.Empty;
		m_ArmorStatusText.text = status;
		m_ArmorStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(status));
	}

	public void SetSelected(bool _selected)
	{
		IsSelected = _selected;
		ApplyBackgroundVisual();
	}

	public void SetInteractionEnabled(bool _enabled)
	{
		m_InteractionEnabled = _enabled;
		if (m_ClickArea != null)
			m_ClickArea.interactable = _enabled;

		if (!_enabled)
			SetHovered(false);
		else
			ApplyBackgroundVisual();
	}

	public void ClearBinding()
	{
		SetSelected(false);
		SetHovered(false);
		BoundUnitRoot = null;
		if (m_UnitNameText != null)
			m_UnitNameText.text = string.Empty;
		SetPresetDisplayName(string.Empty);
		SetRankDisplayName(string.Empty);
		SetHealthStatusText(string.Empty);
		SetArmorStatusText(string.Empty);
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

		if (m_ArmorStatusText == null)
		{
			Transform armorTextTransform = transform.Find("Button/ArmorText");
			if (armorTextTransform != null)
				armorTextTransform.TryGetComponent(out m_ArmorStatusText);
		}

		ApplyBackgroundVisual();
		EnsureHoverRelay();
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

	internal void SetHovered(bool _hovered)
	{
		if (m_IsHovered == _hovered)
			return;

		m_IsHovered = _hovered;
		ApplyBackgroundVisual();
	}

	private void ApplyBackgroundVisual()
	{
		if (m_SelectionBackground == null)
			return;

		if (IsSelected)
			m_SelectionBackground.color = m_SelectedBackgroundColor;
		else if (m_IsHovered && m_InteractionEnabled)
			m_SelectionBackground.color = m_HoverBackgroundColor;
		else
			m_SelectionBackground.color = m_NormalBackgroundColor;
	}

	private void EnsureHoverRelay()
	{
		if (m_ClickArea == null)
			return;

		if (m_ClickArea.GetComponent<UnitCellHoverRelay>() != null)
			return;

		m_ClickArea.gameObject.AddComponent<UnitCellHoverRelay>().Initialize(this);
	}
	#endregion

	private sealed class UnitCellHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		#region Private Fields
		private MissionPrepUnitCellView m_Owner;
		#endregion

		#region Public Methods
		public void Initialize(MissionPrepUnitCellView _owner)
		{
			m_Owner = _owner;
		}
		#endregion

		#region Event Handlers
		public void OnPointerEnter(PointerEventData _eventData)
		{
			if (_eventData == null || _eventData.dragging)
				return;

			m_Owner?.SetHovered(true);
		}

		public void OnPointerExit(PointerEventData _eventData)
		{
			m_Owner?.SetHovered(false);
		}
		#endregion
	}
}
