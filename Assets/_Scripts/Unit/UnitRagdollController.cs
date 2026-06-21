using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-20)]
public sealed class UnitRagdollController : MonoBehaviour
{
	#region Types
	public enum RagdollFallProfile
	{
		HeavyDrop,
		ForwardCollapse,
		BackwardKnockback,
		SideSpin,
		LegBuckle
	}

	[Serializable]
	private sealed class BonePose
	{
		public Transform Transform;
		public Vector3 LocalPosition;
		public Quaternion LocalRotation;
	}

	private struct JointDragState
	{
		public Joint Joint;
		public Rigidbody ConnectedBody;
	}

	private struct CollisionDetectionRestore
	{
		public Rigidbody Body;
		public CollisionDetectionMode Mode;
	}

	private struct RigidbodyInterpolationRestore
	{
		public Rigidbody Body;
		public RigidbodyInterpolation Interpolation;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private NavMeshAgent m_NavMeshAgent;
	[SerializeField] private Transform m_RootBone;
	[SerializeField] private bool m_StartInRagdoll;
	[SerializeField] private bool m_KeepCombatCollidersEnabled = true;

	[Header("Fall Impulse")]
	[SerializeField, Min(0f)] private float m_DefaultImpulse = 5f;
	[SerializeField, Min(0f)] private float m_DefaultUpImpulse = 0.5f;
	[SerializeField, Min(0f)] private float m_HitBoneImpulseMultiplier = 1f;
	[SerializeField, Min(0f)] private float m_RootFollowThroughMultiplier = 0.5f;
	[SerializeField, Range(0f, 1f)] private float m_RandomImpulseVariance = 0.3f;
	[SerializeField, Min(0f)] private float m_RandomSideImpulse = 0.3f;

	[Header("Ragdoll Stability")]
	[SerializeField] private bool m_IgnoreSelfCollision = true;
	[SerializeField, Min(0f)] private float m_RagdollLinearDamping = 0.05f;
	[SerializeField, Min(0f)] private float m_RagdollAngularDamping = 0.1f;
	[SerializeField, Min(0.1f)] private float m_MaxRagdollAngularSpeed = 5f;
	[SerializeField, Min(1f)] private float m_DragLinearDampingMultiplier = 2f;
	[SerializeField, Min(1f)] private float m_DragAngularDampingMultiplier = 1.6f;
	[SerializeField, Min(0f)] private float m_AngularDecayPerSecond = 10f;
	[SerializeField, Min(0f)] private float m_SettleDelay = 0.7f;
	[SerializeField, Min(0f)] private float m_SettleRequiredSeconds = 0.35f;
	[SerializeField, Min(0f)] private float m_SleepLinearSpeed = 0.05f;
	[SerializeField, Min(0f)] private float m_SleepAngularSpeed = 0.1f;
	[SerializeField] private bool m_MakeKinematicWhenSettled = true;

	[Header("Weapon During Ragdoll")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField, Min(0f)] private float m_WeaponDropSideOffset = 0.18f;
	[SerializeField, Min(0f)] private float m_WeaponDropDownOffset = 0.05f;
	[SerializeField, Min(0f)] private float m_WeaponDropImpulse = 0.8f;

	[Header("Transition Blend")]
	[SerializeField, Min(0f)] private float m_TransitionBlendDuration = 0.4f;
	[SerializeField, Min(0f)] private float m_TransitionLinearDamping = 0.8f;
	[SerializeField, Min(0f)] private float m_TransitionAngularDamping = 3f;
	[SerializeField, Min(0f)] private float m_MaxAnimationVelocity = 10f;

	[Header("Fall Torque")]
	[SerializeField, Min(0f)] private float m_FallTorqueMultiplier = 0.3f;
	[SerializeField, Min(0f)] private float m_HitBodyTorqueMultiplier = 0.5f;

	[Header("Soft Settle")]
	[SerializeField, Min(0f)] private float m_SoftSettleDuration = 0.6f;
	[SerializeField, Min(0f)] private float m_SoftSettleLinearDamping = 50f;
	[SerializeField, Min(0f)] private float m_SoftSettleAngularDamping = 200f;

	[Header("Debug")]
	[SerializeField] private bool m_LogImpulse;
	#endregion

	#region Private Fields
	private readonly List<Rigidbody> m_Rigidbodies = new List<Rigidbody>(24);
	private readonly List<Collider> m_CombatColliders = new List<Collider>(24);
	private readonly List<Collider> m_RagdollColliders = new List<Collider>(24);
	private readonly List<BonePose> m_InitialPoses = new List<BonePose>(64);
	private readonly List<JointDragState> m_DisabledJointsForDrag = new List<JointDragState>(24);
	private readonly List<CollisionDetectionRestore> m_CollisionDetectionRestore = new List<CollisionDetectionRestore>(24);
	private readonly List<RigidbodyInterpolationRestore> m_InterpolationRestore = new List<RigidbodyInterpolationRestore>(24);
	private bool m_IsRagdollActive;
	private bool m_HasCached;
	private bool m_IsRagdollSettled;
	private bool m_SuppressAutoSettle;
	private float m_RagdollActivatedAt;
	private float m_SettleCandidateStartedAt = -1f;
	private float m_TransitionBlendStartedAt = -1f;
	private float m_SoftSettleStartedAt = -1f;
	private UnitWeaponAiming m_WeaponAiming;
	private UnitWeaponVisualRecoilKick m_WeaponVisualRecoilKick;
	private AnimatorHandIk m_HandIk;
	private bool m_WeaponAimingWasEnabled;
	private bool m_WeaponVisualRecoilKickWasEnabled;
	private bool m_HandIkWasEnabled;
	private bool m_WeaponDetachedOnKnockout;
	private Vector3[] m_BonePositionsPrevious;
	private bool m_HasBonePreviousPositions;
	#endregion

	#region Public Properties
	public bool IsRagdollActive => m_IsRagdollActive;
	public bool IsRagdollSettled => m_IsRagdollSettled;
	public bool ShouldBlockWeaponPoseScripts => m_IsRagdollActive;
	public Transform RootBone => m_RootBone != null ? m_RootBone : transform;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		CacheReferences();
		CacheInitialPoses();
		SetRagdollActive(m_StartInRagdoll);
	}

	private void FixedUpdate()
	{
		if (!m_IsRagdollActive || m_IsRagdollSettled || m_SuppressAutoSettle)
			return;

		UpdateTransitionBlend();
		UpdateSoftSettle();

		if (m_TransitionBlendStartedAt >= 0f || m_SoftSettleStartedAt >= 0f)
			return;

		DecayRagdollAngularVelocity();
		UpdateRagdollSettling();
	}

	private void LateUpdate()
	{
		if (!m_IsRagdollActive && m_HasCached)
			CaptureBonePositions();
	}
	#endregion

	#region Public Methods
	public void SetRagdollActive(bool _active)
	{
		SetRagdollActive(_active, Vector3.zero);
	}

	public void SetRagdollActive(bool _active, Vector3 _impulse)
	{
		SetRagdollActive(_active, _impulse, _applyImpulseOnActivate: _active);
	}

	public void SetRagdollActive(bool _active, DamageHitInfo _hitInfo, RagdollFallProfile _fallProfile)
	{
		CacheReferences();

		bool wasActive = m_IsRagdollActive;
		SetRagdollActive(_active, Vector3.zero, _applyImpulseOnActivate: false);

		if (_active && !wasActive)
			ApplyHitImpulse(_hitInfo, _fallProfile);
	}

	private void SetRagdollActive(bool _active, Vector3 _impulse, bool _applyImpulseOnActivate)
	{
		CacheReferences();

		if (m_IsRagdollActive == _active)
		{
			if (_active && _applyImpulseOnActivate)
				ApplyImpulse(_impulse);
			return;
		}

		m_IsRagdollActive = _active;
		m_IsRagdollSettled = false;
		m_SettleCandidateStartedAt = -1f;
		m_TransitionBlendStartedAt = -1f;
		m_SoftSettleStartedAt = -1f;
		if (_active)
		{
			m_RagdollActivatedAt = Time.time;
			m_TransitionBlendStartedAt = Time.time;
			if (m_LogImpulse)
				Debug.Log($"[Ragdoll] {name} | АКТИВАЦИЯ | time={Time.time:F2}", this);
		}

		float animDt = Time.deltaTime;
		Vector3 navVelocity = m_NavMeshAgent != null && m_NavMeshAgent.enabled && m_NavMeshAgent.isOnNavMesh
			? m_NavMeshAgent.velocity
			: Vector3.zero;

		if (m_Animator != null)
			m_Animator.enabled = !_active;

		if (m_NavMeshAgent != null)
		{
			if (_active)
			{
				if (m_NavMeshAgent.enabled && m_NavMeshAgent.isOnNavMesh)
				{
					m_NavMeshAgent.isStopped = true;
					m_NavMeshAgent.ResetPath();
				}
				m_NavMeshAgent.enabled = false;
			}
			else
			{
				m_NavMeshAgent.enabled = true;
			}
		}

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			if (_active)
			{
				body.isKinematic = false;
				body.useGravity = true;
				body.linearDamping = m_TransitionBlendDuration > 0f ? m_TransitionLinearDamping : m_RagdollLinearDamping;
				body.angularDamping = m_TransitionBlendDuration > 0f ? m_TransitionAngularDamping : m_RagdollAngularDamping;
				body.maxAngularVelocity = m_MaxRagdollAngularSpeed;
				body.sleepThreshold = 0.08f;

				Vector3 animVelocity = Vector3.zero;
				if (m_HasBonePreviousPositions && m_BonePositionsPrevious != null && i < m_BonePositionsPrevious.Length)
				{
					animVelocity = (body.position - m_BonePositionsPrevious[i]) / animDt;
					if (animVelocity.sqrMagnitude > m_MaxAnimationVelocity * m_MaxAnimationVelocity)
						animVelocity = animVelocity.normalized * m_MaxAnimationVelocity;
				}
				else if (navVelocity.sqrMagnitude > 0.0001f)
				{
					animVelocity = navVelocity;
				}

				body.linearVelocity = animVelocity;
				body.angularVelocity = Vector3.zero;
				body.WakeUp();
			}
			else
			{
				if (!body.isKinematic)
				{
					body.linearVelocity = Vector3.zero;
					body.angularVelocity = Vector3.zero;
				}

				body.isKinematic = true;
				body.useGravity = false;
				body.Sleep();
			}
		}

		SetCombatCollidersEnabled(m_KeepCombatCollidersEnabled);

		if (_active)
		{
			FreezeWeaponControl();
			if (_applyImpulseOnActivate)
				ApplyImpulse(_impulse);
		}
		else
		{
			RestoreWeaponToHand();
			m_WeaponDetachedOnKnockout = false;
			AlignRootToRagdollPose();
			RestoreInitialPose();
		}

		RefreshVisionHitZones();
	}

	public void SetCombatCollidersEnabled(bool _enabled)
	{
		for (int i = 0; i < m_CombatColliders.Count; i++)
		{
			Collider col = m_CombatColliders[i];
			if (col != null)
				col.enabled = _enabled;
		}
	}

	public void Recache()
	{
		m_HasCached = false;
		CacheReferences();
		CacheInitialPoses();
	}

	/// <summary>Подтянуть корень юнита к текущей позе ragdoll (hips). Полезно при перетаскивании.</summary>
	public void SyncRootTransformToRootBone()
	{
		AlignRootToRagdollPosePreservingCurrentPose();
	}

	/// <summary>Разбудить ragdoll-физику (например перед волочением). Не выключает ragdoll.</summary>
	public void WakeRagdollPhysics()
	{
		WakeRagdollPhysicsExcept(null);
	}

	/// <summary>
	/// Разбудить ragdoll, кроме точки хвата: grip остаётся kinematic (ведётся за рукой), остальное dynamic.
	/// </summary>
	public void WakeRagdollPhysicsExcept(Rigidbody _gripBody)
	{
		CacheReferences();
		m_IsRagdollSettled = false;
		m_SettleCandidateStartedAt = -1f;
		m_TransitionBlendStartedAt = -1f;
		m_SoftSettleStartedAt = -1f;

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			if (body == _gripBody)
			{
				if (!body.isKinematic)
				{
					body.linearVelocity = Vector3.zero;
					body.angularVelocity = Vector3.zero;
				}

				body.isKinematic = true;
				body.useGravity = false;
				continue;
			}

			body.isKinematic = false;
			body.useGravity = true;
			body.linearDamping = m_RagdollLinearDamping;
			body.angularDamping = m_RagdollAngularDamping;
			body.maxAngularVelocity = m_MaxRagdollAngularSpeed;
			body.WakeUp();
		}
	}

	/// <summary>Заморозить все тела ragdoll (kinematic) перед parent-креплением к руке.</summary>
	public void FreezeAllRagdollBodiesForDrag()
	{
		CacheReferences();
		SetDragControlled(true);
		m_IsRagdollSettled = false;
		m_SettleCandidateStartedAt = -1f;
		m_TransitionBlendStartedAt = -1f;
		m_SoftSettleStartedAt = -1f;

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			if (!body.isKinematic)
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}

			body.isKinematic = true;
			body.useGravity = false;
		}
	}

	/// <summary>Kinematic freeze + отключение joints + sync RB к трансформам (без растягивания при drag).</summary>
	public void PrepareRagdollRigidPoseForDrag()
	{
		CacheReferences();
		SyncTransformsFromRigidbodies();
		FreezeAllRagdollBodiesForDrag();
		DisableJointsForDrag();
		SyncKinematicRigidbodiesFromTransforms();
	}

	/// <summary>Синхронизировать transform костей из RB (важно после settled ragdoll).</summary>
	public void SyncTransformsFromRigidbodies()
	{
		CacheReferences();

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			body.transform.SetPositionAndRotation(body.position, body.rotation);
		}

		Physics.SyncTransforms();
	}

	/// <summary>Синхронизировать kinematic RB с текущими world-позами костей.</summary>
	public void SyncKinematicRigidbodiesFromTransforms()
	{
		CacheReferences();

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || !body.isKinematic)
				continue;

			body.position = body.transform.position;
			body.rotation = body.transform.rotation;
		}

		Physics.SyncTransforms();
	}

	/// <summary>Вернуть dynamic ragdoll после отпускания (ragdoll остаётся активным).</summary>
	public void RestoreDynamicRagdollAfterDrag()
	{
		if (!m_IsRagdollActive)
		{
			SetDragControlled(false);
			return;
		}

		CacheReferences();
		SetDragControlled(false);
		m_IsRagdollSettled = false;
		m_SettleCandidateStartedAt = -1f;
		m_TransitionBlendStartedAt = -1f;
		m_SoftSettleStartedAt = -1f;
		m_RagdollActivatedAt = Time.time;

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			body.isKinematic = false;
			body.useGravity = true;
			body.linearDamping = m_RagdollLinearDamping;
			body.angularDamping = m_RagdollAngularDamping;
			body.maxAngularVelocity = m_MaxRagdollAngularSpeed;
			body.WakeUp();
		}
	}

	/// <summary>Восстановить joints и dynamic ragdoll после drag.</summary>
	public void RestoreRagdollAfterDrag()
	{
		RestoreRagdollJointsAfterDrag();
		RestoreDynamicRagdollAfterDrag();
		RestoreCollisionDetectionAfterDrag();
		Physics.SyncTransforms();
	}

	/// <summary>Hybrid drag: grip + stable upper kinematic, указанные limbs/lower torso dynamic, остальное kinematic.</summary>
	public void PrepareRagdollPartitionedDrag(
		Rigidbody _gripBody,
		ICollection<Rigidbody> _kinematicStableBodies,
		ICollection<Rigidbody> _dynamicBodies)
	{
		RestoreRagdollJointsAfterDrag();
		SyncTransformsFromRigidbodies();
		SetDragControlled(true);
		CacheReferences();
		m_IsRagdollSettled = false;
		m_SettleCandidateStartedAt = -1f;
		m_TransitionBlendStartedAt = -1f;
		m_SoftSettleStartedAt = -1f;

		var kinematicSet = new HashSet<Rigidbody>(_kinematicStableBodies);
		var dynamicSet = new HashSet<Rigidbody>(_dynamicBodies);
		if (_gripBody != null)
			kinematicSet.Add(_gripBody);

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null)
				continue;

			if (dynamicSet.Contains(body))
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
				body.isKinematic = false;
				body.useGravity = true;
				body.linearDamping = m_RagdollLinearDamping * m_DragLinearDampingMultiplier;
				body.angularDamping = m_RagdollAngularDamping * m_DragAngularDampingMultiplier;
				body.maxAngularVelocity = m_MaxRagdollAngularSpeed;
				body.WakeUp();
				continue;
			}

			if (!body.isKinematic)
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}

			body.isKinematic = true;
			body.useGravity = false;
		}

		ApplyContinuousCollisionForDynamicBodies(_gripBody);
		ApplyDragStabilization(_gripBody);
		DisableJointsBetweenKinematicDragBodies(kinematicSet);
	}

	/// <summary>Hybrid drag: grip kinematic, остальное dynamic, joints включены.</summary>
	public void PrepareRagdollHybridDrag(Rigidbody _gripBody)
	{
		RestoreRagdollJointsAfterDrag();
		SyncTransformsFromRigidbodies();
		SetDragControlled(true);
		WakeRagdollPhysicsExcept(_gripBody);
		ApplyContinuousCollisionForDynamicBodies(_gripBody);
		ApplyDragStabilization(_gripBody);
	}

	/// <summary>Завершить hybrid drag — все тела снова dynamic.</summary>
	public void RestoreAfterHybridDrag()
	{
		RestoreRagdollJointsAfterDrag();
		RestoreDragStabilization();
		RestoreCollisionDetectionAfterDrag();
		RestoreDynamicRagdollAfterDrag();
		Physics.SyncTransforms();
	}

	/// <summary>Блокирует перевод ragdoll в sleep/kinematic settle, пока юнита тащат.</summary>
	public void SetDragControlled(bool _active)
	{
		m_SuppressAutoSettle = _active;
		if (_active)
		{
			m_IsRagdollSettled = false;
			m_SettleCandidateStartedAt = -1f;
			m_TransitionBlendStartedAt = -1f;
			m_SoftSettleStartedAt = -1f;
		}
	}
	#endregion

	#region Private Methods
	private void CacheReferences()
	{
		if (m_HasCached)
			return;

		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_NavMeshAgent == null)
			m_NavMeshAgent = GetComponent<NavMeshAgent>();
		if (m_RootBone == null)
			m_RootBone = FindChildTransformByName(transform, "Hips") ?? transform;
		ResolveWeaponControlComponents();

		m_Rigidbodies.Clear();
		m_CombatColliders.Clear();
		m_RagdollColliders.Clear();

		Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < rigidbodies.Length; i++)
		{
			Rigidbody body = rigidbodies[i];
			if (body != null && body.transform != transform)
			{
				m_Rigidbodies.Add(body);
				Collider bodyCollider = body.GetComponent<Collider>();
				if (bodyCollider != null && !bodyCollider.isTrigger)
					m_RagdollColliders.Add(bodyCollider);
			}
		}

		UnitBodyHitZone[] zones = GetComponentsInChildren<UnitBodyHitZone>(true);
		for (int i = 0; i < zones.Length; i++)
		{
			if (zones[i] != null && zones[i].TryGetComponent(out Collider col))
				m_CombatColliders.Add(col);
		}

		m_HasCached = true;
		if (m_IgnoreSelfCollision)
			IgnoreRagdollSelfCollision();
	}

	private void CacheInitialPoses()
	{
		m_InitialPoses.Clear();
		Transform poseRoot = m_RootBone != null ? m_RootBone : transform;
		Transform[] transforms = poseRoot.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < transforms.Length; i++)
		{
			Transform t = transforms[i];
			if (t == null || t == transform)
				continue;

			m_InitialPoses.Add(new BonePose
			{
				Transform = t,
				LocalPosition = t.localPosition,
				LocalRotation = t.localRotation
			});
		}
	}

	private void RestoreInitialPose()
	{
		for (int i = 0; i < m_InitialPoses.Count; i++)
		{
			BonePose pose = m_InitialPoses[i];
			if (pose?.Transform == null)
				continue;

			pose.Transform.localPosition = pose.LocalPosition;
			pose.Transform.localRotation = pose.LocalRotation;
		}
	}

	private void AlignRootToRagdollPose()
	{
		if (m_RootBone == null)
			return;

		Vector3 rootBoneWorld = m_RootBone.position;
		Vector3 rootBoneLocal = GetInitialLocalPosition(m_RootBone);
		Vector3 targetRootPosition = rootBoneWorld - transform.rotation * rootBoneLocal;
		transform.position = targetRootPosition;

		if (m_NavMeshAgent != null && m_NavMeshAgent.enabled && m_NavMeshAgent.isOnNavMesh)
			m_NavMeshAgent.Warp(targetRootPosition);
	}

	private void AlignRootToRagdollPosePreservingCurrentPose()
	{
		if (m_RootBone == null)
			return;

		Transform[] bones = m_RootBone.GetComponentsInChildren<Transform>(true);
		Vector3[] positions = new Vector3[bones.Length];
		Quaternion[] rotations = new Quaternion[bones.Length];

		for (int i = 0; i < bones.Length; i++)
		{
			Transform bone = bones[i];
			if (bone == null)
				continue;

			positions[i] = bone.position;
			rotations[i] = bone.rotation;
		}

		AlignRootToRagdollPose();

		for (int i = 0; i < bones.Length; i++)
		{
			Transform bone = bones[i];
			if (bone == null)
				continue;

			bone.SetPositionAndRotation(positions[i], rotations[i]);
		}
	}

	private Vector3 GetInitialLocalPosition(Transform _bone)
	{
		for (int i = 0; i < m_InitialPoses.Count; i++)
		{
			BonePose pose = m_InitialPoses[i];
			if (pose != null && pose.Transform == _bone)
				return pose.LocalPosition;
		}

		return _bone.localPosition;
	}

	private void ApplyImpulse(Vector3 _impulse)
	{
		Vector3 impulse = _impulse;
		if (impulse.sqrMagnitude < 0.0001f)
			impulse = (transform.forward * 0.35f + Vector3.down * 0.65f).normalized * m_DefaultImpulse +
			          Vector3.up * m_DefaultUpImpulse;

		Rigidbody rootBody = m_RootBone != null ? m_RootBone.GetComponent<Rigidbody>() : null;
		if (rootBody == null && m_Rigidbodies.Count > 0)
			rootBody = m_Rigidbodies[0];

		if (rootBody != null && !rootBody.isKinematic)
		{
			rootBody.AddForce(impulse, ForceMode.Impulse);

			Vector3 horizontalImpulse = impulse;
			horizontalImpulse.y = 0f;
			Vector3 torqueAxis = horizontalImpulse.sqrMagnitude > 0.0001f
				? Vector3.Cross(horizontalImpulse.normalized, Vector3.up).normalized
				: transform.right;
			Vector3 torque = torqueAxis * impulse.magnitude * m_FallTorqueMultiplier;
			rootBody.AddTorque(torque, ForceMode.Impulse);

			if (m_LogImpulse)
			{
				Debug.Log(
					$"[Ragdoll] {name} | ApplyImpulse (без хит-инфо)\n" +
					$"  impulse={impulse:F3} torque={torque:F3}",
					this);
			}
		}
	}

	private void ApplyHitImpulse(DamageHitInfo _hitInfo, RagdollFallProfile _fallProfile)
	{
		Vector3 incomingDirection = _hitInfo.IncomingDirection.sqrMagnitude > 0.0001f
			? _hitInfo.IncomingDirection.normalized
			: transform.forward;

		Vector3 impulse = BuildProfileImpulse(incomingDirection, _hitInfo.BodyPart, _fallProfile);
		Rigidbody hitBody = ResolveHitRigidbody(_hitInfo.HitCollider);
		Rigidbody rootBody = m_RootBone != null ? m_RootBone.GetComponent<Rigidbody>() : null;

		Vector3 horizontalImpulse = impulse;
		horizontalImpulse.y = 0f;
		Vector3 torqueAxis = horizontalImpulse.sqrMagnitude > 0.0001f
			? Vector3.Cross(horizontalImpulse.normalized, Vector3.up).normalized
			: transform.right;

		Vector3 hitForce = impulse * m_HitBoneImpulseMultiplier;
		Vector3 hitTorque = torqueAxis * impulse.magnitude * m_HitBodyTorqueMultiplier;
		Vector3 rootForce = impulse * m_RootFollowThroughMultiplier;
		Vector3 rootTorque = torqueAxis * impulse.magnitude * m_FallTorqueMultiplier;

		if (m_LogImpulse)
		{
			Debug.Log(
				$"[Ragdoll] {name} | профиль={_fallProfile} | часть тела={_hitInfo.BodyPart}\n" +
				$"  входящее направление={incomingDirection:F3} (magnitude={_hitInfo.IncomingDirection.magnitude:F2})\n" +
				$"  импульс={impulse:F3} (|impulse|={impulse.magnitude:F2})\n" +
				$"  горизонтальная доля={horizontalImpulse.magnitude / impulse.magnitude:P0}\n" +
				$"  hitBody={hitBody?.name ?? "null"} hitForce={hitForce:F3} hitTorque={hitTorque:F3}\n" +
				$"  rootBody={rootBody?.name ?? "null"} rootForce={rootForce:F3} rootTorque={rootTorque:F3}",
				this);
		}

		if (hitBody != null && !hitBody.isKinematic)
		{
			hitBody.AddForce(hitForce, ForceMode.Impulse);
			hitBody.AddTorque(hitTorque, ForceMode.Impulse);
		}

		if (rootBody != null && !rootBody.isKinematic && rootBody != hitBody)
		{
			rootBody.AddForce(rootForce, ForceMode.Impulse);
			rootBody.AddTorque(rootTorque, ForceMode.Impulse);
		}
	}

	private Vector3 BuildProfileImpulse(Vector3 _incomingDirection, BodyPartType _bodyPart, RagdollFallProfile _fallProfile)
	{
		float randomScale = UnityEngine.Random.Range(1f - m_RandomImpulseVariance, 1f + m_RandomImpulseVariance);
		Vector3 side = Vector3.Cross(Vector3.up, _incomingDirection);
		if (side.sqrMagnitude < 0.0001f)
			side = transform.right;
		side.Normalize();
		side *= UnityEngine.Random.Range(-1f, 1f);

		float incomingWeight, downWeight, sideWeight;
		switch (_fallProfile)
		{
			case RagdollFallProfile.ForwardCollapse:
				incomingWeight = 0.85f; downWeight = 0.3f; sideWeight = 0.1f;
				break;
			case RagdollFallProfile.BackwardKnockback:
				incomingWeight = 0.9f; downWeight = 0.2f; sideWeight = 0.05f;
				break;
			case RagdollFallProfile.SideSpin:
				incomingWeight = 0.6f; downWeight = 0.4f; sideWeight = 0.7f;
				break;
			case RagdollFallProfile.LegBuckle:
				incomingWeight = 0.5f; downWeight = 0.7f; sideWeight = 0.1f;
				break;
			default:
				incomingWeight = 0.75f; downWeight = 0.45f; sideWeight = 0.1f;
				break;
		}

		Vector3 impulse = _incomingDirection * incomingWeight + Vector3.down * downWeight + side * m_RandomSideImpulse * sideWeight;

		float bodyPartScale = _bodyPart == BodyPartType.Head || _bodyPart == BodyPartType.Neck
			? 1.1f
			: (_bodyPart == BodyPartType.LeftLeg || _bodyPart == BodyPartType.RightLeg ? 0.8f : 1f);

		return impulse.normalized * m_DefaultImpulse * randomScale * bodyPartScale;
	}

	private void DecayRagdollAngularVelocity()
	{
		float decay = Mathf.Exp(-m_AngularDecayPerSecond * Time.fixedDeltaTime);
		float maxSpeedSqr = m_MaxRagdollAngularSpeed * m_MaxRagdollAngularSpeed;
		float averageLinearSpeed = 0f;
		int activeBodyCount = 0;

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;

			averageLinearSpeed += body.linearVelocity.magnitude;
			activeBodyCount++;
		}

		if (activeBodyCount > 0)
		{
			averageLinearSpeed /= activeBodyCount;
			if (averageLinearSpeed < 0.4f)
				decay *= Mathf.Exp(-6f * Time.fixedDeltaTime);
		}

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;

			if (!IsFiniteVector3(body.angularVelocity))
			{
				body.angularVelocity = Vector3.zero;
				continue;
			}

			Vector3 angularVelocity = body.angularVelocity * decay;
			if (!IsFiniteVector3(angularVelocity))
			{
				body.angularVelocity = Vector3.zero;
				continue;
			}

			if (angularVelocity.sqrMagnitude > maxSpeedSqr)
				angularVelocity = Vector3.ClampMagnitude(angularVelocity, m_MaxRagdollAngularSpeed);

			body.angularVelocity = angularVelocity;
		}
	}

	private static bool IsFiniteVector3(Vector3 _value)
	{
		return float.IsFinite(_value.x) && float.IsFinite(_value.y) && float.IsFinite(_value.z);
	}

	private void UpdateRagdollSettling()
	{
		if (m_IsRagdollSettled || m_SoftSettleStartedAt >= 0f || Time.time - m_RagdollActivatedAt < m_SettleDelay)
			return;

		if (!IsRagdollSlowEnoughToSleep())
		{
			m_SettleCandidateStartedAt = -1f;
			return;
		}

		if (m_SettleCandidateStartedAt < 0f)
		{
			m_SettleCandidateStartedAt = Time.time;
			return;
		}

		if (Time.time - m_SettleCandidateStartedAt >= m_SettleRequiredSeconds)
			StartSoftSettle();
	}

	private bool IsRagdollSlowEnoughToSleep()
	{
		float maxLinearSpeedSqr = m_SleepLinearSpeed * m_SleepLinearSpeed;
		float maxAngularSpeedSqr = m_SleepAngularSpeed * m_SleepAngularSpeed;

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;

			if (body.linearVelocity.sqrMagnitude > maxLinearSpeedSqr ||
			    body.angularVelocity.sqrMagnitude > maxAngularSpeedSqr)
				return false;
		}

		return true;
	}

	private void StartSoftSettle()
	{
		m_SoftSettleStartedAt = Time.time;
	}

	private void UpdateSoftSettle()
	{
		if (m_SoftSettleStartedAt < 0f)
			return;

		float elapsed = Time.time - m_SoftSettleStartedAt;
		if (elapsed >= m_SoftSettleDuration)
		{
			m_SoftSettleStartedAt = -1f;
			FinishSoftSettle();
			return;
		}

		float t = elapsed / m_SoftSettleDuration;
		float linear = Mathf.Lerp(m_RagdollLinearDamping, m_SoftSettleLinearDamping, t);
		float angular = Mathf.Lerp(m_RagdollAngularDamping, m_SoftSettleAngularDamping, t);

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;
			body.linearDamping = linear;
			body.angularDamping = angular;
		}
	}

	private void FinishSoftSettle()
	{
		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;

			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			body.linearDamping = m_RagdollLinearDamping;
			body.angularDamping = m_RagdollAngularDamping;
			if (m_MakeKinematicWhenSettled)
			{
				body.useGravity = false;
				body.isKinematic = true;
			}

			body.Sleep();
		}

		m_IsRagdollSettled = true;
	}

	private void UpdateTransitionBlend()
	{
		if (m_TransitionBlendStartedAt < 0f)
			return;

		float elapsed = Time.time - m_TransitionBlendStartedAt;
		if (elapsed >= m_TransitionBlendDuration)
		{
			m_TransitionBlendStartedAt = -1f;
			for (int i = 0; i < m_Rigidbodies.Count; i++)
			{
				Rigidbody body = m_Rigidbodies[i];
				if (body == null || body.isKinematic)
					continue;
				body.linearDamping = m_RagdollLinearDamping;
				body.angularDamping = m_RagdollAngularDamping;
			}

			return;
		}

		float t = elapsed / m_TransitionBlendDuration;
		float linear = Mathf.Lerp(m_TransitionLinearDamping, m_RagdollLinearDamping, t);
		float angular = Mathf.Lerp(m_TransitionAngularDamping, m_RagdollAngularDamping, t);

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;
			body.linearDamping = linear;
			body.angularDamping = angular;
		}
	}

	private void CaptureBonePositions()
	{
		if (m_BonePositionsPrevious == null || m_BonePositionsPrevious.Length != m_Rigidbodies.Count)
			m_BonePositionsPrevious = new Vector3[m_Rigidbodies.Count];

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body != null)
				m_BonePositionsPrevious[i] = body.position;
		}

		m_HasBonePreviousPositions = true;
	}

	private void FreezeWeaponControl()
	{
		ResolveWeaponControlComponents();
		DropWeaponOnKnockout();

		if (m_WeaponAiming != null)
		{
			m_WeaponAimingWasEnabled = m_WeaponAiming.enabled;
			m_WeaponAiming.enabled = false;
		}

		if (m_WeaponVisualRecoilKick != null)
		{
			m_WeaponVisualRecoilKickWasEnabled = m_WeaponVisualRecoilKick.enabled;
			m_WeaponVisualRecoilKick.enabled = false;
		}

		if (m_HandIk != null)
		{
			m_HandIkWasEnabled = m_HandIk.enabled;
			m_HandIk.enabled = false;
		}
	}

	private void DropWeaponOnKnockout()
	{
		if (m_WeaponDetachedOnKnockout)
			return;

		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null || m_UnitEquipment.MainWeaponRoot == null)
			return;

		Vector3 position;
		Quaternion rotation;
		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		position = weaponRoot.position;
		rotation = weaponRoot.rotation;

		Vector3 side = transform.right;
		side.y = 0f;
		if (side.sqrMagnitude < 0.0001f)
			side = transform.right;
		side.Normalize();

		position += side * m_WeaponDropSideOffset;
		position += Vector3.down * m_WeaponDropDownOffset;

		Vector3 dropImpulse = side * m_WeaponDropImpulse + Vector3.down * (m_WeaponDropImpulse * 0.35f);
		if (m_UnitEquipment.TryDetachMainWeaponToWorld(position, rotation, dropImpulse))
			m_WeaponDetachedOnKnockout = true;
	}

	private void RestoreWeaponToHand()
	{
		ResolveWeaponControlComponents();

		if (m_WeaponDetachedOnKnockout && m_UnitEquipment != null)
			m_UnitEquipment.RestoreDetachedMainWeaponToHand();

		RestoreWeaponControlComponents();
	}

	private void ResolveWeaponControlComponents()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_WeaponAiming == null)
			m_WeaponAiming = GetComponent<UnitWeaponAiming>();
		if (m_WeaponVisualRecoilKick == null)
			m_WeaponVisualRecoilKick = GetComponent<UnitWeaponVisualRecoilKick>();
		if (m_HandIk == null)
			m_HandIk = GetComponentInChildren<AnimatorHandIk>(true);
	}

	private void RestoreWeaponControlComponents()
	{
		if (m_WeaponAiming != null)
			m_WeaponAiming.enabled = m_WeaponAimingWasEnabled;

		if (m_WeaponVisualRecoilKick != null)
			m_WeaponVisualRecoilKick.enabled = m_WeaponVisualRecoilKickWasEnabled;

		if (m_HandIk != null)
			m_HandIk.enabled = m_HandIkWasEnabled;
	}

	private void IgnoreRagdollSelfCollision()
	{
		for (int i = 0; i < m_RagdollColliders.Count; i++)
		{
			Collider first = m_RagdollColliders[i];
			if (first == null)
				continue;

			for (int j = i + 1; j < m_RagdollColliders.Count; j++)
			{
				Collider second = m_RagdollColliders[j];
				if (second == null)
					continue;

				Physics.IgnoreCollision(first, second, true);
			}
		}
	}

	private Rigidbody ResolveHitRigidbody(Collider _hitCollider)
	{
		if (_hitCollider != null)
		{
			Rigidbody attached = _hitCollider.attachedRigidbody;
			if (attached != null)
				return attached;
		}

		return m_RootBone != null ? m_RootBone.GetComponent<Rigidbody>() : null;
	}

	private void RefreshVisionHitZones()
	{
		UnitVision vision = GetComponent<UnitVision>();
		if (vision != null)
			vision.RefreshBodyHitZones();
	}

	private void DisableJointsForDrag()
	{
		m_DisabledJointsForDrag.Clear();
		Joint[] joints = GetComponentsInChildren<Joint>(true);
		for (int i = 0; i < joints.Length; i++)
		{
			Joint joint = joints[i];
			if (joint == null)
				continue;

			m_DisabledJointsForDrag.Add(new JointDragState
			{
				Joint = joint,
				ConnectedBody = joint.connectedBody
			});
			joint.connectedBody = null;
		}
	}

	private void DisableJointsBetweenKinematicDragBodies(HashSet<Rigidbody> _kinematicBodies)
	{
		Joint[] joints = GetComponentsInChildren<Joint>(true);
		for (int i = 0; i < joints.Length; i++)
		{
			Joint joint = joints[i];
			if (joint == null || joint.connectedBody == null)
				continue;

			if (!joint.TryGetComponent(out Rigidbody ownBody))
				continue;

			if (!_kinematicBodies.Contains(ownBody) || !_kinematicBodies.Contains(joint.connectedBody))
				continue;

			m_DisabledJointsForDrag.Add(new JointDragState
			{
				Joint = joint,
				ConnectedBody = joint.connectedBody
			});
			joint.connectedBody = null;
		}
	}

	private void RestoreRagdollJointsAfterDrag()
	{
		for (int i = 0; i < m_DisabledJointsForDrag.Count; i++)
		{
			JointDragState state = m_DisabledJointsForDrag[i];
			if (state.Joint != null)
				state.Joint.connectedBody = state.ConnectedBody;
		}

		m_DisabledJointsForDrag.Clear();
	}

	private void ApplyContinuousCollisionForDynamicBodies(Rigidbody _gripBody)
	{
		m_CollisionDetectionRestore.Clear();

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic || body == _gripBody)
				continue;

			m_CollisionDetectionRestore.Add(new CollisionDetectionRestore
			{
				Body = body,
				Mode = body.collisionDetectionMode
			});
			body.collisionDetectionMode = CollisionDetectionMode.Continuous;
		}
	}

	private void RestoreCollisionDetectionAfterDrag()
	{
		for (int i = 0; i < m_CollisionDetectionRestore.Count; i++)
		{
			CollisionDetectionRestore entry = m_CollisionDetectionRestore[i];
			if (entry.Body != null)
				entry.Body.collisionDetectionMode = entry.Mode;
		}

		m_CollisionDetectionRestore.Clear();
	}

	private void ApplyDragStabilization(Rigidbody _gripBody)
	{
		m_InterpolationRestore.Clear();

		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic || body == _gripBody)
				continue;

			m_InterpolationRestore.Add(new RigidbodyInterpolationRestore
			{
				Body = body,
				Interpolation = body.interpolation
			});

			body.interpolation = RigidbodyInterpolation.Interpolate;
			body.linearDamping = m_RagdollLinearDamping * m_DragLinearDampingMultiplier;
			body.angularDamping = m_RagdollAngularDamping * m_DragAngularDampingMultiplier;
		}
	}

	private void RestoreDragStabilization()
	{
		for (int i = 0; i < m_InterpolationRestore.Count; i++)
		{
			RigidbodyInterpolationRestore entry = m_InterpolationRestore[i];
			if (entry.Body != null)
				entry.Body.interpolation = entry.Interpolation;
		}

		m_InterpolationRestore.Clear();
	}

	private static Transform FindChildTransformByName(Transform _root, string _name)
	{
		if (_root == null)
			return null;

		Transform[] children = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < children.Length; i++)
		{
			if (children[i] != null && children[i].name == _name)
				return children[i];
		}

		return null;
	}
	#endregion
}
