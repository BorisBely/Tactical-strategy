using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инвентарно-зависимые подсумки и прикреплённые гранаты на теле юнита.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10)]
public sealed class UnitInventoryBodyDecorations : MonoBehaviour
{
	#region Constants
	public const string SideRightProfileId = "body_pouch_side_right";
	public const string SideLeftProfileId = "body_pouch_side_left";
	public const string ChestProfileId = "body_pouch_chest";

	private const int c_MinSideVariant = 1;
	private const int c_MaxSideVariant = 3;
	private const int c_DefaultSideVariant = 1;
	private const int c_DefaultChestVariant = 0;
	private const int c_AttachedGrenadeCellCount = 2;
	private const int c_AttachedGrenadeStartIndex = 2;
	private const float c_DecorationPosePositionEpsilon = 0.0001f;
	private const float c_DecorationPoseRotationEpsilon = 0.01f;
	#endregion

	#region Serialized Fields
	[Header("Anchors")]
	[SerializeField] private Transform m_Spine01Anchor;
	[SerializeField] private Transform m_Spine02Anchor;
	[SerializeField] private Transform m_Spine03Anchor;
	[SerializeField] private Transform[] m_AttachedGrenadeCells = new Transform[c_AttachedGrenadeCellCount];

	[Header("Magazine Pouches")]
	[SerializeField] private CharacterBodyDecorationVariant m_MagDefaultVariant;
	[SerializeField] private CharacterBodyDecorationVariant[] m_MagM4Variants = new CharacterBodyDecorationVariant[3];
	[SerializeField] private CharacterBodyDecorationVariant[] m_MagAkVariants = new CharacterBodyDecorationVariant[3];

	[Header("Side Pouches")]
	[SerializeField] private CharacterBodyDecorationVariant[] m_SideRightVariants = new CharacterBodyDecorationVariant[3];
	[SerializeField] private CharacterBodyDecorationVariant[] m_SideLeftVariants = new CharacterBodyDecorationVariant[3];

	[Header("Chest Pouches")]
	[SerializeField] private CharacterBodyDecorationVariant[] m_ChestVariants = new CharacterBodyDecorationVariant[2];

	[Header("Grenade Pouches")]
	[SerializeField] private CharacterBodyDecorationVariant m_GrenadeRightPouchVariant;
	[SerializeField] private CharacterBodyDecorationVariant m_GrenadeLeftPouchVariant;
	#endregion

	#region Private Fields
	private CharacterInventory m_SubscribedInventory;
	private GameObject m_MagPouchInstance;
	private GameObject m_SideRightInstance;
	private GameObject m_SideLeftInstance;
	private GameObject m_ChestInstance;
	private GameObject m_GrenadeRightPouchInstance;
	private GameObject m_GrenadeLeftPouchInstance;
	private GameObject[] m_AttachedGrenadeInstances;
	private GameObject m_AppliedMagPouchPrefab;
	private GameObject m_AppliedSideRightPrefab;
	private GameObject m_AppliedSideLeftPrefab;
	private GameObject m_AppliedChestPrefab;
	private GameObject m_AppliedGrenadeRightPouchPrefab;
	private GameObject m_AppliedGrenadeLeftPouchPrefab;
	private GameObject[] m_AppliedAttachedGrenadePrefabs;
	private int m_LastGrenadePouchCount = -1;
	private MagazineCaliberVisualPreference m_LastMagazinePreference = MagazineCaliberVisualPreference.Undefined;
	private int m_LastMagazineVariant = -1;
	private MagazineCaliberVisualPreference m_AppliedMagazinePreference = MagazineCaliberVisualPreference.Undefined;
	private int m_AppliedMagazineVariant = -1;
	#endregion

	#region Public Properties
	public bool OnlyGrenades { get; set; }
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureAttachedInstanceArray();
	}

	private void OnEnable()
	{
		SubscribeToInventory(ResolveInventory());
	}

	private void Start()
	{
		RefreshFromInventory(ResolveInventory());
	}

	private void OnDisable()
	{
		SubscribeToInventory(null);
	}
	#endregion

	#region Public Methods
	public void RefreshFromInventory(CharacterInventory _inventory)
	{
		EnsureAttachedInstanceArray();
		if (_inventory == null)
			_inventory = ResolveInventory();

		SubscribeToInventory(_inventory);

		if (!OnlyGrenades)
		{
			UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
			ApplyPreferencePouches(traits);
		}

		RefreshInventoryDependentDecorations(_inventory);
	}

	public void RefreshFromPresetSnapshot(MissionPrepPresetSnapshot _snapshot)
	{
		EnsureAttachedInstanceArray();

		if (!OnlyGrenades)
		{
			UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
			ApplyPreferencePouches(traits);
		}

		RefreshSnapshotDependentDecorations(_snapshot);
	}
	#endregion

	#region Private Methods
	private CharacterInventory ResolveInventory()
	{
		CharacterInventory inventory = GetComponentInParent<CharacterInventory>(true);
		if (inventory != null)
			return inventory;

		return GetComponentInChildren<CharacterInventory>(true);
	}

	private void SubscribeToInventory(CharacterInventory _inventory)
	{
		if (m_SubscribedInventory == _inventory)
			return;

		if (m_SubscribedInventory != null)
			m_SubscribedInventory.InventoryChanged -= HandleInventoryChanged;

		m_SubscribedInventory = _inventory;

		if (m_SubscribedInventory != null)
			m_SubscribedInventory.InventoryChanged += HandleInventoryChanged;
	}

	private void HandleInventoryChanged(CharacterInventory _inventory)
	{
		RefreshInventoryDependentDecorations(_inventory);
	}

	private void RefreshInventoryDependentDecorations(CharacterInventory _inventory)
	{
		if (_inventory == null)
			return;

		if (!OnlyGrenades)
		{
			MagazineCaliberVisualPreference magazinePreference = MagazineCaliberPreferenceResolver.Resolve(_inventory);
			ApplyMagazinePouch(magazinePreference);
		}

		List<ItemDefinition> grenades = GrenadeVisualOrderResolver.CollectOrderedGrenades(_inventory);
		ApplyGrenadePouches(grenades.Count);
		ApplyAttachedGrenades(grenades);
	}

	private void RefreshSnapshotDependentDecorations(MissionPrepPresetSnapshot _snapshot)
	{
		if (_snapshot == null)
			return;

		if (!OnlyGrenades)
		{
			MagazineCaliberVisualPreference magazinePreference = MagazineCaliberPreferenceResolver.Resolve(_snapshot);
			ApplyMagazinePouch(magazinePreference);
		}

		List<ItemDefinition> grenades = GrenadeVisualOrderResolver.CollectOrderedGrenades(_snapshot);
		ApplyGrenadePouches(grenades.Count);
		ApplyAttachedGrenades(grenades);
	}

	private void ApplyPreferencePouches(UnitIndividualTraits _traits)
	{
		int rightVariant = ResolveTraitVariant(_traits, SideRightProfileId, c_DefaultSideVariant);
		int leftVariant = ResolveTraitVariant(_traits, SideLeftProfileId, c_DefaultSideVariant);
		int chestVariant = ResolveTraitVariant(_traits, ChestProfileId, c_DefaultChestVariant);

		rightVariant = Mathf.Clamp(rightVariant, c_MinSideVariant, c_MaxSideVariant);
		leftVariant = Mathf.Clamp(leftVariant, c_MinSideVariant, c_MaxSideVariant);

		ApplyDecoration(ref m_SideRightInstance, ref m_AppliedSideRightPrefab, m_Spine01Anchor, GetArrayVariant(m_SideRightVariants, rightVariant));
		ApplyDecoration(ref m_SideLeftInstance, ref m_AppliedSideLeftPrefab, m_Spine01Anchor, GetArrayVariant(m_SideLeftVariants, leftVariant));

		if (chestVariant <= 0)
			ClearTrackedDecoration(ref m_ChestInstance, ref m_AppliedChestPrefab);
		else
			ApplyDecoration(ref m_ChestInstance, ref m_AppliedChestPrefab, m_Spine03Anchor, GetArrayVariant(m_ChestVariants, chestVariant));
	}

	private void ApplyMagazinePouch(MagazineCaliberVisualPreference _preference)
	{
		int variant = ResolveMagazineVariant(_preference);
		CharacterBodyDecorationVariant config = ResolveMagazinePouchConfig(_preference, variant);

		if (m_AppliedMagazinePreference == _preference &&
		    m_AppliedMagazineVariant == variant &&
		    IsSameDecorationInstance(m_MagPouchInstance, m_AppliedMagPouchPrefab, m_Spine01Anchor, config))
			return;

		m_AppliedMagazinePreference = _preference;
		m_AppliedMagazineVariant = variant;
		ApplyDecoration(ref m_MagPouchInstance, ref m_AppliedMagPouchPrefab, m_Spine01Anchor, config);
	}

	private CharacterBodyDecorationVariant ResolveMagazinePouchConfig(
		MagazineCaliberVisualPreference _preference,
		int _variant)
	{
		return _preference switch
		{
			MagazineCaliberVisualPreference.Five56 when _variant > 0 => GetArrayVariant(m_MagM4Variants, _variant),
			MagazineCaliberVisualPreference.Ak when _variant > 0 => GetArrayVariant(m_MagAkVariants, _variant),
			_ => m_MagDefaultVariant
		};
	}

	private int ResolveMagazineVariant(MagazineCaliberVisualPreference _preference)
	{
		if (_preference == MagazineCaliberVisualPreference.Undefined)
		{
			m_LastMagazinePreference = _preference;
			m_LastMagazineVariant = 0;
			return 0;
		}

		if (m_LastMagazinePreference == _preference && m_LastMagazineVariant >= 0)
			return m_LastMagazineVariant;

		m_LastMagazinePreference = _preference;
		int roll = Random.Range(0, 100);
		if (roll < 10)
		{
			m_LastMagazineVariant = 0;
			return 0;
		}

		m_LastMagazineVariant = roll < 40 ? 1 : roll < 70 ? 2 : 3;
		return m_LastMagazineVariant;
	}

	private void ApplyGrenadePouches(int _grenadeCount)
	{
		if (_grenadeCount == m_LastGrenadePouchCount)
			return;

		m_LastGrenadePouchCount = _grenadeCount;

		if (_grenadeCount >= 1)
			ApplyDecoration(ref m_GrenadeRightPouchInstance, ref m_AppliedGrenadeRightPouchPrefab, m_Spine01Anchor, m_GrenadeRightPouchVariant);
		else
			ClearTrackedDecoration(ref m_GrenadeRightPouchInstance, ref m_AppliedGrenadeRightPouchPrefab);

		if (_grenadeCount >= 2)
			ApplyDecoration(ref m_GrenadeLeftPouchInstance, ref m_AppliedGrenadeLeftPouchPrefab, m_Spine01Anchor, m_GrenadeLeftPouchVariant);
		else
			ClearTrackedDecoration(ref m_GrenadeLeftPouchInstance, ref m_AppliedGrenadeLeftPouchPrefab);
	}

	private void ApplyAttachedGrenades(IReadOnlyList<ItemDefinition> _grenades)
	{
		for (int i = 0; i < m_AttachedGrenadeCells.Length; i++)
		{
			int grenadeIndex = i + c_AttachedGrenadeStartIndex;
			ItemDefinition grenade = _grenades != null && grenadeIndex < _grenades.Count ? _grenades[grenadeIndex] : null;
			GameObject prefab = grenade != null ? grenade.AttachedBodyVisualPrefab : null;
			ApplyAttachedGrenade(i, prefab);
		}
	}

	private void ApplyAttachedGrenade(int _cellIndex, GameObject _prefab)
	{
		if (_cellIndex < 0 || _cellIndex >= m_AttachedGrenadeInstances.Length)
			return;

		EnsureAppliedAttachedGrenadePrefabArray();

		if (_prefab == null || m_AttachedGrenadeCells == null || _cellIndex >= m_AttachedGrenadeCells.Length)
		{
			ClearTrackedDecoration(ref m_AttachedGrenadeInstances[_cellIndex], ref m_AppliedAttachedGrenadePrefabs[_cellIndex]);
			return;
		}

		GameObject currentPrefab = m_AppliedAttachedGrenadePrefabs[_cellIndex];
		GameObject currentInstance = m_AttachedGrenadeInstances[_cellIndex];
		Transform cell = m_AttachedGrenadeCells[_cellIndex];
		if (currentInstance != null &&
		    ReferenceEquals(currentPrefab, _prefab) &&
		    cell != null &&
		    currentInstance.transform.parent == cell)
			return;

		ClearTrackedDecoration(ref m_AttachedGrenadeInstances[_cellIndex], ref m_AppliedAttachedGrenadePrefabs[_cellIndex]);
		if (cell == null)
			return;

		m_AppliedAttachedGrenadePrefabs[_cellIndex] = _prefab;
		m_AttachedGrenadeInstances[_cellIndex] = CharacterDecorationSpawnUtility.SpawnPrefab(cell, _prefab);
	}

	private static int ResolveTraitVariant(UnitIndividualTraits _traits, string _profileId, int _fallback)
	{
		if (_traits != null && _traits.TryGetPreference(_profileId, out UnitEquipmentVisualPreferenceEntry preference))
			return preference.PrimaryVariant;

		return _fallback;
	}

	private static CharacterBodyDecorationVariant GetArrayVariant(CharacterBodyDecorationVariant[] _variants, int _oneBasedVariant)
	{
		if (_variants == null || _oneBasedVariant <= 0 || _oneBasedVariant > _variants.Length)
			return default;

		return _variants[_oneBasedVariant - 1];
	}

	private static void ApplyDecoration(
		ref GameObject _instance,
		ref GameObject _appliedPrefab,
		Transform _anchor,
		CharacterBodyDecorationVariant _config)
	{
		GameObject targetPrefab = _config.Prefab;
		if (targetPrefab == null || _anchor == null)
		{
			ClearTrackedDecoration(ref _instance, ref _appliedPrefab);
			return;
		}

		if (IsSameDecorationInstance(_instance, _appliedPrefab, _anchor, _config))
			return;

		ClearTrackedDecoration(ref _instance, ref _appliedPrefab);
		_appliedPrefab = targetPrefab;
		_instance = CharacterDecorationSpawnUtility.SpawnDecoration(_anchor, _config);
	}

	private static bool IsSameDecorationInstance(
		GameObject _instance,
		GameObject _appliedPrefab,
		Transform _anchor,
		CharacterBodyDecorationVariant _config)
	{
		if (_instance == null || _anchor == null || _config.Prefab == null)
			return false;

		return ReferenceEquals(_appliedPrefab, _config.Prefab) &&
		       _instance.transform.parent == _anchor &&
		       HasMatchingLocalPose(_instance.transform, _config);
	}

	private static bool HasMatchingLocalPose(Transform _transform, CharacterBodyDecorationVariant _config)
	{
		if (_transform == null)
			return false;

		if ((_transform.localPosition - _config.LocalPosition).sqrMagnitude >
		    c_DecorationPosePositionEpsilon * c_DecorationPosePositionEpsilon)
			return false;

		return Quaternion.Angle(_transform.localRotation, Quaternion.Euler(_config.LocalEulerAngles)) <=
		       c_DecorationPoseRotationEpsilon;
	}

	private static void ClearTrackedDecoration(ref GameObject _instance, ref GameObject _appliedPrefab)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref _instance);
		_appliedPrefab = null;
	}

	private void EnsureAttachedInstanceArray()
	{
		int count = m_AttachedGrenadeCells != null ? m_AttachedGrenadeCells.Length : 0;
		if (m_AttachedGrenadeInstances != null && m_AttachedGrenadeInstances.Length == count)
			return;

		m_AttachedGrenadeInstances = new GameObject[count];
		m_AppliedAttachedGrenadePrefabs = new GameObject[count];
	}

	private void EnsureAppliedAttachedGrenadePrefabArray()
	{
		int count = m_AttachedGrenadeInstances != null ? m_AttachedGrenadeInstances.Length : 0;
		if (m_AppliedAttachedGrenadePrefabs != null && m_AppliedAttachedGrenadePrefabs.Length == count)
			return;

		m_AppliedAttachedGrenadePrefabs = new GameObject[count];
	}
	#endregion
}
