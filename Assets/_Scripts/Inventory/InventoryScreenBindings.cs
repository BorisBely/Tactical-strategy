using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
	[SerializeField] private TMP_Text m_HealthWindowTitleText;
	[SerializeField] private HealthStatusPanelView m_HealthStatusPanel;
	[SerializeField] private HealthStatusSlotView m_HealthStatusSlotPrefab;
	#endregion

	#region Static Access
	private static InventoryScreenBindings s_Instance;

	public static InventoryScreenBindings Instance => s_Instance;
	#endregion

	#region Private Fields
	private bool m_PendingActiveCharacterPanelRefresh;
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
	public bool IsHealthWindowOpen =>
		m_HealthStatusPanel != null && m_HealthStatusPanel.gameObject.activeSelf;
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
		ConfigureHealthWindowReadOnly();
		EnsureRuntimeModificationCoordinator();
		SetInventoryTitleVisible(IsInventoryOpen);
		SetHealthTitleVisible(IsHealthWindowOpen);
	}

	private void Start()
	{
		ReconcileSingletonInstance();
		RefreshActiveCharacterPanel();
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
		{
			ToggleInventoryWindow();
			return;
		}

		if (kb.hKey.wasPressedThisFrame)
			ToggleHealthWindow();
	}

	private void OnEnable()
	{
		LocalizationManager.LanguageChanged += HandleLanguageChanged;
	}

	private void OnDestroy()
	{
		LocalizationManager.LanguageChanged -= HandleLanguageChanged;
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
		RefreshActiveCharacterPanel();
		if (IsInventoryOpen)
			RefreshGroundPanelForActiveCharacter();
		if (IsHealthWindowOpen)
			RefreshHealthPanel();
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

	public void ToggleHealthWindow()
	{
		SetHealthWindowOpen(!IsHealthWindowOpen);
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

		if (_open && m_HealthStatusPanel != null && m_HealthStatusPanel.gameObject.activeSelf)
		{
			m_HealthStatusPanel.gameObject.SetActive(false);
			SetHealthTitleVisible(false);
		}

		if (!_open)
			RuntimeInventoryModificationCoordinator.Instance?.ClearAllModificationVisuals();

		m_InventoryCanvasRoot.SetActive(_open);
		SetInventoryTitleVisible(_open);
		if (_open)
			RefreshPanelsOnOpen();
	}

	public void SetHealthWindowOpen(bool _open)
	{
		if (m_HealthStatusPanel == null)
			return;

		if (_open && m_InventoryCanvasRoot != null && m_InventoryCanvasRoot.activeSelf)
		{
			m_InventoryCanvasRoot.SetActive(false);
			SetInventoryTitleVisible(false);
		}

		m_HealthStatusPanel.gameObject.SetActive(_open);
		SetHealthTitleVisible(_open);
		if (_open)
			RefreshHealthPanel();
	}

	private void HandleLanguageChanged()
	{
		RefreshLocalizedTexts();
		if (IsInventoryOpen)
			RefreshPanelsOnOpen();
		if (IsHealthWindowOpen)
			RefreshHealthPanel();
	}

	private void RefreshLocalizedTexts()
	{
		if (m_InventoryTitleText != null)
			m_InventoryTitleText.text = LocalizationManager.Get("inventory.window.title");
		if (m_GroundItemsTitleText != null)
			m_GroundItemsTitleText.text = LocalizationManager.Get("inventory.ground.title");

		if (m_HealthWindowTitleText != null)
			m_HealthWindowTitleText.text = LocalizationManager.Get("health.window.title");
	}

	private void RefreshHealthPanel()
	{
		if (m_HealthStatusPanel == null)
			return;

		m_HealthStatusPanel.ClearAllSlots();
		if (m_ActiveCharacterInventory == null)
			return;

		HealthStatusEntryData healthyState = HealthStatusEntryData.FromLocalizedKey("health.status.ok");
		m_HealthStatusPanel.TryAdd(healthyState);
		m_HealthStatusPanel.RebuildContentLayout();
	}

	private void ConfigureHealthWindowReadOnly()
	{
		if (m_HealthStatusPanel == null)
			return;

		GameObject root = m_HealthStatusPanel.gameObject;
		CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
		if (canvasGroup == null)
			canvasGroup = root.AddComponent<CanvasGroup>();

		canvasGroup.interactable = false;
		canvasGroup.blocksRaycasts = false;
	}

	private void SetInventoryTitleVisible(bool _visible)
	{
		if (m_InventoryTitleText != null)
			m_InventoryTitleText.gameObject.SetActive(_visible);
	}

	private void SetHealthTitleVisible(bool _visible)
	{
		if (m_HealthWindowTitleText != null)
			m_HealthWindowTitleText.gameObject.SetActive(_visible);
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
