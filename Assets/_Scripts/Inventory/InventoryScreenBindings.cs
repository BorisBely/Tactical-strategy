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
	[SerializeField] private PlayerInventoryCoordinator m_Coordinator;
	[Tooltip("Инвентарь юнита по умолчанию при старте сцены (можно сменить через SetActiveCharacterInventory).")]
	[SerializeField] private CharacterInventory m_ActiveCharacterInventory;
	[Header("Открытие / закрытие")]
	[Tooltip("Корневой объект панели инвентаря на Canvas (весь блок, который показывается по I).")]
	[SerializeField] private GameObject m_InventoryCanvasRoot;
	[Tooltip("При старте сцены сразу скрыть панель.")]
	[SerializeField] private bool m_StartWithInventoryClosed = true;
	#endregion

	#region Static Access
	private static InventoryScreenBindings s_Instance;

	public static InventoryScreenBindings Instance => s_Instance;
	#endregion

	#region Public Properties
	public PlayerInventoryCoordinator Coordinator => m_Coordinator;
	public InventoryPanelView GroundPanel => m_Coordinator != null ? m_Coordinator.GroundPanel : null;
	public InventoryPanelView CharacterInventoryPanel =>
		m_Coordinator != null ? m_Coordinator.CharacterInventoryPanel : null;
	public CharacterInventory ActiveCharacterInventory => m_ActiveCharacterInventory;
	public bool IsInventoryOpen =>
		m_InventoryCanvasRoot != null && m_InventoryCanvasRoot.activeSelf;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		s_Instance = this;
		if (m_StartWithInventoryClosed && m_InventoryCanvasRoot != null)
			m_InventoryCanvasRoot.SetActive(false);
	}

	private void Start()
	{
		RefreshActiveCharacterPanel();
	}

	private void Update()
	{
		if (m_InventoryCanvasRoot == null)
			return;

		Keyboard kb = Keyboard.current;
		if (kb == null || !kb.iKey.wasPressedThisFrame)
			return;

		bool opening = !m_InventoryCanvasRoot.activeSelf;
		m_InventoryCanvasRoot.SetActive(opening);
		if (opening)
			RefreshPanelsOnOpen();
	}

	private void OnDestroy()
	{
		if (s_Instance == this)
			s_Instance = null;
	}
	#endregion

	#region Public Methods
	public void SetCoordinator(PlayerInventoryCoordinator _coordinator)
	{
		m_Coordinator = _coordinator;
		RefreshActiveCharacterPanel();
	}

	/// <summary>При смене выбранного юнита: подставить его инвентарь и перерисовать UI.</summary>
	public void SetActiveCharacterInventory(CharacterInventory _inventory)
	{
		m_ActiveCharacterInventory = _inventory;
		RefreshActiveCharacterPanel();
	}

	public void RefreshActiveCharacterPanel()
	{
		if (m_Coordinator == null)
			return;

		InventoryPanelView bagPanel = m_Coordinator.CharacterInventoryPanel;
		if (bagPanel == null)
			return;

		if (m_ActiveCharacterInventory != null)
			m_ActiveCharacterInventory.RepaintInventoryPanel(bagPanel);
		else
			bagPanel.ClearAllSlots();
	}

	/// <summary>Полное обновление UI при открытии: рюкзак из <see cref="CharacterInventory"/>, «земля» из текущих пересечений <see cref="InventoryPickupZone"/>.</summary>
	public void RefreshPanelsOnOpen()
	{
		RefreshActiveCharacterPanel();

		InventoryPickupZone zone = FindPickupZoneOnActiveCharacter();
		if (zone != null)
			zone.RepopulateGroundPanelFromCurrentOverlaps();
		else if (GroundPanel != null)
			GroundPanel.ClearAllSlots();
	}

	private InventoryPickupZone FindPickupZoneOnActiveCharacter()
	{
		if (m_ActiveCharacterInventory == null)
			return null;
		return m_ActiveCharacterInventory.GetComponentInChildren<InventoryPickupZone>(true);
	}
	#endregion
}
