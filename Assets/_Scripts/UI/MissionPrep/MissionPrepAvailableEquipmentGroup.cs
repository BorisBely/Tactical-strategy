/// <summary>
/// Подгруппы внутри фильтра панели «доступное снаряжение».
/// Порядок значений = порядок секций в списке.
/// </summary>
public enum MissionPrepAvailableEquipmentGroup : byte
{
	AssaultRifles = 0,
	SniperRifles = 1,
	Shotguns = 2,
	MachineGuns = 3,
	RocketLaunchers = 4,
	TurretWeapons = 5,
	Optics = 6,
	TacticalGrips = 7,
	Muzzle = 8,
	LaserFlashlight = 9,
	Stocks = 10,
	Magazines545 = 11,
	Magazines762x39 = 12,
	Magazines556 = 13,
	Magazines762Long = 14,
	MagazineBoxes = 15,
	AmmoBoxes = 16,
	RpgRockets = 17,
	Helmets = 18,
	Backpacks = 19,
	Grenades = 20,
	Medkits = 21,
	TurretShields = 22,
	Other = 23
}

/// <summary>
/// Классификация предметов каталога по подгруппам для сортировки и подзаголовков.
/// </summary>
public static class MissionPrepAvailableEquipmentGroupClassifier
{
	public const string HeaderObjectNamePrefix = "AvailGroup_";

	public static MissionPrepAvailableEquipmentGroup GetGroup(ItemDefinition _definition)
	{
		if (_definition == null)
			return MissionPrepAvailableEquipmentGroup.Other;

		if (_definition.IsRocketLauncher)
			return MissionPrepAvailableEquipmentGroup.RocketLaunchers;

		if (_definition.IsTurretWeapon)
			return MissionPrepAvailableEquipmentGroup.TurretWeapons;

		if (_definition.IsTurretFrontalShield || _definition.IsTurretSurroundShield)
			return MissionPrepAvailableEquipmentGroup.TurretShields;

		if (_definition.IsGrenade)
			return MissionPrepAvailableEquipmentGroup.Grenades;

		if (_definition.IsMedkit)
			return MissionPrepAvailableEquipmentGroup.Medkits;

		if (_definition.IsEquipment && _definition.EquipmentKind == EquipmentKind.Helmet)
			return MissionPrepAvailableEquipmentGroup.Helmets;

		if (_definition.IsEquipment && _definition.EquipmentKind == EquipmentKind.Backpack)
			return MissionPrepAvailableEquipmentGroup.Backpacks;

		if (_definition.IsRpgRocketAmmo)
			return MissionPrepAvailableEquipmentGroup.RpgRockets;

		WeaponDefinition weapon = _definition.WeaponDefinition;
		if (weapon != null)
			return GetWeaponGroup(weapon.WeaponClass);

		if (_definition.WeaponAttachmentDefinition != null)
			return GetAttachmentGroup(_definition.WeaponAttachmentDefinition);

		if (_definition.MagazineDefinition != null)
			return GetMagazineGroup(_definition.MagazineDefinition);

		if (_definition.AmmoDefinition != null)
			return MissionPrepAvailableEquipmentGroup.AmmoBoxes;

		return MissionPrepAvailableEquipmentGroup.Other;
	}

	public static string GetObjectName(MissionPrepAvailableEquipmentGroup _group)
	{
		return HeaderObjectNamePrefix + _group;
	}

	public static string GetLocalizationKey(MissionPrepAvailableEquipmentGroup _group)
	{
		return _group switch
		{
			MissionPrepAvailableEquipmentGroup.AssaultRifles => "mission_prep.equipment.group.assault_rifles",
			MissionPrepAvailableEquipmentGroup.SniperRifles => "mission_prep.equipment.group.sniper_rifles",
			MissionPrepAvailableEquipmentGroup.Shotguns => "mission_prep.equipment.group.shotguns",
			MissionPrepAvailableEquipmentGroup.MachineGuns => "mission_prep.equipment.group.machine_guns",
			MissionPrepAvailableEquipmentGroup.RocketLaunchers => "mission_prep.equipment.group.rocket_launchers",
			MissionPrepAvailableEquipmentGroup.TurretWeapons => "mission_prep.equipment.group.turret_weapons",
			MissionPrepAvailableEquipmentGroup.Optics => "mission_prep.equipment.group.optics",
			MissionPrepAvailableEquipmentGroup.TacticalGrips => "mission_prep.equipment.group.tactical_grips",
			MissionPrepAvailableEquipmentGroup.Muzzle => "mission_prep.equipment.group.muzzle",
			MissionPrepAvailableEquipmentGroup.LaserFlashlight => "mission_prep.equipment.group.laser_flashlight",
			MissionPrepAvailableEquipmentGroup.Stocks => "mission_prep.equipment.group.stocks",
			MissionPrepAvailableEquipmentGroup.Magazines545 => "mission_prep.equipment.group.mag_545",
			MissionPrepAvailableEquipmentGroup.Magazines762x39 => "mission_prep.equipment.group.mag_762x39",
			MissionPrepAvailableEquipmentGroup.Magazines556 => "mission_prep.equipment.group.mag_556",
			MissionPrepAvailableEquipmentGroup.Magazines762Long => "mission_prep.equipment.group.mag_762_long",
			MissionPrepAvailableEquipmentGroup.MagazineBoxes => "mission_prep.equipment.group.mag_boxes",
			MissionPrepAvailableEquipmentGroup.AmmoBoxes => "mission_prep.equipment.group.ammo_boxes",
			MissionPrepAvailableEquipmentGroup.RpgRockets => "mission_prep.equipment.group.rpg_rockets",
			MissionPrepAvailableEquipmentGroup.Helmets => "mission_prep.equipment.group.helmets",
			MissionPrepAvailableEquipmentGroup.Backpacks => "mission_prep.equipment.group.backpacks",
			MissionPrepAvailableEquipmentGroup.Grenades => "mission_prep.equipment.group.grenades",
			MissionPrepAvailableEquipmentGroup.Medkits => "mission_prep.equipment.group.medkits",
			MissionPrepAvailableEquipmentGroup.TurretShields => "mission_prep.equipment.group.turret_shields",
			_ => "mission_prep.equipment.group.other"
		};
	}

	public static string GetFallback(MissionPrepAvailableEquipmentGroup _group)
	{
		return _group switch
		{
			MissionPrepAvailableEquipmentGroup.AssaultRifles => "Assault rifles",
			MissionPrepAvailableEquipmentGroup.SniperRifles => "Sniper rifles",
			MissionPrepAvailableEquipmentGroup.Shotguns => "Shotguns",
			MissionPrepAvailableEquipmentGroup.MachineGuns => "Machine guns",
			MissionPrepAvailableEquipmentGroup.RocketLaunchers => "Launchers",
			MissionPrepAvailableEquipmentGroup.TurretWeapons => "Turret weapons",
			MissionPrepAvailableEquipmentGroup.Optics => "Optics",
			MissionPrepAvailableEquipmentGroup.TacticalGrips => "Tactical grips",
			MissionPrepAvailableEquipmentGroup.Muzzle => "Muzzle",
			MissionPrepAvailableEquipmentGroup.LaserFlashlight => "Laser / flashlight",
			MissionPrepAvailableEquipmentGroup.Stocks => "Stocks",
			MissionPrepAvailableEquipmentGroup.Magazines545 => "Magazines 5.45",
			MissionPrepAvailableEquipmentGroup.Magazines762x39 => "Magazines 7.62x39",
			MissionPrepAvailableEquipmentGroup.Magazines556 => "Magazines 5.56",
			MissionPrepAvailableEquipmentGroup.Magazines762Long => "Magazines 7.62x51 / 7.62x54R",
			MissionPrepAvailableEquipmentGroup.MagazineBoxes => "Belts / boxes",
			MissionPrepAvailableEquipmentGroup.AmmoBoxes => "Ammunition",
			MissionPrepAvailableEquipmentGroup.RpgRockets => "Rockets",
			MissionPrepAvailableEquipmentGroup.Helmets => "Helmets",
			MissionPrepAvailableEquipmentGroup.Backpacks => "Backpacks",
			MissionPrepAvailableEquipmentGroup.Grenades => "Grenades",
			MissionPrepAvailableEquipmentGroup.Medkits => "Medical",
			MissionPrepAvailableEquipmentGroup.TurretShields => "Turret shields",
			_ => "Other"
		};
	}

	public static int CompareSlots(InventorySlotRuntimeData _a, InventorySlotRuntimeData _b)
	{
		int categoryA = (int)MissionPrepAvailableEquipmentFilterClassifier.GetCategory(_a.Definition);
		int categoryB = (int)MissionPrepAvailableEquipmentFilterClassifier.GetCategory(_b.Definition);
		if (categoryA != categoryB)
			return categoryA.CompareTo(categoryB);

		int groupA = (int)GetGroup(_a.Definition);
		int groupB = (int)GetGroup(_b.Definition);
		if (groupA != groupB)
			return groupA.CompareTo(groupB);

		string nameA = _a.Definition != null ? _a.Definition.name : string.Empty;
		string nameB = _b.Definition != null ? _b.Definition.name : string.Empty;
		return string.Compare(nameA, nameB, System.StringComparison.Ordinal);
	}

	private static MissionPrepAvailableEquipmentGroup GetWeaponGroup(WeaponClassType _weaponClass)
	{
		return _weaponClass switch
		{
			WeaponClassType.SniperRifle => MissionPrepAvailableEquipmentGroup.SniperRifles,
			WeaponClassType.Shotgun => MissionPrepAvailableEquipmentGroup.Shotguns,
			WeaponClassType.LightMachineGun => MissionPrepAvailableEquipmentGroup.MachineGuns,
			WeaponClassType.HeavyMachineGun => MissionPrepAvailableEquipmentGroup.MachineGuns,
			WeaponClassType.AutomaticGrenadeLauncher => MissionPrepAvailableEquipmentGroup.TurretWeapons,
			_ => MissionPrepAvailableEquipmentGroup.AssaultRifles
		};
	}

	private static MissionPrepAvailableEquipmentGroup GetAttachmentGroup(WeaponAttachmentDefinition _attachment)
	{
		if (_attachment == null)
			return MissionPrepAvailableEquipmentGroup.Other;

		if (_attachment.AttachmentType == WeaponAttachmentType.Stock ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.Stock)
			return MissionPrepAvailableEquipmentGroup.Stocks;

		if (_attachment.AttachmentType == WeaponAttachmentType.Foregrip ||
		    _attachment.AttachmentType == WeaponAttachmentType.Bipod ||
		    _attachment.AttachmentType == WeaponAttachmentType.UnderBarrelGrenadeLauncher ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.UnderBarrel)
			return MissionPrepAvailableEquipmentGroup.TacticalGrips;

		if (_attachment.AttachmentType == WeaponAttachmentType.Suppressor ||
		    _attachment.AttachmentType == WeaponAttachmentType.Compensator ||
		    _attachment.AttachmentType == WeaponAttachmentType.FlashHider ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.Muzzle)
			return MissionPrepAvailableEquipmentGroup.Muzzle;

		if (_attachment.AttachmentType == WeaponAttachmentType.Optic ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.Optic ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.SideRail)
			return MissionPrepAvailableEquipmentGroup.Optics;

		if (_attachment.AttachmentType == WeaponAttachmentType.Flashlight ||
		    _attachment.AttachmentType == WeaponAttachmentType.LaserDesignator ||
		    _attachment.AttachmentType == WeaponAttachmentType.RailCover ||
		    _attachment.RequiredSlot == WeaponAttachmentSlotType.Rail)
			return MissionPrepAvailableEquipmentGroup.LaserFlashlight;

		return MissionPrepAvailableEquipmentGroup.Other;
	}

	private static MissionPrepAvailableEquipmentGroup GetMagazineGroup(MagazineDefinition _magazine)
	{
		if (_magazine == null)
			return MissionPrepAvailableEquipmentGroup.Other;

		if (IsBoxMagazine(_magazine.MagazineType))
			return MissionPrepAvailableEquipmentGroup.MagazineBoxes;

		return _magazine.SupportedCaliber switch
		{
			CaliberType.Five45By39 => MissionPrepAvailableEquipmentGroup.Magazines545,
			CaliberType.Seven62By39 => MissionPrepAvailableEquipmentGroup.Magazines762x39,
			CaliberType.Five56By45 => MissionPrepAvailableEquipmentGroup.Magazines556,
			CaliberType.Seven62By51 => MissionPrepAvailableEquipmentGroup.Magazines762Long,
			CaliberType.Seven62By54R => MissionPrepAvailableEquipmentGroup.Magazines762Long,
			_ => MissionPrepAvailableEquipmentGroup.Other
		};
	}

	private static bool IsBoxMagazine(MagazineType _type)
	{
		return _type == MagazineType.M249Box ||
		       _type == MagazineType.PkmBox ||
		       _type == MagazineType.M2Box ||
		       _type == MagazineType.Mk19Box;
	}
}
