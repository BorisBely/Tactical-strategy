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
	private MagazineCaliberVisualPreference m_LastMagazinePreference = MagazineCaliberVisualPreference.Undefined;
	private int m_LastMagazineVariant = -1;
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
		UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
		ApplyPreferencePouches(traits);

		MagazineCaliberVisualPreference magazinePreference = MagazineCaliberPreferenceResolver.Resolve(_inventory);
		ApplyMagazinePouch(magazinePreference);

		List<ItemDefinition> grenades = GrenadeVisualOrderResolver.CollectOrderedGrenades(_inventory);
		ApplyGrenadePouches(grenades.Count);
		ApplyAttachedGrenades(grenades);
	}

	public void RefreshFromPresetSnapshot(MissionPrepPresetSnapshot _snapshot)
	{
		EnsureAttachedInstanceArray();
		UnitIndividualTraits traits = GetComponentInParent<UnitIndividualTraits>(true);
		ApplyPreferencePouches(traits);

		MagazineCaliberVisualPreference magazinePreference = MagazineCaliberPreferenceResolver.Resolve(_snapshot);
		ApplyMagazinePouch(magazinePreference);

		List<ItemDefinition> grenades = GrenadeVisualOrderResolver.CollectOrderedGrenades(_snapshot);
		ApplyGrenadePouches(grenades.Count);
		ApplyAttachedGrenades(grenades);
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
		RefreshFromInventory(_inventory);
	}

	private void ApplyPreferencePouches(UnitIndividualTraits _traits)
	{
		int rightVariant = ResolveTraitVariant(_traits, SideRightProfileId, c_DefaultSideVariant);
		int leftVariant = ResolveTraitVariant(_traits, SideLeftProfileId, c_DefaultSideVariant);
		int chestVariant = ResolveTraitVariant(_traits, ChestProfileId, c_DefaultChestVariant);

		rightVariant = Mathf.Clamp(rightVariant, c_MinSideVariant, c_MaxSideVariant);
		leftVariant = Mathf.Clamp(leftVariant, c_MinSideVariant, c_MaxSideVariant);

		ApplyDecoration(ref m_SideRightInstance, m_Spine01Anchor, GetArrayVariant(m_SideRightVariants, rightVariant));
		ApplyDecoration(ref m_SideLeftInstance, m_Spine01Anchor, GetArrayVariant(m_SideLeftVariants, leftVariant));

		if (chestVariant <= 0)
			CharacterDecorationSpawnUtility.ClearDecoration(ref m_ChestInstance);
		else
			ApplyDecoration(ref m_ChestInstance, m_Spine03Anchor, GetArrayVariant(m_ChestVariants, chestVariant));
	}

	private void ApplyMagazinePouch(MagazineCaliberVisualPreference _preference)
	{
		int variant = ResolveMagazineVariant(_preference);
		CharacterBodyDecorationVariant config = _preference switch
		{
			MagazineCaliberVisualPreference.Five56 when variant > 0 => GetArrayVariant(m_MagM4Variants, variant),
			MagazineCaliberVisualPreference.Ak when variant > 0 => GetArrayVariant(m_MagAkVariants, variant),
			_ => m_MagDefaultVariant
		};

		ApplyDecoration(ref m_MagPouchInstance, m_Spine01Anchor, config);
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
		if (_grenadeCount >= 1)
			ApplyDecoration(ref m_GrenadeRightPouchInstance, m_Spine01Anchor, m_GrenadeRightPouchVariant);
		else
			CharacterDecorationSpawnUtility.ClearDecoration(ref m_GrenadeRightPouchInstance);

		if (_grenadeCount >= 2)
			ApplyDecoration(ref m_GrenadeLeftPouchInstance, m_Spine01Anchor, m_GrenadeLeftPouchVariant);
		else
			CharacterDecorationSpawnUtility.ClearDecoration(ref m_GrenadeLeftPouchInstance);
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

		CharacterDecorationSpawnUtility.ClearDecoration(ref m_AttachedGrenadeInstances[_cellIndex]);
		if (_prefab == null || m_AttachedGrenadeCells == null || _cellIndex >= m_AttachedGrenadeCells.Length)
			return;

		Transform cell = m_AttachedGrenadeCells[_cellIndex];
		if (cell == null)
			return;

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
		Transform _anchor,
		CharacterBodyDecorationVariant _config)
	{
		CharacterDecorationSpawnUtility.ClearDecoration(ref _instance);
		if (_config.Prefab == null || _anchor == null)
			return;

		_instance = CharacterDecorationSpawnUtility.SpawnDecoration(_anchor, _config);
	}

	private void EnsureAttachedInstanceArray()
	{
		int count = m_AttachedGrenadeCells != null ? m_AttachedGrenadeCells.Length : 0;
		if (m_AttachedGrenadeInstances != null && m_AttachedGrenadeInstances.Length == count)
			return;

		m_AttachedGrenadeInstances = new GameObject[count];
	}
	#endregion
}
