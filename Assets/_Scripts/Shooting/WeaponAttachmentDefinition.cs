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
	[Tooltip("Слот на оружии: Muzzle, UnderBarrel, Rail или Optic (магазин отдельно).")]
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
	public float AimTimeModifier => m_AimTimeModifier;
	public float EffectiveRangeModifier => m_EffectiveRangeModifier;
	public float RecoilModifier => m_RecoilModifier;
	public float ReloadTimeModifier => m_ReloadTimeModifier;
	public float WearPerShotMultiplier => m_WearPerShotMultiplier;
	public float FoulingPerShotMultiplier => m_FoulingPerShotMultiplier;
	public float JamRiskModifier => m_JamRiskModifier;
	public AudioClip SuppressedFireSound => m_SuppressedFireSound;
	public GameObject EquippedVisualPrefab => m_EquippedVisualPrefab;
	#endregion
}
