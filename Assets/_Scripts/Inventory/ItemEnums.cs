/// <summary>
/// Категория предмета — от неё зависит, можно ли экипировать и как отображать в инвентаре.
/// </summary>
public enum ItemCategory
{
	/// <summary>Расходники, квестовое, ресурсы — только в инвентаре, без модели на теле.</summary>
	General = 0,
	/// <summary>Сейчас: только основное оружие на модели. Другие типы снаряжения добавим отдельно, когда будут готовы.</summary>
	Equipment = 1
}

/// <summary>
/// Слот под снаряжение. В коде только то, что уже используется; новые значения — вместе с новой механикой.
/// </summary>
public enum EquipmentSlotType
{
	None = 0,
	/// <summary>Основное оружие.</summary>
	MainHand = 1,
	/// <summary>Шлем / голова.</summary>
	Head = 2,
	/// <summary>Рюкзак / спина.</summary>
	Back = 3
}

/// <summary>
/// Тип оружия для геймплея/экипировки (прототип).
/// </summary>
public enum WeaponType
{
	/// <summary>Основное оружие (винтовка).</summary>
	Primary = 0,
	/// <summary>Второстепенное оружие (пистолет).</summary>
	Secondary = 1
}

/// <summary>
/// Подтип снаряжения (Category = Equipment), чтобы отличать оружие от прочего экипа (например аптечки).
/// </summary>
public enum EquipmentKind
{
	Weapon = 0,
	Other = 1,
	Helmet = 2,
	Backpack = 3
}

/// <summary>
/// Тип гранаты для сортировки и выбора визуала на теле юнита.
/// </summary>
public enum GrenadeType
{
	Unknown = 0,
	Fragmentation = 1,
	Flash = 2,
	Smoke = 3
}

/// <summary>
/// Соответствует int-параметру <c>WeaponMode</c> на Animator (см. <c>NavMeshLocomotion</c> и граф локомоции).
/// </summary>
public enum LocomotionWeaponMode
{
	Unarmed = 0,
	Rifle = 1,
	/// <summary>Временно: в NavMeshLocomotion используется винтовочная локомоция до отдельного графа пистолета.</summary>
	Pistol = 3
}
