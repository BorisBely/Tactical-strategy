using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Данные патрона. Именно патрон задаёт поражающие свойства и модификаторы выстрела.
/// </summary>
[CreateAssetMenu(fileName = "AmmoDefinition", menuName = "Polygone/Shooting/Ammo Definition", order = 11)]
public sealed class AmmoDefinition : ScriptableObject
{
	#region Private Fields
	[Header("Identity")]
	[Tooltip("Калибр этого патрона. Должен совпадать с калибром оружия и магазина.")]
	[SerializeField] private CaliberType m_Caliber = CaliberType.None;

	[Header("Damage")]
	[Tooltip("Базовое поражающее действие патрона по незащищённой цели.")]
	[SerializeField, Min(0f)] private float m_BaseDamage = 20f;
	[Tooltip("Пробивное действие патрона.")]
	[SerializeField, Min(0f)] private float m_Penetration = 10f;
	[Tooltip("Насколько сильно патрон повреждает броню как объект.")]
	[SerializeField, Min(0f)] private float m_ArmorDamage = 5f;

	[Header("Ballistics")]
	[Tooltip("Количество поражающих элементов за один выстрел. Для обычной пули 1, для дроби больше 1.")]
	[SerializeField, Min(1)] private int m_ProjectileCount = 1;
	[Tooltip("Начальная скорость поражающего элемента.")]
	[SerializeField, Min(0.1f)] private float m_Velocity = 400f;
	[Tooltip("Эффективная дальность самого патрона до сильного ухудшения характеристик.")]
	[SerializeField, Min(0.1f)] private float m_EffectiveRangeMeters = 100f;

	[Header("Audio")]
	[Tooltip("Профиль вариантов звука выстрела. Если заполнен — заменяет профиль оружия / глушителя.")]
	[SerializeField] private WeaponFireSoundProfile m_FireSoundOverrideProfile = new WeaponFireSoundProfile();
	[Tooltip("Субзвуковой патрон: при выстреле с глушителем громкость клипа глушителя умножается на коэффициент (см. UnitWeaponFireAudio).")]
	[SerializeField] private bool m_IsSubsonic;

	[Header("Гильза (выброс после выстрела)")]
	[Tooltip("Корень префаба: Rigidbody + Collider + ShellCasingBehaviour на одном объекте (можно дочерний к корню префаба).")]
	[SerializeField] private GameObject m_ShellPrefab;
	[SerializeField, Min(0.1f)] private float m_ShellEjectSpeed = 5.5f;
	[SerializeField, Min(0f)] private float m_ShellEjectSpeedVariance = 0.75f;
	[SerializeField] private float m_ShellEjectUpSpeed = 1.2f;
	[SerializeField, Min(0f)] private float m_ShellAngularVelocity = 18f;
	[Tooltip("Если 0 — звук при ударе с любого слоя. Иначе только если слой объекта входит в маску.")]
	[SerializeField] private LayerMask m_ShellImpactLayers;
	[Tooltip("Минимальная относительная скорость удара для звука (м/с). 0 — без порога.")]
	[SerializeField, Min(0f)] private float m_ShellImpactMinSpeed = 0.35f;
	[SerializeField] private AudioClip[] m_ShellImpactSounds;
	[SerializeField, Range(0f, 1f)] private float m_ShellImpactVolume = 0.55f;
	[SerializeField, Min(0.05f)] private float m_ShellLifetimeAfterImpactSeconds = 3f;
	[Tooltip("Если за это время не было удара — гильза возвращается в пул (застряла, улетела).")]
	[SerializeField, Min(0.5f)] private float m_ShellMaxAirborneSeconds = 12f;

	[Header("Shot Modifiers")]
	[Tooltip("Как этот патрон меняет разброс текущего выстрела.")]
	[SerializeField, Min(0f)] private float m_SpreadModifier = 1f;
	[Tooltip("Как этот патрон меняет накопление отдачи оружия.")]
	[SerializeField, Min(0f)] private float m_RecoilModifier = 1f;

	[Header("Weapon Condition (за один выстрел)")]
	[Tooltip("Базовые единицы износа за выстрел; итог делится на Base Durability оружия и умножается на модули.")]
	[FormerlySerializedAs("m_WearModifier")]
	[SerializeField, Min(0f)] private float m_WearPerShot = 1f;
	[Tooltip("Базовые единицы загрязнения за выстрел; итог делится на Base Fouling Budget оружия и умножается на модули (макс. 100%%).")]
	[FormerlySerializedAs("m_FoulingModifier")]
	[SerializeField, Min(0f)] private float m_FoulingPerShot = 1f;
	[Tooltip("Множитель вероятности клина с этого выстрела (оба канала: износ и загрязнение).")]
	[SerializeField, Min(0f)] private float m_JamRiskModifier = 1f;
	#endregion

	#region Public Properties
	public CaliberType Caliber => m_Caliber;
	public WeaponFireSoundProfile FireSoundOverrideProfile => m_FireSoundOverrideProfile;
	public bool IsSubsonic => m_IsSubsonic;
	public bool HasShellPrefab => m_ShellPrefab != null;
	public GameObject ShellPrefab => m_ShellPrefab;
	public float ShellEjectSpeed => m_ShellEjectSpeed;
	public float ShellEjectSpeedVariance => m_ShellEjectSpeedVariance;
	public float ShellEjectUpSpeed => m_ShellEjectUpSpeed;
	public float ShellAngularVelocity => m_ShellAngularVelocity;
	public int ShellImpactMaskBits => m_ShellImpactLayers.value;
	public float ShellImpactMinSpeedSqr => m_ShellImpactMinSpeed * m_ShellImpactMinSpeed;
	public float ShellImpactVolume => m_ShellImpactVolume;
	public float ShellLifetimeAfterImpactSeconds => m_ShellLifetimeAfterImpactSeconds;
	public float ShellMaxAirborneSeconds => m_ShellMaxAirborneSeconds;
	public float BaseDamage => m_BaseDamage;
	public float Penetration => m_Penetration;
	public float ArmorDamage => m_ArmorDamage;
	public int ProjectileCount => m_ProjectileCount;
	public float Velocity => m_Velocity;
	public float EffectiveRangeMeters => m_EffectiveRangeMeters;
	public float SpreadModifier => m_SpreadModifier;
	public float RecoilModifier => m_RecoilModifier;
	public float WearPerShot => m_WearPerShot;
	public float FoulingPerShot => m_FoulingPerShot;
	public float JamRiskModifier => m_JamRiskModifier;
	#endregion

	#region Public Methods
	public bool TryPickShellImpactSound(out AudioClip _clip, out float _volume)
	{
		_clip = null;
		_volume = m_ShellImpactVolume;
		if (m_ShellImpactSounds == null || m_ShellImpactSounds.Length == 0)
			return false;

		int validCount = 0;
		for (int i = 0; i < m_ShellImpactSounds.Length; i++)
		{
			if (m_ShellImpactSounds[i] != null)
				validCount++;
		}

		if (validCount == 0)
			return false;

		int pick = Random.Range(0, validCount);
		for (int i = 0; i < m_ShellImpactSounds.Length; i++)
		{
			if (m_ShellImpactSounds[i] == null)
				continue;
			if (pick == 0)
			{
				_clip = m_ShellImpactSounds[i];
				return true;
			}
			pick--;
		}

		return false;
	}
	#endregion
}
