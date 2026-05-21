using UnityEngine;

/// <summary>
/// Экран предмиссии: один активный пресет на юнита. Броня и инвентарь — часть снимка пресета.
/// </summary>
[DisallowMultipleComponent]
public sealed class MissionPrepLoadoutCoordinator : MonoBehaviour
{
	#region Static Access
	private static MissionPrepLoadoutCoordinator s_Instance;

	public static MissionPrepLoadoutCoordinator Instance => s_Instance;
	#endregion

	#region Serialized Fields
	[SerializeField] private MissionPrepEquipmentPresetCatalog m_PresetCatalog;
	[SerializeField] private InventoryPanelView m_PresetInventoryPanel;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Private Fields
	private MissionPrepUnitPresetState m_BoundPresetState;
	private CharacterInventory m_BoundInventory;
	#endregion

	#region Public Properties
	public InventoryPanelView PresetInventoryPanel => m_PresetInventoryPanel;
	public CharacterInventory BoundInventory => m_BoundInventory;
	public MissionPrepUnitPresetState BoundPresetState => m_BoundPresetState;
	public bool HasBoundUnit => m_BoundPresetState != null;
	#endregion

	#region Public Methods
	public void Configure(MissionPrepEquipmentPresetCatalog _catalog, InventoryPanelView _inventoryPanel)
	{
		if (_catalog != null)
			m_PresetCatalog = _catalog;

		if (_inventoryPanel != null)
			m_PresetInventoryPanel = _inventoryPanel;
	}

	/// <summary>Выбор юнита: сохранить предыдущего, загрузить снимок активного пресета нового.</summary>
	public void BindUnit(GameObject _unitRoot)
	{
		SaveActivePresetBeforeRebind();

		m_BoundPresetState = _unitRoot != null
			? MissionPrepUnitPresetState.GetOrCreate(_unitRoot, 0)
			: null;

		m_BoundInventory = _unitRoot != null
			? _unitRoot.GetComponentInChildren<CharacterInventory>(true)
			: null;

		if (m_BoundPresetState != null)
		{
			m_BoundPresetState.EnsurePresetSnapshots(GetPresetSlotCount());
			m_BoundPresetState.EnsureDefaultsFromCatalog(m_PresetCatalog);
			m_BoundPresetState.EnsureSnapshotDefaultsFromCatalog(
				m_BoundPresetState.PresetCatalogIndex,
				m_PresetCatalog);
		}

		ApplyActivePresetToRuntime();
	}

	public void ClearUnitBinding()
	{
		SaveActivePresetBeforeRebind();

		m_BoundPresetState = null;
		m_BoundInventory = null;

		if (m_PresetInventoryPanel != null)
			m_PresetInventoryPanel.ClearAllSlots();
	}

	/// <summary>Сохранить правки (инвентарь + броня) в снимок текущего активного пресета.</summary>
	public void SaveActivePresetFromRuntime()
	{
		if (m_BoundPresetState == null || m_BoundInventory == null)
			return;

		m_BoundPresetState.SaveActivePresetFromRuntime(m_BoundInventory);
	}

	/// <summary>Смена пресета: сохранить текущий снимок, переключить индекс, применить снимок нового пресета целиком.</summary>
	public void SwitchToPreset(int _newPresetIndex)
	{
		if (m_BoundPresetState == null)
			return;

		int clamped = m_PresetCatalog != null
			? m_PresetCatalog.ClampPresetIndex(_newPresetIndex)
			: Mathf.Max(0, _newPresetIndex);

		m_BoundPresetState.ChangeActivePresetIndex(clamped, m_BoundInventory, GetPresetSlotCount());
		m_BoundPresetState.EnsureSnapshotDefaultsFromCatalog(clamped, m_PresetCatalog);
		ApplyActivePresetToRuntime();
	}

	/// <summary>Смена брони — правка активного пресета (в его снимке), не отдельное состояние юнита.</summary>
	public void SetActivePresetArmor(int _armorIndex)
	{
		if (m_BoundPresetState == null)
			return;

		int clamped = m_PresetCatalog != null
			? m_PresetCatalog.ClampArmorIndex(_armorIndex)
			: Mathf.Clamp(_armorIndex, 0, MissionPrepUnitArmorVisualController.ArmorVariantCount - 1);

		SaveActivePresetFromRuntime();
		m_BoundPresetState.SetArmorOnActivePreset(clamped);
		ApplyArmorVisual();
	}

	public void NotifyInventoryMutated()
	{
		SaveActivePresetFromRuntime();
		RepaintInventoryPanel();
	}

	public bool TryResolveInventorySlot(
		InventorySlotView _slot,
		out bool _isMainHandEquipmentSlot,
		out int _bagIndex)
	{
		_isMainHandEquipmentSlot = false;
		_bagIndex = -1;

		if (_slot == null || m_PresetInventoryPanel == null || m_BoundInventory == null)
			return false;

		int containerIndex = m_PresetInventoryPanel.GetInventorySlotContainerIndex(_slot);
		if (containerIndex < 0)
			return false;

		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		if (containerIndex < lead)
		{
			_isMainHandEquipmentSlot = containerIndex == 0;
			return _isMainHandEquipmentSlot;
		}

		_bagIndex = containerIndex - lead;
		return _bagIndex >= 0 && _bagIndex < m_BoundInventory.BagCount;
	}

	public void RepaintInventoryPanel()
	{
		if (m_BoundInventory == null || m_PresetInventoryPanel == null)
		{
			if (m_PresetInventoryPanel != null)
				m_PresetInventoryPanel.ClearAllSlots();
			return;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		int lead = Mathf.Max(0, m_PresetInventoryPanel.LeadingEquipmentSlotCount);
		if (lead < 1 && m_BoundInventory.HasMainHandEquipment)
		{
			Debug.LogWarning(
				$"{nameof(MissionPrepLoadoutCoordinator)}: на панели инвентаря пресета «{m_PresetInventoryPanel.name}» " +
				$"{nameof(InventoryPanelView)}.{nameof(InventoryPanelView.LeadingEquipmentSlotCount)} = 0 — " +
				"слот основного оружия не рисуется, хотя в рантайме он занят. Выставьте ≥ 1 на панели экрана предмиссии.",
				m_PresetInventoryPanel);
		}
#endif

		m_BoundInventory.RepaintInventoryPanel(m_PresetInventoryPanel);
	}

	public bool TryGetActivePresetLabel(out string _label)
	{
		_label = string.Empty;

		if (m_BoundPresetState == null || m_PresetCatalog == null)
			return false;

		_label = m_PresetCatalog.GetPresetLabel(m_PresetCatalog.ClampPresetIndex(m_BoundPresetState.PresetCatalogIndex));
		return !string.IsNullOrEmpty(_label);
	}

	public int GetActivePresetArmorIndex()
	{
		return m_BoundPresetState != null ? m_BoundPresetState.ActivePresetArmorIndex : 0;
	}
	#endregion

	#region Private Methods
	private void SaveActivePresetBeforeRebind()
	{
		if (m_BoundPresetState == null || m_BoundInventory == null)
			return;

		m_BoundPresetState.SaveActivePresetFromRuntime(m_BoundInventory);
	}

	private int GetPresetSlotCount()
	{
		if (m_PresetCatalog != null && m_PresetCatalog.PresetCount > 0)
			return m_PresetCatalog.PresetCount;

		return 2;
	}

	private void ApplyActivePresetToRuntime()
	{
		if (m_BoundPresetState == null)
		{
			RepaintInventoryPanel();
			return;
		}

		if (m_BoundInventory != null)
		{
			m_BoundPresetState.ApplyActivePresetToRuntime(m_BoundInventory);
			RefreshBoundUnitEquipment();
		}

		ApplyArmorVisual();
		RepaintInventoryPanel();
	}

	private void RefreshBoundUnitEquipment()
	{
		if (m_BoundInventory == null)
			return;

		UnitWeaponRuntime weaponRuntime = m_BoundInventory.GetComponentInChildren<UnitWeaponRuntime>(true);
		if (weaponRuntime != null)
			weaponRuntime.RefreshFromEquipment();
	}

	private void ApplyArmorVisual()
	{
		if (m_BoundPresetState == null)
			return;

		GameObject unitRoot = m_BoundPresetState.gameObject;
		int armorIndex = m_BoundPresetState.ActivePresetArmorIndex;
		MissionPrepUnitArmorVisualController visual = MissionPrepUnitArmorVisualController.GetOrCreate(unitRoot, armorIndex);
		visual.ApplyArmorVisual(armorIndex);
	}
	#endregion
}
