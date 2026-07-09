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
	#endregion
}
