using UnityEngine;

/// <summary>
/// Данные модуля оружия. Пока это только data-слой с модификаторами, без runtime-логики установки.
/// </summary>
[CreateAssetMenu(fileName = "WeaponAttachmentDefinition", menuName = "Polygone/Shooting/Weapon Attachment Definition", order = 13)]
public sealed class WeaponAttachmentDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Категория модуля: прицел, надульник, ЛЦУ, рукоятка или приклад.")]
	[SerializeField] private WeaponAttachmentType m_AttachmentType = WeaponAttachmentType.Optic;
	[Tooltip("Слот оружия, в который этот модуль может быть установлен.")]
	[SerializeField] private WeaponAttachmentSlotType m_RequiredSlot = WeaponAttachmentSlotType.Optic;

	[Header("Modifiers")]
	[Tooltip("Как модуль меняет скорость прицеливания. Значение меньше 1 ускоряет, больше 1 замедляет.")]
	[SerializeField, Min(0f)] private float m_AimTimeModifier = 1f;
	[Tooltip("Как модуль меняет эффективную дальность оружия.")]
	[SerializeField, Min(0f)] private float m_EffectiveRangeModifier = 1f;
	[Tooltip("Как модуль меняет накопление отдачи.")]
	[SerializeField, Min(0f)] private float m_RecoilModifier = 1f;
	[Tooltip("Как модуль меняет скорость смены магазина в оружии.")]
	[SerializeField, Min(0f)] private float m_ReloadTimeModifier = 1f;
	#endregion

	#region Public Properties
	public WeaponAttachmentType AttachmentType => m_AttachmentType;
	public WeaponAttachmentSlotType RequiredSlot => m_RequiredSlot;
	public float AimTimeModifier => m_AimTimeModifier;
	public float EffectiveRangeModifier => m_EffectiveRangeModifier;
	public float RecoilModifier => m_RecoilModifier;
	public float ReloadTimeModifier => m_ReloadTimeModifier;
	#endregion
}
