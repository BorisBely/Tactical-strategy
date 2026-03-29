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
	/// <summary>Основное оружие (единственный слот на данный момент).</summary>
	MainHand = 1
}

/// <summary>
/// Соответствует int-параметру <c>WeaponMode</c> на Animator (см. <c>NavMeshLocomotion</c> и граф локомоции).
/// </summary>
public enum LocomotionWeaponMode
{
	Unarmed = 0,
	Rifle = 1,
	/// <summary>В Animator сейчас та же ветка, что и <see cref="Rifle"/> (WeaponMode &gt; 0).</summary>
	RifleCrouchProne = 2,
	/// <summary>Временно: в NavMeshLocomotion используется винтовочная локомоция до отдельного графа пистолета.</summary>
	Pistol = 3
}
