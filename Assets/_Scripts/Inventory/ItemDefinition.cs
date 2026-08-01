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

	[Header("Vehicle — weapon pose (Hand_R local)")]
	[Tooltip("Local position of the weapon in vehicle (not ready / relax). Zeros — copy from standing.")]
	[SerializeField] private Vector3 m_VehicleRightHandLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleRightHandReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandReadyLocalEulerAngles;

	[Header("Vehicle — right hand IK (weapon local)")]
	[SerializeField] private Vector3 m_VehicleRightHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleRightHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleRightHandIkReadyLocalEulerAngles;

	[Header("Vehicle — left hand IK (weapon local)")]
	[SerializeField] private Vector3 m_VehicleLeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleLeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleLeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleLeftHandIkReadyLocalEulerAngles;

	[Header("ForeGrip Left Hand IK (weapon local)")]
	[Tooltip("Override weapon left IK when ForeGrip1 is attached.")]
	[SerializeField] private Vector3 m_ForeGrip1LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip1LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ForeGrip1LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	[Tooltip("Override weapon left IK when ForeGrip2 is attached.")]
	[SerializeField] private Vector3 m_ForeGrip2LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip2LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ForeGrip2LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	[Tooltip("Override weapon left IK when ForeGrip3 is attached.")]
	[SerializeField] private Vector3 m_ForeGrip3LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip3LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ForeGrip3LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	[Tooltip("Override weapon left IK when ForeGrip4 is attached.")]
	[SerializeField] private Vector3 m_ForeGrip4LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip4LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ForeGrip4LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	[Tooltip("Override weapon left IK when ForeGrip5 is attached.")]
	[SerializeField] private Vector3 m_ForeGrip5LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip5LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_ForeGrip5LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_ForeGrip5LeftHandIkNotReadyLocalEulerAngles;

	[Header("Crouch — ForeGrip Left Hand IK (weapon local)")]
	[SerializeField] private Vector3 m_CrouchForeGrip1LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip1LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip1LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip2LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip2LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip2LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip3LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip3LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip3LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip4LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip4LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip4LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip5LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip5LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_CrouchForeGrip5LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles;

	[Header("Vehicle — ForeGrip Left Hand IK (weapon local)")]
	[SerializeField] private Vector3 m_VehicleForeGrip1LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip1LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip1LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip2LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip2LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip2LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip3LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip3LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip3LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip4LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip4LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip4LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip5LeftHandIkReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip5LeftHandIkReadyLocalEulerAngles;
	[SerializeField] private Vector3 m_VehicleForeGrip5LeftHandIkNotReadyLocalPosition;
	[SerializeField] private Vector3 m_VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles;

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

	public Vector3 VehicleRightHandLocalPosition => m_VehicleRightHandLocalPosition;
	public Vector3 VehicleRightHandLocalEulerAngles => m_VehicleRightHandLocalEulerAngles;
	public Quaternion VehicleRightHandLocalRotation => Quaternion.Euler(m_VehicleRightHandLocalEulerAngles);
	public Vector3 VehicleRightHandReadyLocalPosition => m_VehicleRightHandReadyLocalPosition;
	public Vector3 VehicleRightHandReadyLocalEulerAngles => m_VehicleRightHandReadyLocalEulerAngles;
	public Quaternion VehicleRightHandReadyLocalRotation => Quaternion.Euler(m_VehicleRightHandReadyLocalEulerAngles);

	public Vector3 VehicleRightHandIkNotReadyLocalPosition => m_VehicleRightHandIkNotReadyLocalPosition;
	public Vector3 VehicleRightHandIkNotReadyLocalEulerAngles => m_VehicleRightHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleRightHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleRightHandIkNotReadyLocalEulerAngles);
	public Vector3 VehicleRightHandIkReadyLocalPosition => m_VehicleRightHandIkReadyLocalPosition;
	public Vector3 VehicleRightHandIkReadyLocalEulerAngles => m_VehicleRightHandIkReadyLocalEulerAngles;
	public Quaternion VehicleRightHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleRightHandIkReadyLocalEulerAngles);

	public Vector3 VehicleLeftHandIkNotReadyLocalPosition => m_VehicleLeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleLeftHandIkNotReadyLocalEulerAngles => m_VehicleLeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleLeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleLeftHandIkNotReadyLocalEulerAngles);
	public Vector3 VehicleLeftHandIkReadyLocalPosition => m_VehicleLeftHandIkReadyLocalPosition;
	public Vector3 VehicleLeftHandIkReadyLocalEulerAngles => m_VehicleLeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleLeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleLeftHandIkReadyLocalEulerAngles);

	public Vector3 ForeGrip1LeftHandIkReadyLocalPosition => m_ForeGrip1LeftHandIkReadyLocalPosition;
	public Vector3 ForeGrip1LeftHandIkReadyLocalEulerAngles => m_ForeGrip1LeftHandIkReadyLocalEulerAngles;
	public Quaternion ForeGrip1LeftHandIkReadyLocalRotation => Quaternion.Euler(m_ForeGrip1LeftHandIkReadyLocalEulerAngles);
	public Vector3 ForeGrip1LeftHandIkNotReadyLocalPosition => m_ForeGrip1LeftHandIkNotReadyLocalPosition;
	public Vector3 ForeGrip1LeftHandIkNotReadyLocalEulerAngles => m_ForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion ForeGrip1LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_ForeGrip1LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 ForeGrip2LeftHandIkReadyLocalPosition => m_ForeGrip2LeftHandIkReadyLocalPosition;
	public Vector3 ForeGrip2LeftHandIkReadyLocalEulerAngles => m_ForeGrip2LeftHandIkReadyLocalEulerAngles;
	public Quaternion ForeGrip2LeftHandIkReadyLocalRotation => Quaternion.Euler(m_ForeGrip2LeftHandIkReadyLocalEulerAngles);
	public Vector3 ForeGrip2LeftHandIkNotReadyLocalPosition => m_ForeGrip2LeftHandIkNotReadyLocalPosition;
	public Vector3 ForeGrip2LeftHandIkNotReadyLocalEulerAngles => m_ForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion ForeGrip2LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_ForeGrip2LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 ForeGrip3LeftHandIkReadyLocalPosition => m_ForeGrip3LeftHandIkReadyLocalPosition;
	public Vector3 ForeGrip3LeftHandIkReadyLocalEulerAngles => m_ForeGrip3LeftHandIkReadyLocalEulerAngles;
	public Quaternion ForeGrip3LeftHandIkReadyLocalRotation => Quaternion.Euler(m_ForeGrip3LeftHandIkReadyLocalEulerAngles);
	public Vector3 ForeGrip3LeftHandIkNotReadyLocalPosition => m_ForeGrip3LeftHandIkNotReadyLocalPosition;
	public Vector3 ForeGrip3LeftHandIkNotReadyLocalEulerAngles => m_ForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion ForeGrip3LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_ForeGrip3LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 ForeGrip4LeftHandIkReadyLocalPosition => m_ForeGrip4LeftHandIkReadyLocalPosition;
	public Vector3 ForeGrip4LeftHandIkReadyLocalEulerAngles => m_ForeGrip4LeftHandIkReadyLocalEulerAngles;
	public Quaternion ForeGrip4LeftHandIkReadyLocalRotation => Quaternion.Euler(m_ForeGrip4LeftHandIkReadyLocalEulerAngles);
	public Vector3 ForeGrip4LeftHandIkNotReadyLocalPosition => m_ForeGrip4LeftHandIkNotReadyLocalPosition;
	public Vector3 ForeGrip4LeftHandIkNotReadyLocalEulerAngles => m_ForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion ForeGrip4LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_ForeGrip4LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 ForeGrip5LeftHandIkReadyLocalPosition => m_ForeGrip5LeftHandIkReadyLocalPosition;
	public Vector3 ForeGrip5LeftHandIkReadyLocalEulerAngles => m_ForeGrip5LeftHandIkReadyLocalEulerAngles;
	public Quaternion ForeGrip5LeftHandIkReadyLocalRotation => Quaternion.Euler(m_ForeGrip5LeftHandIkReadyLocalEulerAngles);
	public Vector3 ForeGrip5LeftHandIkNotReadyLocalPosition => m_ForeGrip5LeftHandIkNotReadyLocalPosition;
	public Vector3 ForeGrip5LeftHandIkNotReadyLocalEulerAngles => m_ForeGrip5LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion ForeGrip5LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_ForeGrip5LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 CrouchForeGrip1LeftHandIkReadyLocalPosition => m_CrouchForeGrip1LeftHandIkReadyLocalPosition;
	public Vector3 CrouchForeGrip1LeftHandIkReadyLocalEulerAngles => m_CrouchForeGrip1LeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip1LeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip1LeftHandIkReadyLocalEulerAngles);
	public Vector3 CrouchForeGrip1LeftHandIkNotReadyLocalPosition => m_CrouchForeGrip1LeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles => m_CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip1LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 CrouchForeGrip2LeftHandIkReadyLocalPosition => m_CrouchForeGrip2LeftHandIkReadyLocalPosition;
	public Vector3 CrouchForeGrip2LeftHandIkReadyLocalEulerAngles => m_CrouchForeGrip2LeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip2LeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip2LeftHandIkReadyLocalEulerAngles);
	public Vector3 CrouchForeGrip2LeftHandIkNotReadyLocalPosition => m_CrouchForeGrip2LeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles => m_CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip2LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 CrouchForeGrip3LeftHandIkReadyLocalPosition => m_CrouchForeGrip3LeftHandIkReadyLocalPosition;
	public Vector3 CrouchForeGrip3LeftHandIkReadyLocalEulerAngles => m_CrouchForeGrip3LeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip3LeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip3LeftHandIkReadyLocalEulerAngles);
	public Vector3 CrouchForeGrip3LeftHandIkNotReadyLocalPosition => m_CrouchForeGrip3LeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles => m_CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip3LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 CrouchForeGrip4LeftHandIkReadyLocalPosition => m_CrouchForeGrip4LeftHandIkReadyLocalPosition;
	public Vector3 CrouchForeGrip4LeftHandIkReadyLocalEulerAngles => m_CrouchForeGrip4LeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip4LeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip4LeftHandIkReadyLocalEulerAngles);
	public Vector3 CrouchForeGrip4LeftHandIkNotReadyLocalPosition => m_CrouchForeGrip4LeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles => m_CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip4LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 CrouchForeGrip5LeftHandIkReadyLocalPosition => m_CrouchForeGrip5LeftHandIkReadyLocalPosition;
	public Vector3 CrouchForeGrip5LeftHandIkReadyLocalEulerAngles => m_CrouchForeGrip5LeftHandIkReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip5LeftHandIkReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip5LeftHandIkReadyLocalEulerAngles);
	public Vector3 CrouchForeGrip5LeftHandIkNotReadyLocalPosition => m_CrouchForeGrip5LeftHandIkNotReadyLocalPosition;
	public Vector3 CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles => m_CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion CrouchForeGrip5LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 VehicleForeGrip1LeftHandIkReadyLocalPosition => m_VehicleForeGrip1LeftHandIkReadyLocalPosition;
	public Vector3 VehicleForeGrip1LeftHandIkReadyLocalEulerAngles => m_VehicleForeGrip1LeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip1LeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip1LeftHandIkReadyLocalEulerAngles);
	public Vector3 VehicleForeGrip1LeftHandIkNotReadyLocalPosition => m_VehicleForeGrip1LeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles => m_VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip1LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 VehicleForeGrip2LeftHandIkReadyLocalPosition => m_VehicleForeGrip2LeftHandIkReadyLocalPosition;
	public Vector3 VehicleForeGrip2LeftHandIkReadyLocalEulerAngles => m_VehicleForeGrip2LeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip2LeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip2LeftHandIkReadyLocalEulerAngles);
	public Vector3 VehicleForeGrip2LeftHandIkNotReadyLocalPosition => m_VehicleForeGrip2LeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles => m_VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip2LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 VehicleForeGrip3LeftHandIkReadyLocalPosition => m_VehicleForeGrip3LeftHandIkReadyLocalPosition;
	public Vector3 VehicleForeGrip3LeftHandIkReadyLocalEulerAngles => m_VehicleForeGrip3LeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip3LeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip3LeftHandIkReadyLocalEulerAngles);
	public Vector3 VehicleForeGrip3LeftHandIkNotReadyLocalPosition => m_VehicleForeGrip3LeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles => m_VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip3LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 VehicleForeGrip4LeftHandIkReadyLocalPosition => m_VehicleForeGrip4LeftHandIkReadyLocalPosition;
	public Vector3 VehicleForeGrip4LeftHandIkReadyLocalEulerAngles => m_VehicleForeGrip4LeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip4LeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip4LeftHandIkReadyLocalEulerAngles);
	public Vector3 VehicleForeGrip4LeftHandIkNotReadyLocalPosition => m_VehicleForeGrip4LeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles => m_VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip4LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles);

	public Vector3 VehicleForeGrip5LeftHandIkReadyLocalPosition => m_VehicleForeGrip5LeftHandIkReadyLocalPosition;
	public Vector3 VehicleForeGrip5LeftHandIkReadyLocalEulerAngles => m_VehicleForeGrip5LeftHandIkReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip5LeftHandIkReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip5LeftHandIkReadyLocalEulerAngles);
	public Vector3 VehicleForeGrip5LeftHandIkNotReadyLocalPosition => m_VehicleForeGrip5LeftHandIkNotReadyLocalPosition;
	public Vector3 VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles => m_VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles;
	public Quaternion VehicleForeGrip5LeftHandIkNotReadyLocalRotation => Quaternion.Euler(m_VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles);

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

	public bool HasVehicleWeaponPoseConfigured()
	{
		return HasAnyLocalPoseData(
			m_VehicleRightHandLocalPosition,
			m_VehicleRightHandLocalEulerAngles,
			m_VehicleRightHandReadyLocalPosition,
			m_VehicleRightHandReadyLocalEulerAngles);
	}

	public bool HasVehicleRightHandIkConfigured()
	{
		return HasAnyLocalPoseData(
			m_VehicleRightHandIkNotReadyLocalPosition,
			m_VehicleRightHandIkNotReadyLocalEulerAngles,
			m_VehicleRightHandIkReadyLocalPosition,
			m_VehicleRightHandIkReadyLocalEulerAngles);
	}

	public bool HasVehicleLeftHandIkConfigured()
	{
		return HasAnyLocalPoseData(
			m_VehicleLeftHandIkNotReadyLocalPosition,
			m_VehicleLeftHandIkNotReadyLocalEulerAngles,
			m_VehicleLeftHandIkReadyLocalPosition,
			m_VehicleLeftHandIkReadyLocalEulerAngles);
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

	public static bool UsesVehicleHandPose(bool _isVehiclePassengerReady) => _isVehiclePassengerReady;

	/// <summary>Vehicle NotReady weapon local position; falls back to standing NotReady when unset.</summary>
	public Vector3 ResolveVehicleRightHandLocalPosition()
	{
		return HasVehicleWeaponPoseConfigured()
			? m_VehicleRightHandLocalPosition
			: m_RightHandLocalPosition;
	}

	/// <summary>Vehicle NotReady weapon local rotation; falls back to standing NotReady when unset.</summary>
	public Quaternion ResolveVehicleRightHandLocalRotation()
	{
		return HasVehicleWeaponPoseConfigured()
			? VehicleRightHandLocalRotation
			: RightHandLocalRotation;
	}

	public Vector3 ResolveVehicleRightHandReadyLocalPosition()
	{
		if (!HasVehicleWeaponPoseConfigured())
			return m_RightHandReadyLocalPosition;

		if (m_VehicleRightHandReadyLocalPosition == Vector3.zero
		    && m_VehicleRightHandReadyLocalEulerAngles == Vector3.zero)
			return m_VehicleRightHandLocalPosition;

		return m_VehicleRightHandReadyLocalPosition;
	}

	public Quaternion ResolveVehicleRightHandReadyLocalRotation()
	{
		if (!HasVehicleWeaponPoseConfigured())
			return RightHandReadyLocalRotation;

		if (m_VehicleRightHandReadyLocalPosition == Vector3.zero
		    && m_VehicleRightHandReadyLocalEulerAngles == Vector3.zero)
			return VehicleRightHandLocalRotation;

		return VehicleRightHandReadyLocalRotation;
	}

	public Vector3 ResolveVehicleRightHandIkNotReadyLocalPosition()
	{
		return HasVehicleRightHandIkConfigured()
			? m_VehicleRightHandIkNotReadyLocalPosition
			: m_RightHandIkNotReadyLocalPosition;
	}

	public Quaternion ResolveVehicleRightHandIkNotReadyLocalRotation()
	{
		return HasVehicleRightHandIkConfigured()
			? VehicleRightHandIkNotReadyLocalRotation
			: RightHandIkNotReadyLocalRotation;
	}

	public Vector3 ResolveVehicleRightHandIkReadyLocalPosition()
	{
		return HasVehicleRightHandIkConfigured()
			? m_VehicleRightHandIkReadyLocalPosition
			: m_RightHandIkReadyLocalPosition;
	}

	public Quaternion ResolveVehicleRightHandIkReadyLocalRotation()
	{
		return HasVehicleRightHandIkConfigured()
			? VehicleRightHandIkReadyLocalRotation
			: RightHandIkReadyLocalRotation;
	}

	public Vector3 ResolveVehicleLeftHandIkNotReadyLocalPosition()
	{
		return HasVehicleLeftHandIkConfigured()
			? m_VehicleLeftHandIkNotReadyLocalPosition
			: m_LeftHandIkNotReadyLocalPosition;
	}

	public Quaternion ResolveVehicleLeftHandIkNotReadyLocalRotation()
	{
		return HasVehicleLeftHandIkConfigured()
			? VehicleLeftHandIkNotReadyLocalRotation
			: LeftHandIkNotReadyLocalRotation;
	}

	public Vector3 ResolveVehicleLeftHandIkReadyLocalPosition()
	{
		return HasVehicleLeftHandIkConfigured()
			? m_VehicleLeftHandIkReadyLocalPosition
			: m_LeftHandIkReadyLocalPosition;
	}

	public Quaternion ResolveVehicleLeftHandIkReadyLocalRotation()
	{
		return HasVehicleLeftHandIkConfigured()
			? VehicleLeftHandIkReadyLocalRotation
			: LeftHandIkReadyLocalRotation;
	}

	public Vector3 ResolveVehicleRightHandIkNotReadyLocalEulerAngles()
	{
		return HasVehicleRightHandIkConfigured()
			? m_VehicleRightHandIkNotReadyLocalEulerAngles
			: m_RightHandIkNotReadyLocalEulerAngles;
	}

	public Vector3 ResolveVehicleRightHandIkReadyLocalEulerAngles()
	{
		return HasVehicleRightHandIkConfigured()
			? m_VehicleRightHandIkReadyLocalEulerAngles
			: m_RightHandIkReadyLocalEulerAngles;
	}

	public Vector3 ResolveVehicleLeftHandIkNotReadyLocalEulerAngles()
	{
		return HasVehicleLeftHandIkConfigured()
			? m_VehicleLeftHandIkNotReadyLocalEulerAngles
			: m_LeftHandIkNotReadyLocalEulerAngles;
	}

	public Vector3 ResolveVehicleLeftHandIkReadyLocalEulerAngles()
	{
		return HasVehicleLeftHandIkConfigured()
			? m_VehicleLeftHandIkReadyLocalEulerAngles
			: m_LeftHandIkReadyLocalEulerAngles;
	}

	/// <summary>Copies standing weapon pose + hand IK into vehicle fields (editor bootstrap).</summary>
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

		m_VehicleRightHandIkNotReadyLocalPosition = m_RightHandIkNotReadyLocalPosition;
		m_VehicleRightHandIkNotReadyLocalEulerAngles = m_RightHandIkNotReadyLocalEulerAngles;
		m_VehicleRightHandIkReadyLocalPosition = m_RightHandIkReadyLocalPosition;
		m_VehicleRightHandIkReadyLocalEulerAngles = m_RightHandIkReadyLocalEulerAngles;
		m_VehicleLeftHandIkNotReadyLocalPosition = m_LeftHandIkNotReadyLocalPosition;
		m_VehicleLeftHandIkNotReadyLocalEulerAngles = m_LeftHandIkNotReadyLocalEulerAngles;
		m_VehicleLeftHandIkReadyLocalPosition = m_LeftHandIkReadyLocalPosition;
		m_VehicleLeftHandIkReadyLocalEulerAngles = m_LeftHandIkReadyLocalEulerAngles;
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

	public bool HasForeGripIkConfigured(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => HasAnyLocalPoseData(m_ForeGrip1LeftHandIkNotReadyLocalPosition,
				m_ForeGrip1LeftHandIkNotReadyLocalEulerAngles,
				m_ForeGrip1LeftHandIkReadyLocalPosition,
				m_ForeGrip1LeftHandIkReadyLocalEulerAngles),
			2 => HasAnyLocalPoseData(m_ForeGrip2LeftHandIkNotReadyLocalPosition,
				m_ForeGrip2LeftHandIkNotReadyLocalEulerAngles,
				m_ForeGrip2LeftHandIkReadyLocalPosition,
				m_ForeGrip2LeftHandIkReadyLocalEulerAngles),
			3 => HasAnyLocalPoseData(m_ForeGrip3LeftHandIkNotReadyLocalPosition,
				m_ForeGrip3LeftHandIkNotReadyLocalEulerAngles,
				m_ForeGrip3LeftHandIkReadyLocalPosition,
				m_ForeGrip3LeftHandIkReadyLocalEulerAngles),
			4 => HasAnyLocalPoseData(m_ForeGrip4LeftHandIkNotReadyLocalPosition,
				m_ForeGrip4LeftHandIkNotReadyLocalEulerAngles,
				m_ForeGrip4LeftHandIkReadyLocalPosition,
				m_ForeGrip4LeftHandIkReadyLocalEulerAngles),
			5 => HasAnyLocalPoseData(m_ForeGrip5LeftHandIkNotReadyLocalPosition,
				m_ForeGrip5LeftHandIkNotReadyLocalEulerAngles,
				m_ForeGrip5LeftHandIkReadyLocalPosition,
				m_ForeGrip5LeftHandIkReadyLocalEulerAngles),
			_ => false
		};
	}

	public Vector3 GetForeGripLeftHandIkReadyLocalPosition(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => m_ForeGrip1LeftHandIkReadyLocalPosition,
			2 => m_ForeGrip2LeftHandIkReadyLocalPosition,
			3 => m_ForeGrip3LeftHandIkReadyLocalPosition,
			4 => m_ForeGrip4LeftHandIkReadyLocalPosition,
			5 => m_ForeGrip5LeftHandIkReadyLocalPosition,
			_ => Vector3.zero
		};
	}

	public Vector3 GetForeGripLeftHandIkReadyLocalEulerAngles(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => m_ForeGrip1LeftHandIkReadyLocalEulerAngles,
			2 => m_ForeGrip2LeftHandIkReadyLocalEulerAngles,
			3 => m_ForeGrip3LeftHandIkReadyLocalEulerAngles,
			4 => m_ForeGrip4LeftHandIkReadyLocalEulerAngles,
			5 => m_ForeGrip5LeftHandIkReadyLocalEulerAngles,
			_ => Vector3.zero
		};
	}

	public Quaternion GetForeGripLeftHandIkReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetForeGripLeftHandIkReadyLocalEulerAngles(_gripIndex));

	public Quaternion GetForeGripLeftHandIkNotReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetForeGripLeftHandIkNotReadyLocalEulerAngles(_gripIndex));

	public Vector3 GetForeGripLeftHandIkNotReadyLocalPosition(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => m_ForeGrip1LeftHandIkNotReadyLocalPosition,
			2 => m_ForeGrip2LeftHandIkNotReadyLocalPosition,
			3 => m_ForeGrip3LeftHandIkNotReadyLocalPosition,
			4 => m_ForeGrip4LeftHandIkNotReadyLocalPosition,
			5 => m_ForeGrip5LeftHandIkNotReadyLocalPosition,
			_ => Vector3.zero
		};
	}

	public Vector3 GetForeGripLeftHandIkNotReadyLocalEulerAngles(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => m_ForeGrip1LeftHandIkNotReadyLocalEulerAngles,
			2 => m_ForeGrip2LeftHandIkNotReadyLocalEulerAngles,
			3 => m_ForeGrip3LeftHandIkNotReadyLocalEulerAngles,
			4 => m_ForeGrip4LeftHandIkNotReadyLocalEulerAngles,
			5 => m_ForeGrip5LeftHandIkNotReadyLocalEulerAngles,
			_ => Vector3.zero
		};
	}

	public bool HasCrouchForeGripIkConfigured(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => HasAnyLocalPoseData(m_CrouchForeGrip1LeftHandIkNotReadyLocalPosition,
				m_CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles,
				m_CrouchForeGrip1LeftHandIkReadyLocalPosition, m_CrouchForeGrip1LeftHandIkReadyLocalEulerAngles),
			2 => HasAnyLocalPoseData(m_CrouchForeGrip2LeftHandIkNotReadyLocalPosition,
				m_CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles,
				m_CrouchForeGrip2LeftHandIkReadyLocalPosition, m_CrouchForeGrip2LeftHandIkReadyLocalEulerAngles),
			3 => HasAnyLocalPoseData(m_CrouchForeGrip3LeftHandIkNotReadyLocalPosition,
				m_CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles,
				m_CrouchForeGrip3LeftHandIkReadyLocalPosition, m_CrouchForeGrip3LeftHandIkReadyLocalEulerAngles),
			4 => HasAnyLocalPoseData(m_CrouchForeGrip4LeftHandIkNotReadyLocalPosition,
				m_CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles,
				m_CrouchForeGrip4LeftHandIkReadyLocalPosition, m_CrouchForeGrip4LeftHandIkReadyLocalEulerAngles),
			5 => HasAnyLocalPoseData(m_CrouchForeGrip5LeftHandIkNotReadyLocalPosition,
				m_CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles,
				m_CrouchForeGrip5LeftHandIkReadyLocalPosition, m_CrouchForeGrip5LeftHandIkReadyLocalEulerAngles),
			_ => false
		};
	}

	public bool HasVehicleForeGripIkConfigured(int _gripIndex)
	{
		return _gripIndex switch
		{
			1 => HasAnyLocalPoseData(m_VehicleForeGrip1LeftHandIkNotReadyLocalPosition,
				m_VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles,
				m_VehicleForeGrip1LeftHandIkReadyLocalPosition, m_VehicleForeGrip1LeftHandIkReadyLocalEulerAngles),
			2 => HasAnyLocalPoseData(m_VehicleForeGrip2LeftHandIkNotReadyLocalPosition,
				m_VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles,
				m_VehicleForeGrip2LeftHandIkReadyLocalPosition, m_VehicleForeGrip2LeftHandIkReadyLocalEulerAngles),
			3 => HasAnyLocalPoseData(m_VehicleForeGrip3LeftHandIkNotReadyLocalPosition,
				m_VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles,
				m_VehicleForeGrip3LeftHandIkReadyLocalPosition, m_VehicleForeGrip3LeftHandIkReadyLocalEulerAngles),
			4 => HasAnyLocalPoseData(m_VehicleForeGrip4LeftHandIkNotReadyLocalPosition,
				m_VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles,
				m_VehicleForeGrip4LeftHandIkReadyLocalPosition, m_VehicleForeGrip4LeftHandIkReadyLocalEulerAngles),
			5 => HasAnyLocalPoseData(m_VehicleForeGrip5LeftHandIkNotReadyLocalPosition,
				m_VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles,
				m_VehicleForeGrip5LeftHandIkReadyLocalPosition, m_VehicleForeGrip5LeftHandIkReadyLocalEulerAngles),
			_ => false
		};
	}

	public Vector3 GetCrouchForeGripLeftHandIkReadyLocalPosition(int _gripIndex) => _gripIndex switch
	{
		1 => m_CrouchForeGrip1LeftHandIkReadyLocalPosition, 2 => m_CrouchForeGrip2LeftHandIkReadyLocalPosition,
		3 => m_CrouchForeGrip3LeftHandIkReadyLocalPosition, 4 => m_CrouchForeGrip4LeftHandIkReadyLocalPosition,
		5 => m_CrouchForeGrip5LeftHandIkReadyLocalPosition, _ => Vector3.zero
	};

	public Vector3 GetCrouchForeGripLeftHandIkReadyLocalEulerAngles(int _gripIndex) => _gripIndex switch
	{
		1 => m_CrouchForeGrip1LeftHandIkReadyLocalEulerAngles, 2 => m_CrouchForeGrip2LeftHandIkReadyLocalEulerAngles,
		3 => m_CrouchForeGrip3LeftHandIkReadyLocalEulerAngles, 4 => m_CrouchForeGrip4LeftHandIkReadyLocalEulerAngles,
		5 => m_CrouchForeGrip5LeftHandIkReadyLocalEulerAngles, _ => Vector3.zero
	};

	public Quaternion GetCrouchForeGripLeftHandIkReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetCrouchForeGripLeftHandIkReadyLocalEulerAngles(_gripIndex));

	public Vector3 GetCrouchForeGripLeftHandIkNotReadyLocalPosition(int _gripIndex) => _gripIndex switch
	{
		1 => m_CrouchForeGrip1LeftHandIkNotReadyLocalPosition, 2 => m_CrouchForeGrip2LeftHandIkNotReadyLocalPosition,
		3 => m_CrouchForeGrip3LeftHandIkNotReadyLocalPosition, 4 => m_CrouchForeGrip4LeftHandIkNotReadyLocalPosition,
		5 => m_CrouchForeGrip5LeftHandIkNotReadyLocalPosition, _ => Vector3.zero
	};

	public Vector3 GetCrouchForeGripLeftHandIkNotReadyLocalEulerAngles(int _gripIndex) => _gripIndex switch
	{
		1 => m_CrouchForeGrip1LeftHandIkNotReadyLocalEulerAngles, 2 => m_CrouchForeGrip2LeftHandIkNotReadyLocalEulerAngles,
		3 => m_CrouchForeGrip3LeftHandIkNotReadyLocalEulerAngles, 4 => m_CrouchForeGrip4LeftHandIkNotReadyLocalEulerAngles,
		5 => m_CrouchForeGrip5LeftHandIkNotReadyLocalEulerAngles, _ => Vector3.zero
	};

	public Quaternion GetCrouchForeGripLeftHandIkNotReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetCrouchForeGripLeftHandIkNotReadyLocalEulerAngles(_gripIndex));

	public Vector3 GetVehicleForeGripLeftHandIkReadyLocalPosition(int _gripIndex) => _gripIndex switch
	{
		1 => m_VehicleForeGrip1LeftHandIkReadyLocalPosition, 2 => m_VehicleForeGrip2LeftHandIkReadyLocalPosition,
		3 => m_VehicleForeGrip3LeftHandIkReadyLocalPosition, 4 => m_VehicleForeGrip4LeftHandIkReadyLocalPosition,
		5 => m_VehicleForeGrip5LeftHandIkReadyLocalPosition, _ => Vector3.zero
	};

	public Vector3 GetVehicleForeGripLeftHandIkReadyLocalEulerAngles(int _gripIndex) => _gripIndex switch
	{
		1 => m_VehicleForeGrip1LeftHandIkReadyLocalEulerAngles, 2 => m_VehicleForeGrip2LeftHandIkReadyLocalEulerAngles,
		3 => m_VehicleForeGrip3LeftHandIkReadyLocalEulerAngles, 4 => m_VehicleForeGrip4LeftHandIkReadyLocalEulerAngles,
		5 => m_VehicleForeGrip5LeftHandIkReadyLocalEulerAngles, _ => Vector3.zero
	};

	public Quaternion GetVehicleForeGripLeftHandIkReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetVehicleForeGripLeftHandIkReadyLocalEulerAngles(_gripIndex));

	public Vector3 GetVehicleForeGripLeftHandIkNotReadyLocalPosition(int _gripIndex) => _gripIndex switch
	{
		1 => m_VehicleForeGrip1LeftHandIkNotReadyLocalPosition, 2 => m_VehicleForeGrip2LeftHandIkNotReadyLocalPosition,
		3 => m_VehicleForeGrip3LeftHandIkNotReadyLocalPosition, 4 => m_VehicleForeGrip4LeftHandIkNotReadyLocalPosition,
		5 => m_VehicleForeGrip5LeftHandIkNotReadyLocalPosition, _ => Vector3.zero
	};

	public Vector3 GetVehicleForeGripLeftHandIkNotReadyLocalEulerAngles(int _gripIndex) => _gripIndex switch
	{
		1 => m_VehicleForeGrip1LeftHandIkNotReadyLocalEulerAngles, 2 => m_VehicleForeGrip2LeftHandIkNotReadyLocalEulerAngles,
		3 => m_VehicleForeGrip3LeftHandIkNotReadyLocalEulerAngles, 4 => m_VehicleForeGrip4LeftHandIkNotReadyLocalEulerAngles,
		5 => m_VehicleForeGrip5LeftHandIkNotReadyLocalEulerAngles, _ => Vector3.zero
	};

	public Quaternion GetVehicleForeGripLeftHandIkNotReadyLocalRotation(int _gripIndex)
		=> Quaternion.Euler(GetVehicleForeGripLeftHandIkNotReadyLocalEulerAngles(_gripIndex));

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
