using UnityEngine;

/// <summary>
/// Данные модуля оружия. Пока это только data-слой с модификаторами, без runtime-логики установки.
/// </summary>
[CreateAssetMenu(fileName = "WeaponAttachmentDefinition", menuName = "Polygone/Shooting/Weapon Attachment Definition", order = 13)]
public sealed class WeaponAttachmentDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Тип модуля (соответствует одному из семейств слотов: Muzzle / UnderBarrel / Rail / Optic).")]
	[SerializeField] private WeaponAttachmentType m_AttachmentType = WeaponAttachmentType.Optic;
	[Tooltip("Основной слот на оружии. Используется как fallback, если Compatible Slots пуст.")]
	[SerializeField] private WeaponAttachmentSlotType m_RequiredSlot = WeaponAttachmentSlotType.Optic;
	[Tooltip("Если включено, модуль ставится только на оружие из Compatible Weapons. Если выключено и список пустой — старые assets считаются совместимыми со всем.")]
	[SerializeField] private bool m_UseExplicitWeaponCompatibility;
	[Tooltip("Оружие, с которым совместим модуль. Пустой список = совместим со всеми оружиями старого контента.")]
	[SerializeField] private WeaponDefinition[] m_CompatibleWeapons;
	[Tooltip("Слоты, куда можно ставить модуль. Пустой список = только Required Slot.")]
	[SerializeField] private WeaponAttachmentSlotType[] m_CompatibleSlots;
	[Tooltip("Для Rail-модулей: допустимые физические RailSocket индексы 0..2. Пустой список = любой RailSocket.")]
	[SerializeField] private int[] m_CompatibleRailSocketIndices;

	[Header("Modifiers")]
	[Tooltip("Как модуль меняет скорость прицеливания. Значение меньше 1 ускоряет, больше 1 замедляет.")]
	[SerializeField, Min(0f)] private float m_AimTimeModifier = 1f;
	[Tooltip("Как модуль меняет эффективную дальность оружия.")]
	[SerializeField, Min(0f)] private float m_EffectiveRangeModifier = 1f;
	[Tooltip("Как модуль меняет накопление отдачи.")]
	[SerializeField, Min(0f)] private float m_RecoilModifier = 1f;
	[Tooltip("Как модуль меняет скорость смены магазина в оружии.")]
	[SerializeField, Min(0f)] private float m_ReloadTimeModifier = 1f;
	[Tooltip("Как модуль меняет точность и скорость прицеливания на дистанции 0..100 м.")]
	[SerializeField] private WeaponDistanceAimProfile m_DistanceAimProfile = new WeaponDistanceAimProfile();

	[Header("Weapon condition (за выстрел)")]
	[Tooltip("Множитель накопления износа от патрона за выстрел.")]
	[SerializeField, Min(0f)] private float m_WearPerShotMultiplier = 1f;
	[Tooltip("Множитель накопления загрязнения от патрона за выстрел.")]
	[SerializeField, Min(0f)] private float m_FoulingPerShotMultiplier = 1f;
	[Tooltip("Множитель вероятности клина с каждого выстрела (оба канала).")]
	[SerializeField, Min(0f)] private float m_JamRiskModifier = 1f;

	[Header("Audio")]
	[Tooltip("Звук выстрела с установленным глушителем (AttachmentType = Suppressor). Пусто — при экипированном глушителе используется звук оружия.")]
	[SerializeField] private AudioClip m_SuppressedFireSound;

	[Header("Визуал на оружии")]
	[Tooltip("Меш модуля в руках: родитель — сокет на EquippedWeapon (дуло / прицел / планка и т.д.), не Barrel и не Sight Pivot.")]
	[SerializeField] private GameObject m_EquippedVisualPrefab;
	#endregion

	#region Public Properties
	public WeaponAttachmentType AttachmentType => m_AttachmentType;
	public WeaponAttachmentSlotType RequiredSlot => m_RequiredSlot;
	public bool UseExplicitWeaponCompatibility => m_UseExplicitWeaponCompatibility;
	public WeaponDefinition[] CompatibleWeapons => m_CompatibleWeapons;
	public WeaponAttachmentSlotType[] CompatibleSlots => m_CompatibleSlots;
	public int[] CompatibleRailSocketIndices => m_CompatibleRailSocketIndices;
	public float AimTimeModifier => m_AimTimeModifier;
	public float EffectiveRangeModifier => m_EffectiveRangeModifier;
	public float RecoilModifier => m_RecoilModifier;
	public float ReloadTimeModifier => m_ReloadTimeModifier;
	public WeaponDistanceAimProfile DistanceAimProfile => m_DistanceAimProfile;
	public float WearPerShotMultiplier => m_WearPerShotMultiplier;
	public float FoulingPerShotMultiplier => m_FoulingPerShotMultiplier;
	public float JamRiskModifier => m_JamRiskModifier;
	public AudioClip SuppressedFireSound => m_SuppressedFireSound;
	public GameObject EquippedVisualPrefab => m_EquippedVisualPrefab;
	#endregion

	#region Public Methods
	public bool SupportsWeapon(WeaponDefinition _weaponDefinition)
	{
		if (_weaponDefinition == null)
			return false;
		if (!m_UseExplicitWeaponCompatibility && (m_CompatibleWeapons == null || m_CompatibleWeapons.Length == 0))
			return true;
		if (m_CompatibleWeapons == null || m_CompatibleWeapons.Length == 0)
			return false;

		for (int i = 0; i < m_CompatibleWeapons.Length; i++)
		{
			if (m_CompatibleWeapons[i] == _weaponDefinition)
				return true;
		}

		return false;
	}

	public bool SupportsSlot(WeaponAttachmentSlotType _slotType)
	{
		if (m_CompatibleSlots == null || m_CompatibleSlots.Length == 0)
			return m_RequiredSlot == _slotType;

		for (int i = 0; i < m_CompatibleSlots.Length; i++)
		{
			if (m_CompatibleSlots[i] == _slotType)
				return true;
		}

		return false;
	}

	public bool SupportsWeaponSlot(WeaponAttachmentSlotType _slotType, int _railSocketIndex)
	{
		if (!SupportsSlot(_slotType))
			return false;
		if (_slotType != WeaponAttachmentSlotType.Rail)
			return true;
		if (m_CompatibleRailSocketIndices == null || m_CompatibleRailSocketIndices.Length == 0)
			return true;

		for (int i = 0; i < m_CompatibleRailSocketIndices.Length; i++)
		{
			if (m_CompatibleRailSocketIndices[i] == _railSocketIndex)
				return true;
		}

		return false;
	}

	public float GetDistanceDispersionMultiplier(float _distanceMeters)
	{
		return m_DistanceAimProfile != null
			? m_DistanceAimProfile.GetDispersionMultiplier(_distanceMeters)
			: 1f;
	}

	public float GetDistanceAimTimeMultiplier(float _distanceMeters)
	{
		return m_DistanceAimProfile != null
			? m_DistanceAimProfile.GetAimTimeMultiplier(_distanceMeters)
			: 1f;
	}
	#endregion
}
