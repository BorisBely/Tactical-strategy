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
	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private NavMeshAgent m_NavMeshAgent;
	[SerializeField] private Transform m_RootBone;
	[SerializeField] private bool m_StartInRagdoll;
	[SerializeField] private bool m_KeepCombatCollidersEnabled = true;

	[Header("Fall Impulse")]
	[SerializeField, Min(0f)] private float m_DefaultImpulse = 1.6f;
	[SerializeField, Min(0f)] private float m_DefaultUpImpulse = 0f;
	[SerializeField, Min(0f)] private float m_HitBoneImpulseMultiplier = 0.8f;
	[SerializeField, Min(0f)] private float m_RootFollowThroughMultiplier = 0.25f;
	[SerializeField, Range(0f, 1f)] private float m_RandomImpulseVariance = 0.2f;
	[SerializeField, Min(0f)] private float m_RandomSideImpulse = 0.15f;

	[Header("Ragdoll Stability")]
	[SerializeField] private bool m_IgnoreSelfCollision = true;
	[SerializeField, Min(0f)] private float m_RagdollLinearDamping = 0.35f;
	[SerializeField, Min(0f)] private float m_RagdollAngularDamping = 4.5f;
	[SerializeField, Min(0.1f)] private float m_MaxRagdollAngularSpeed = 2.5f;
	[SerializeField, Min(0f)] private float m_AngularDecayPerSecond = 10f;
	[SerializeField, Min(0f)] private float m_SettleDelay = 0.7f;
	[SerializeField, Min(0f)] private float m_SettleRequiredSeconds = 0.35f;
	[SerializeField, Min(0f)] private float m_SleepLinearSpeed = 0.12f;
	[SerializeField, Min(0f)] private float m_SleepAngularSpeed = 0.25f;
	[SerializeField] private bool m_MakeKinematicWhenSettled = true;

	[Header("Weapon During Ragdoll")]
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField, Min(0f)] private float m_WeaponDropSideOffset = 0.18f;
	[SerializeField, Min(0f)] private float m_WeaponDropDownOffset = 0.05f;
	[SerializeField, Min(0f)] private float m_WeaponDropImpulse = 0.45f;
	#endregion

	#region Private Fields
	private readonly List<Rigidbody> m_Rigidbodies = new List<Rigidbody>(24);
	private readonly List<Collider> m_CombatColliders = new List<Collider>(24);
	private readonly List<Collider> m_RagdollColliders = new List<Collider>(24);
	private readonly List<BonePose> m_InitialPoses = new List<BonePose>(64);
	private bool m_IsRagdollActive;
	private bool m_HasCached;
	private bool m_IsRagdollSettled;
	private float m_RagdollActivatedAt;
	private float m_SettleCandidateStartedAt = -1f;
	private UnitWeaponAiming m_WeaponAiming;
	private UnitWeaponVisualRecoilKick m_WeaponVisualRecoilKick;
	private AnimatorHandIk m_HandIk;
	private bool m_WeaponAimingWasEnabled;
	private bool m_WeaponVisualRecoilKickWasEnabled;
	private bool m_HandIkWasEnabled;
	private bool m_WeaponDetachedOnKnockout;
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
		if (!m_IsRagdollActive || m_IsRagdollSettled)
			return;

		DecayRagdollAngularVelocity();
		UpdateRagdollSettling();
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
		if (_active)
			m_RagdollActivatedAt = Time.time;

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
				body.linearDamping = m_RagdollLinearDamping;
				body.angularDamping = m_RagdollAngularDamping;
				body.maxAngularVelocity = m_MaxRagdollAngularSpeed;
				body.sleepThreshold = 0.08f;
				body.linearVelocity = Vector3.zero;
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
			rootBody.AddForce(impulse, ForceMode.Impulse);
	}

	private void ApplyHitImpulse(DamageHitInfo _hitInfo, RagdollFallProfile _fallProfile)
	{
		Vector3 incomingDirection = _hitInfo.IncomingDirection.sqrMagnitude > 0.0001f
			? _hitInfo.IncomingDirection.normalized
			: transform.forward;

		Vector3 impulse = BuildProfileImpulse(incomingDirection, _hitInfo.BodyPart, _fallProfile);
		Rigidbody hitBody = ResolveHitRigidbody(_hitInfo.HitCollider);
		Rigidbody rootBody = m_RootBone != null ? m_RootBone.GetComponent<Rigidbody>() : null;

		if (hitBody != null && !hitBody.isKinematic)
			hitBody.AddForce(impulse * m_HitBoneImpulseMultiplier, ForceMode.Impulse);

		if (rootBody != null && !rootBody.isKinematic && rootBody != hitBody)
			rootBody.AddForce(impulse * m_RootFollowThroughMultiplier, ForceMode.Impulse);
	}

	private Vector3 BuildProfileImpulse(Vector3 _incomingDirection, BodyPartType _bodyPart, RagdollFallProfile _fallProfile)
	{
		float randomScale = UnityEngine.Random.Range(1f - m_RandomImpulseVariance, 1f + m_RandomImpulseVariance);
		Vector3 side = Vector3.Cross(Vector3.up, _incomingDirection);
		if (side.sqrMagnitude < 0.0001f)
			side = transform.right;
		side.Normalize();
		side *= UnityEngine.Random.Range(-m_RandomSideImpulse, m_RandomSideImpulse);

		Vector3 impulse;
		switch (_fallProfile)
		{
			case RagdollFallProfile.ForwardCollapse:
				impulse = transform.forward * 0.45f + Vector3.down * 0.8f + side;
				break;
			case RagdollFallProfile.BackwardKnockback:
				impulse = _incomingDirection * 0.5f + Vector3.down * 0.75f + side * 0.15f;
				break;
			case RagdollFallProfile.SideSpin:
				impulse = _incomingDirection * 0.35f + side * 0.45f + Vector3.down * 0.8f;
				break;
			case RagdollFallProfile.LegBuckle:
				impulse = _incomingDirection * 0.25f + Vector3.down * 0.9f + side * 0.2f;
				break;
			default:
				impulse = _incomingDirection * 0.25f + Vector3.down * 0.9f + side * 0.15f;
				break;
		}

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

			Vector3 angularVelocity = body.angularVelocity * decay;
			if (angularVelocity.sqrMagnitude > maxSpeedSqr)
				angularVelocity = Vector3.ClampMagnitude(angularVelocity, m_MaxRagdollAngularSpeed);

			body.angularVelocity = angularVelocity;
		}
	}

	private void UpdateRagdollSettling()
	{
		if (m_IsRagdollSettled || Time.time - m_RagdollActivatedAt < m_SettleDelay)
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
			SleepRagdollBodies();
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

	private void SleepRagdollBodies()
	{
		for (int i = 0; i < m_Rigidbodies.Count; i++)
		{
			Rigidbody body = m_Rigidbodies[i];
			if (body == null || body.isKinematic)
				continue;

			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			if (m_MakeKinematicWhenSettled)
			{
				body.useGravity = false;
				body.isKinematic = true;
			}

			body.Sleep();
		}

		m_IsRagdollSettled = true;
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
