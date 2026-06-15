using UnityEngine;

/// <summary>
/// Режим обмена с сражённым юнитом: панель «земля» показывает его инвентарь (заголовок «Найдено»).
/// </summary>
public sealed class InventoryExchangeController
{
	#region Constants
	private const string c_FoundTitleKey = "inventory.exchange.found";
	private const string c_FoundTitleFallback = "Найдено";
	#endregion

	#region Static Access
	private static InventoryExchangeController s_Instance;

	public static InventoryExchangeController Instance => s_Instance ??= new InventoryExchangeController();
	#endregion

	#region Private Fields
	private RtsUnitMember m_PlayerUnit;
	private CharacterInventory m_PlayerInventory;
	private RtsUnitMember m_PartnerUnit;
	private CharacterInventory m_PartnerInventory;
	private int m_SavedGroundLeadingSlotCount = -1;
	private bool m_IsActive;
	#endregion

	#region Public Properties
	public bool IsActive => m_IsActive;
	public CharacterInventory PlayerInventory => m_PlayerInventory;
	public RtsUnitMember PlayerUnit => m_PlayerUnit;
	public CharacterInventory PartnerInventory => m_PartnerInventory;
	public RtsUnitMember PartnerUnit => m_PartnerUnit;
	#endregion

	#region Public Methods
	public bool TryBeginExchange(RtsUnitMember _partnerUnit, RtsUnitMember _playerUnit, out string _failureMessage)
	{
		_failureMessage = null;

		if (_partnerUnit == null || _playerUnit == null || _partnerUnit == _playerUnit)
		{
			_failureMessage = "Не удалось начать обмен.";
			return false;
		}

		CharacterInventory partnerInventory = ResolveCharacterInventory(_partnerUnit);
		CharacterInventory playerInventory = ResolveCharacterInventory(_playerUnit);
		if (partnerInventory == null || playerInventory == null)
		{
			_failureMessage = "У юнита нет инвентаря для обмена.";
			return false;
		}

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
		{
			_failureMessage = "UI инвентаря не найден в сцене.";
			return false;
		}

		RtsUnitSelectionManager selectionManager = bindings.SelectionManager;
		InventoryPanelView groundPanel = selectionManager != null ? selectionManager.GroundPanel : null;
		InventoryPanelView characterPanel = selectionManager != null ? selectionManager.CharacterInventoryPanel : null;
		if (groundPanel == null || characterPanel == null)
		{
			_failureMessage = "Панели инвентаря не настроены.";
			return false;
		}

		if (m_IsActive)
			EndExchange();

		m_PlayerUnit = _playerUnit;
		m_PlayerInventory = playerInventory;
		m_PartnerUnit = _partnerUnit;
		m_PartnerInventory = partnerInventory;
		m_IsActive = true;

		m_SavedGroundLeadingSlotCount = groundPanel.LeadingEquipmentSlotCount;
		int partnerLeading = Mathf.Max(characterPanel.LeadingEquipmentSlotCount, 3);
		groundPanel.SetLeadingEquipmentSlotCount(partnerLeading);

		ApplyFoundTitle(bindings);
		partnerInventory.RepaintInventoryPanel(groundPanel);

		RuntimeInventoryModificationCoordinator coordinator = RuntimeInventoryModificationCoordinator.Instance;
		coordinator?.EnsureGroundPanelUiHooks();
		coordinator?.EnsurePartnerPanelEquipmentSlots();
		InventoryGroundDropZone.EnsureOnGroundPanel(groundPanel);
		InventoryExchangePartnerBagDropZone.EnsureOnPartnerPanel(groundPanel);

		bindings.SetActiveCharacterInventory(playerInventory);
		if (!bindings.IsInventoryOpen)
			bindings.SetInventoryWindowOpen(true);
		else
		{
			bindings.RefreshActiveCharacterPanelImmediate();
			RefreshPartnerPanel();
		}

		return true;
	}

	public bool TryBeginExchange(RtsUnitMember _partnerUnit, RtsUnitMember _playerUnit)
	{
		return TryBeginExchange(_partnerUnit, _playerUnit, out _);
	}

	public void EndExchangeIfActive()
	{
		if (!m_IsActive)
			return;

		EndExchange();
	}

	public void RefreshPartnerPanel()
	{
		if (!m_IsActive || m_PartnerInventory == null)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		InventoryPanelView groundPanel = bindings != null ? bindings.GroundPanel : null;
		if (groundPanel == null)
			return;

		m_PartnerInventory.RepaintInventoryPanel(groundPanel);
		RuntimeInventoryModificationCoordinator.Instance?.EnsureGroundPanelUiHooks();
		RuntimeInventoryModificationCoordinator.Instance?.EnsurePartnerPanelEquipmentSlots();
	}

	public void RepaintBothExchangePanels()
	{
		if (!m_IsActive)
			return;

		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		if (bindings == null)
			return;

		CharacterInventory playerInventory = bindings.GetActiveCharacterInventoryForUi();
		InventoryPanelView characterPanel = bindings.CharacterInventoryPanel;
		if (playerInventory != null && characterPanel != null)
			playerInventory.RepaintInventoryPanel(characterPanel);

		RefreshPartnerPanel();
	}
	#endregion

	#region Private Methods
	private static CharacterInventory ResolveCharacterInventory(RtsUnitMember _unit)
	{
		if (_unit == null)
			return null;

		CharacterInventory inventory = _unit.CharacterInventory;
		if (inventory != null)
			return inventory;

		if (_unit.TryGetComponent(out inventory))
			return inventory;

		return _unit.GetComponentInChildren<CharacterInventory>(true);
	}

	private void EndExchange()
	{
		InventoryScreenBindings bindings = InventoryScreenBindings.Instance;
		InventoryPanelView groundPanel = bindings != null ? bindings.GroundPanel : null;

		if (groundPanel != null && m_SavedGroundLeadingSlotCount >= 0)
			groundPanel.SetLeadingEquipmentSlotCount(m_SavedGroundLeadingSlotCount);

		m_PlayerUnit = null;
		m_PlayerInventory = null;
		m_PartnerUnit = null;
		m_PartnerInventory = null;
		m_SavedGroundLeadingSlotCount = -1;
		m_IsActive = false;

		if (bindings != null)
		{
			RestoreGroundTitle(bindings);
			if (bindings.IsInventoryOpen)
				bindings.RefreshGroundPanelForActiveCharacter();
		}
	}

	private static void ApplyFoundTitle(InventoryScreenBindings _bindings)
	{
		_bindings.SetGroundPanelTitle(LocalizationManager.Get(c_FoundTitleKey, c_FoundTitleFallback));
	}

	private static void RestoreGroundTitle(InventoryScreenBindings _bindings)
	{
		_bindings.SetGroundPanelTitle(LocalizationManager.Get("inventory.ground.title"));
	}
	#endregion
}
