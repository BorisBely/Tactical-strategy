using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps NavMeshAgent locomotion from walking through VehicleBlocker hulls.
/// Agent.updatePosition=true bypasses PhysX; we resolve penetration after the agent moves.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class UnitVehicleBlockResolver : MonoBehaviour
{
	#region Constants
	private const string c_BlockerLayerName = "VehicleBlocker";
	private const int c_OverlapBufferSize = 8;
	private const float c_Skin = 0.04f;
	private const float c_MaxPushPerFrame = 0.55f;
	#endregion

	#region Static
	private static readonly Collider[] s_Overlap = new Collider[c_OverlapBufferSize];
	#endregion

	#region Serialized
	[SerializeField] private NavMeshAgent m_Agent;
	[SerializeField] private CapsuleCollider m_LocomotionCapsule;
	#endregion

	#region Runtime
	private int m_BlockerMask;
	private RtsUnitMember m_Unit;
	#endregion

	#region Public Properties
	public CapsuleCollider LocomotionCapsule => m_LocomotionCapsule;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Agent == null)
			m_Agent = GetComponent<NavMeshAgent>();
		m_Unit = GetComponent<RtsUnitMember>();
		EnsureLocomotionCapsule();
		UnitVehicleImpactReceiver.Ensure(gameObject);
		m_BlockerMask = LayerMask.GetMask(c_BlockerLayerName);
	}

	private void LateUpdate()
	{
		if (m_BlockerMask == 0 || m_Agent == null || !m_Agent.enabled || !m_Agent.isOnNavMesh)
			return;
		if (ShouldSkip())
			return;
		if (m_LocomotionCapsule == null || !m_LocomotionCapsule.enabled)
			return;

		ResolvePenetration();
	}
	#endregion

	#region Public Methods
	public static UnitVehicleBlockResolver Ensure(GameObject _unitRoot)
	{
		if (_unitRoot == null)
			return null;
		if (!_unitRoot.TryGetComponent(out UnitVehicleBlockResolver resolver))
			resolver = _unitRoot.AddComponent<UnitVehicleBlockResolver>();
		resolver.EnsureLocomotionCapsule();
		UnitVehicleImpactReceiver.Ensure(_unitRoot);
		return resolver;
	}
	#endregion

	#region Private Methods
	private void EnsureLocomotionCapsule()
	{
		if (m_LocomotionCapsule == null)
		{
			CapsuleCollider[] capsules = GetComponents<CapsuleCollider>();
			for (int i = 0; i < capsules.Length; i++)
			{
				CapsuleCollider c = capsules[i];
				if (c == null || c.isTrigger)
					continue;
				m_LocomotionCapsule = c;
				break;
			}

			if (m_LocomotionCapsule == null)
			{
				m_LocomotionCapsule = gameObject.AddComponent<CapsuleCollider>();
				m_LocomotionCapsule.radius = 0.22f;
				m_LocomotionCapsule.height = 1.82f;
				m_LocomotionCapsule.direction = 1;
				m_LocomotionCapsule.center = new Vector3(0f, 0.91f, 0f);
			}
		}

		m_LocomotionCapsule.isTrigger = false;
		m_LocomotionCapsule.enabled = true;
		// Only interact with VehicleBlocker — avoid fighting NavMeshAgent vs ground/props.
		int blocker = LayerMask.NameToLayer(c_BlockerLayerName);
		if (blocker >= 0)
		{
			m_LocomotionCapsule.includeLayers = 1 << blocker;
			m_LocomotionCapsule.excludeLayers = 0;
		}
	}

	private bool ShouldSkip()
	{
		if (GetComponentInParent<VehicleController>() != null)
			return true;

		if (TryGetComponent(out VehiclePassengerState passenger) && passenger.IsAttached)
			return true;

		if (m_Unit != null && m_Unit.transform.parent != null)
		{
			// Carried / attached — don't fight parenting.
			if (m_Unit.transform.parent.GetComponentInParent<RtsUnitMember>() != null)
				return true;
		}

		return false;
	}

	private void ResolvePenetration()
	{
		GetCapsuleWorld(out Vector3 p0, out Vector3 p1, out float radius);
		int count = Physics.OverlapCapsuleNonAlloc(
			p0, p1, radius, s_Overlap, m_BlockerMask, QueryTriggerInteraction.Ignore);
		if (count <= 0)
			return;

		Vector3 push = Vector3.zero;
		int hits = 0;
		VehicleUnitBlocker strongestBlocker = null;
		float strongestSpeedMs = 0f;
		Vector3 strongestNormal = transform.forward;

		for (int i = 0; i < count; i++)
		{
			Collider col = s_Overlap[i];
			if (col == null)
				continue;
			VehicleUnitBlocker blocker = col.GetComponentInParent<VehicleUnitBlocker>();
			if (blocker == null)
				continue;

			Vector3 capsuleCenter = (p0 + p1) * 0.5f;
			if (!Physics.ComputePenetration(
				    m_LocomotionCapsule, transform.position, transform.rotation,
				    col, col.transform.position, col.transform.rotation,
				    out Vector3 dir, out float dist))
				continue;

			dir.y = 0f;
			if (dir.sqrMagnitude < 1e-6f)
			{
				Vector3 away = capsuleCenter - col.bounds.center;
				away.y = 0f;
				dir = away.sqrMagnitude > 1e-6f ? away.normalized : transform.forward;
			}
			else
			{
				dir.Normalize();
			}

			push += dir * (dist + c_Skin);
			hits++;

			float speedMs = ResolveVehiclePlanarSpeedMs(blocker.Vehicle);
			if (blocker.Vehicle != null && speedMs >= strongestSpeedMs)
			{
				strongestSpeedMs = speedMs;
				strongestBlocker = blocker;
				strongestNormal = dir;
			}
		}

		if (hits <= 0 || push.sqrMagnitude < 1e-8f)
			return;

		push /= hits;
		if (push.magnitude > c_MaxPushPerFrame)
			push = push.normalized * c_MaxPushPerFrame;

		// Reliable impact path: kinematic hull often never raises OnCollision*, and
		// blocker OverlapBox can miss if speed reporting is stale. Penetration is certain.
		if (strongestBlocker != null && strongestBlocker.Vehicle != null && m_Unit != null &&
		    strongestSpeedMs >= 0.5f)
		{
			var hit = new VehicleUnitHitEvent(
				strongestBlocker.Vehicle,
				m_Unit,
				transform.position,
				strongestNormal,
				strongestSpeedMs,
				false);
			strongestBlocker.DispatchHit(in hit);
		}

		Vector3 next = transform.position + push;
		if (NavMesh.SamplePosition(next, out NavMeshHit navHit, 1.25f, m_Agent.areaMask))
			next = navHit.position;

		transform.position = next;
		m_Agent.nextPosition = next;

		// If still aiming through the hull, stop so the next order can repath around carving.
		if (m_Agent.hasPath)
		{
			Vector3 toDest = m_Agent.destination - next;
			toDest.y = 0f;
			if (toDest.sqrMagnitude > 0.25f &&
			    Vector3.Dot(toDest.normalized, push.normalized) < -0.2f)
				m_Agent.ResetPath();
		}
	}

	private static float ResolveVehiclePlanarSpeedMs(VehicleController _vehicle)
	{
		if (_vehicle == null)
			return 0f;

		float fromBrain = 0f;
		if (_vehicle.Brain != null)
			fromBrain = Mathf.Abs(_vehicle.Brain.CurrentSpeedKmh) / 3.6f;

		float fromBody = 0f;
		if (_vehicle.TryGetComponent(out Rigidbody body))
		{
			Vector3 flat = body.linearVelocity;
			flat.y = 0f;
			fromBody = flat.magnitude;
		}

		return Mathf.Max(fromBrain, fromBody);
	}

	private void GetCapsuleWorld(out Vector3 _p0, out Vector3 _p1, out float _radius)
	{
		Transform t = transform;
		float height = Mathf.Max(m_LocomotionCapsule.height, m_LocomotionCapsule.radius * 2f);
		_radius = m_LocomotionCapsule.radius * MaxAbsAxisScale(t);
		float half = height * 0.5f - m_LocomotionCapsule.radius;
		half = Mathf.Max(0f, half);
		Vector3 center = t.TransformPoint(m_LocomotionCapsule.center);
		Vector3 axis = t.up;
		_p0 = center + axis * half;
		_p1 = center - axis * half;
	}

	private static float MaxAbsAxisScale(Transform _t)
	{
		Vector3 s = _t.lossyScale;
		return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
	}
	#endregion
}
