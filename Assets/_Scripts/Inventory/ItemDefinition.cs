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

	[Header("Основное оружие (только если Category = Equipment)")]
	[Tooltip("Префаб модели в руке. Якорь — Main Hand в UnitEquipment. Для General не используется.")]
	[SerializeField] private GameObject m_EquippedVisualPrefab;
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
	public bool IsEquipment => m_Category == ItemCategory.Equipment;
	public GameObject DropWorldPrefab => m_DropWorldPrefab;
	#endregion
}
