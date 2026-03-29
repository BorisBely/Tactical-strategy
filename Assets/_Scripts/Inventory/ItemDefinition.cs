using UnityEngine;

/// <summary>
/// Статические данные предмета. Один asset на тип предмета.
/// В мире тот же предмет может выглядеть по-разному: задайте отдельные префабы лута (меш + коллайдер),
/// ссылаясь на один и тот же ItemDefinition.
/// </summary>
[CreateAssetMenu(fileName = "ItemDefinition", menuName = "Polygone/Inventory/Item Definition", order = 0)]
public class ItemDefinition : ScriptableObject
{
	#region Serialized Fields
	[SerializeField] private string m_DisplayName = "Предмет";
	[TextArea(2, 6)]
	[SerializeField] private string m_Description;
	[SerializeField] private Sprite m_Icon;

	[Header("Тип")]
	[SerializeField] private ItemCategory m_Category = ItemCategory.General;

	[Header("Выброс из инвентаря на землю")]
	[Tooltip("Спавн перед юнитом при переносе слота на панель «земля». Нужны Collider и при необходимости Rigidbody на префабе.")]
	[SerializeField] private GameObject m_DropWorldPrefab;

	[Header("Снаряжение (Category = Equipment)")]
	[Tooltip("Префаб модели в правой руке (без физики лута). Родитель — якорь правой руки в UnitEquipment.")]
	[SerializeField] private GameObject m_EquippedVisualPrefab;
	[Tooltip("Локальная позиция префаба оружия относительно правой руки.")]
	[SerializeField] private Vector3 m_RightHandLocalPosition;
	[Tooltip("Локальные углы Эйлера оружия относительно правой руки.")]
	[SerializeField] private Vector3 m_RightHandLocalEulerAngles;
	[Tooltip("Имя дочернего объекта на префабе оружия: мировая позиция/поворот для IK левой кисти. Пусто — левая рука без IK.")]
	[SerializeField] private string m_LeftHandIkTargetChildName = "LeftHandIkTarget";
	[Header("Анимация (Equipment)")]
	[Tooltip("Какую ветку локомоции включать при экипировке этого предмета. Должен совпадать с переходами по WeaponMode в Animator.")]
	[SerializeField] private LocomotionWeaponMode m_LocomotionWeaponMode = LocomotionWeaponMode.Rifle;
	#endregion

	#region Public Properties
	public string DisplayName => m_DisplayName;
	public string Description => m_Description;
	public Sprite Icon => m_Icon;
	public ItemCategory Category => m_Category;
	/// <summary>Equipment сейчас всегда основная рука; другие слоты появятся позже.</summary>
	public EquipmentSlotType EquipmentSlot =>
		m_Category == ItemCategory.Equipment ? EquipmentSlotType.MainHand : EquipmentSlotType.None;
	public GameObject EquippedVisualPrefab => m_EquippedVisualPrefab;
	public Vector3 RightHandLocalPosition => m_RightHandLocalPosition;
	public Quaternion RightHandLocalRotation => Quaternion.Euler(m_RightHandLocalEulerAngles);
	public string LeftHandIkTargetChildName => m_LeftHandIkTargetChildName;
	public bool IsEquipment => m_Category == ItemCategory.Equipment;
	public GameObject DropWorldPrefab => m_DropWorldPrefab;
	/// <summary>Режим аниматора при экипировке; для не-Equipment не используется.</summary>
	public LocomotionWeaponMode LocomotionWeaponMode => m_LocomotionWeaponMode;
	#endregion
}
