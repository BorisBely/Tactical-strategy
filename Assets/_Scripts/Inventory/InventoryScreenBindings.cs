using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Единая точка доступа к UI инвентаря на Canvas. Юниты не ссылаются на панели;
/// активный <see cref="CharacterInventory"/> задаётся здесь при выборе персонажа.
/// Клавиша I показывает/скрывает корень UI; при открытии списки ячеек очищаются и строятся заново.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class InventoryScreenBindings : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private RtsUnitSelectionManager m_SelectionManager;
	[Tooltip("Инвентарь юнита по умолчанию при старте сцены (можно сменить через SetActiveCharacterInventory).")]
	[SerializeField] private CharacterInventory m_ActiveCharacterInventory;
	[SerializeField] private VehicleInventory m_ActiveVehicleInventory;
	[Header("Открытие / закрытие")]
	[Tooltip("Корневой объект панели инвентаря на Canvas (весь блок, который показывается по I).")]
	[SerializeField] private GameObject m_InventoryCanvasRoot;
	[Tooltip("При старте сцены сразу скрыть панель.")]
	[SerializeField] private bool m_StartWithInventoryClosed = true;
	[SerializeField] private TMP_Text m_InventoryTitleText;
	[SerializeField] private TMP_Text m_GroundItemsTitleText;
	[SerializeField] private HealthStatusPanelView m_HealthStatusPanel;
	[SerializeField] private HealthStatusSlotView m_HealthStatusSlotPrefab;
	[Header("Список юнита")]
	[SerializeField] private InventoryUnitListPresenter m_UnitListPresenter;
	[Header("Обмен — партнёр")]
	[SerializeField] private InventoryUnitListPresenter m_ExchangeUnitListPresenter;
	[SerializeField] private HealthStatusPanelView m_ExchangeHealthStatusPanel;
	#endregion

	#region Static Access
	private static InventoryScreenBindings s_Instance;

	public static InventoryScreenBindings Instance => s_Instance;
	#endregion

	#region Private Fields
	private bool m_PendingActiveCharacterPanelRefresh;
	private CharacterInventory m_SubscribedInventory;
	private UnitHealth m_SubscribedUnitHealth;
	private UnitArmor m_SubscribedUnitArmor;
	private UnitHealth m_SubscribedPartnerHealth;
	private UnitArmor m_SubscribedPartnerArmor;
	private UnitStamina m_SubscribedUnitStamina;
	#endregion

	#region Public Properties
	public RtsUnitSelectionManager SelectionManager => m_SelectionManager != null ? m_SelectionManager : RtsUnitSelectionManager.Instance;
	public InventoryPanelView GroundPanel => SelectionManager != null ? SelectionManager.GroundPanel : null;
	public InventoryPanelView CharacterInventoryPanel =>
		SelectionManager != null ? SelectionManager.CharacterInventoryPanel : null;
	public CharacterInventory ActiveCharacterInventory => m_ActiveCharacterInventory;
	public VehicleInventory ActiveVehicleInventory => m_ActiveVehicleInventory;
	public bool IsVehicleInventoryActive => m_ActiveVehicleInventory != null;

	/// <summary>Кэшированный или выбранный инвентарь для UI (drag, repaint).</summary>
	public CharacterInventory GetActiveCharacterInventoryForUi() => ResolveActiveCharacterInventoryForUi();
	public VehicleInventory GetActiveVehicleInventoryForUi() => m_ActiveVehicleInventory;
	public bool IsInventoryOpen =>
		m_InventoryCanvasRoot != null && m_InventoryCanvasRoot.activeSelf;
	#endregion

	#region Exchange UI
	public void SetGroundPanelTitle(string _title)
	{
		if (m_GroundItemsTitleText != null)
			m_GroundItemsTitleText.text = _title;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (!TryClaimSingletonInstance())
			return;

		if (m_SelectionManager == null)
			m_SelectionManager = RtsUnitSelectionManager.Instance;
		RefreshLocalizedTexts();
		if (m_StartWithInventoryClosed && m_InventoryCanvasRoot != null)
			m_InventoryCanvasRoot.SetActive(false);
		if (m_HealthStatusPanel != null)
			m_HealthStatusPanel.gameObject.SetActive(false);
		if (m_HealthStatusSlotPrefab != null && m_HealthStatusPanel != null)
			m_HealthStatusPanel.SetRuntimeSlotPrefab(m_HealthStatusSlotPrefab);
		if (m_HealthStatusSlotPrefab != null && m_ExchangeHealthStatusPanel != null)
			m_ExchangeHealthStatusPanel.SetRuntimeSlotPrefab(m_HealthStatusSlotPrefab);
		HideExchangePartnerUi();
		EnsureRuntimeModificationCoordinator();
		SetInventoryTitleVisible(IsInventoryOpen);
	}

	private void Start()
	{
		ReconcileSingletonInstance();
		RefreshActiveCharacterPanel();
		SubscribeToActiveUnitHealth();
		SubscribeToActiveUnitArmor();
		RefreshHealthUi();
	}

	private void LateUpdate()
	{
		if (!m_PendingActiveCharacterPanelRefresh)
			return;

		m_PendingActiveCharacterPanelRefresh = false;
		RefreshActiveCharacterPanelImmediate();
	}

	private void Update()
	{
		if (PauseMenuController.IsPaused)
			return;

		Keyboard kb = Keyboard.current;
		if (kb == null)
			return;

		if (kb.iKey.wasPressedThisFrame)
			ToggleInventoryWindow();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
	}

	private void OnDestroy()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
		UnsubscribeFromActiveInventory();
		UnsubscribeFromActiveUnitStamina();
		UnsubscribeFromActiveUnitHealth();
		UnsubscribeFromActiveUnitArmor();
		UnsubscribeFromPartnerUnitHealth();
		UnsubscribeFromPartnerUnitArmor();
		ClearActiveVehicleInventoryInternal();
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Methods
	public void SetSelectionManager(RtsUnitSelectionManager _selectionManager)
	{
		m_SelectionManager = _selectionManager;
		RuntimeInventoryModificationCoordinator.Instance?.EnsureModificationUiHooks();
		RefreshActiveCharacterPanel();
		if (IsInventoryOpen)
			RefreshGroundPanelForActiveCharacter();
	}

	/// <summary>При смене выбранного юнита: подставить его инвентарь и перерисовать UI.</summary>
	public void SetActiveCharacterInventory(CharacterInventory _inventory)
	{
		ClearActiveVehicleInventoryInternal();

		if (_inventory == null)
		{
			CharacterInventory pinnedInventory = ResolvePinnedCharacterInventory();
			if (pinnedInventory != null)
				_inventory = pinnedInventory;
		}

		m_ActiveCharacterInventory = _inventory;
		SubscribeToActiveInventory();
		SubscribeToActiveUnitStamina();
		SubscribeToActiveUnitHealth();
		SubscribeToActiveUnitArmor();

		if (_inventory == null && IsInventoryOpen)
		{
			SetInventoryWindowOpen(false);
			RefreshActiveCharacterPanel();
			RefreshHealthUi();
			return;
		}

		RefreshActiveCharacterPanel();
		if (IsInventoryOpen)
		{
			RefreshGroundPanelForActiveCharacter();
			RefreshInventoryUnitList();
		}

		RefreshHealthUi();
		if (IsInventoryOpen)
			RefreshExchangePartnerUi();
	}

	/// <summary>При выделении машины: показать её инвентарь (3 слота турели + багаж).</summary>
	public void SetActiveVehicleInventory(VehicleInventory _inventory)
	{
		UnsubscribeFromActiveInventory();
		UnsubscribeFromActiveUnitStamina();
		UnsubscribeFromActiveUnitHealth();
		UnsubscribeFromActiveUnitArmor();
		m_ActiveCharacterInventory = null;
		m_ActiveVehicleInventory = _inventory;

		if (_inventory == null)
		{
			if (IsInventoryOpen)
			{
				SetInventoryWindowOpen(false);
				RefreshActiveCharacterPanel();
			}

			return;
		}

		SubscribeToActiveVehicleInventory();
		RefreshActiveCharacterPanel();
		if (IsInventoryOpen)
		{
			RefreshGroundPanelForActiveCharacter();
			RefreshInventoryUnitList();
		}

		if (m_HealthStatusPanel != null)
			m_HealthStatusPanel.gameObject.SetActive(false);
	}

	private void ClearActiveVehicleInventoryInternal()
	{
		if (m_ActiveVehicleInventory == null)
			return;
		m_ActiveVehicleInventory.InventoryChanged -= HandleVehicleInventoryChanged;
		m_ActiveVehicleInventory = null;
	}

	private void SubscribeToActiveVehicleInventory()
	{
		if (m_ActiveVehicleInventory == null)
			return;
		m_ActiveVehicleInventory.InventoryChanged -= HandleVehicleInventoryChanged;
		m_ActiveVehicleInventory.InventoryChanged += HandleVehicleInventoryChanged;
	}

	private void HandleVehicleInventoryChanged(VehicleInventory _)
	{
		if (IsInventoryOpen)
			RefreshActiveCharacterPanel();
	}

	public void RefreshActiveCharacterPanel()
	{
		m_PendingActiveCharacterPanelRefresh = true;
	}

	public void RefreshActiveCharacterPanelImmediate()
	{
		InventoryPanelView panel = CharacterInventoryPanel;
		if (panel == null)
			return;

		if (m_ActiveVehicleInventory != null)
		{
			panel.SetLeadingEquipmentSlotCount(VehicleInventory.LeadingEquipmentSlotCount);
			m_ActiveVehicleInventory.RepaintInventoryPanel(panel);
			RefreshInventoryWeightTitle();
			// Nested magazine/box row under turret weapon (same as infantry).
			RuntimeInventoryModificationCoordinator.Instance?.RefreshInlineModificationRows();
			return;
		}

		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory != null)
			inventory.RepaintInventoryPanel(panel);
		else
		{
			RuntimeInventoryModificationCoordinator.Instance?.ClearAllModificationVisuals();
			panel.ClearAllSlots();
		}

		RefreshInventoryWeightTitle();
	}

	/// <summary>Полное обновление UI при открытии: рюкзак из <see cref="CharacterInventory"/>, «земля» из текущих пересечений <see cref="InventoryPickupZone"/>.</summary>
	public void RefreshPanelsOnOpen()
	{
		SelectionManager?.SyncActiveInventoryForUi();
		RefreshActiveCharacterPanelImmediate();
		RuntimeInventoryModificationCoordinator.Instance?.EnsureModificationUiHooks();
		RefreshGroundPanelForActiveCharacter();
		RefreshInventoryUnitList();
		RefreshHealthUi();
		RefreshExchangePartnerUi();
	}

	/// <summary>Перестроить панель «земля» по <see cref="InventoryPickupZone"/> активного юнита.</summary>
	public void RefreshGroundPanelForActiveCharacter()
	{
		if (InventoryExchangeController.Instance.IsActive)
		{
			InventoryExchangeController.Instance.RefreshPartnerPanel();
			return;
		}

		InventoryPickupZone zone = FindPickupZoneOnActiveCharacter();
		if (zone != null)
			zone.RepopulateGroundPanelFromCurrentOverlaps();
		else if (GroundPanel != null)
		{
			RuntimeInlineModificationBuilder.ClearAllRows(GroundPanel);
			GroundPanel.ClearAllSlots();
			RuntimeInventoryModificationCoordinator.Instance?.EnsureGroundPanelUiHooks();
			RuntimeInventoryModificationCoordinator.Instance?.RefreshInlineModificationRows();
		}
	}

	public void ToggleInventoryWindow()
	{
		SetInventoryWindowOpen(!IsInventoryOpen);
	}

	public void SetInventoryWindowOpen(bool _open)
	{
		if (m_InventoryCanvasRoot == null)
		{
			Debug.LogWarning(
				$"{nameof(InventoryScreenBindings)} on '{gameObject.name}' has no {nameof(m_InventoryCanvasRoot)} assigned; inventory window cannot open.",
				this);
			return;
		}

		if (_open && MissionPrepScreenBindings.Instance != null && MissionPrepScreenBindings.Instance.IsMissionPrepOpen)
			MissionPrepScreenBindings.Instance.SetMissionPrepWindowOpen(false);

		if (!_open)
		{
			InventoryExchangeController.Instance.EndExchangeIfActive();
			RuntimeInventoryModificationCoordinator.Instance?.ClearAllModificationVisuals();
			HealthStatusTooltip.Instance.HideImmediate();
		}

		m_InventoryCanvasRoot.SetActive(_open);
		SetInventoryTitleVisible(_open);
		if (_open)
			RefreshPanelsOnOpen();
		else
		{
			m_UnitListPresenter?.Clear();
			if (m_HealthStatusPanel != null)
				m_HealthStatusPanel.gameObject.SetActive(false);
			HideExchangePartnerUi();
		}
	}

	public void RefreshExchangePartnerUi()
	{
		bool exchangeActive = InventoryExchangeController.Instance.IsActive;
		SetExchangePartnerUiVisible(exchangeActive);
		if (!exchangeActive)
		{
			ClearExchangePartnerUi();
			return;
		}

		CharacterInventory partnerInventory = InventoryExchangeController.Instance.PartnerInventory;
		m_ExchangeUnitListPresenter?.RefreshForInventory(partnerInventory);
		SubscribeToPartnerUnitHealth();
		SubscribeToPartnerUnitArmor();
		RefreshExchangePartnerHealthUi();
		RefreshExchangePartnerUnitHealthSummary();
	}

	public void HideExchangePartnerUi()
	{
		UnsubscribeFromPartnerUnitHealth();
		UnsubscribeFromPartnerUnitArmor();
		SetExchangePartnerUiVisible(false);
		ClearExchangePartnerUi();
	}

	public void RefreshHealthVitalsSummary()
	{
		RefreshInventoryUnitHealthSummary();
		RefreshExchangePartnerUnitHealthSummary();
	}

	public void RefreshHealthUi()
	{
		RefreshInventoryUnitHealthSummary();

		if (m_HealthStatusPanel == null)
			return;

		m_HealthStatusPanel.ClearAllSlots();

		UnitHealth health = ResolveActiveUnitHealth();
		bool hasInjuries = health != null && health.HasInjuries;
		bool showHealthPanel = hasInjuries && IsInventoryOpen;
		m_HealthStatusPanel.gameObject.SetActive(showHealthPanel);

		if (!showHealthPanel)
		{
			m_HealthStatusPanel.RebuildContentLayout();
			return;
		}

		if (health != null)
		{
			IReadOnlyList<InjuryIndexedEntry> injuries = health.GetSortedIndexedInjuryEntries();
			for (int i = 0; i < injuries.Count; i++)
				m_HealthStatusPanel.TryAdd(injuries[i].Entry.ToEntryData(injuries[i].Index));
		}

		m_HealthStatusPanel.RebuildContentLayout();
		ApplyHealProgressToHealthPanels();
	}

	public void ApplyHealProgressToHealthPanels()
	{
		if (m_HealthStatusPanel != null)
			m_HealthStatusPanel.ApplyHealProgressForUnit(ResolveActiveUnitHealth());

		if (m_ExchangeHealthStatusPanel != null)
			m_ExchangeHealthStatusPanel.ApplyHealProgressForUnit(ResolvePartnerUnitHealth());
	}

	private void HandleLanguageChanged()
	{
		RefreshLocalizedTexts();
		if (IsInventoryOpen)
			RefreshPanelsOnOpen();
		else
			RefreshHealthUi();
	}

	private void RefreshInventoryUnitList()
	{
		if (m_UnitListPresenter == null)
			return;

		m_UnitListPresenter.RefreshForInventory(ResolveActiveCharacterInventoryForUi());
	}

	private void RefreshInventoryUnitHealthSummary()
	{
		if (m_UnitListPresenter == null)
			return;

		m_UnitListPresenter.RefreshStatusSummaryForActiveCell();
	}

	private void RefreshExchangePartnerUnitHealthSummary()
	{
		if (m_ExchangeUnitListPresenter == null)
			return;

		m_ExchangeUnitListPresenter.RefreshStatusSummaryForActiveCell();
	}

	private void RefreshExchangePartnerHealthUi()
	{
		if (m_ExchangeHealthStatusPanel == null)
			return;

		m_ExchangeHealthStatusPanel.ClearAllSlots();

		UnitHealth health = ResolvePartnerUnitHealth();
		bool hasInjuries = health != null && health.HasInjuries;
		bool showHealthPanel = hasInjuries && InventoryExchangeController.Instance.IsActive && IsInventoryOpen;
		m_ExchangeHealthStatusPanel.gameObject.SetActive(showHealthPanel);

		if (!showHealthPanel)
		{
			m_ExchangeHealthStatusPanel.RebuildContentLayout();
			return;
		}

		if (health != null)
		{
			IReadOnlyList<InjuryIndexedEntry> injuries = health.GetSortedIndexedInjuryEntries();
			for (int i = 0; i < injuries.Count; i++)
				m_ExchangeHealthStatusPanel.TryAdd(injuries[i].Entry.ToEntryData(injuries[i].Index));
		}

		m_ExchangeHealthStatusPanel.RebuildContentLayout();
		ApplyHealProgressToHealthPanels();
	}

	private void ClearExchangePartnerUi()
	{
		m_ExchangeUnitListPresenter?.Clear();
		if (m_ExchangeHealthStatusPanel != null)
		{
			m_ExchangeHealthStatusPanel.ClearAllSlots();
			m_ExchangeHealthStatusPanel.RebuildContentLayout();
		}
	}

	private void SetExchangePartnerUiVisible(bool _visible)
	{
		if (m_ExchangeUnitListPresenter != null)
			m_ExchangeUnitListPresenter.gameObject.SetActive(_visible);

		if (!_visible && m_ExchangeHealthStatusPanel != null)
			m_ExchangeHealthStatusPanel.gameObject.SetActive(false);
	}

	private void RefreshLocalizedTexts()
	{
		RefreshInventoryWeightTitle();
		if (m_GroundItemsTitleText != null)
			m_GroundItemsTitleText.text = LocalizationManager.Get("inventory.ground.title");
	}

	private void SubscribeToActiveUnitHealth()
	{
		UnitHealth health = ResolveActiveUnitHealth();
		if (health == m_SubscribedUnitHealth)
			return;

		UnsubscribeFromActiveUnitHealth();
		m_SubscribedUnitHealth = health;
		if (m_SubscribedUnitHealth != null)
		{
			m_SubscribedUnitHealth.Changed += HandleActiveUnitHealthChanged;
			m_SubscribedUnitHealth.VitalsChanged += HandleActiveUnitHealthVitalsChanged;
		}
	}

	private void UnsubscribeFromActiveUnitHealth()
	{
		if (m_SubscribedUnitHealth == null)
			return;

		m_SubscribedUnitHealth.Changed -= HandleActiveUnitHealthChanged;
		m_SubscribedUnitHealth.VitalsChanged -= HandleActiveUnitHealthVitalsChanged;
		m_SubscribedUnitHealth = null;
	}

	private void HandleActiveUnitHealthChanged()
	{
		RefreshHealthUi();
	}

	private void HandleActiveUnitHealthVitalsChanged()
	{
		RefreshHealthVitalsSummary();
	}

	private void SubscribeToActiveUnitArmor()
	{
		UnitArmor armor = ResolveActiveUnitArmor();
		if (armor == m_SubscribedUnitArmor)
			return;

		UnsubscribeFromActiveUnitArmor();
		m_SubscribedUnitArmor = armor;
		if (m_SubscribedUnitArmor != null)
			m_SubscribedUnitArmor.Changed += HandleActiveUnitArmorChanged;
	}

	private void UnsubscribeFromActiveUnitArmor()
	{
		if (m_SubscribedUnitArmor == null)
			return;

		m_SubscribedUnitArmor.Changed -= HandleActiveUnitArmorChanged;
		m_SubscribedUnitArmor = null;
	}

	private void HandleActiveUnitArmorChanged()
	{
		RefreshInventoryWeightTitle();
		RefreshInventoryUnitHealthSummary();
	}

	private void SubscribeToActiveUnitStamina()
	{
		UnitStamina stamina = ResolveActiveUnitStamina();
		if (stamina == m_SubscribedUnitStamina)
			return;

		UnsubscribeFromActiveUnitStamina();
		m_SubscribedUnitStamina = stamina;
		if (m_SubscribedUnitStamina != null)
			m_SubscribedUnitStamina.StaminaChanged += HandleActiveUnitStaminaChanged;
	}

	private void UnsubscribeFromActiveUnitStamina()
	{
		if (m_SubscribedUnitStamina == null)
			return;

		m_SubscribedUnitStamina.StaminaChanged -= HandleActiveUnitStaminaChanged;
		m_SubscribedUnitStamina = null;
	}

	private void HandleActiveUnitStaminaChanged(float _stamina)
	{
		RefreshInventoryUnitHealthSummary();
	}

	private void SubscribeToActiveInventory()
	{
		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory == m_SubscribedInventory)
			return;

		UnsubscribeFromActiveInventory();
		m_SubscribedInventory = inventory;
		if (m_SubscribedInventory != null)
			m_SubscribedInventory.InventoryChanged += HandleActiveInventoryChanged;

		RefreshInventoryWeightTitle();
	}

	private void UnsubscribeFromActiveInventory()
	{
		if (m_SubscribedInventory == null)
			return;

		m_SubscribedInventory.InventoryChanged -= HandleActiveInventoryChanged;
		m_SubscribedInventory = null;
	}

	private void HandleActiveInventoryChanged(CharacterInventory _inventory)
	{
		RefreshInventoryWeightTitle();
	}

	private void RefreshInventoryWeightTitle()
	{
		if (m_InventoryTitleText == null)
			return;

		if (m_ActiveVehicleInventory != null)
		{
			float total = m_ActiveVehicleInventory.CargoWeightKg;
			float max = m_ActiveVehicleInventory.MaxCargoWeightKg;
			m_InventoryTitleText.text = $"Машина ({total:F1}/{max:F1} кг)";
			return;
		}

		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory != null)
		{
			float total = inventory.TotalWeightKg;
			float max = inventory.TotalMaxWeightKg;
			string title = LocalizationManager.Get("inventory.window.title");
			m_InventoryTitleText.text = $"{title} ({total:F1}/{max:F1} кг)";
		}
		else
			m_InventoryTitleText.text = LocalizationManager.Get("inventory.window.title");
	}

	private void SubscribeToPartnerUnitHealth()
	{
		UnitHealth health = ResolvePartnerUnitHealth();
		if (health == m_SubscribedPartnerHealth)
			return;

		UnsubscribeFromPartnerUnitHealth();
		m_SubscribedPartnerHealth = health;
		if (m_SubscribedPartnerHealth != null)
		{
			m_SubscribedPartnerHealth.Changed += HandlePartnerUnitHealthChanged;
			m_SubscribedPartnerHealth.VitalsChanged += HandlePartnerUnitHealthVitalsChanged;
		}
	}

	private void UnsubscribeFromPartnerUnitHealth()
	{
		if (m_SubscribedPartnerHealth == null)
			return;

		m_SubscribedPartnerHealth.Changed -= HandlePartnerUnitHealthChanged;
		m_SubscribedPartnerHealth.VitalsChanged -= HandlePartnerUnitHealthVitalsChanged;
		m_SubscribedPartnerHealth = null;
	}

	private void HandlePartnerUnitHealthChanged()
	{
		RefreshExchangePartnerHealthUi();
		RefreshExchangePartnerUnitHealthSummary();
	}

	private void HandlePartnerUnitHealthVitalsChanged()
	{
		RefreshExchangePartnerUnitHealthSummary();
	}

	private void SubscribeToPartnerUnitArmor()
	{
		UnitArmor armor = ResolvePartnerUnitArmor();
		if (armor == m_SubscribedPartnerArmor)
			return;

		UnsubscribeFromPartnerUnitArmor();
		m_SubscribedPartnerArmor = armor;
		if (m_SubscribedPartnerArmor != null)
			m_SubscribedPartnerArmor.Changed += HandlePartnerUnitArmorChanged;
	}

	private void UnsubscribeFromPartnerUnitArmor()
	{
		if (m_SubscribedPartnerArmor == null)
			return;

		m_SubscribedPartnerArmor.Changed -= HandlePartnerUnitArmorChanged;
		m_SubscribedPartnerArmor = null;
	}

	private void HandlePartnerUnitArmorChanged()
	{
		RefreshInventoryWeightTitle();
		RefreshExchangePartnerUnitHealthSummary();
	}

	private UnitHealth ResolvePartnerUnitHealth()
	{
		CharacterInventory inventory = InventoryExchangeController.Instance.PartnerInventory;
		if (inventory == null)
			return null;

		RtsUnitMember member = inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null && member.TryGetComponent(out UnitHealth health))
			return health;

		return inventory.GetComponentInParent<UnitHealth>(true);
	}

	private UnitArmor ResolvePartnerUnitArmor()
	{
		CharacterInventory inventory = InventoryExchangeController.Instance.PartnerInventory;
		if (inventory == null)
			return null;

		RtsUnitMember member = inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null && member.TryGetComponent(out UnitArmor armor))
			return armor;

		return inventory.GetComponentInParent<UnitArmor>(true);
	}

	private UnitHealth ResolveActiveUnitHealth()
	{
		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory == null)
			return null;

		RtsUnitMember member = inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null && member.TryGetComponent(out UnitHealth health))
			return health;

		return inventory.GetComponentInParent<UnitHealth>(true);
	}

	private UnitArmor ResolveActiveUnitArmor()
	{
		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory == null)
			return null;

		RtsUnitMember member = inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null && member.TryGetComponent(out UnitArmor armor))
			return armor;

		return inventory.GetComponentInParent<UnitArmor>(true);
	}

	private UnitStamina ResolveActiveUnitStamina()
	{
		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory == null)
			return null;

		RtsUnitMember member = inventory.GetComponentInParent<RtsUnitMember>(true);
		if (member != null && member.TryGetComponent(out UnitStamina stamina))
			return stamina;

		return inventory.GetComponentInParent<UnitStamina>(true);
	}

	private InventoryPickupZone FindPickupZoneOnActiveCharacter()
	{
		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory == null)
			return null;
		return inventory.GetComponentInChildren<InventoryPickupZone>(true);
	}

	private CharacterInventory ResolveActiveCharacterInventoryForUi()
	{
		RtsUnitSelectionManager selectionManager = SelectionManager;
		if (selectionManager != null)
		{
			CharacterInventory fromSelection = selectionManager.TryGetActiveCharacterInventoryForUi();
			if (fromSelection != null)
				return fromSelection;
		}

		return m_ActiveCharacterInventory;
	}

	private CharacterInventory ResolvePinnedCharacterInventory()
	{
		InventoryExchangeController exchange = InventoryExchangeController.Instance;
		RtsUnitSelectionManager selectionManager = SelectionManager;
		if (exchange.IsActive && exchange.PlayerInventory != null &&
		    selectionManager != null && selectionManager.ShouldPinActiveExchangeInventory)
			return exchange.PlayerInventory;

		if (selectionManager != null && selectionManager.HasPendingExchangeApproach)
			return selectionManager.TryGetActiveCharacterInventoryForUi();

		return null;
	}

	private void SetInventoryTitleVisible(bool _visible)
	{
		if (m_InventoryTitleText != null)
			m_InventoryTitleText.gameObject.SetActive(_visible);
	}

	private bool TryClaimSingletonInstance()
	{
		if (!IsConfiguredForRuntimeUi())
		{
			enabled = false;
			return false;
		}

		if (s_Instance == null)
		{
			s_Instance = this;
			return true;
		}

		if (s_Instance == this)
			return true;

		if (!s_Instance.IsConfiguredForRuntimeUi())
		{
			s_Instance.enabled = false;
			s_Instance = this;
			return true;
		}

		Debug.LogWarning(
			$"Duplicate configured {nameof(InventoryScreenBindings)} on '{gameObject.name}'. Keeping the first configured instance.",
			s_Instance);
		enabled = false;
		return false;
	}

	private void ReconcileSingletonInstance()
	{
		if (!IsConfiguredForRuntimeUi())
			return;

		if (s_Instance == this)
			return;

		if (s_Instance == null || !s_Instance.IsConfiguredForRuntimeUi())
		{
			if (s_Instance != null)
				s_Instance.enabled = false;
			s_Instance = this;
			enabled = true;
		}
	}

	private bool IsConfiguredForRuntimeUi()
	{
		return m_InventoryCanvasRoot != null;
	}

	private void EnsureRuntimeModificationCoordinator()
	{
		if (RuntimeInventoryModificationCoordinator.Instance != null)
			return;

		if (!TryGetComponent(out RuntimeInventoryModificationCoordinator coordinator))
			coordinator = gameObject.AddComponent<RuntimeInventoryModificationCoordinator>();

		if (m_SelectionManager == null)
			m_SelectionManager = RtsUnitSelectionManager.Instance;
	}
	#endregion
}
