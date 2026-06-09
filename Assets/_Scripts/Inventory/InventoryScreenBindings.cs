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
	#endregion

	#region Static Access
	private static InventoryScreenBindings s_Instance;

	public static InventoryScreenBindings Instance => s_Instance;
	#endregion

	#region Private Fields
	private bool m_PendingActiveCharacterPanelRefresh;
	private UnitHealth m_SubscribedUnitHealth;
	#endregion

	#region Public Properties
	public RtsUnitSelectionManager SelectionManager => m_SelectionManager != null ? m_SelectionManager : RtsUnitSelectionManager.Instance;
	public InventoryPanelView GroundPanel => SelectionManager != null ? SelectionManager.GroundPanel : null;
	public InventoryPanelView CharacterInventoryPanel =>
		SelectionManager != null ? SelectionManager.CharacterInventoryPanel : null;
	public CharacterInventory ActiveCharacterInventory => m_ActiveCharacterInventory;

	/// <summary>Кэшированный или выбранный инвентарь для UI (drag, repaint).</summary>
	public CharacterInventory GetActiveCharacterInventoryForUi() => ResolveActiveCharacterInventoryForUi();
	public bool IsInventoryOpen =>
		m_InventoryCanvasRoot != null && m_InventoryCanvasRoot.activeSelf;
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
		EnsureRuntimeModificationCoordinator();
		SetInventoryTitleVisible(IsInventoryOpen);
	}

	private void Start()
	{
		ReconcileSingletonInstance();
		RefreshActiveCharacterPanel();
		SubscribeToActiveUnitHealth();
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
		UnsubscribeFromActiveUnitHealth();
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
		m_ActiveCharacterInventory = _inventory;
		SubscribeToActiveUnitHealth();

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

		CharacterInventory inventory = ResolveActiveCharacterInventoryForUi();
		if (inventory != null)
			inventory.RepaintInventoryPanel(panel);
		else
		{
			RuntimeInventoryModificationCoordinator.Instance?.ClearAllModificationVisuals();
			panel.ClearAllSlots();
		}
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
	}

	/// <summary>Перестроить панель «земля» по <see cref="InventoryPickupZone"/> активного юнита.</summary>
	public void RefreshGroundPanelForActiveCharacter()
	{
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
		}
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
			IReadOnlyList<InjuryUiEntry> injuries = health.GetSortedInjuryEntries();
			for (int i = 0; i < injuries.Count; i++)
				m_HealthStatusPanel.TryAdd(injuries[i].ToEntryData());
		}

		m_HealthStatusPanel.RebuildContentLayout();
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

		m_UnitListPresenter.RefreshHealthSummaryForActiveCell();
	}

	private void RefreshLocalizedTexts()
	{
		if (m_InventoryTitleText != null)
			m_InventoryTitleText.text = LocalizationManager.Get("inventory.window.title");
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
			m_SubscribedUnitHealth.Changed += HandleActiveUnitHealthChanged;
	}

	private void UnsubscribeFromActiveUnitHealth()
	{
		if (m_SubscribedUnitHealth == null)
			return;

		m_SubscribedUnitHealth.Changed -= HandleActiveUnitHealthChanged;
		m_SubscribedUnitHealth = null;
	}

	private void HandleActiveUnitHealthChanged()
	{
		RefreshHealthUi();
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
			return selectionManager.TryGetActiveCharacterInventoryForUi();

		return m_ActiveCharacterInventory;
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
