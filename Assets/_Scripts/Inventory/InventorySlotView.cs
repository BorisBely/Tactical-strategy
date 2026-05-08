using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Одна ячейка инвентаря: имя в TMP, иконка через <see cref="Image"/> и <see cref="ItemDefinition.Icon"/>.
/// </summary>
[DisallowMultipleComponent]
public class InventorySlotView : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private TMP_Text m_NameText;
	[Tooltip("Дочерний Image под иконку (Sprite); если пусто — ищется в детях (не фон ячейки).")]
	[SerializeField] private Image m_IconImage;
	[SerializeField] private GameObject m_OccupiedRoot;
	[SerializeField] private GameObject m_EmptyRoot;
	#endregion

	#region Private Fields
	private InventorySlotRuntimeData m_Data;
	private bool m_HasItem;
	private bool m_RuntimeSpawned;
	#endregion

	#region Public Properties
	public bool HasItem => m_HasItem;
	public InventorySlotRuntimeData Data => m_Data;
	/// <summary>Ячейка создана из префаба в рантайме (учёт при Clear / переносе).</summary>
	public bool IsRuntimeSpawned => m_RuntimeSpawned;
	#endregion

	#region Unity Lifecycle
	private void Reset()
	{
		if (m_NameText == null)
			m_NameText = GetComponentInChildren<TMP_Text>(true);
		if (m_IconImage == null)
			m_IconImage = FindChildIconImage();
	}

	private void Awake()
	{
		if (m_IconImage == null)
			m_IconImage = FindChildIconImage();
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

	public void SetItem(InventorySlotRuntimeData _data)
	{
		m_Data = _data;
		m_HasItem = !_data.IsEmpty;

		RefreshVisuals();
	}

	public void Clear()
	{
		m_Data = default;
		m_HasItem = false;
		RefreshVisuals();
	}

	public bool TryTakeItem(out InventorySlotRuntimeData _data)
	{
		if (!m_HasItem)
		{
			_data = default;
			return false;
		}

		_data = m_Data;
		Clear();
		return true;
	}
	#endregion

	#region Private Methods
	/// <summary>Иконка на дочернем объекте; не берём <see cref="Image"/> на корне (фон ячейки).</summary>
	private Image FindChildIconImage()
	{
		foreach (Image image in GetComponentsInChildren<Image>(true))
		{
			if (image.gameObject != gameObject)
				return image;
		}

		return null;
	}

	private void RefreshVisuals()
	{
		if (m_NameText != null)
			m_NameText.text = m_HasItem ? FormatLabel(m_Data) : string.Empty;

		if (m_OccupiedRoot != null)
			m_OccupiedRoot.SetActive(m_HasItem);
		if (m_EmptyRoot != null)
			m_EmptyRoot.SetActive(!m_HasItem);

		Sprite iconSprite = null;
		if (m_HasItem && m_Data.Definition != null)
			iconSprite = m_Data.Definition.Icon;

		if (m_IconImage != null)
		{
			m_IconImage.sprite = iconSprite;
			bool showIcon = m_HasItem && iconSprite != null;
			m_IconImage.gameObject.SetActive(showIcon);
		}
	}

	private static string FormatLabel(InventorySlotRuntimeData _data)
	{
		string baseLabel;
		if (!string.IsNullOrWhiteSpace(_data.LocalizationKey))
			baseLabel = LocalizationManager.Get(_data.LocalizationKey, _data.DisplayName);
		else if (_data.Definition != null)
			baseLabel = _data.Definition.GetLocalizedDisplayName();
		else
			baseLabel = _data.DisplayName;

		if (_data.InstanceState != null)
		{
			MagazineRuntimeState magazineState = _data.InstanceState.MagazineState;
			if (magazineState != null && magazineState.Definition != null)
				return $"{baseLabel} [{magazineState.CurrentAmmoCount}/{magazineState.Definition.Capacity}]";

			AmmoContainerRuntimeState ammoContainerState = _data.InstanceState.AmmoContainerState;
			if (ammoContainerState != null && ammoContainerState.AmmoDefinition != null)
				return $"{baseLabel} x{ammoContainerState.CurrentAmmoCount}";
		}

		return baseLabel;
	}

	private void HandleLanguageChanged()
	{
		if (!isActiveAndEnabled)
			return;

		RefreshVisuals();
	}
	#endregion
}

