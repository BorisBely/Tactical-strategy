using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Статические данные предмета. Один asset на тип предмета.
/// В мире тот же предмет может выглядеть по-разному: задайте отдельные префабы лута (меш + коллайдер),
/// ссылаясь на один и тот же ItemDefinition.
/// </summary>
[CreateAssetMenu(fileName = "ItemDefinition", menuName = "Polygone/Inventory/Item Definition", order = 0)]
public class ItemDefinition : ScriptableObject
{
	#region Serialized Fields
	[Tooltip("Ключ локализации имени предмета.")]
	[SerializeField] private string m_LocalizationKey;
	[TextArea(2, 6)]
	[Tooltip("Описание предмета для UI и справки.")]
	[SerializeField] private string m_Description;
	[Tooltip("Иконка предмета в UI.")]
	[SerializeField] private Sprite m_Icon;

	[Header("Economy")]
	[Tooltip("Базовая цена предмета без учёта скидок, состояния и рыночных модификаторов.")]
	[SerializeField, Min(0)] private int m_BasePrice = 100;
	[Tooltip("Вес предмета в килограммах.")]
	[SerializeField, Min(0f)] private float m_WeightKg;

	[Header("Тип")]
	[Tooltip("Категория предмета: обычный предмет или экипируемое снаряжение.")]
	[SerializeField] private ItemCategory m_Category = ItemCategory.General;

	[Header("Выброс из инвентаря на землю")]
	[Tooltip("Спавн перед юнитом при переносе слота на панель «земля». Нужны Collider и при необходимости Rigidbody на префабе.")]
	[SerializeField] private GameObject m_DropWorldPrefab;

	[Header("Снаряжение (Category = Equipment)")]
	[Tooltip("Префаб модели в правой руке (без физики лута). Родитель — якорь правой руки в UnitEquipment.")]
	[SerializeField] private GameObject m_EquippedVisualPrefab;
	[Tooltip("Source of truth for Standing/Crouch/Vehicle × Ready/NotReady weapon local poses. When set, preferred over flat Hand_R fields.")]
	[SerializeField] private WeaponPoseDefinition m_WeaponPoseDefinition;
	[Tooltip("LEGACY fallback Hand_R NotReady position when WeaponPoseDefinition is null. Prefer WeaponPoseDefinition.")]
	[SerializeField] private Vector3 m_RightHandLocalPosition;
	[Tooltip("LEGACY fallback. Prefer WeaponPoseDefinition.")]
	[SerializeField] private Vector3 m_RightHandLocalEulerAngles;
	[Tooltip("LEGACY fallback Hand_R Ready position when WeaponPoseDefinition is null.")]
	[SerializeField] private Vector3 m_RightHandReadyLocalPosition;
	[Tooltip("LEGACY fallback. Prefer WeaponPoseDefinition.")]
	[SerializeField] private Vector3 m_RightHandReadyLocalEulerAngles;

	[Header("Crouch — weapon pose (Hand_R local) — LEGACY if PoseDefinition set")]
	[Tooltip("Local position of the weapon in crouch (low ready). Zeros — copy from standing.")]
	[SerializeField] private Vector3 m_CrouchRightHandLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchRightHandReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandReadyLocalEulerAngles;

	[Header("Vehicle — weapon pose (Hand_R local)")]
	[Tooltip("Local position of the weapon in vehicle (not ready / relax). Zeros — copy from standing.")]
	[SerializeField] private Vector3 m_VehicleRightHandLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleRightHandReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandReadyLocalEulerAngles;

	[Header("Снаряжение (Equipment)")]
	[Tooltip("Подтип снаряжения: оружие, шлем или другой экипируемый предмет.")]
	[SerializeField] private EquipmentKind m_EquipmentKind = EquipmentKind.Weapon;
	[Tooltip("Профиль визуальных вариантов декора (шлем и т.д.).")]
	[SerializeField] private EquipmentVisualProfileDefinition m_VisualProfile;

	[Header("Шлем (Equipment, Kind = Helmet)")]
	[Tooltip("Шанс поглощения пули при попадании в голову (0–1). Без HP у шлема.")]
	[SerializeField, Range(0f, 1f)] private float m_HeadBulletBlockChance;

	[Header("Рюкзак (Equipment, Kind = Backpack)")]
	[Tooltip("Заготовка под будущую вместимость рюкзака. Пока не влияет на размер сумки.")]
	[SerializeField, Min(0)] private int m_BackpackCapacity;

	[Header("Оружие (Equipment, Kind = Weapon)")]
	[Tooltip("Тип оружия: основное (винтовка) или второстепенное (пистолет).")]
	[SerializeField] private WeaponType m_WeaponType = WeaponType.Primary;

	[Header("Турель машины (Equipment, Kind = Turret*)")]
	[Tooltip("Вариант визуала орудия на Light Armored Car. None — не орудие турели.")]
	[SerializeField] private TurretWeaponVariant m_TurretWeaponVariant = TurretWeaponVariant.None;

	[Header("Shooting Data")]
	[Tooltip("Ссылка на базовые данные оружейной платформы для этого предмета.")]
	[SerializeField] private WeaponDefinition m_WeaponDefinition;
	[Tooltip("Ссылка на данные патрона, если этот предмет является патроном.")]
	[SerializeField] private AmmoDefinition m_AmmoDefinition;
	[Tooltip("Сколько патронов этого типа находится в одном экземпляре коробки/пачки по умолчанию.")]
	[SerializeField, Min(0)] private int m_InitialAmmoCount;
	[Tooltip("Ссылка на данные магазина, если этот предмет является магазином.")]
	[SerializeField] private MagazineDefinition m_MagazineDefinition;
	[Tooltip("Ссылка на данные модуля оружия, если этот предмет является аксессуаром.")]
	[SerializeField] private WeaponAttachmentDefinition m_WeaponAttachmentDefinition;

	[Header("Grenade (Category = General)")]
	[Tooltip("Тип гранаты для порядка отображения и выбора прикреплённого визуала.")]
	[SerializeField] private GrenadeType m_GrenadeType = GrenadeType.Unknown;
	[Tooltip("Префаб гранаты, который крепится к ячейкам на теле юнита.")]
	[SerializeField] private GameObject m_AttachedBodyVisualPrefab;

	[Header("Rocket Launcher (Category = General)")]
	[Tooltip("Тип гранатомёта в сумке. None — не гранатомёт.")]
	[SerializeField] private RocketLauncherType m_RocketLauncherType = RocketLauncherType.None;
	[Tooltip("Префаб в руках во время приказа (без физики лута).")]
	[SerializeField] private GameObject m_RocketLauncherHandPrefab;
	[Tooltip("Префаб летящего снаряда (RocketProjectile).")]
	[SerializeField] private GameObject m_RocketProjectilePrefab;
	[Tooltip("Только для РПГ-7: ItemDefinition ракеты в сумке (заряжается анимацией).")]
	[SerializeField] private ItemDefinition m_RpgRocketItemDefinition;
	[Tooltip("Только для РПГ-7: визуал ракеты в руке во время перезарядки.")]
	[SerializeField] private GameObject m_RpgRocketHandPrefab;
	[Tooltip("Со старта заряжен (одноразовый — всегда true; РПГ — обычно false пока не зарядят).")]
	[SerializeField] private bool m_RocketLauncherStartsLoaded;
	[Tooltip("Отдельная ракета РПГ в сумке (не гранатомёт).")]
	[SerializeField] private bool m_IsRpgRocketAmmo;

	[Header("Medkit (Category = General)")]
	[Tooltip("Данные аптечки: ёмкость ресурса и стоимость стабилизации по травмам.")]
	[SerializeField] private MedkitDefinition m_MedkitDefinition;

	[Header("Inventory Audio")]
	[Tooltip("Подбор / перенос в сумку. Случайный клип из списка.")]
	[SerializeField] private WeaponRandomAudioClipSet m_BagAddSounds = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_BagAddSoundVolume = 0.9f;
	[Tooltip("Выброс из сумки на землю. Случайный клип из списка.")]
	[SerializeField] private WeaponRandomAudioClipSet m_BagRemoveSounds = new WeaponRandomAudioClipSet();
	[SerializeField, Range(0f, 1f)] private float m_BagRemoveSoundVolume = 0.85f;

	[Header("Equipment Audio")]
	[Tooltip("Экипировка в слот оружия (main hand). Случайный клип из списка.")]
	[FormerlySerializedAs("m_InventoryAddSounds")]
	[SerializeField] private WeaponRandomAudioClipSet m_EquipmentAddSounds = new WeaponRandomAudioClipSet();
	[FormerlySerializedAs("m_InventoryAddSoundVolume")]
	[SerializeField, Range(0f, 1f)] private float m_EquipmentAddSoundVolume = 0.9f;
	[Tooltip("Снятие из слота оружия или выброс экипированного оружия. Случайный клип из списка.")]
	[FormerlySerializedAs("m_InventoryRemoveSounds")]
	[SerializeField] private WeaponRandomAudioClipSet m_EquipmentRemoveSounds = new WeaponRandomAudioClipSet();
	[FormerlySerializedAs("m_InventoryRemoveSoundVolume")]
	[SerializeField, Range(0f, 1f)] private float m_EquipmentRemoveSoundVolume = 0.85f;

	#endregion

	#region Public Properties
	public string LocalizationKey => m_LocalizationKey;
	public string Description => m_Description;
	public Sprite Icon => m_Icon;
	public int BasePrice => m_BasePrice;
	public float WeightKg
	{
		get
		{
			if (m_WeightKg <= 0f || Mathf.Approximately(m_WeightKg, 0.5f))
				return ItemWeightDefaults.GetWeight(m_LocalizationKey);
			return m_WeightKg;
		}
	}
	public ItemCategory Category => m_Category;
	/// <summary>Слот экипировки по подтипу.</summary>
	public EquipmentSlotType EquipmentSlot
	{
		get
		{
			if (m_Category != ItemCategory.Equipment)
				return EquipmentSlotType.None;

			return m_EquipmentKind switch
			{
				EquipmentKind.Helmet => EquipmentSlotType.Head,
				EquipmentKind.Backpack => EquipmentSlotType.Back,
				_ => EquipmentSlotType.MainHand
			};
		}
	}
	public GameObject EquippedVisualPrefab => m_EquippedVisualPrefab;
	public WeaponPoseDefinition WeaponPoseDefinition => m_WeaponPoseDefinition;
	public Vector3 RightHandLocalPosition => m_RightHandLocalPosition;
	public Vector3 RightHandLocalEulerAngles => m_RightHandLocalEulerAngles;
	public Quaternion RightHandLocalRotation => Quaternion.Euler(m_RightHandLocalEulerAngles);
	public Vector3 RightHandReadyLocalPosition => m_RightHandReadyLocalPosition;
	public Vector3 RightHandReadyLocalEulerAngles => m_RightHandReadyLocalEulerAngles;
	public Quaternion RightHandReadyLocalRotation => Quaternion.Euler(m_RightHandReadyLocalEulerAngles);
	public Vector3 CrouchRightHandLocalPosition => m_CrouchRightHandLocalPosition;
	public Vector3 CrouchRightHandLocalEulerAngles => m_CrouchRightHandLocalEulerAngles;
	public Quaternion CrouchRightHandLocalRotation => Quaternion.Euler(m_CrouchRightHandLocalEulerAngles);
	public Vector3 CrouchRightHandReadyLocalPosition => m_CrouchRightHandReadyLocalPosition;
	public Vector3 CrouchRightHandReadyLocalEulerAngles => m_CrouchRightHandReadyLocalEulerAngles;
	public Quaternion CrouchRightHandReadyLocalRotation => Quaternion.Euler(m_CrouchRightHandReadyLocalEulerAngles);

	public Vector3 VehicleRightHandLocalPosition => m_VehicleRightHandLocalPosition;
	public Vector3 VehicleRightHandLocalEulerAngles => m_VehicleRightHandLocalEulerAngles;
	public Quaternion VehicleRightHandLocalRotation => Quaternion.Euler(m_VehicleRightHandLocalEulerAngles);
	public Vector3 VehicleRightHandReadyLocalPosition => m_VehicleRightHandReadyLocalPosition;
	public Vector3 VehicleRightHandReadyLocalEulerAngles => m_VehicleRightHandReadyLocalEulerAngles;
	public Quaternion VehicleRightHandReadyLocalRotation => Quaternion.Euler(m_VehicleRightHandReadyLocalEulerAngles);

	public bool IsEquipment => m_Category == ItemCategory.Equipment;
	public GameObject DropWorldPrefab => m_DropWorldPrefab;
	/// <summary>Подтип снаряжения (для Equipment).</summary>
	public EquipmentKind EquipmentKind => m_EquipmentKind;
	public EquipmentVisualProfileDefinition VisualProfile => m_VisualProfile;
	/// <summary>Тип оружия (для Equipment).</summary>
	public WeaponType WeaponType => m_WeaponType;
	public TurretWeaponVariant TurretWeaponVariant => m_TurretWeaponVariant;
	public bool IsTurretWeapon =>
		m_Category == ItemCategory.Equipment && m_EquipmentKind == EquipmentKind.TurretWeapon;
	public bool IsTurretFrontalShield =>
		m_Category == ItemCategory.Equipment && m_EquipmentKind == EquipmentKind.TurretFrontalShield;
	public bool IsTurretSurroundShield =>
		m_Category == ItemCategory.Equipment && m_EquipmentKind == EquipmentKind.TurretSurroundShield;
	public bool IsVehicleTurretEquipment =>
		IsTurretWeapon || IsTurretFrontalShield || IsTurretSurroundShield;
	public WeaponDefinition WeaponDefinition => m_WeaponDefinition;
	public AmmoDefinition AmmoDefinition => m_AmmoDefinition;
	public int InitialAmmoCount => m_InitialAmmoCount;
	public MagazineDefinition MagazineDefinition => m_MagazineDefinition;
	public WeaponAttachmentDefinition WeaponAttachmentDefinition => m_WeaponAttachmentDefinition;
	public GrenadeType GrenadeType => m_GrenadeType;
	public GameObject AttachedBodyVisualPrefab => m_AttachedBodyVisualPrefab;
	public bool IsGrenade => m_Category == ItemCategory.General && m_GrenadeType != GrenadeType.Unknown;
	public RocketLauncherType RocketLauncherType => m_RocketLauncherType;
	public GameObject RocketLauncherHandPrefab => m_RocketLauncherHandPrefab;
	public GameObject RocketProjectilePrefab => m_RocketProjectilePrefab;
	public ItemDefinition RpgRocketItemDefinition => m_RpgRocketItemDefinition;
	public GameObject RpgRocketHandPrefab => m_RpgRocketHandPrefab;
	public bool RocketLauncherStartsLoaded => m_RocketLauncherStartsLoaded;
	public bool IsRocketLauncher => m_Category == ItemCategory.General && m_RocketLauncherType != RocketLauncherType.None;
	public bool IsRpgRocketAmmo => m_IsRpgRocketAmmo;
	public MedkitDefinition MedkitDefinition => m_MedkitDefinition;
	public bool IsMedkit => m_MedkitDefinition != null;
	public int BackpackCapacity => m_BackpackCapacity;
	public float InventoryAddSoundVolume => m_BagAddSoundVolume;
	public float InventoryRemoveSoundVolume => m_BagRemoveSoundVolume;
	public float EquipmentAddSoundVolume => m_EquipmentAddSoundVolume;
	public float EquipmentRemoveSoundVolume => m_EquipmentRemoveSoundVolume;

	/// <summary>Шанс поглощения пули в голову (только для шлемов).</summary>
	public float GetHeadBulletBlockChance()
	{
		if (m_EquipmentKind != EquipmentKind.Helmet)
			return 0f;

		if (m_HeadBulletBlockChance > 0f)
			return m_HeadBulletBlockChance;

		return HelmetCombatDesign.ResolveDefaultBlockChance(m_LocalizationKey);
	}
	#endregion

	#region Public Methods
	public string GetLocalizedDisplayName()
	{
		if (string.IsNullOrWhiteSpace(m_LocalizationKey))
			return LocalizationManager.Get("item.generic", "Item");

		return LocalizationManager.Get(m_LocalizationKey, LocalizationManager.Get("item.generic", "Item"));
	}

	public string GetLocalizedDescription()
	{
		if (!string.IsNullOrWhiteSpace(m_LocalizationKey))
		{
			string descriptionKey = $"{m_LocalizationKey}.desc";
			string localized = LocalizationManager.Get(descriptionKey, m_Description);
			if (localized != descriptionKey)
				return localized;
		}

		return m_Description ?? string.Empty;
	}

	public bool TryGetGrenadeType(out GrenadeType _grenadeType)
	{
		_grenadeType = m_GrenadeType;
		return IsGrenade;
	}

	public bool TryPickInventoryAddSound(out AudioClip _clip) => m_BagAddSounds.TryPickClip(out _clip);

	public bool TryPickInventoryRemoveSound(out AudioClip _clip) => m_BagRemoveSounds.TryPickClip(out _clip);

	public bool TryPickEquipmentAddSound(out AudioClip _clip) => m_EquipmentAddSounds.TryPickClip(out _clip);

	public bool TryPickEquipmentRemoveSound(out AudioClip _clip) => m_EquipmentRemoveSounds.TryPickClip(out _clip);

	/// <summary>True when crouch weapon pose has any authored data (otherwise standing coords are used).</summary>
	public bool HasCrouchWeaponPoseConfigured()
	{
		return HasAnyLocalPoseData(
			m_CrouchRightHandLocalPosition,
			m_CrouchRightHandLocalEulerAngles,
			m_CrouchRightHandReadyLocalPosition,
			m_CrouchRightHandReadyLocalEulerAngles);
	}

	public bool HasVehicleWeaponPoseConfigured()
	{
		return HasAnyLocalPoseData(
			m_VehicleRightHandLocalPosition,
			m_VehicleRightHandLocalEulerAngles,
			m_VehicleRightHandReadyLocalPosition,
			m_VehicleRightHandReadyLocalEulerAngles);
	}

	public static bool UsesCrouchHandPose(LocomotionStance _stance) => _stance == LocomotionStance.Crouch;

	public Vector3 ResolveRightHandLocalPosition(LocomotionStance _stance)
	{
		if (TryGetPoseFromDefinition(ToWeaponStance(_stance, vehicle: false), WeaponPoseState.LowReady, out WeaponPoseEntry e))
			return e.Position;
		return UsesCrouchHandPose(_stance) && HasCrouchWeaponPoseConfigured()
			? m_CrouchRightHandLocalPosition
			: m_RightHandLocalPosition;
	}

	public Quaternion ResolveRightHandLocalRotation(LocomotionStance _stance)
	{
		if (TryGetPoseFromDefinition(ToWeaponStance(_stance, vehicle: false), WeaponPoseState.LowReady, out WeaponPoseEntry e))
			return e.Rotation;
		return UsesCrouchHandPose(_stance) && HasCrouchWeaponPoseConfigured()
			? CrouchRightHandLocalRotation
			: RightHandLocalRotation;
	}

	public Vector3 ResolveRightHandReadyLocalPosition(LocomotionStance _stance)
	{
		if (TryGetPoseFromDefinition(ToWeaponStance(_stance, vehicle: false), WeaponPoseState.PointAim, out WeaponPoseEntry e))
			return e.Position;

		if (!UsesCrouchHandPose(_stance) || !HasCrouchWeaponPoseConfigured())
			return m_RightHandReadyLocalPosition;

		if (m_CrouchRightHandReadyLocalPosition == Vector3.zero && m_CrouchRightHandReadyLocalEulerAngles == Vector3.zero)
			return m_CrouchRightHandLocalPosition;

		return m_CrouchRightHandReadyLocalPosition;
	}

	public Quaternion ResolveRightHandReadyLocalRotation(LocomotionStance _stance)
	{
		if (TryGetPoseFromDefinition(ToWeaponStance(_stance, vehicle: false), WeaponPoseState.PointAim, out WeaponPoseEntry e))
			return e.Rotation;

		if (!UsesCrouchHandPose(_stance) || !HasCrouchWeaponPoseConfigured())
			return RightHandReadyLocalRotation;

		if (m_CrouchRightHandReadyLocalPosition == Vector3.zero && m_CrouchRightHandReadyLocalEulerAngles == Vector3.zero)
			return CrouchRightHandLocalRotation;

		return CrouchRightHandReadyLocalRotation;
	}

	public static bool UsesVehicleHandPose(bool _isVehiclePassengerReady) => _isVehiclePassengerReady;

	/// <summary>Vehicle NotReady weapon local position; falls back to standing NotReady when unset.</summary>
	public Vector3 ResolveVehicleRightHandLocalPosition()
	{
		if (TryGetPoseFromDefinition(WeaponStance.Vehicle, WeaponPoseState.LowReady, out WeaponPoseEntry e))
			return e.Position;
		return HasVehicleWeaponPoseConfigured()
			? m_VehicleRightHandLocalPosition
			: m_RightHandLocalPosition;
	}

	/// <summary>Vehicle NotReady weapon local rotation; falls back to standing NotReady when unset.</summary>
	public Quaternion ResolveVehicleRightHandLocalRotation()
	{
		if (TryGetPoseFromDefinition(WeaponStance.Vehicle, WeaponPoseState.LowReady, out WeaponPoseEntry e))
			return e.Rotation;
		return HasVehicleWeaponPoseConfigured()
			? VehicleRightHandLocalRotation
			: RightHandLocalRotation;
	}

	public Vector3 ResolveVehicleRightHandReadyLocalPosition()
	{
		if (TryGetPoseFromDefinition(WeaponStance.Vehicle, WeaponPoseState.PointAim, out WeaponPoseEntry e))
			return e.Position;

		if (!HasVehicleWeaponPoseConfigured())
			return m_RightHandReadyLocalPosition;

		if (m_VehicleRightHandReadyLocalPosition == Vector3.zero
		    && m_VehicleRightHandReadyLocalEulerAngles == Vector3.zero)
			return m_VehicleRightHandLocalPosition;

		return m_VehicleRightHandReadyLocalPosition;
	}

	public Quaternion ResolveVehicleRightHandReadyLocalRotation()
	{
		if (TryGetPoseFromDefinition(WeaponStance.Vehicle, WeaponPoseState.PointAim, out WeaponPoseEntry e))
			return e.Rotation;

		if (!HasVehicleWeaponPoseConfigured())
			return RightHandReadyLocalRotation;

		if (m_VehicleRightHandReadyLocalPosition == Vector3.zero
		    && m_VehicleRightHandReadyLocalEulerAngles == Vector3.zero)
			return VehicleRightHandLocalRotation;

		return VehicleRightHandReadyLocalRotation;
	}

	/// <summary>Editor/migration: assign pose SO (runtime must not call).</summary>
	public void SetWeaponPoseDefinition(WeaponPoseDefinition _definition)
	{
		m_WeaponPoseDefinition = _definition;
	}

	public bool TryGetBlendedWeaponPose(
		WeaponStance _stance,
		float _readyBlend01,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		if (m_WeaponPoseDefinition == null)
		{
			_position = Vector3.zero;
			_rotation = Quaternion.identity;
			return false;
		}

		m_WeaponPoseDefinition.GetBlended(_stance, _readyBlend01, out _position, out _rotation);
		return true;
	}

	private bool TryGetPoseFromDefinition(WeaponStance _stance, WeaponPoseState _pose, out WeaponPoseEntry _entry)
	{
		_entry = null;
		if (m_WeaponPoseDefinition == null)
			return false;
		if (m_WeaponPoseDefinition.TryGetPose(_stance, _pose, out _entry) && _entry != null)
			return true;
		if (_stance != WeaponStance.Standing
		    && m_WeaponPoseDefinition.TryGetPose(WeaponStance.Standing, _pose, out _entry)
		    && _entry != null)
			return true;
		return false;
	}

	private static WeaponStance ToWeaponStance(LocomotionStance _stance, bool vehicle)
	{
		if (vehicle)
			return WeaponStance.Vehicle;
		return UsesCrouchHandPose(_stance) ? WeaponStance.Crouching : WeaponStance.Standing;
	}

	/// <summary>Copies standing weapon pose into vehicle fields (editor bootstrap).</summary>
	public void CopyStandingHandPoseToVehicle()
	{
		m_VehicleRightHandLocalPosition = m_RightHandLocalPosition;
		m_VehicleRightHandLocalEulerAngles = m_RightHandLocalEulerAngles;
		m_VehicleRightHandReadyLocalPosition = m_RightHandReadyLocalPosition;
		m_VehicleRightHandReadyLocalEulerAngles = m_RightHandReadyLocalEulerAngles;

		if (m_VehicleRightHandReadyLocalPosition == Vector3.zero
		    && m_VehicleRightHandReadyLocalEulerAngles == Vector3.zero)
		{
			m_VehicleRightHandReadyLocalPosition = m_VehicleRightHandLocalPosition;
			m_VehicleRightHandReadyLocalEulerAngles = m_VehicleRightHandLocalEulerAngles;
		}
	}

	/// <summary>Copies standing weapon pose into crouch fields (editor bootstrap).</summary>
	public void CopyStandingHandPoseToCrouch()
	{
		m_CrouchRightHandLocalPosition = m_RightHandLocalPosition;
		m_CrouchRightHandLocalEulerAngles = m_RightHandLocalEulerAngles;
		m_CrouchRightHandReadyLocalPosition = m_RightHandReadyLocalPosition;
		m_CrouchRightHandReadyLocalEulerAngles = m_RightHandReadyLocalEulerAngles;

		if (m_CrouchRightHandReadyLocalPosition == Vector3.zero && m_CrouchRightHandReadyLocalEulerAngles == Vector3.zero)
		{
			m_CrouchRightHandReadyLocalPosition = m_CrouchRightHandLocalPosition;
			m_CrouchRightHandReadyLocalEulerAngles = m_CrouchRightHandLocalEulerAngles;
		}
	}

	private static bool HasAnyLocalPoseData(
		Vector3 _relaxedPosition,
		Vector3 _relaxedEuler,
		Vector3 _readyPosition,
		Vector3 _readyEuler)
	{
		return _relaxedPosition != Vector3.zero || _relaxedEuler != Vector3.zero
		       || _readyPosition != Vector3.zero || _readyEuler != Vector3.zero;
	}
	#endregion
}
