using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Панель снаряжения: дропдаун выбора из <see cref="MissionPrepWeaponSelectionLibrary"/> с живыми портретами
/// через <see cref="WeaponEquippedVisualPreviewPortraitFactory"/>, опционально последней строкой — «Создать новый пресет».
/// На префабе TMP Dropdown нужны назначенные Caption Image и Item Icon (Image), см. TMP_Dropdown.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepEquipmentPanelView : MonoBehaviour
{
	#region Events
	public event Action<ItemDefinition, int> WeaponSelected;
	public event Action CreateNewPresetRequested;
	#endregion

	#region Private Fields
	[SerializeField] private TMP_Dropdown m_PresetDropdown;

	[Tooltip("Если не задан — строки только с текстом.")]
	[SerializeField] private MissionPrepWeaponSelectionLibrary m_WeaponLibrary;

	[SerializeField] private WeaponEquippedVisualPreviewPortraitFactory m_PortraitFactory;

	[SerializeField] private bool m_AppendCreateNewPresetEntry = true;

	private int m_LastCreateNewIndex = -1;
	private int m_WeaponSlotCount;

	private bool m_SuppressDropdownEvent;

	private static readonly Color DefaultOptionTint = Color.white;
	#endregion

	#region Public Methods
	public void SetVisible(bool _visible)
	{
		gameObject.SetActive(_visible);
	}

	/// <summary>
	/// Пересборка списка оружий (и при необходимости перерисовка runtime-портретов).
	/// </summary>
	public void RefreshPresetDropdown()
	{
		if (m_PresetDropdown == null)
			return;

		m_SuppressDropdownEvent = true;
		m_PresetDropdown.ClearOptions();

		List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
		m_WeaponSlotCount = m_WeaponLibrary != null ? m_WeaponLibrary.WeaponCount : 0;

		if (m_WeaponLibrary == null)
			Debug.LogWarning($"{nameof(MissionPrepEquipmentPanelView)} ({name}): назначьте {nameof(MissionPrepWeaponSelectionLibrary)}.", this);
		else
		{
			for (int i = 0; i < m_WeaponLibrary.WeaponCount; i++)
			{
				ItemDefinition weaponDef = m_WeaponLibrary.GetWeapon(i);
				if (weaponDef == null)
				{
					Debug.LogWarning($"{nameof(MissionPrepEquipmentPanelView)}: пустая запись [{i}] в библиотеке.", this);
					options.Add(new TMP_Dropdown.OptionData($"(empty #{i})", null, DefaultOptionTint));
					continue;
				}

				string displayName = weaponDef.GetLocalizedDisplayName();
				Sprite portrait = null;
				if (m_PortraitFactory != null)
				{
					portrait = m_PortraitFactory.GetOrCreatePortraitSprite(weaponDef);
					if (portrait == null)
						Debug.LogWarning(
							$"{nameof(MissionPrepEquipmentPanelView)}: не удалось собрать портрет для «{weaponDef.name}» — проверьте слой и префаб.",
							weaponDef);
				}

				options.Add(new TMP_Dropdown.OptionData(displayName, portrait, DefaultOptionTint));
			}
		}

		if (m_AppendCreateNewPresetEntry)
		{
			string createLabel = LocalizationManager.Get("mission_prep.equipment.create_new_preset");
			options.Add(new TMP_Dropdown.OptionData(createLabel, null, DefaultOptionTint));
			m_LastCreateNewIndex = options.Count - 1;
		}
		else
			m_LastCreateNewIndex = -1;

		if (options.Count == 0)
		{
			m_SuppressDropdownEvent = false;
			Debug.LogWarning($"{nameof(MissionPrepEquipmentPanelView)}: список оружий пуст — добавьте ItemDefinition на библиотеку.", this);
			return;
		}

		m_PresetDropdown.AddOptions(options);
		m_PresetDropdown.SetValueWithoutNotify(0);
		m_PresetDropdown.RefreshShownValue();
		m_SuppressDropdownEvent = false;
	}

	public ItemDefinition ResolveWeaponDefinitionAtDropdownIndex(int _index)
	{
		if (m_WeaponLibrary == null || _index < 0 || _index >= m_WeaponSlotCount)
			return null;

		return m_WeaponLibrary.GetWeapon(_index);
	}
	#endregion

	#region Unity Lifecycle
	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.AddListener(HandlePresetDropdownValueChanged);

		if (gameObject.activeInHierarchy)
			RefreshPresetDropdown();
	}

	private void OnDisable()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		if (m_PresetDropdown != null)
			m_PresetDropdown.onValueChanged.RemoveListener(HandlePresetDropdownValueChanged);
	}
	#endregion

	#region Private Methods
	private void HandleLanguageChanged()
	{
		int previous = m_PresetDropdown != null ? m_PresetDropdown.value : 0;
		RefreshPresetDropdown();
		if (m_PresetDropdown != null && m_PresetDropdown.options.Count > 0)
		{
			int clamped = Mathf.Clamp(previous, 0, m_PresetDropdown.options.Count - 1);
			m_PresetDropdown.SetValueWithoutNotify(clamped);
			m_PresetDropdown.RefreshShownValue();
		}
	}

	private void HandlePresetDropdownValueChanged(int _index)
	{
		if (m_SuppressDropdownEvent)
			return;

		if (m_LastCreateNewIndex >= 0 && _index == m_LastCreateNewIndex)
		{
			CreateNewPresetRequested?.Invoke();
			return;
		}

		if (m_WeaponLibrary == null || _index < 0 || _index >= m_WeaponSlotCount)
			return;

		ItemDefinition def = m_WeaponLibrary.GetWeapon(_index);
		WeaponSelected?.Invoke(def, _index);
	}
	#endregion
}
