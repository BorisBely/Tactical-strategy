using System;
using UnityEngine;

/// <summary>
/// Простая цель для hitscan: здоровье и события попадания. Повесь на корень объекта или на объект с коллайдером (ищется через GetComponentInParent).
/// Текущее HP сериализуется, чтобы в Play Mode оно отображалось и обновлялось в инспекторе.
/// </summary>
[DisallowMultipleComponent]
public sealed class DamageableTarget : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField, Min(0.01f)] private float m_MaxHealth = 100f;
	[SerializeField, Tooltip("Текущее здоровье. В начале сцены в Awake выставляется в Max Health.")]
	private float m_CurrentHealth = 100f;
	[SerializeField] private bool m_DestroyOnDeath;
	[SerializeField] private GameObject m_RootToDestroy;
	#endregion

	#region Public Properties
	public float MaxHealth => m_MaxHealth;
	public float CurrentHealth => m_CurrentHealth;
	public bool IsAlive => m_CurrentHealth > 0f;
	#endregion

	#region Events
	public event Action<DamageHitInfo> Damaged;
	public event Action<DamageHitInfo> Died;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		m_CurrentHealth = m_MaxHealth;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_MaxHealth = Mathf.Max(0.01f, m_MaxHealth);
		m_CurrentHealth = Mathf.Clamp(m_CurrentHealth, 0f, m_MaxHealth);
	}
#endif
	#endregion

	#region Public Methods
	public void ResetHealth()
	{
		m_CurrentHealth = m_MaxHealth;
	}

	public void SetMaxHealth(float _maxHealth, bool _resetCurrent = true)
	{
		m_MaxHealth = Mathf.Max(0.01f, _maxHealth);
		if (_resetCurrent)
			m_CurrentHealth = m_MaxHealth;
	}

	/// <summary>Нанести урон от выстрела. Точка и направление — в мировых координатах (для VFX).</summary>
	public void ApplyDamage(float _damage, Vector3 _hitPointWorld, Vector3 _hitNormalWorld, Vector3 _incomingDirection, AmmoDefinition _ammo)
	{
		ApplyDamage(_damage, _hitPointWorld, _hitNormalWorld, _incomingDirection, _ammo, null);
	}

	/// <summary>Нанести урон от выстрела с учётом опциональной зоны тела на коллайдере.</summary>
	public void ApplyDamage(
		float _damage,
		Vector3 _hitPointWorld,
		Vector3 _hitNormalWorld,
		Vector3 _incomingDirection,
		AmmoDefinition _ammo,
		Collider _hitCollider)
	{
		ApplyDamage(_damage, _hitPointWorld, _hitNormalWorld, _incomingDirection, _ammo, _hitCollider, out _, out _);
	}

	/// <summary>Нанести урон и вернуть назначенную травму (если на цели есть <see cref="UnitHealth"/>).</summary>
	public bool ApplyDamage(
		float _damage,
		Vector3 _hitPointWorld,
		Vector3 _hitNormalWorld,
		Vector3 _incomingDirection,
		AmmoDefinition _ammo,
		Collider _hitCollider,
		out InjuryUiEntry _resolvedInjury,
		out bool _armorFullyBlocked)
	{
		_resolvedInjury = default;
		_armorFullyBlocked = false;

		if (!IsAlive || _damage <= 0f)
			return false;

		UnitBodyHitZone hitZone = _hitCollider != null
			? _hitCollider.GetComponent<UnitBodyHitZone>() ?? _hitCollider.GetComponentInParent<UnitBodyHitZone>()
			: null;
		if (hitZone != null && _ammo != null)
		{
			if (hitZone.BodyPart == BodyPartType.Head)
			{
				UnitHeadEquipment headEquipment = GetComponent<UnitHeadEquipment>();
				if (headEquipment != null)
				{
					ArmorMitigationResult helmetMitigation = headEquipment.TryMitigateHeadBullet(_ammo);
					if (helmetMitigation.FullyBlocked)
					{
						_armorFullyBlocked = true;
						return false;
					}
				}
			}
			else if (TryGetComponent(out UnitArmor armor))
			{
				ArmorMitigationResult armorMitigation = armor.TryMitigateBullet(hitZone.BodyPart, _ammo);
				if (armorMitigation.FullyBlocked)
				{
					_armorFullyBlocked = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log(
						$"[Броня] {name} | попадание в {BodyPartTypeUtility.GetDisplayName(hitZone.BodyPart)} полностью заблокировано — травма не назначается",
						this);
#endif
					return false;
				}
			}
		}

		float damageMultiplier = hitZone != null ? hitZone.DamageMultiplier : 1f;
		float finalDamage = Mathf.Max(0f, _damage * damageMultiplier);
		if (finalDamage <= 0f)
			return false;

		UnitHealth unitHealth = GetComponent<UnitHealth>();
		bool injuryOnly = unitHealth != null;
		float applied;
		if (injuryOnly)
		{
			applied = finalDamage;
		}
		else
		{
			applied = Mathf.Min(finalDamage, m_CurrentHealth);
			m_CurrentHealth -= applied;
		}

		var info = new DamageHitInfo
		{
			Damage = applied,
			HitPointWorld = _hitPointWorld,
			HitNormalWorld = _hitNormalWorld,
			IncomingDirection = _incomingDirection,
			Ammo = _ammo,
			HitCollider = _hitCollider,
			BodyPart = hitZone != null ? hitZone.BodyPart : BodyPartType.Unknown,
			BodyZone = hitZone != null ? hitZone.Zone : CombatBodyZone.Unknown,
			RemainingHealth = injuryOnly ? m_CurrentHealth : Mathf.Max(0f, m_CurrentHealth)
		};

		if (hitZone != null)
			hitZone.ApplyConditionEffects(GetComponentInParent<UnitCombatCondition>(), applied);

		if (injuryOnly && TryGetComponent(out InjuryResolver injuryResolver))
			injuryResolver.TryApplyInjury(info, out _resolvedInjury);

		Damaged?.Invoke(info);

		if (!injuryOnly && m_CurrentHealth <= 0f)
		{
			Died?.Invoke(info);
			if (m_DestroyOnDeath)
			{
				GameObject root = m_RootToDestroy != null ? m_RootToDestroy : gameObject;
				Destroy(root);
			}
		}

		return true;
	}
	#endregion
}

/// <summary>Данные одного попадания по <see cref="DamageableTarget"/>.</summary>
public struct DamageHitInfo
{
	public float Damage;
	public Vector3 HitPointWorld;
	public Vector3 HitNormalWorld;
	public Vector3 IncomingDirection;
	public AmmoDefinition Ammo;
	public Collider HitCollider;
	public BodyPartType BodyPart;
	public CombatBodyZone BodyZone;
	public float RemainingHealth;
}
