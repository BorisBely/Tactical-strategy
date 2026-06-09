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
	[SerializeField] private DamageSourceType m_DefaultDamageSource = DamageSourceType.Bullet;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
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
			return false;

		UnitBodyHitZone hitZone = ResolveHitZone(_info.HitCollider);
		DamageSourceType source = ResolveDamageSource(_info.Ammo);
		_injury = InjuryRollTable.ResolveFromHitZone(hitZone, source);
		m_UnitHealth.AddInjury(_injury);
		return true;
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_DamageableTarget == null)
			m_DamageableTarget = GetComponent<DamageableTarget>();
		if (m_UnitHealth == null)
			m_UnitHealth = GetComponent<UnitHealth>();
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
	#endregion
}
