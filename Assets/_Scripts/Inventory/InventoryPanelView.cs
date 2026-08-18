using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Панель со списком ячеек. Если задан префаб — каждое добавление создаёт новую ячейку под контент.
/// Без префаба — ищется пустая ячейка в иерархии.
/// </summary>
[DisallowMultipleComponent]
public class InventoryPanelView : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("RectTransform Content (родитель для ячеек). Обязателен, если используется Slot Prefab.")]
	[SerializeField] private Transform m_SlotsContainer;
	[Tooltip("Если задан — каждый TryAdd создаёт новый экземпляр под контент (пустые старые не переиспользуются).")]
	[SerializeField] private InventorySlotView m_SlotPrefab;
	[Tooltip("После ClearAllSlots уничтожать ячейки, созданные из префаба (ручные в сцене не трогаем).")]
	[SerializeField] private bool m_DestroySpawnedSlotsOnClearAll = true;

	[Header("Панель рюкзака персонажа")]
	[Tooltip("Сколько первых ячеек под снаряжение (0 = только сумка). Обычно 1 = основное оружие, далее броня и т.д.")]
	[SerializeField] private int m_LeadingEquipmentSlotCount;

	[Header("Слот экипированного оружия (первая leading-ячейка)")]
	[Tooltip("Применяется при создании ячейки в RepaintFromCharacterInventory / RepaintFromPresetSnapshot.")]
	[SerializeField] private InventoryEquipmentSlotAppearance m_EquipmentSlotAppearance = new InventoryEquipmentSlotAppearance();

	[Header("Иконки")]
	[Tooltip("Каталог доступного снаряжения: bake-иконка, если есть; иначе runtime-студия.")]
	[SerializeField] private bool m_UseDefinitionIconsOnly;

	[Header("Прокрутка")]
	[SerializeField, Range(5f, 80f)] private float m_ScrollSensitivity = c_DefaultScrollSensitivity;

	[Header("Связи Canvas (опционально)")]
	[Tooltip("Для панели инвентаря персонажа: зона drag-and-drop с «земли». Заполняется на общем Canvas.")]
	[SerializeField] private InventoryCharacterBagDropZone m_CharacterBagDropZone;
	[Tooltip("Для панели «земля»: зона сброса из рюкзака.")]
	[SerializeField] private InventoryGroundDropZone m_GroundDropZone;
	#endregion

	#region Constants
	public const float c_DefaultScrollSensitivity = 40f;
	#endregion

	#region Private Fields
	private readonly List<InventorySlotView> m_Slots = new List<InventorySlotView>();
	private readonly List<InventorySlotView> m_SpawnedSlots = new List<InventorySlotView>();
	private readonly List<InventorySlotView> m_SlotPool = new List<InventorySlotView>(32);
	private bool m_LeadingEquipmentUsesVehicleLabels;
	private Coroutine m_ScrollToTopCoroutine;
	#endregion

	#region Public Properties
	public IReadOnlyList<InventorySlotView> Slots => m_Slots;
	public int LeadingEquipmentSlotCount => m_LeadingEquipmentSlotCount;
	public InventoryEquipmentSlotAppearance EquipmentSlotAppearance => m_EquipmentSlotAppearance;
	public InventoryCharacterBagDropZone CharacterBagDropZone => m_CharacterBagDropZone;
	public InventoryGroundDropZone GroundDropZone => m_GroundDropZone;
	/// <summary>Leading-слоты показывают подписи машины (вооружение / щиты), а не юнита.</summary>
	public bool LeadingEquipmentUsesVehicleLabels => m_LeadingEquipmentUsesVehicleLabels;

	/// <summary>Префаб и контент заданы — ячейки создаются в runtime, в сцене их может не быть.</summary>
	public bool IsConfiguredForDynamicRepaint => m_SlotPrefab != null && m_SlotsContainer != null;

	public bool UseDefinitionIconsOnly => m_UseDefinitionIconsOnly;

	/// <summary>Родитель для динамических ячеек (Content в ScrollRect).</summary>
	public Transform SlotsContainerTransform => m_SlotsContainer;
	#endregion

	#region Runtime Configuration
	public void SetLeadingEquipmentSlotCount(int _count)
	{
		m_LeadingEquipmentSlotCount = Mathf.Max(0, _count);
	}

	public void SetUseDefinitionIconsOnly(bool _enabled)
	{
		m_UseDefinitionIconsOnly = _enabled;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ApplyScrollSensitivity();
		RefreshSlotsFromHierarchy();
		if (m_CharacterBagDropZone != null)
			m_CharacterBagDropZone.BindBagPanel(this);
		if (m_GroundDropZone != null)
			m_GroundDropZone.BindGroundPanel(this);
	}

	private void OnDisable()
	{
		m_ScrollToTopCoroutine = null;
	}
	#endregion

	#region Public Methods
	/// <summary>Пересобрать список слотов из иерархии (ручные + уже созданные из префаба).</summary>
	public void RefreshSlotsFromHierarchy()
	{
		m_Slots.Clear();
		Transform root = m_SlotsContainer != null ? m_SlotsContainer : transform;
		CollectDirectChildSlotViews(root, m_Slots);
		m_SpawnedSlots.RemoveAll(_s => _s == null);
		for (int i = m_SpawnedSlots.Count - 1; i >= 0; i--)
		{
			if (!m_Slots.Contains(m_SpawnedSlots[i]))
				m_SpawnedSlots.RemoveAt(i);
		}

		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView s = m_Slots[i];
			if (s != null && s.IsRuntimeSpawned && !m_SpawnedSlots.Contains(s))
				m_SpawnedSlots.Add(s);
		}
	}

	/// <summary>Убрать ячейку из учёта панели перед перетаскиванием (иерархию не трогает).</summary>
	public void DetachSlotForDrag(InventorySlotView _slot)
	{
		if (_slot == null)
			return;
		m_Slots.Remove(_slot);
		m_SpawnedSlots.Remove(_slot);
	}

	/// <summary>Перепривязать перетаскиваемую ячейку к контенту этой панели (рюкзак / земля).</summary>
	public bool AdoptDraggedSlot(InventorySlotView _slot)
	{
		if (_slot == null || m_SlotsContainer == null)
			return false;

		// На земле нет empty equipment-слотов: не adopt'ить персистентный EquipSlot_*.
		if (m_LeadingEquipmentSlotCount <= 0 &&
		    !_slot.IsRuntimeSpawned &&
		    !string.IsNullOrWhiteSpace(_slot.EmptyLocalizationKey))
			return false;

		_slot.transform.SetParent(m_SlotsContainer, false);
		_slot.transform.SetAsLastSibling();
		_slot.SetEmptyLocalizationKey(string.Empty);
		if (!m_Slots.Contains(_slot))
			m_Slots.Add(_slot);
		if (_slot.IsRuntimeSpawned && !m_SpawnedSlots.Contains(_slot))
			m_SpawnedSlots.Add(_slot);

		RebuildContentLayout();
		return true;
	}

	public void RebuildContentLayout()
	{
		if (m_SlotsContainer is RectTransform rt)
			LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
	}

	/// <summary>Сохранить/восстановить позицию скролла вокруг repaint (моды, фильтры, обмен).</summary>
	public Vector2 CaptureScrollNormalizedPosition()
	{
		ScrollRect scroll = ResolveScrollRect();
		if (scroll == null)
			return Vector2.up;

		Vector2 n = scroll.normalizedPosition;
		// Unity отдаёт y=0 и для низа списка, и когда скроллить ещё нечего.
		// Пустой/короткий список считаем верхом, иначе после наполнения окажемся внизу.
		if (n.y < 0.01f && !IsVerticallyScrollable(scroll))
			n.y = 1f;

		return n;
	}

	public void RestoreScrollNormalizedPosition(Vector2 _normalized)
	{
		ScrollRect scroll = ResolveScrollRect();
		if (scroll == null)
			return;

		if (scroll.content != null)
			LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);

		scroll.StopMovement();
		scroll.normalizedPosition = _normalized;
		if (Mathf.Abs(_normalized.y - 1f) < 0.01f)
			ApplyScrollToTopImmediate();
	}

	public void ScrollToTop()
	{
		ApplyScrollToTopImmediate();
		if (!isActiveAndEnabled)
			return;

		if (m_ScrollToTopCoroutine != null)
			StopCoroutine(m_ScrollToTopCoroutine);
		m_ScrollToTopCoroutine = StartCoroutine(CoScrollToTopAfterLayout());
	}

	/// <summary>
	/// Якоря Content сверху, без вертикального stretch — иначе соседний ForceRebuild
	/// переворачивает визуальный порядок списка.
	/// </summary>
	public void StabilizeListLayout()
	{
		ScrollRect scroll = ResolveScrollRect();
		if (scroll != null)
		{
			InventoryUiScrollbarUtility.ConfigureScrollRect(scroll);
			return;
		}

		if (m_SlotsContainer is RectTransform content)
			InventoryUiScrollbarUtility.FixScrollContent(content);
	}

	public void SetRuntimeSlotPrefab(InventorySlotView _slotPrefab)
	{
		if (_slotPrefab == null)
			return;

		m_SlotPrefab = _slotPrefab;
	}

	/// <summary>Ячейка из префаба только для drag-visual (не учитывается в списке панели).</summary>
	public InventorySlotView CreateDetachedDragVisual(InventorySlotRuntimeData _data, Transform _canvasRoot)
	{
		if (m_SlotPrefab == null || _canvasRoot == null || _data.IsEmpty)
			return null;

		InventorySlotView created = Instantiate(m_SlotPrefab, _canvasRoot);
		created.gameObject.name = $"{m_SlotPrefab.name}_DragVisual";
		created.MarkRuntimeSpawned();
		created.SetItem(_data);
		return created;
	}

	/// <summary>После переноса предмета с «земли»: убрать пустую строку (земля не хранит empty equipment-слоты).</summary>
	public void NotifyGroundSlotItemTakenAway(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		RuntimeInlineModificationBuilder.ClearAllRowsImmediate(this);

		if (_slot.HasItem)
			_slot.Clear();

		m_Slots.Remove(_slot);
		m_SpawnedSlots.Remove(_slot);

		if (_slot.IsRuntimeSpawned)
			EditorSelectionGuard.DestroyRuntimeSpawnedSlot(_slot.gameObject, transform);
		else if (Application.isPlaying)
			Destroy(_slot.gameObject);

		RefreshSlotsFromHierarchy();
		RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
	}

	/// <summary>Убрать строку «земли» для лута (выход из радиуса подбора и т.п.).</summary>
	public bool TryRemoveGroundListingForPickup(WorldPickupItem _pickup)
	{
		if (_pickup == null)
			return false;

		_pickup.ClearGroundUiListing();
		RuntimeInlineModificationBuilder.ClearAllRowsImmediate(this);

		RefreshSlotsFromHierarchy();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView slot = m_Slots[i];
			if (slot == null || !slot.HasItem)
				continue;
			if (slot.Data.WorldSource != _pickup)
				continue;

			if (slot.IsRuntimeSpawned)
			{
				slot.Clear();
				m_Slots.Remove(slot);
				m_SpawnedSlots.Remove(slot);
				EditorSelectionGuard.DestroyRuntimeSpawnedSlot(slot.gameObject, transform);
			}
			else
				slot.Clear();

			RefreshSlotsFromHierarchy();
			RebuildContentLayout();
			RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
			return true;
		}

		RebuildContentLayout();
		RuntimeInventoryModificationCoordinator.Instance?.NotifyGroundListingRemoved();
		return false;
	}

	public bool TryAdd(InventorySlotRuntimeData _data)
	{
		if (_data.IsEmpty)
			return false;

		if (m_SlotPrefab != null && m_SlotsContainer != null)
		{
			InventorySlotView created = SpawnNewSlotFromPrefab();
			created.SetItem(_data);
			return true;
		}

		EnsureSlotsCached();

		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null && !m_Slots[i].HasItem)
			{
				m_Slots[i].SetItem(_data);
				return true;
			}
		}

		return false;
	}

	/// <summary>Перерисовать ячейки: сначала слоты снаряжения (оружие в первом), затем сумка. Нужны Slot Prefab и Slots Container.</summary>
	public void RepaintFromCharacterInventory(CharacterInventory _inventory)
	{
		if (_inventory == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		Vector2 scroll = CaptureScrollNormalizedPosition();
		ClearAllSlots();
		m_LeadingEquipmentUsesVehicleLabels = false;

		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		InventorySlotRuntimeData main = _inventory.MainHandEquipment;
		InventorySlotRuntimeData head = _inventory.HeadEquipment;
		InventorySlotRuntimeData back = _inventory.BackEquipment;
		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = EnsureLeadingEquipmentSlot(i, false);
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(main);
			else if (i == 1 && !head.IsEmpty)
				cell.SetItem(head);
			else if (i == 2 && !back.IsEmpty)
				cell.SetItem(back);
			else
				cell.Clear();
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(bag[b]);
		}

		RefreshSlotsFromHierarchy();
		ApplySectionHeadersAndOrder();
		RebuildContentLayout();
		RestoreScrollNormalizedPosition(scroll);
	}

	/// <summary>Перерисовать панель из инвентаря машины (3 слота турели + багаж).</summary>
	public void RepaintFromVehicleInventory(VehicleInventory _inventory)
	{
		if (_inventory == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		Vector2 scroll = CaptureScrollNormalizedPosition();
		ClearAllSlots();
		m_LeadingEquipmentUsesVehicleLabels = true;

		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		InventorySlotRuntimeData main = _inventory.MainHandEquipment;
		InventorySlotRuntimeData head = _inventory.HeadEquipment;
		InventorySlotRuntimeData back = _inventory.BackEquipment;
		IReadOnlyList<InventorySlotRuntimeData> bag = _inventory.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = EnsureLeadingEquipmentSlot(i, true);
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(main);
			else if (i == 1 && !head.IsEmpty)
				cell.SetItem(head);
			else if (i == 2 && !back.IsEmpty)
				cell.SetItem(back);
			else
				cell.Clear();
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(bag[b]);
		}

		RefreshSlotsFromHierarchy();
		ApplySectionHeadersAndOrder();
		RebuildContentLayout();
		RestoreScrollNormalizedPosition(scroll);
	}

	/// <summary>Перерисовать панель из снимка пресета (слот оружия + сумка).</summary>
	public void RepaintFromPresetSnapshot(MissionPrepPresetSnapshot _snapshot)
	{
		if (_snapshot == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		Vector2 scroll = CaptureScrollNormalizedPosition();
		ClearAllSlots();
		m_LeadingEquipmentUsesVehicleLabels = false;

		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		InventorySlotRuntimeData main = _snapshot.MainHandEquipment;
		InventorySlotRuntimeData head = _snapshot.HeadEquipment;
		InventorySlotRuntimeData back = _snapshot.BackEquipment;
		IReadOnlyList<InventorySlotRuntimeData> bag = _snapshot.BagItems;

		for (int i = 0; i < lead; i++)
		{
			InventorySlotView cell = EnsureLeadingEquipmentSlot(i, false);
			if (i == 0 && !main.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(main));
			else if (i == 1 && !head.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(head));
			else if (i == 2 && !back.IsEmpty)
				cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(back));
			else
				cell.Clear();
		}

		for (int b = 0; b < bag.Count; b++)
		{
			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(MissionPrepInventoryCopyUtility.CloneSlot(bag[b]));
		}

		RefreshSlotsFromHierarchy();
		ApplySectionHeadersAndOrder();
		RebuildContentLayout();
		RestoreScrollNormalizedPosition(scroll);
	}

	/// <summary>Статический список ячеек (панель «доступное снаряжение»). Пустые записи пропускаются.</summary>
	public void RepaintFromSlotList(IReadOnlyList<InventorySlotRuntimeData> _slots)
	{
		if (_slots == null || m_SlotPrefab == null || m_SlotsContainer == null)
			return;

		ClearAllSlots();
		HideSectionHeaders();
		ApplyScrollToTopImmediate();

		for (int i = 0; i < _slots.Count; i++)
		{
			InventorySlotRuntimeData data = _slots[i];
			if (data.IsEmpty)
				continue;

			InventorySlotView cell = SpawnNewSlotFromPrefab();
			cell.SetItem(data);
		}

		RefreshSlotsFromHierarchy();
		ApplyAvailableEquipmentGroupHeaders();
		RebuildContentLayout();
		ScrollToTop();
	}

	/// <summary>Индекс ячейки среди <see cref="InventorySlotView"/> на панели (без inline-строк модификации).</summary>
	public int GetInventorySlotListIndex(InventorySlotView _slot)
	{
		if (_slot == null)
			return -1;

		RefreshSlotsFromHierarchy();
		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] == _slot)
				return i;
		}

		return -1;
	}

	/// <summary>Индекс ячейки среди прямых детей контента (0 = первая строка).</summary>
	public int GetInventorySlotContainerIndex(InventorySlotView _slot)
	{
		if (_slot == null || m_SlotsContainer == null)
			return -1;

		Transform t = _slot.transform;
		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			if (m_SlotsContainer.GetChild(i) == t)
				return i;
		}

		return -1;
	}

	public void ClearAllSlots()
	{
		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null && m_Slots[i].HasItem)
			{
				InventorySlotRuntimeData d = m_Slots[i].Data;
				if (d.WorldSource != null)
					d.WorldSource.ClearGroundUiListing();
			}
		}

		for (int i = 0; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null)
				m_Slots[i].Clear();
		}

		if (IsConfiguredForDynamicRepaint && m_DestroySpawnedSlotsOnClearAll && m_SlotsContainer != null)
		{
			RecycleRuntimeSpawnedSlotsFromContainer();
			return;
		}

		if (m_DestroySpawnedSlotsOnClearAll && m_SpawnedSlots.Count > 0)
			RecycleSpawnedSlotsList();
	}
	#endregion

	#region Private Methods
	private void EnsureSlotsCached()
	{
		if (m_Slots.Count == 0)
			RefreshSlotsFromHierarchy();
	}

	private static void CollectDirectChildSlotViews(Transform _container, List<InventorySlotView> _outSlots)
	{
		if (_container == null || _outSlots == null)
			return;

		for (int i = 0; i < _container.childCount; i++)
		{
			Transform child = _container.GetChild(i);
			if (child == null || !child.gameObject.activeInHierarchy)
				continue;

			InventorySlotView slot = child.GetComponent<InventorySlotView>();
			if (slot != null)
				_outSlots.Add(slot);
		}
	}

	/// <summary>
	/// Берёт ручной leading-слот из иерархии (сцена/пресет) или создаёт из префаба.
	/// </summary>
	private InventorySlotView EnsureLeadingEquipmentSlot(int _equipmentSlotIndex, bool _vehicleEquipment)
	{
		InventorySlotView existing = FindSceneLeadingEquipmentSlot(_equipmentSlotIndex);
		if (existing != null)
		{
			ConfigureLeadingEquipmentSlot(existing, _equipmentSlotIndex, _vehicleEquipment);
			if (!m_Slots.Contains(existing))
				m_Slots.Add(existing);
			return existing;
		}

		return SpawnNewSlotFromPrefab(_equipmentSlotIndex, _vehicleEquipment);
	}

	private void ApplySectionHeadersAndOrder()
	{
		int lead = Mathf.Max(0, m_LeadingEquipmentSlotCount);
		if (lead <= 0 || m_SlotsContainer == null)
		{
			HideSectionHeaders();
			return;
		}

		HideEquipmentSlotHeaders();

		InventoryPanelSectionHeader equipmentHeader = InventoryPanelSectionHeader.Ensure(
			m_SlotsContainer,
			InventoryPanelSectionHeader.EquipmentObjectName,
			InventoryPanelSectionHeader.EquipmentLocalizationKey,
			"Equipment");
		InventoryPanelSectionHeader bagHeader = InventoryPanelSectionHeader.Ensure(
			m_SlotsContainer,
			InventoryPanelSectionHeader.BagObjectName,
			InventoryPanelSectionHeader.BagLocalizationKey,
			"Bag");

		int sibling = 0;
		if (equipmentHeader != null)
			equipmentHeader.transform.SetSiblingIndex(sibling++);

		for (int i = 0; i < lead && i < m_Slots.Count; i++)
		{
			InventorySlotView slot = m_Slots[i];
			if (slot == null)
				continue;

			if (slot.TryGetComponent(out InventoryEquipmentSlotChrome chrome) && chrome.Header != null)
			{
				chrome.Header.gameObject.SetActive(true);
				chrome.Header.transform.SetSiblingIndex(sibling++);
			}

			slot.transform.SetSiblingIndex(sibling++);
		}

		if (bagHeader != null)
			bagHeader.transform.SetSiblingIndex(sibling++);

		for (int i = lead; i < m_Slots.Count; i++)
		{
			if (m_Slots[i] != null)
				m_Slots[i].transform.SetSiblingIndex(sibling++);
		}
	}

	private void HideSectionHeaders()
	{
		if (m_SlotsContainer == null)
			return;

		Transform equipment = m_SlotsContainer.Find(InventoryPanelSectionHeader.EquipmentObjectName);
		if (equipment != null)
			equipment.gameObject.SetActive(false);

		Transform bag = m_SlotsContainer.Find(InventoryPanelSectionHeader.BagObjectName);
		if (bag != null)
			bag.gameObject.SetActive(false);

		HideEquipmentSlotHeaders();
		HideAvailableGroupHeaders();
	}

	private void HideAvailableGroupHeaders()
	{
		if (m_SlotsContainer == null)
			return;

		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			Transform child = m_SlotsContainer.GetChild(i);
			if (child == null ||
			    !child.name.StartsWith(MissionPrepAvailableEquipmentGroupClassifier.HeaderObjectNamePrefix))
				continue;

			child.gameObject.SetActive(false);
		}
	}

	private void ApplyAvailableEquipmentGroupHeaders()
	{
		if (m_SlotsContainer == null)
			return;

		HideAvailableGroupHeaders();
		if (m_Slots.Count == 0)
			return;

		int sibling = 0;
		MissionPrepAvailableEquipmentGroup? lastGroup = null;
		for (int i = 0; i < m_Slots.Count; i++)
		{
			InventorySlotView slot = m_Slots[i];
			if (slot == null)
				continue;

			MissionPrepAvailableEquipmentGroup group =
				MissionPrepAvailableEquipmentGroupClassifier.GetGroup(slot.HasItem ? slot.Data.Definition : null);
			if (!lastGroup.HasValue || lastGroup.Value != group)
			{
				InventoryPanelSectionHeader header = InventoryPanelSectionHeader.Ensure(
					m_SlotsContainer,
					MissionPrepAvailableEquipmentGroupClassifier.GetObjectName(group),
					MissionPrepAvailableEquipmentGroupClassifier.GetLocalizationKey(group),
					MissionPrepAvailableEquipmentGroupClassifier.GetFallback(group));
				if (header != null)
					header.transform.SetSiblingIndex(sibling++);

				lastGroup = group;
			}

			slot.transform.SetSiblingIndex(sibling++);
		}
	}

	private void HideEquipmentSlotHeaders()
	{
		if (m_SlotsContainer == null)
			return;

		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			Transform child = m_SlotsContainer.GetChild(i);
			if (child == null || !child.name.StartsWith(InventoryEquipmentSlotChrome.HeaderObjectNamePrefix))
				continue;

			child.gameObject.SetActive(false);
		}
	}

	private InventorySlotView FindSceneLeadingEquipmentSlot(int _equipmentSlotIndex)
	{
		if (m_SlotsContainer == null || _equipmentSlotIndex < 0)
			return null;

		int foundIndex = 0;
		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			Transform child = m_SlotsContainer.GetChild(i);
			if (child == null)
				continue;

			InventorySlotView slot = child.GetComponent<InventorySlotView>();
			if (slot == null || slot.IsRuntimeSpawned)
				continue;

			if (foundIndex == _equipmentSlotIndex)
				return slot;

			foundIndex++;
		}

		return null;
	}

	private void ConfigureLeadingEquipmentSlot(
		InventorySlotView _slot,
		int _equipmentSlotIndex,
		bool _vehicleEquipment)
	{
		if (_slot == null)
			return;

		if (_equipmentSlotIndex == 0)
			InventorySlotUiUtility.ConfigureMainHandEquipmentSlot(_slot, m_EquipmentSlotAppearance, _vehicleEquipment);
		else if (_equipmentSlotIndex == 1)
			InventorySlotUiUtility.ConfigureHeadEquipmentSlot(_slot, m_EquipmentSlotAppearance, _vehicleEquipment);
		else if (_equipmentSlotIndex == 2)
			InventorySlotUiUtility.ConfigureBackEquipmentSlot(_slot, m_EquipmentSlotAppearance, _vehicleEquipment);
		else
			InventorySlotUiUtility.ApplyEmptyEquipmentSlotLabel(_slot, _equipmentSlotIndex, _vehicleEquipment);
	}

	private InventorySlotView SpawnNewSlotFromPrefab(int _equipmentSlotIndex = -1, bool _vehicleEquipment = false)
	{
		InventorySlotView created = TakeFromPool();
		if (created == null)
		{
			created = Instantiate(m_SlotPrefab, m_SlotsContainer);
			created.MarkRuntimeSpawned();
		}
		else
		{
			created.transform.SetParent(m_SlotsContainer, false);
			created.gameObject.SetActive(true);
		}

		created.transform.SetAsLastSibling();

		created.gameObject.name = $"{m_SlotPrefab.name}_{m_SpawnedSlots.Count}";
		created.Clear();
		created.SetUseDefinitionIconOnly(m_UseDefinitionIconsOnly);
		created.SetEmptyLocalizationKey(string.Empty);
		m_SpawnedSlots.Add(created);
		m_Slots.Add(created);

		if (_equipmentSlotIndex >= 0)
			ConfigureLeadingEquipmentSlot(created, _equipmentSlotIndex, _vehicleEquipment);

		return created;
	}

	private InventorySlotView TakeFromPool()
	{
		while (m_SlotPool.Count > 0)
		{
			InventorySlotView slot = m_SlotPool[0];
			m_SlotPool.RemoveAt(0);
			if (slot != null)
				return slot;
		}

		return null;
	}

	private void RecycleSpawnedSlotsList()
	{
		for (int i = 0; i < m_SpawnedSlots.Count; i++)
			RecycleSlot(m_SpawnedSlots[i]);

		m_Slots.Clear();
		m_SpawnedSlots.Clear();
		HideEquipmentSlotHeaders();
	}

	private void RecycleRuntimeSpawnedSlotsFromContainer()
	{
		var toRecycle = new List<InventorySlotView>(m_SpawnedSlots.Count + 8);
		for (int i = 0; i < m_SpawnedSlots.Count; i++)
		{
			if (m_SpawnedSlots[i] != null && !toRecycle.Contains(m_SpawnedSlots[i]))
				toRecycle.Add(m_SpawnedSlots[i]);
		}

		for (int i = 0; i < m_SlotsContainer.childCount; i++)
		{
			Transform child = m_SlotsContainer.GetChild(i);
			if (child == null)
				continue;

			InventorySlotView slot = child.GetComponent<InventorySlotView>();
			if (slot == null || !slot.IsRuntimeSpawned)
				continue;

			if (!toRecycle.Contains(slot))
				toRecycle.Add(slot);
		}

		m_Slots.Clear();
		m_SpawnedSlots.Clear();
		HideEquipmentSlotHeaders();
		for (int i = 0; i < toRecycle.Count; i++)
			RecycleSlot(toRecycle[i]);
	}

	private void RecycleSlot(InventorySlotView _slot)
	{
		if (_slot == null)
			return;

		_slot.Clear();
		_slot.SetEmptyLocalizationKey(string.Empty);
		if (_slot.TryGetComponent(out InventoryEquipmentSlotChrome chrome) && chrome.Header != null)
			chrome.Header.gameObject.SetActive(false);
		_slot.gameObject.SetActive(false);
		if (!m_SlotPool.Contains(_slot))
			m_SlotPool.Add(_slot);
	}

	private ScrollRect ResolveScrollRect()
	{
		if (TryGetComponent(out ScrollRect self))
			return self;

		return GetComponentInParent<ScrollRect>();
	}

	private static bool IsVerticallyScrollable(ScrollRect _scroll)
	{
		if (_scroll == null || _scroll.content == null)
			return false;

		RectTransform view = _scroll.viewport != null
			? _scroll.viewport
			: _scroll.transform as RectTransform;
		if (view == null)
			return false;

		return _scroll.content.rect.height > view.rect.height + 1f;
	}

	private void ApplyScrollToTopImmediate()
	{
		RectTransform content = m_SlotsContainer as RectTransform;
		if (content != null)
			content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);

		ScrollRect scroll = ResolveScrollRect();
		if (scroll == null)
			return;

		scroll.StopMovement();
		scroll.verticalNormalizedPosition = 1f;
		if (scroll.verticalScrollbar != null)
			scroll.verticalScrollbar.value = 1f;
	}

	private IEnumerator CoScrollToTopAfterLayout()
	{
		yield return null;
		ApplyScrollToTopImmediate();
		yield return null;
		ApplyScrollToTopImmediate();
		m_ScrollToTopCoroutine = null;
	}

	private void ApplyScrollSensitivity()
	{
		ScrollRect scrollRect = ResolveScrollRect();
		if (scrollRect == null)
			return;

		scrollRect.scrollSensitivity = m_ScrollSensitivity;
	}
	#endregion
}
