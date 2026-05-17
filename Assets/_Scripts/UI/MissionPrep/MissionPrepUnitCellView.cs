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
	[SerializeField] private Image m_RankIcon;
	[SerializeField] private TextMeshProUGUI m_UnitNameText;
	[SerializeField] private TextMeshProUGUI m_UnitPresetText;
	#endregion

	#region Public Properties
	public Image RankIcon => m_RankIcon;
	public TextMeshProUGUI UnitNameText => m_UnitNameText;
	public TextMeshProUGUI UnitPresetText => m_UnitPresetText;
	public GameObject BoundUnitRoot { get; private set; }
	#endregion

	#region Public Methods
	public void BindToUnit(GameObject _unitRoot, string _displayName)
	{
		BoundUnitRoot = _unitRoot;

		if (m_UnitNameText != null)
			m_UnitNameText.text = _displayName ?? string.Empty;
	}

	public void ClearBinding()
	{
		BoundUnitRoot = null;
		if (m_UnitNameText != null)
			m_UnitNameText.text = string.Empty;
	}
	#endregion

	#region Unity Lifecycle
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
		Clicked?.Invoke(this);
	}
	#endregion
}
