using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Reacts to <see cref="VehicleUnitHitEvent"/>: NavMesh knockback + blunt trauma.
/// Never applies forces to the vehicle drive Rigidbody.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitVehicleImpactReceiver : MonoBehaviour, IVehicleUnitHitReceiver
{
	#region Constants
	private const float c_LightSpeedMinMs = 1.0f;
	private const float c_SevereSpeedMs = 3.3f;
	private const float c_ImpactCooldownSec = 0.75f;
	private const float c_LightPushMeters = 0.55f;
	private const float c_AgentPauseSec = 0.12f;
	private const float c_SevereImpulsePerMs = 1.15f;
	private const float c_SevereImpulseMin = 4.5f;
	private const float c_SevereImpulseMax = 14f;
	private const float c_NavSampleRadius = 1.25f;
	#endregion

	#region Serialized
	[SerializeField] private NavMeshAgent m_Agent;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitHealth m_UnitHealth;
	[SerializeField] private InjuryResolver m_InjuryResolver;
	[SerializeField] private UnitRagdollController m_Ragdoll;
	[SerializeField] private RtsUnitMember m_Unit;
	[SerializeField] private bool m_LogImpacts = true;
	#endregion

	#region Runtime
	private float m_NextImpactTime;
	private float m_AgentResumeTime = -1f;
	private bool m_AgentWasStopped;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void Update()
	{
		if (m_AgentResumeTime < 0f || m_Agent == null)
			return;
		if (Time.time < m_AgentResumeTime)
			return;

		m_AgentResumeTime = -1f;
		if (!m_Agent.enabled || !m_Agent.isOnNavMesh)
			return;
		if (!m_AgentWasStopped)
			m_Agent.isStopped = false;
	}
	#endregion

	#region Public Methods
	public static UnitVehicleImpactReceiver Ensure(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;
		if (!_unitRoot.TryGetComponent(out UnitVehicleImpactReceiver receiver))
			receiver = _unitRoot.AddComponent<UnitVehicleImpactReceiver>();
		receiver.ResolveReferences();
		return receiver;
	}

	public void OnVehicleUnitHit(in VehicleUnitHitEvent _hit)
	{
		if (_hit.Unit == null || _hit.Vehicle == null)
			return;
		if (_hit.Unit.gameObject != gameObject && _hit.Unit.transform != transform)
			return;

		ResolveReferences();
		if (ShouldIgnore(_hit))
		{
			LogImpact($"ignore unit={name} speed={_hit.RelativeSpeedMs:F2}");
			return;
		}

		float speedMs = Mathf.Max(0f, _hit.RelativeSpeedMs);
		if (speedMs < 0.05f)
			return;

		Vector3 pushDir = ResolvePushDirection(in _hit);
		if (pushDir.sqrMagnitude < 1e-6f)
			return;

		if (speedMs < c_LightSpeedMinMs)
		{
			ApplyNavMeshPush(pushDir, c_LightPushMeters * 0.45f);
			LogImpact($"soft-push speed={speedMs:F2}");
			return;
		}

		if (Time.time < m_NextImpactTime)
		{
			// Soft re-shove while pinned, no new trauma.
			ApplyNavMeshPush(pushDir, c_LightPushMeters * 0.35f);
			return;
		}

		m_NextImpactTime = Time.time + c_ImpactCooldownSec;

		if (speedMs >= c_SevereSpeedMs)
		{
			LogImpact($"SEVERE speed={speedMs:F2} (~{speedMs * 3.6f:F0} km/h)");
			ApplySevereImpact(in _hit, pushDir, speedMs);
		}
		else
		{
			LogImpact($"light speed={speedMs:F2} (~{speedMs * 3.6f:F0} km/h)");
			ApplyLightImpact(in _hit, pushDir);
		}
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_UnitHealth == null)
			m_UnitHealth = GetComponent<UnitHealth>();
		if (m_InjuryResolver == null)
			m_InjuryResolver = GetComponent<InjuryResolver>() ?? GetComponentInChildren<InjuryResolver>();
		if (m_Ragdoll == null)
			m_Ragdoll = GetComponent<UnitRagdollController>();
		if (m_Unit == null)
			m_Unit = GetComponent<RtsUnitMember>();
	}

	private bool ShouldIgnore(in VehicleUnitHitEvent _hit)
	{
		if (m_Consciousness != null && !m_Consciousness.IsConscious)
			return true;

		if (TryGetComponent(out VehiclePassengerState passenger) && passenger.IsAttached)
			return true;

		if (GetComponentInParent<VehicleController>() != null)
			return true;

		// Only the root locomotion capsule — bone/hitbox IgnoreCollision pairs must not
		// suppress vehicle trauma (boarding ignore is set on the locomotion capsule).
		VehicleUnitBlocker blocker = _hit.Vehicle != null ? _hit.Vehicle.UnitBlocker : null;
		if (blocker != null && blocker.BlockCollider != null)
		{
			CapsuleCollider loco = null;
			if (TryGetComponent(out UnitVehicleBlockResolver resolver))
				loco = resolver.LocomotionCapsule;
			if (loco == null)
				loco = GetComponent<CapsuleCollider>();
			if (loco != null && Physics.GetIgnoreCollision(blocker.BlockCollider, loco))
				return true;
		}

		return false;
	}

	private static Vector3 ResolvePushDirection(in VehicleUnitHitEvent _hit)
	{
		Vector3 dir = Vector3.zero;
		if (_hit.Vehicle != null)
		{
			if (_hit.Vehicle.TryGetComponent(out Rigidbody body))
			{
				dir = body.linearVelocity;
				dir.y = 0f;
			}

			if (dir.sqrMagnitude < 0.05f && _hit.Vehicle.Brain != null)
			{
				float kmh = _hit.Vehicle.Brain.CurrentSpeedKmh;
				if (Mathf.Abs(kmh) > 0.2f)
					dir = _hit.Vehicle.transform.forward * Mathf.Sign(kmh);
			}
		}

		if (dir.sqrMagnitude < 1e-4f)
		{
			dir = -_hit.ContactNormal;
			dir.y = 0f;
		}

		if (dir.sqrMagnitude < 1e-4f && _hit.Unit != null && _hit.Vehicle != null)
		{
			dir = _hit.Unit.transform.position - _hit.Vehicle.transform.position;
			dir.y = 0f;
		}

		return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
	}

	private void ApplyLightImpact(in VehicleUnitHitEvent _hit, Vector3 _pushDir)
	{
		ApplyNavMeshPush(_pushDir, c_LightPushMeters);
		PauseAgentBriefly();

		DamageHitInfo hitInfo = BuildHitInfo(in _hit, _pushDir, BodyPartType.Chest, _severe: false);
		if (m_InjuryResolver != null)
			m_InjuryResolver.TryApplyBluntInjury(hitInfo, _severe: false, out _);
		else if (m_UnitHealth != null)
			m_UnitHealth.AddInjury(InjuryRollTable.RollBlunt(BodyPartType.Chest, _severe: false));
	}

	private void ApplySevereImpact(in VehicleUnitHitEvent _hit, Vector3 _pushDir, float _speedMs)
	{
		BodyPartType primary = ResolveContactBodyPart(in _hit);
		DamageHitInfo hitInfo = BuildHitInfo(in _hit, _pushDir, primary, _severe: true);

		InjuryUiEntry last = default;
		if (m_InjuryResolver != null)
		{
			m_InjuryResolver.TryApplyBluntInjury(hitInfo, _severe: true, out last);
			if (Random.value < 0.45f && m_UnitHealth != null)
			{
				BodyPartType secondary = primary == BodyPartType.Chest || primary == BodyPartType.Abdomen
					? (Random.value < 0.5f ? BodyPartType.LeftLeg : BodyPartType.RightLeg)
					: BodyPartType.Chest;
				m_UnitHealth.AddInjury(InjuryRollTable.RollBlunt(secondary, _severe: true));
			}
		}
		else if (m_UnitHealth != null)
		{
			last = InjuryRollTable.RollBlunt(primary, _severe: true);
			m_UnitHealth.AddInjury(last);
			UnitConsciousnessRules rules = GetComponent<UnitConsciousnessRules>();
			rules?.EvaluateAfterInjury(hitInfo, last);
		}

		UnitRagdollController.RagdollFallProfile fallProfile =
			Mathf.Abs(Vector3.Dot(_pushDir, transform.forward)) < 0.3f
				? UnitRagdollController.RagdollFallProfile.SideSpin
				: UnitRagdollController.RagdollFallProfile.BackwardKnockback;

		if (m_Consciousness != null && m_Consciousness.IsConscious)
			m_Consciousness.EnterUnconscious(hitInfo, fallProfile);

		float impulseMag = Mathf.Clamp(_speedMs * c_SevereImpulsePerMs, c_SevereImpulseMin, c_SevereImpulseMax);
		Vector3 extraImpulse = _pushDir * impulseMag + Vector3.up * (impulseMag * 0.12f);
		if (m_Ragdoll != null)
			m_Ragdoll.SetRagdollActive(true, extraImpulse);
	}

	private static BodyPartType ResolveContactBodyPart(in VehicleUnitHitEvent _hit)
	{
		if (_hit.Unit == null)
			return BodyPartType.Chest;

		float localY = _hit.ContactPoint.y - _hit.Unit.transform.position.y;
		if (localY < 0.55f)
			return Random.value < 0.5f ? BodyPartType.LeftLeg : BodyPartType.RightLeg;
		if (localY > 1.45f)
			return BodyPartType.Head;
		return Random.value < 0.35f ? BodyPartType.Abdomen : BodyPartType.Chest;
	}

	private static DamageHitInfo BuildHitInfo(
		in VehicleUnitHitEvent _hit,
		Vector3 _pushDir,
		BodyPartType _bodyPart,
		bool _severe)
	{
		return new DamageHitInfo
		{
			Damage = _severe ? 40f : 8f,
			HitPointWorld = _hit.ContactPoint,
			HitNormalWorld = _hit.ContactNormal,
			IncomingDirection = _pushDir,
			Ammo = null,
			HitCollider = null,
			BodyPart = _bodyPart,
			BodyZone = BodyPartTypeUtility.ToCombatBodyZone(_bodyPart),
			RemainingHealth = 0f
		};
	}

	private void ApplyNavMeshPush(Vector3 _pushDir, float _distance)
	{
		Vector3 target = transform.position + _pushDir * _distance;
		if (NavMesh.SamplePosition(target, out NavMeshHit hit, c_NavSampleRadius, NavMesh.AllAreas))
			target = hit.position;
		else
			target.y = transform.position.y;

		if (m_Agent != null && m_Agent.enabled && m_Agent.isOnNavMesh)
		{
			m_Agent.Warp(target);
			m_Agent.nextPosition = target;
		}
		else
		{
			transform.position = target;
		}
	}

	private void PauseAgentBriefly()
	{
		if (m_Agent == null || !m_Agent.enabled || !m_Agent.isOnNavMesh)
			return;

		m_AgentWasStopped = m_Agent.isStopped;
		m_Agent.isStopped = true;
		m_AgentResumeTime = Time.time + c_AgentPauseSec;
	}

	private void LogImpact(string _message)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!m_LogImpacts)
			return;
		Debug.Log($"[VehicleImpact] {name} | {_message}", this);
#endif
	}
	#endregion
}
