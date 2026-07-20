using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Визуал «второго оружия» за спиной (PUBG-style): два слота на Spine_02.
/// Left — только гранатомёт. Right — sniper/shotgun/прочее оружие (без гранатомётов).
/// Источник — сумка инвентаря; MainHand и лут не затрагиваются.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(11)]
public sealed class UnitBackWeaponHolsterVisuals : MonoBehaviour
{
	#region Constants
	public const string LeftCellName = "Holster_Weapon_Cell_Left";
	public const string RightCellName = "Holster_Weapon_Cell_Right";
	private const int c_SlotCount = 2;
	#endregion

	#region Serialized Fields
	[Header("Anchors (empty dummies on Spine_02)")]
	[SerializeField] private Transform m_LeftCell;
	[SerializeField] private Transform m_RightCell;

	[Header("LOD")]
	[Tooltip("Дальше этой дистанции от камеры визуалы слотов скрываются.")]
	[SerializeField, Min(5f)] private float m_HideDistanceMeters = 45f;
	[SerializeField, Min(0.1f)] private float m_LodCheckIntervalSeconds = 0.2f;

	[Header("Debug")]
	[SerializeField] private bool m_ForceVisibleInEditor;
	#endregion

	#region Private Fields
	private CharacterInventory m_Inventory;
	private UnitEquipment m_Equipment;
	private UnitRocketLauncherOrderController m_RocketOrder;
	private CharacterInventory m_SubscribedInventory;
	private UnitEquipment m_SubscribedEquipment;
	private UnitRocketLauncherOrderController m_SubscribedRocketOrder;
	private readonly GameObject[] m_SlotInstances = new GameObject[c_SlotCount];
	private readonly GameObject[] m_AppliedPrefabs = new GameObject[c_SlotCount];
	private readonly ItemDefinition[] m_AppliedDefinitions = new ItemDefinition[c_SlotCount];
	private readonly ItemInstanceState[] m_AppliedInstanceStates = new ItemInstanceState[c_SlotCount];
	private readonly bool[] m_AppliedLoaded = new bool[c_SlotCount];
	private float m_NextLodCheckTime;
	private bool m_LodVisible = true;
	private static readonly List<HolsterCandidate> s_Candidates = new List<HolsterCandidate>(16);
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		EnsureCells();
	}

	private void OnEnable()
	{
		ResolveReferences();
		Subscribe(true);
		Refresh();
	}

	private void Start()
	{
		// Loadout часто заполняет сумку после OnEnable — повторный sync.
		ResolveReferences();
		Subscribe(true);
		Refresh();
	}

	private void OnDisable()
	{
		Subscribe(false);
		ClearAllSlots();
	}

	private void LateUpdate()
	{
		if (Time.unscaledTime < m_NextLodCheckTime)
			return;

		m_NextLodCheckTime = Time.unscaledTime + m_LodCheckIntervalSeconds;
		UpdateLodVisibility();
	}
	#endregion

	#region Public Methods
	public void Refresh()
	{
		ResolveReferences();
		EnsureCells();
		Subscribe(true);

		if (m_Inventory == null)
		{
			ClearAllSlots();
			return;
		}

		CollectCandidates(s_Candidates);
		ResolveSlotAssignments(s_Candidates, out HolsterCandidate left, out HolsterCandidate right);
		ApplySlot(0, m_LeftCell, left);
		ApplySlot(1, m_RightCell, right);
		ApplyLodToInstances();
	}
	#endregion

	#region Selection
	private void CollectCandidates(List<HolsterCandidate> _buffer)
	{
		_buffer.Clear();
		if (m_Inventory == null)
			return;

		for (int i = 0; i < m_Inventory.BagCount; i++)
		{
			InventorySlotRuntimeData slot = m_Inventory.BagItems[i];
			if (slot.IsEmpty || slot.Definition == null)
				continue;

			ItemDefinition def = slot.Definition;
			if (!TryResolveHolsterPrefab(def, out GameObject prefab))
				continue;

			// MainHand в сумке не лежит — не фильтруем по Definition (ломало дубликаты).
			// Гранатомёт в руках во время приказа — не дублировать за спиной.
			if (m_RocketOrder != null && m_RocketOrder.IsBagSlotHeldAsActiveLauncher(i, slot))
				continue;

			_buffer.Add(new HolsterCandidate
			{
				Definition = def,
				Prefab = prefab,
				InstanceState = slot.InstanceState,
				BagIndex = i,
				IsRocketLauncher = def.IsRocketLauncher,
				IsSniper = IsSniperWeapon(def),
				IsShotgun = IsShotgunWeapon(def),
				IsLoaded = !def.IsRocketLauncher || RocketLauncherVisualUtility.ResolveIsLoaded(slot)
			});
		}
	}

	/// <summary>
	/// Left — только гранатомёты (если нет — слот пустой).
	/// Right — любое оружие кроме гранатомётов: sniper → shotgun → остальное.
	/// </summary>
	private static void ResolveSlotAssignments(
		List<HolsterCandidate> _candidates,
		out HolsterCandidate _left,
		out HolsterCandidate _right)
	{
		_left = default;
		_right = default;

		_left = TakeFirst(_candidates, static c => c.IsRocketLauncher);

		_right = TakeFirst(_candidates, static c => !c.IsRocketLauncher && c.IsSniper);
		if (_right.Definition == null)
			_right = TakeFirst(_candidates, static c => !c.IsRocketLauncher && c.IsShotgun);
		if (_right.Definition == null)
			_right = TakeFirst(_candidates, static c => !c.IsRocketLauncher);
	}

	private static HolsterCandidate TakeFirst(List<HolsterCandidate> _candidates, System.Predicate<HolsterCandidate> _match)
	{
		for (int i = 0; i < _candidates.Count; i++)
		{
			if (!_match(_candidates[i]))
				continue;

			HolsterCandidate candidate = _candidates[i];
			_candidates.RemoveAt(i);
			return candidate;
		}

		return default;
	}

	private static bool TryResolveHolsterPrefab(ItemDefinition _definition, out GameObject _prefab)
	{
		_prefab = null;
		if (_definition == null)
			return false;

		if (_definition.IsRocketLauncher)
		{
			_prefab = _definition.RocketLauncherHandPrefab != null
				? _definition.RocketLauncherHandPrefab
				: _definition.EquippedVisualPrefab;
			return _prefab != null;
		}

		if (_definition.WeaponDefinition == null)
			return false;

		_prefab = _definition.EquippedVisualPrefab;
		return _prefab != null;
	}

	private static bool IsShotgunWeapon(ItemDefinition _definition)
	{
		return _definition != null &&
		       _definition.WeaponDefinition != null &&
		       _definition.WeaponDefinition.WeaponClass == WeaponClassType.Shotgun;
	}

	private static bool IsSniperWeapon(ItemDefinition _definition)
	{
		if (_definition == null || _definition.WeaponDefinition == null)
			return false;

		WeaponDefinition weapon = _definition.WeaponDefinition;
		if (weapon.RequiresManualBoltCycle)
			return true;

		if (weapon.SupportedMagazineType == MagazineType.Svd ||
		    weapon.SupportedMagazineType == MagazineType.Bolt762x54R)
			return true;

		return ContainsSniperToken(_definition.LocalizationKey) ||
		       ContainsSniperToken(_definition.name) ||
		       ContainsSniperToken(weapon.name);
	}

	private static bool ContainsSniperToken(string _value)
	{
		if (string.IsNullOrEmpty(_value))
			return false;

		return _value.IndexOf("sniper", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		       _value.IndexOf("mosin", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
		       _value.IndexOf("svd", System.StringComparison.OrdinalIgnoreCase) >= 0;
	}
	#endregion

	#region Slot apply / LOD
	private void ApplySlot(int _index, Transform _cell, HolsterCandidate _candidate)
	{
		if (_index < 0 || _index >= c_SlotCount)
			return;

		ItemDefinition definition = _candidate.Definition;
		if (definition == null || _cell == null)
		{
			ClearSlot(_index, _cell);
			return;
		}

		GameObject prefab = _candidate.Prefab;
		if (prefab == null && !TryResolveHolsterPrefab(definition, out prefab))
		{
			ClearSlot(_index, _cell);
			return;
		}

		bool instanceAlive = m_SlotInstances[_index] != null;
		bool sameIdentity =
			instanceAlive &&
			m_AppliedDefinitions[_index] == definition &&
			m_AppliedPrefabs[_index] == prefab &&
			ReferenceEquals(m_AppliedInstanceStates[_index], _candidate.InstanceState);

		if (sameIdentity)
		{
			if (definition.IsRocketLauncher && m_AppliedLoaded[_index] != _candidate.IsLoaded)
			{
				m_AppliedLoaded[_index] = _candidate.IsLoaded;
				RocketLauncherVisualUtility.ApplyLoadedRocketVisual(m_SlotInstances[_index], _candidate.IsLoaded);
			}

			return;
		}

		ClearSlot(_index, _cell);
		GameObject instance = CharacterDecorationSpawnUtility.SpawnBackWeaponHolsterVisual(_cell, prefab);
		m_SlotInstances[_index] = instance;
		m_AppliedPrefabs[_index] = prefab;
		m_AppliedDefinitions[_index] = definition;
		m_AppliedInstanceStates[_index] = _candidate.InstanceState;
		m_AppliedLoaded[_index] = _candidate.IsLoaded;

		if (instance != null)
		{
			if (definition.IsRocketLauncher)
				RocketLauncherVisualUtility.ApplyLoadedRocketVisual(instance, _candidate.IsLoaded);

			instance.SetActive(m_LodVisible);
		}
	}

	private void ClearSlot(int _index, Transform _cell = null)
	{
		if (_index < 0 || _index >= c_SlotCount)
			return;

		CharacterDecorationSpawnUtility.ClearDecorationImmediate(ref m_SlotInstances[_index]);
		if (_cell == null)
			_cell = _index == 0 ? m_LeftCell : m_RightCell;
		CharacterDecorationSpawnUtility.ClearAllChildrenImmediate(_cell);

		m_AppliedPrefabs[_index] = null;
		m_AppliedDefinitions[_index] = null;
		m_AppliedInstanceStates[_index] = null;
		m_AppliedLoaded[_index] = false;
	}

	private void ClearAllSlots()
	{
		for (int i = 0; i < c_SlotCount; i++)
			ClearSlot(i);
	}

	private void UpdateLodVisibility()
	{
#if UNITY_EDITOR
		if (m_ForceVisibleInEditor && !Application.isPlaying)
		{
			SetLodVisible(true);
			return;
		}
#endif
		Camera camera = Camera.main;
		if (camera == null)
		{
			SetLodVisible(true);
			return;
		}

		float sqr = m_HideDistanceMeters * m_HideDistanceMeters;
		bool visible = (transform.position - camera.transform.position).sqrMagnitude <= sqr;
		SetLodVisible(visible);
	}

	private void SetLodVisible(bool _visible)
	{
		if (m_LodVisible == _visible)
			return;

		m_LodVisible = _visible;
		ApplyLodToInstances();
	}

	private void ApplyLodToInstances()
	{
		for (int i = 0; i < c_SlotCount; i++)
		{
			if (m_SlotInstances[i] != null)
				m_SlotInstances[i].SetActive(m_LodVisible);
		}
	}
	#endregion

	#region Refs / cells
	private void ResolveReferences()
	{
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_Inventory == null)
			m_Inventory = GetComponentInParent<CharacterInventory>();
		if (m_Inventory == null)
			m_Inventory = GetComponentInChildren<CharacterInventory>(true);

		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_Equipment == null)
			m_Equipment = GetComponentInChildren<UnitEquipment>(true);

		if (m_RocketOrder == null)
			m_RocketOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (m_RocketOrder == null)
			m_RocketOrder = GetComponentInChildren<UnitRocketLauncherOrderController>(true);
	}

	private void EnsureCells()
	{
		if (m_LeftCell != null && m_RightCell != null)
			return;

		Transform spine02 = FindBone("Spine_02");
		if (spine02 == null)
			return;

		if (m_LeftCell == null)
			m_LeftCell = FindOrCreateCell(spine02, LeftCellName, new Vector3(-0.18f, 0.05f, -0.2f), new Vector3(0f, 0f, 15f));
		if (m_RightCell == null)
			m_RightCell = FindOrCreateCell(spine02, RightCellName, new Vector3(0.18f, 0.05f, -0.2f), new Vector3(0f, 0f, -15f));
	}

	private static Transform FindOrCreateCell(Transform _parent, string _name, Vector3 _localPos, Vector3 _localEuler)
	{
		Transform existing = _parent.Find(_name);
		if (existing != null)
			return existing;

		GameObject cell = new GameObject(_name);
		Transform t = cell.transform;
		t.SetParent(_parent, false);
		t.localPosition = _localPos;
		t.localRotation = Quaternion.Euler(_localEuler);
		t.localScale = Vector3.one;
		return t;
	}

	private Transform FindBone(string _name)
	{
		Transform[] transforms = GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
		{
			if (transforms[i] != null && transforms[i].name == _name)
				return transforms[i];
		}

		return null;
	}

	private void Subscribe(bool _subscribe)
	{
		if (m_SubscribedInventory != null)
			m_SubscribedInventory.InventoryChanged -= OnInventoryChanged;
		if (m_SubscribedEquipment != null)
			m_SubscribedEquipment.EquipmentChanged -= OnEquipmentChanged;
		if (m_SubscribedRocketOrder != null)
			m_SubscribedRocketOrder.OrderStateChanged -= OnRocketOrderStateChanged;

		m_SubscribedInventory = null;
		m_SubscribedEquipment = null;
		m_SubscribedRocketOrder = null;

		if (!_subscribe)
			return;

		m_SubscribedInventory = m_Inventory;
		m_SubscribedEquipment = m_Equipment;
		m_SubscribedRocketOrder = m_RocketOrder;

		if (m_SubscribedInventory != null)
			m_SubscribedInventory.InventoryChanged += OnInventoryChanged;
		if (m_SubscribedEquipment != null)
			m_SubscribedEquipment.EquipmentChanged += OnEquipmentChanged;
		if (m_SubscribedRocketOrder != null)
			m_SubscribedRocketOrder.OrderStateChanged += OnRocketOrderStateChanged;
	}

	private void OnInventoryChanged(CharacterInventory _)
	{
		Refresh();
	}

	private void OnEquipmentChanged()
	{
		Refresh();
	}

	private void OnRocketOrderStateChanged()
	{
		Refresh();
	}
	#endregion

	#region Nested
	private struct HolsterCandidate
	{
		public ItemDefinition Definition;
		public GameObject Prefab;
		public ItemInstanceState InstanceState;
		public int BagIndex;
		public bool IsRocketLauncher;
		public bool IsSniper;
		public bool IsShotgun;
		public bool IsLoaded;
	}
	#endregion
}
