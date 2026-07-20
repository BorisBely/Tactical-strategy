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
	[Tooltip("Local position of the weapon prefab relative to the right hand (low ready / relaxed mode).")]
	[SerializeField] private Vector3 m_RightHandLocalPosition;
	[Tooltip("Local Euler angles of the weapon relative to the right hand (low ready / relaxed mode).")]
	[SerializeField] private Vector3 m_RightHandLocalEulerAngles;
	[Tooltip("Local position of the weapon prefab relative to the right hand (high ready mode).")]
	[SerializeField] private Vector3 m_RightHandReadyLocalPosition;
	[Tooltip("Local Euler angles of the weapon relative to the right hand (high ready mode).")]
	[SerializeField] private Vector3 m_RightHandReadyLocalEulerAngles;
	[Header("Правая рука — IK кисти (локально на оружии)")]
	[Tooltip("Local position of the right‑hand IK target on the weapon (low ready mode).")]
	[SerializeField] private Vector3 m_RightHandIkNotReadyLocalPosition;
	[Tooltip("Local Euler angles of the right‑hand IK target on the weapon (low ready mode).")]
	[SerializeField] private Vector3 m_RightHandIkNotReadyLocalEulerAngles;
	[Tooltip("Local position of the right‑hand IK target on the weapon (high ready mode). Zeros — use the dummy on the prefab.")]
	[SerializeField] private Vector3 m_RightHandIkReadyLocalPosition;
	[Tooltip("Local Euler angles of the right‑hand IK target on the weapon (high ready mode). Zeros — use the dummy on the prefab.")]
	[SerializeField] private Vector3 m_RightHandIkReadyLocalEulerAngles;
	[Header("Левая рука — IK кисти (локально на оружии)")]
	[Tooltip("Local left-hand IK on weapon (low ready). Zeros — use LeftHandIkTarget_NotReady dummy (or Ready if missing).")]
	[SerializeField] private Vector3 m_LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_LeftHandIkNotReadyLocalEulerAngles;
	[Tooltip("Local left-hand IK on weapon (high ready). Zeros — use LeftHandIkTarget dummy.")]
	[SerializeField] private Vector3 m_LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_LeftHandIkReadyLocalEulerAngles;
	[Tooltip("Child on weapon/foregrip prefab: left-hand IK in high ready. Empty — no left IK.")]
	[SerializeField] private string m_LeftHandIkTargetChildName = "LeftHandIkTarget";
	[Tooltip("Child on weapon/foregrip prefab: left-hand IK in low ready / not ready.")]
	[SerializeField] private string m_LeftHandIkTargetNotReadyChildName = "LeftHandIkTarget_NotReady";
	[Tooltip("Right‑hand IK dummy in high ready mode, if Ik Ready Local is not set. Empty — coordinates from asset only.")]
	[SerializeField] private string m_RightHandIkTargetChildName = "RightHandIkTarget";
	[Tooltip("Right‑hand IK dummy in low ready mode, if Ik Not Ready Local is not set.")]
	[SerializeField] private string m_RightHandIkTargetNotReadyChildName = "RightHandIkTarget_NotReady";

	[Header("Crouch — weapon pose (Hand_R local)")]
	[Tooltip("Local position of the weapon in crouch (low ready). Zeros — copy from standing.")]
	[SerializeField] private Vector3 m_CrouchRightHandLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchRightHandReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandReadyLocalEulerAngles;

	[Header("Crouch — right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_CrouchRightHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchRightHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchRightHandIkReadyLocalEulerAngles;

	[Header("Crouch — left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_CrouchLeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchLeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchLeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchLeftHandIkReadyLocalEulerAngles;

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
	public Vector3 RightHandLocalPosition => m_RightHandLocalPosition;
	public Vector3 RightHandLocalEulerAngles => m_RightHandLocalEulerAngles;
	public Quaternion RightHandLocalRotation => Quaternion.Euler(m_RightHandLocalEulerAngles);
	public Vector3 RightHandReadyLocalPosition => m_RightHandReadyLocalPosition;
	public Vector3 RightHandReadyLocalEulerAngles => m_RightHandReadyLocalEulerAngles;
	public Quaternion RightHandReadyLocalRotation => Quaternion.Euler(m_RightHandReadyLocalEulerAngles);
	public Vector3 RightHandIkNotReadyLocalPosition => m_RightHandIkNotReadyLocalPosition;
	public Vector3 RightHandIkNotReadyLocalEulerAngles => m_RightHandIkNotReadyLocalEulerAngles;
	public Quaternion RightHandIkNotReadyLocalRotation => Quaternion.Euler(m_RightHandIkNotReadyLocalEulerAngles);
	public Vector3 RightHandIkReadyLocalPosition => m_RightHandIkReadyLocalPosition;
	public Vector3 RightHandIkReadyLocalEulerAngles => m_RightHandIkReadyLocalEulerAngles;
	public Quaternion RightHandIkReadyLocalRotation => Quaternion.Euler(m_RightHandIkReadyLocalEulerAngles);
	public Vector3 LeftHandIkNotReadyLocalPosition => m_LeftHandIkNotReadyLocalPosition;
	public Vector3 LeftHandIkNotReadyLocalEulerAngles => m_LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_LeftHandIkNotReadyLocalEulerAngles);
	public Vector3 LeftHandIkReadyLocalPosition => m_LeftHandIkReadyLocalPosition;
	public Vector3 LeftHandIkReadyLocalEulerAngles => m_LeftHandIkReadyLocalEulerAngles;
	public Quaternion LeftHandIkReadyLocalRotation => Quaternion.Euler(m_LeftHandIkReadyLocalEulerAngles);
	public string LeftHandIkTargetChildName => m_LeftHandIkTargetChildName;
	public string LeftHandIkTargetNotReadyChildName => m_LeftHandIkTargetNotReadyChildName;
	public string RightHandIkTargetChildName => m_RightHandIkTargetChildName;
	public string RightHandIkTargetNotReadyChildName => m_RightHandIkTargetNotReadyChildName;
	public Vector3 CrouchRightHandLocalPosition => m_CrouchRightHandLocalPosition;
	public Vector3 CrouchRightHandLocalEulerAngles => m_CrouchRightHandLocalEulerAngles;
	public Quaternion CrouchRightHandLocalRotation => Quaternion.Euler(m_CrouchRightHandLocalEulerAngles);
	public Vector3 CrouchRightHandReadyLocalPosition => m_CrouchRightHandReadyLocalPosition;
	public Vector3 CrouchRightHandReadyLocalEulerAngles => m_CrouchRightHandReadyLocalEulerAngles;
	public Quaternion CrouchRightHandReadyLocalRotation => Quaternion.Euler(m_CrouchRightHandReadyLocalEulerAngles);
	public Vector3 CrouchRightHandIkNotReadyLocalPosition => m_CrouchRightHandIkNotReadyLocalPosition;
	public Vector3 CrouchRightHandIkNotReadyLocalEulerAngles => m_CrouchRightHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchRightHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchRightHandIkNotReadyLocalEulerAngles);
	public Vector3 CrouchRightHandIkReadyLocalPosition => m_CrouchRightHandIkReadyLocalPosition;
	public Vector3 CrouchRightHandIkReadyLocalEulerAngles => m_CrouchRightHandIkReadyLocalEulerAngles;
	public Quaternion CrouchRightHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchRightHandIkReadyLocalEulerAngles);
	public Vector3 CrouchLeftHandIkNotReadyLocalPosition => m_CrouchLeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchLeftHandIkNotReadyLocalEulerAngles => m_CrouchLeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchLeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchLeftHandIkNotReadyLocalEulerAngles);
	public Vector3 CrouchLeftHandIkReadyLocalPosition => m_CrouchLeftHandIkReadyLocalPosition;
	public Vector3 CrouchLeftHandIkReadyLocalEulerAngles => m_CrouchLeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchLeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchLeftHandIkReadyLocalEulerAngles);
	public bool IsEquipment => m_Category == ItemCategory.Equipment;
	public GameObject DropWorldPrefab => m_DropWorldPrefab;
	/// <summary>Подтип снаряжения (для Equipment).</summary>
	public EquipmentKind EquipmentKind => m_EquipmentKind;
	public EquipmentVisualProfileDefinition VisualProfile => m_VisualProfile;
	/// <summary>Тип оружия (для Equipment).</summary>
	public WeaponType WeaponType => m_WeaponType;
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

	/// <summary>True when crouch right-hand IK has any authored data (otherwise standing IK coords are used).</summary>
	public bool HasCrouchRightHandIkConfigured()
	{
		return HasAnyLocalPoseData(
			m_CrouchRightHandIkNotReadyLocalPosition,
			m_CrouchRightHandIkNotReadyLocalEulerAngles,
			m_CrouchRightHandIkReadyLocalPosition,
			m_CrouchRightHandIkReadyLocalEulerAngles);
	}

	/// <summary>True when crouch left-hand IK has any authored data (otherwise standing IK coords are used).</summary>
	public bool HasCrouchLeftHandIkConfigured()
	{
		return HasAnyLocalPoseData(
			m_CrouchLeftHandIkNotReadyLocalPosition,
			m_CrouchLeftHandIkNotReadyLocalEulerAngles,
			m_CrouchLeftHandIkReadyLocalPosition,
			m_CrouchLeftHandIkReadyLocalEulerAngles);
	}

	public static bool UsesCrouchHandPose(LocomotionStance _stance) => _stance == LocomotionStance.Crouch;

	public Vector3 ResolveRightHandLocalPosition(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchWeaponPoseConfigured()
			? m_CrouchRightHandLocalPosition
			: m_RightHandLocalPosition;
	}

	public Quaternion ResolveRightHandLocalRotation(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchWeaponPoseConfigured()
			? CrouchRightHandLocalRotation
			: RightHandLocalRotation;
	}

	public Vector3 ResolveRightHandReadyLocalPosition(LocomotionStance _stance)
	{
		if (!UsesCrouchHandPose(_stance) || !HasCrouchWeaponPoseConfigured())
			return m_RightHandReadyLocalPosition;

		if (m_CrouchRightHandReadyLocalPosition == Vector3.zero && m_CrouchRightHandReadyLocalEulerAngles == Vector3.zero)
			return m_CrouchRightHandLocalPosition;

		return m_CrouchRightHandReadyLocalPosition;
	}

	public Quaternion ResolveRightHandReadyLocalRotation(LocomotionStance _stance)
	{
		if (!UsesCrouchHandPose(_stance) || !HasCrouchWeaponPoseConfigured())
			return RightHandReadyLocalRotation;

		if (m_CrouchRightHandReadyLocalPosition == Vector3.zero && m_CrouchRightHandReadyLocalEulerAngles == Vector3.zero)
			return CrouchRightHandLocalRotation;

		return CrouchRightHandReadyLocalRotation;
	}

	public Vector3 ResolveRightHandIkNotReadyLocalPosition(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? m_CrouchRightHandIkNotReadyLocalPosition
			: m_RightHandIkNotReadyLocalPosition;
	}

	public Quaternion ResolveRightHandIkNotReadyLocalRotation(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? CrouchRightHandIkNotReadyLocalRotation
			: RightHandIkNotReadyLocalRotation;
	}

	public Vector3 ResolveRightHandIkReadyLocalPosition(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? m_CrouchRightHandIkReadyLocalPosition
			: m_RightHandIkReadyLocalPosition;
	}

	public Quaternion ResolveRightHandIkReadyLocalRotation(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? CrouchRightHandIkReadyLocalRotation
			: RightHandIkReadyLocalRotation;
	}

	public Vector3 ResolveLeftHandIkNotReadyLocalPosition(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? m_CrouchLeftHandIkNotReadyLocalPosition
			: m_LeftHandIkNotReadyLocalPosition;
	}

	public Quaternion ResolveLeftHandIkNotReadyLocalRotation(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? CrouchLeftHandIkNotReadyLocalRotation
			: LeftHandIkNotReadyLocalRotation;
	}

	public Vector3 ResolveLeftHandIkReadyLocalPosition(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? m_CrouchLeftHandIkReadyLocalPosition
			: m_LeftHandIkReadyLocalPosition;
	}

	public Quaternion ResolveLeftHandIkReadyLocalRotation(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? CrouchLeftHandIkReadyLocalRotation
			: LeftHandIkReadyLocalRotation;
	}

	public Vector3 ResolveRightHandIkNotReadyLocalEulerAngles(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? m_CrouchRightHandIkNotReadyLocalEulerAngles
			: m_RightHandIkNotReadyLocalEulerAngles;
	}

	public Vector3 ResolveRightHandIkReadyLocalEulerAngles(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchRightHandIkConfigured()
			? m_CrouchRightHandIkReadyLocalEulerAngles
			: m_RightHandIkReadyLocalEulerAngles;
	}

	public Vector3 ResolveLeftHandIkNotReadyLocalEulerAngles(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? m_CrouchLeftHandIkNotReadyLocalEulerAngles
			: m_LeftHandIkNotReadyLocalEulerAngles;
	}

	public Vector3 ResolveLeftHandIkReadyLocalEulerAngles(LocomotionStance _stance)
	{
		return UsesCrouchHandPose(_stance) && HasCrouchLeftHandIkConfigured()
			? m_CrouchLeftHandIkReadyLocalEulerAngles
			: m_LeftHandIkReadyLocalEulerAngles;
	}

	/// <summary>Copies standing weapon pose + hand IK into crouch fields (editor bootstrap).</summary>
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

		m_CrouchRightHandIkNotReadyLocalPosition = m_RightHandIkNotReadyLocalPosition;
		m_CrouchRightHandIkNotReadyLocalEulerAngles = m_RightHandIkNotReadyLocalEulerAngles;
		m_CrouchRightHandIkReadyLocalPosition = m_RightHandIkReadyLocalPosition;
		m_CrouchRightHandIkReadyLocalEulerAngles = m_RightHandIkReadyLocalEulerAngles;
		m_CrouchLeftHandIkNotReadyLocalPosition = m_LeftHandIkNotReadyLocalPosition;
		m_CrouchLeftHandIkNotReadyLocalEulerAngles = m_LeftHandIkNotReadyLocalEulerAngles;
		m_CrouchLeftHandIkReadyLocalPosition = m_LeftHandIkReadyLocalPosition;
		m_CrouchLeftHandIkReadyLocalEulerAngles = m_LeftHandIkReadyLocalEulerAngles;
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
