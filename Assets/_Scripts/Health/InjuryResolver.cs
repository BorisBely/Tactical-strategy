using UnityEngine;

/// <summary>
/// Связывает попадания по <see cref="DamageableTarget"/> с локальными травмами в <see cref="UnitHealth"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageableTarget))]
public sealed class InjuryResolver : MonoBehaviour
{
	#region Private Fields
	[SerializeField] private UnitHealth m_UnitHealth;
	[SerializeField] private DamageableTarget m_DamageableTarget;
	[SerializeField] private UnitConsciousnessRules m_ConsciousnessRules;
	[SerializeField] private DamageSourceType m_DefaultDamageSource = DamageSourceType.Bullet;

	[Header("Debug")]
	[SerializeField] private bool m_LogInjuries = true;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		EnsureConsciousnessRules();
	}
	#endregion

	#region Public Methods
	public bool TryApplyInjury(DamageHitInfo _info, out InjuryUiEntry _injury)
	{
		_injury = default;

		if (m_UnitHealth == null)
		{
			ResolveReferences();
			if (m_UnitHealth == null)
				m_UnitHealth = GetComponentInParent<UnitHealth>();
		}

		if (m_UnitHealth == null)
		{
			LogInjury("травма не назначена: нет UnitHealth");
			return false;
		}

		EnsureConsciousnessRules();

		UnitBodyHitZone hitZone = ResolveHitZone(_info.HitCollider);
		DamageSourceType source = ResolveDamageSource(_info.Ammo);
		_injury = InjuryRollTable.ResolveFromHitZone(hitZone, source);
		m_UnitHealth.AddInjury(_injury);

		LogInjury(
			$"травма={_injury.StatusLocalizationKey} | priority={_injury.SortPriority} | " +
			$"всего={m_UnitHealth.InjuryCount} | зона={_info.BodyPart}");

		if (m_ConsciousnessRules == null)
		{
			LogInjury("правила бессознания не найдены — падение не проверяется");
			return true;
		}

		m_ConsciousnessRules.EvaluateAfterInjury(_info, _injury);
		return true;
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_DamageableTarget == null)
			m_DamageableTarget = GetComponent<DamageableTarget>();
		if (m_UnitHealth == null)
			m_UnitHealth = GetComponentInParent<UnitHealth>();
		if (m_ConsciousnessRules == null && m_UnitHealth != null)
			m_ConsciousnessRules = m_UnitHealth.GetComponent<UnitConsciousnessRules>();
	}

	private void EnsureConsciousnessRules()
	{
		if (m_ConsciousnessRules != null)
			return;

		ResolveReferences();
		if (m_UnitHealth == null)
			return;

		Transform healthRoot = m_UnitHealth.transform;
		m_ConsciousnessRules = healthRoot.GetComponent<UnitConsciousnessRules>();
		if (m_ConsciousnessRules != null)
			return;

		if (healthRoot.GetComponent<UnitConsciousness>() == null)
			return;

		m_ConsciousnessRules = healthRoot.gameObject.AddComponent<UnitConsciousnessRules>();
		LogInjury("добавлен UnitConsciousnessRules в runtime — обновите префаб через Polygone/Combat Balance");
	}

	private static UnitBodyHitZone ResolveHitZone(Collider _hitCollider)
	{
		if (_hitCollider == null)
			return null;

		return _hitCollider.GetComponent<UnitBodyHitZone>() ??
		       _hitCollider.GetComponentInParent<UnitBodyHitZone>();
	}

	private DamageSourceType ResolveDamageSource(AmmoDefinition _ammo)
	{
		return m_DefaultDamageSource;
	}

	private void LogInjury(string _message)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!m_LogInjuries)
			return;

		Debug.Log($"[Травма] {name} | {_message}", this);
#endif
	}
	#endregion
}
