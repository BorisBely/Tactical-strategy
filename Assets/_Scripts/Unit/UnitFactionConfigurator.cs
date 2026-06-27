using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Настройка универсального префаба юнита под команду: инвентарь, включение подсистем, RTS/AI.
/// Вызывается спавнером сразу после Instantiate или из Awake по сериализованному конфигу.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class UnitFactionConfigurator : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private UnitSpawnConfig m_RuntimeConfig = new UnitSpawnConfig();
	#endregion

	#region Private Fields
	private UnitTeam m_Team;
	private CharacterInventory m_Inventory;
	private UnitEquipment m_Equipment;
	private UnitHeadEquipment m_HeadEquipment;
	private UnitBackEquipment m_BackEquipment;
	private UnitIndividualTraits m_Traits;
	private UnitCharacterAppearance m_Appearance;
	private UnitWeaponReadyHandsLayer m_ReadyHands;
	private RtsUnitMember m_RtsMember;
	private UnitClickToMove m_ClickToMove;
	private UnitNavLocomotionDriver m_LocomotionDriver;
	private InventoryPickupZone m_PickupZone;
	private UnitAnimatorStance m_Stance;
	private UnitVision m_Vision;
	private DamageableTarget m_DamageableTarget;
	private UnitWeaponRuntime m_WeaponRuntime;
	private UnitHealth m_UnitHealth;
	private UnitSelfStabilizationController m_SelfStabilizationController;
	private UnitStabilizeOtherController m_StabilizeOtherController;
	private UnitFiremanCarryController m_FiremanCarryController;
	private UnitHealthDeteriorationController m_HealthDeteriorationController;
	private UnitBodyMeshSelector m_BodyMeshSelector;
	private UnitCharacterHeadAppearance m_HeadAppearance;
	private UnitCharacterBodyDecorations m_BodyDecorations;
	private UnitInventoryBodyDecorations m_InventoryDecorations;
	#endregion

	#region Public Methods
	public void Configure(UnitSpawnConfig _config)
	{
		m_RuntimeConfig = _config ?? new UnitSpawnConfig();
	}

	public void ApplyConfiguration()
	{
		CacheComponents();
		EnsureHealthRuntimeControllers();

		UnitTeamId team = m_RuntimeConfig.Team;
		bool isPlayer = team == UnitTeamId.Player;

		if (m_Team != null)
			m_Team.SetTeam(team);

		ApplyFactionComponentStates();
		ApplyCharacterGender();
		ApplyCharacterSkinTone();
		EnsureIndividualTraits();
		ApplyBodyMesh();
		ApplyLoadout();
		ApplyArmor();
		if (m_RuntimeConfig.BodyMeshArchetype != UnitBodyMeshArchetype.Soldier)
			ApplyCamouflage(ResolveCamouflageIndex());
		ApplyFactionVisualRefreshes();
		ApplyRoleComponents(isPlayer);

		if (m_ReadyHands != null)
			m_ReadyHands.SetReadyWanted(m_RuntimeConfig.StartReady, false);

		if (!string.IsNullOrWhiteSpace(m_RuntimeConfig.DisplayName))
			UnitRosterDisplayState.GetOrCreate(gameObject)?.SetCallsign(m_RuntimeConfig.DisplayName);

		RefreshVisionRegistry();
	}

	/// <summary>Готовый конфиг для игрока (RTS, без прямого ввода).</summary>
	public static UnitSpawnConfig CreatePlayerConfig(UnitSpawnLoadout _loadout, bool _startReady = false, string _displayName = null)
	{
		return new UnitSpawnConfig(UnitTeamId.Player, _loadout, _startReady, _displayName);
	}

	/// <summary>Готовый конфиг для врага.</summary>
	public static UnitSpawnConfig CreateEnemyConfig(UnitSpawnLoadout _loadout, bool _startReady = false, string _displayName = null)
	{
		return new UnitSpawnConfig(UnitTeamId.Enemy, _loadout, _startReady, _displayName);
	}
	#endregion

	#region Private Methods
	private void CacheComponents()
	{
		if (m_Team == null)
			m_Team = GetComponent<UnitTeam>();
		if (m_Inventory == null)
			m_Inventory = GetComponent<CharacterInventory>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_HeadEquipment == null)
			m_HeadEquipment = GetComponentInChildren<UnitHeadEquipment>(true);
		if (m_BackEquipment == null)
			m_BackEquipment = GetComponentInChildren<UnitBackEquipment>(true);
		if (m_Traits == null)
			m_Traits = GetComponentInChildren<UnitIndividualTraits>(true);
		if (m_Appearance == null)
			m_Appearance = GetComponentInChildren<UnitCharacterAppearance>(true);
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_PickupZone == null)
			m_PickupZone = GetComponentInChildren<InventoryPickupZone>(true);
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_DamageableTarget == null)
			m_DamageableTarget = GetComponent<DamageableTarget>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_UnitHealth == null)
			m_UnitHealth = GetComponent<UnitHealth>();
		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = GetComponent<UnitSelfStabilizationController>();
		if (m_StabilizeOtherController == null)
			m_StabilizeOtherController = GetComponent<UnitStabilizeOtherController>();
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = GetComponent<UnitFiremanCarryController>();
		if (m_HealthDeteriorationController == null)
			m_HealthDeteriorationController = GetComponent<UnitHealthDeteriorationController>();
		if (m_BodyMeshSelector == null)
			m_BodyMeshSelector = GetComponent<UnitBodyMeshSelector>();
		if (m_HeadAppearance == null)
			m_HeadAppearance = GetComponentInChildren<UnitCharacterHeadAppearance>(true);
		if (m_BodyDecorations == null)
			m_BodyDecorations = GetComponentInChildren<UnitCharacterBodyDecorations>(true);
		if (m_InventoryDecorations == null)
			m_InventoryDecorations = GetComponentInChildren<UnitInventoryBodyDecorations>(true);
	}

	private void EnsureHealthRuntimeControllers()
	{
		if (m_UnitHealth == null)
			return;

		if (m_SelfStabilizationController == null)
			m_SelfStabilizationController = gameObject.AddComponent<UnitSelfStabilizationController>();
		if (m_StabilizeOtherController == null)
			m_StabilizeOtherController = gameObject.AddComponent<UnitStabilizeOtherController>();
		if (m_FiremanCarryController == null)
			m_FiremanCarryController = gameObject.AddComponent<UnitFiremanCarryController>();
		if (m_HealthDeteriorationController == null)
			m_HealthDeteriorationController = gameObject.AddComponent<UnitHealthDeteriorationController>();
	}

	private void ApplyCharacterGender()
	{
		UnitCharacterAppearance appearance = UnitCharacterAppearance.GetOrCreate(gameObject);
		if (appearance == null)
			return;

		appearance.RollInitialGender(m_RuntimeConfig.FemaleSpawnChance);
		m_Appearance = appearance;
	}

	private void EnsureIndividualTraits()
	{
		UnitIndividualTraits traits = UnitIndividualTraits.GetOrCreate(gameObject);
		if (traits != null && !traits.IsInitialized)
			traits.RollRandomTraits();

		if (traits != null && m_RuntimeConfig.BodyMeshArchetype == UnitBodyMeshArchetype.Soldier)
			traits.RollHeadAppearance(ResolveRankPreset(), m_Appearance != null ? m_Appearance.Gender : CharacterGender.Male);

		m_Traits = traits;
	}

	private UnitCombatRankDefinition ResolveRankPreset()
	{
		UnitCombatStats stats = GetComponent<UnitCombatStats>();
		return stats != null ? stats.RankPreset : null;
	}

	private void RefreshHeadAppearance()
	{
		UnitCharacterHeadAppearance headAppearance = GetComponentInChildren<UnitCharacterHeadAppearance>(true);
		if (headAppearance != null)
			headAppearance.RefreshFromTraits(m_Traits, m_Appearance);
	}

	private int ResolveCamouflageIndex()
	{
		int index = m_RuntimeConfig.CamouflageVisualIndex;
		if (index < 0)
			index = Random.Range(0, 4);
		return index;
	}

	private void ApplyCharacterSkinTone()
	{
		UnitCharacterMaterialAppearance materialAppearance = UnitCharacterMaterialAppearance.GetOrCreate(gameObject);
		if (materialAppearance != null)
			materialAppearance.RollInitialSkinTone();
	}

	private void ApplyCamouflage(int _camouflageVisualIndex)
	{
		UnitCharacterMaterialAppearance materialAppearance = UnitCharacterMaterialAppearance.GetOrCreate(gameObject);
		if (materialAppearance == null)
			return;

		int clamped = UnitCamouflagePatternUtility.ClampIndex(_camouflageVisualIndex);
		materialAppearance.SetCamouflageIndex(clamped);
	}

	private void ApplyBodyMesh()
	{
		if (m_BodyMeshSelector == null)
			m_BodyMeshSelector = gameObject.AddComponent<UnitBodyMeshSelector>();

		CharacterGender gender = m_Appearance != null ? m_Appearance.Gender : CharacterGender.Male;
		m_BodyMeshSelector.SelectMesh(m_RuntimeConfig.BodyMeshArchetype, gender);
	}

	private void ApplyArmor()
	{
		UnitBodyMeshArchetype archetype = m_RuntimeConfig.BodyMeshArchetype;
		int armorIndex = m_RuntimeConfig.ArmorVisualIndex;

		if (archetype != UnitBodyMeshArchetype.Soldier)
		{
			UnitArmorType defaultType = UnitBodyMeshSelector.GetDefaultArmorType(archetype);
			if (defaultType == UnitArmorType.None)
			{
				if (TryGetComponent(out UnitArmor armor))
					armor.ClearArmor();
				return;
			}

			armorIndex = defaultType == UnitArmorType.Light
				? MissionPrepUnitArmorVisualController.LightArmorIndex
				: MissionPrepUnitArmorVisualController.HeavyArmorIndex;

			UnitArmor unitArmor = GetComponent<UnitArmor>() ?? gameObject.AddComponent<UnitArmor>();
			unitArmor.SetArmorFromPresetIndex(armorIndex);
			return;
		}

		if (armorIndex < 0)
		{
			if (TryGetComponent(out UnitArmor armor))
				armor.ClearArmor();
			return;
		}

		int clamped = Mathf.Clamp(
			armorIndex,
			MissionPrepUnitArmorVisualController.LightArmorIndex,
			MissionPrepUnitArmorVisualController.HeavyArmorIndex);

		MissionPrepUnitArmorVisualController.GetOrCreate(gameObject, clamped).ApplyArmorVisual(clamped);
		UnitArmor soldierArmor = GetComponent<UnitArmor>() ?? gameObject.AddComponent<UnitArmor>();
		soldierArmor.SetArmorFromPresetIndex(clamped);
	}

	/// <summary>Рантайм-смена команды. Визуал не меняется, переключаются только RTS/AI-компоненты.</summary>
	public void ChangeTeam(UnitTeamId _newTeam)
	{
		m_RuntimeConfig.SetTeam(_newTeam);
		bool isPlayer = _newTeam == UnitTeamId.Player;
		ApplyRoleComponents(isPlayer);
		RefreshVisionRegistry();
	}

	private void ApplyFactionComponentStates()
	{
		bool isSoldier = m_RuntimeConfig.BodyMeshArchetype == UnitBodyMeshArchetype.Soldier;

		if (isSoldier)
		{
			SetBehaviourEnabled(m_HeadAppearance, true);
			SetBehaviourEnabled(m_BodyDecorations, true);
			SetBehaviourEnabled(m_InventoryDecorations, true);
			if (m_InventoryDecorations != null)
				m_InventoryDecorations.OnlyGrenades = false;
		}
		else
		{
			SetBehaviourEnabled(m_HeadAppearance, false);
			SetBehaviourEnabled(m_BodyDecorations, false);
			SetBehaviourEnabled(m_InventoryDecorations, true);
			if (m_InventoryDecorations != null)
				m_InventoryDecorations.OnlyGrenades = true;
		}
	}

	private void ApplyFactionVisualRefreshes()
	{
		if (m_RuntimeConfig.BodyMeshArchetype == UnitBodyMeshArchetype.Soldier)
			RefreshHeadAppearance();
	}

	private void ApplyLoadout()
	{
		if (m_Inventory == null)
			return;

		m_Inventory.Clear();

		UnitSpawnLoadout loadout = m_RuntimeConfig.Loadout;
		if (loadout == null)
			return;

		var bagSlots = new List<InventorySlotRuntimeData>();
		ItemDefinition[] bagItems = loadout.BagItems;
		for (int i = 0; i < bagItems.Length; i++)
		{
			ItemDefinition item = bagItems[i];
			if (item == null)
				continue;

			if (InventoryLoadedMagazineUtility.IsMagazineDefinition(item) &&
			    InventoryLoadedMagazineUtility.TryBuildLoadedMagazineSlot(
				    item,
				    loadout.AmmoForMagazines,
				    loadout.RoundsPerMagazine,
				    out InventorySlotRuntimeData loadedMagazine))
				bagSlots.Add(loadedMagazine);
			else
				bagSlots.Add(InventorySlotRuntimeData.FromDefinition(item));
		}

		InventorySlotRuntimeData mainHand = default;
		if (loadout.MainHandWeapon != null)
		{
			mainHand = InventorySlotRuntimeData.FromDefinition(loadout.MainHandWeapon);
			TryInsertLoadedMagazineIntoWeapon(mainHand, bagSlots, loadout.LoadMagazineIntoWeapon);
		}

		if (!mainHand.IsEmpty && m_Equipment != null)
			m_Inventory.RestoreAfterFailedDrop(true, mainHand);

		if (loadout.HeadItem != null)
		{
			InventorySlotRuntimeData headSlot = InventorySlotRuntimeData.FromDefinition(loadout.HeadItem);
			if (HelmetEquipUtility.CanEquipToHead(headSlot))
				m_Inventory.RestoreAfterFailedDrop(false, true, headSlot);
		}

		if (loadout.BackItem != null)
		{
			InventorySlotRuntimeData backSlot = InventorySlotRuntimeData.FromDefinition(loadout.BackItem);
			if (BackpackEquipUtility.CanEquipToBack(backSlot))
				m_Inventory.RestoreAfterFailedDrop(false, false, true, backSlot);
		}

		for (int i = 0; i < bagSlots.Count; i++)
			m_Inventory.TryAdd(bagSlots[i]);

		if (m_WeaponRuntime != null)
			m_WeaponRuntime.RefreshFromEquipment();
	}

	private static void TryInsertLoadedMagazineIntoWeapon(
		InventorySlotRuntimeData _mainHand,
		List<InventorySlotRuntimeData> _bagSlots,
		bool _loadMagazineIntoWeapon)
	{
		if (!_loadMagazineIntoWeapon || _bagSlots == null || _bagSlots.Count == 0)
			return;

		WeaponRuntimeState weaponState = _mainHand.InstanceState?.WeaponState;
		if (weaponState == null)
			return;

		for (int i = 0; i < _bagSlots.Count; i++)
		{
			InventorySlotRuntimeData candidate = _bagSlots[i];
			MagazineRuntimeState magazineState = candidate.InstanceState?.MagazineState;
			if (magazineState == null || magazineState.CurrentAmmoCount <= 0)
				continue;

			if (!weaponState.TryInsertMagazine(candidate))
				continue;

			weaponState.TryChamberRoundFromMagazine();
			_bagSlots.RemoveAt(i);
			return;
		}
	}

	private void ApplyRoleComponents(bool _isPlayer)
	{
		SetBehaviourEnabled(m_RtsMember, _isPlayer);
		SetBehaviourEnabled(m_ClickToMove, _isPlayer);
		SetBehaviourEnabled(m_LocomotionDriver, !_isPlayer);
		SetBehaviourEnabled(m_PickupZone, _isPlayer);
		SetBehaviourEnabled(m_Stance, true);
		SetBehaviourEnabled(m_ReadyHands, true);
		SetBehaviourEnabled(m_Vision, true);
		SetBehaviourEnabled(m_DamageableTarget, true);

		if (m_Stance != null)
			m_Stance.SetKeyboardInputEnabled(false);

		if (m_ReadyHands != null)
			m_ReadyHands.SetKeyboardInputEnabled(false);

		if (_isPlayer && m_RtsMember != null && m_RtsMember.isActiveAndEnabled)
			m_RtsMember.ApplyDirectInputState(false);
	}

	private static void SetBehaviourEnabled(Behaviour _behaviour, bool _enabled)
	{
		if (_behaviour == null)
			return;

		_behaviour.enabled = _enabled;
	}

	private void RefreshVisionRegistry()
	{
		if (m_Vision == null)
			return;

		bool wasEnabled = m_Vision.enabled;
		if (wasEnabled)
			m_Vision.enabled = false;

		if (wasEnabled)
			m_Vision.enabled = true;
	}
	#endregion
}
