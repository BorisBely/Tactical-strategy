using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hybrid drag: Spine_03 + верх торса kinematic у руки; руки, ноги, Hips/Spine_01 — dynamic.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class UnitFallenDragVictimFollower : MonoBehaviour
{
	#region Types
	private struct StableUpperPose
	{
		public Rigidbody Body;
		public Vector3 LocalPositionInGrip;
		public Quaternion LocalRotationInGrip;
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitRagdollController m_RagdollController;
	[Tooltip("Rigidbody точки хвата. Если пусто — Spine_03.")]
	[SerializeField] private Transform m_GripAnchorOverride;

	[Header("Grip Follow")]
	[SerializeField, Min(1f)] private float m_MaxGripFollowSpeed = 14f;
	[SerializeField, Min(0.01f)] private float m_GripPositionSmoothTime = 0.07f;
	[SerializeField, Min(0.01f)] private float m_GripRotationSmoothTime = 0.1f;
	[SerializeField, Min(1f)] private float m_MaxDynamicLinearSpeed = 10f;
	[SerializeField, Min(1f)] private float m_MaxDynamicAngularSpeed = 6f;

	[Header("Floor")]
	[SerializeField, Min(0.01f)] private float m_FloorSkinMeters = 0.06f;
	[SerializeField, Min(0.1f)] private float m_FloorProbeUpMeters = 0.25f;
	[SerializeField, Min(0.1f)] private float m_FloorProbeDownMeters = 0.6f;
	[SerializeField, Min(0f)] private float m_FloorPushStrength = 10f;
	[SerializeField, Min(0f)] private float m_MaxDownwardSpeed = 1.8f;
	[SerializeField, Min(0f)] private float m_FloorPushMinPenetration = 0.02f;
	[SerializeField] private LayerMask m_GroundLayers = Physics.DefaultRaycastLayers;

	[Header("Release")]
	[SerializeField] private bool m_SnapToGroundOnRelease = true;
	[SerializeField, Min(0.05f)] private float m_RootSyncIntervalSeconds = 0.35f;

	[Header("Debug")]
	[SerializeField] private bool m_LogDragFollow;
	#endregion

	#region Private Fields
	private readonly List<StableUpperPose> m_StableUpperPoses = new List<StableUpperPose>(8);
	private readonly List<Rigidbody> m_KinematicStableBodies = new List<Rigidbody>(8);
	private readonly List<Rigidbody> m_DynamicDragBodies = new List<Rigidbody>(16);

	private UnitFallenDragController m_CurrentDragger;
	private Rigidbody m_GripBody;
	private Transform m_GripReference;
	private Quaternion m_GripLocalRotationInHand;
	private Transform m_HandAnchor;
	private bool m_IsHybridDragActive;
	private bool m_EndFollowRequested;
	private float m_NextRootSyncTime;

	private bool m_UseExplicitOffsets;
	private Vector3 m_ExplicitGripPositionOffset;
	private Vector3 m_ExplicitGripRotationEuler;

	private Quaternion m_BakedHandSpaceOffset = Quaternion.identity;

	private bool m_UseOverrideSmoothTimes;
	private float m_OverridePositionSmoothTime;
	private float m_OverrideRotationSmoothTime;

	private Vector3 m_CachedGripTarget;
	private Quaternion m_CachedGripRotation;
	private Vector3 m_GripPositionSmoothVelocity;
	#endregion

	#region Public Properties
	public bool IsBeingDragged => m_CurrentDragger != null;
	public bool IsHybridDragActive => m_IsHybridDragActive;
	public bool IsBeingCarried => m_IsHybridDragActive;
	public UnitFallenDragController CurrentDragger => m_CurrentDragger;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
	}

	private void LateUpdate()
	{
		if (!m_IsHybridDragActive || m_HandAnchor == null)
			return;

		CacheHandGripTarget(m_HandAnchor);
	}

	private void FixedUpdate()
	{
		if (!m_IsHybridDragActive || m_GripBody == null)
			return;

		ApplyDragPose(false);

		SanitizeDynamicDragBodies();
		PreventFloorPenetration();

		if (Time.time >= m_NextRootSyncTime)
		{
			m_NextRootSyncTime = Time.time + m_RootSyncIntervalSeconds;
			m_RagdollController?.SyncRootTransformToRootBone();
		}
	}

	private void OnDisable()
	{
		if (IsBeingDragged || m_IsHybridDragActive)
			EndFollow();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying || !m_IsHybridDragActive || m_HandAnchor == null || m_GripBody == null)
			return;

		RefreshDragPreviewImmediate();
	}
#endif
	#endregion

	#region Public Methods
	public static bool IsDraggableTarget(RtsUnitMember _unit)
	{
		return UnitFallenStateUtility.IsFallenOrDead(_unit);
	}

	public bool CanBeDraggedBy(UnitFallenDragController _dragger)
	{
		if (_dragger == null || IsBeingDragged)
			return false;

		RtsUnitMember member = GetComponent<RtsUnitMember>();
		return IsDraggableTarget(member);
	}

	public void BeginFollow(UnitFallenDragController _dragger, Transform _leftHand)
	{
		if (_dragger == null || _leftHand == null)
			return;

		LogFollow($"BeginFollow: dragger='{_dragger.name}' victim='{name}' leftHand='{_leftHand.name}'");

		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();

		m_GripBody = ResolveGripRigidbody();
		m_GripReference = m_GripBody != null ? m_GripBody.transform : ResolveGripReferenceTransform();
		if (m_GripBody == null || m_GripReference == null)
		{
			Debug.LogWarning($"[UnitFallenDragVictimFollower] BeginFollow failed: no grip rigidbody on '{name}'.", this);
			return;
		}

		m_EndFollowRequested = false;
		m_CurrentDragger = _dragger;
		m_HandAnchor = _leftHand;
		m_NextRootSyncTime = Time.time;
		m_GripPositionSmoothVelocity = Vector3.zero;

		m_RagdollController?.SyncTransformsFromRigidbodies();
		AlignRagdollRigidlyToGrip(_leftHand, m_GripReference, ResolveLiveGripPositionOffset());
		m_RagdollController?.SyncKinematicRigidbodiesFromTransforms();

		m_BakedHandSpaceOffset = Quaternion.AngleAxis(ResolveLiveGripRotationOffset().y, _leftHand.up)
		                       * Quaternion.AngleAxis(ResolveLiveGripRotationOffset().x, _leftHand.right)
		                       * Quaternion.AngleAxis(ResolveLiveGripRotationOffset().z, _leftHand.forward);

		m_GripLocalRotationInHand = Quaternion.Inverse(_leftHand.rotation) * m_GripBody.rotation;
		m_StableUpperPoses.Clear();
		m_KinematicStableBodies.Clear();
		m_DynamicDragBodies.Clear();
		m_RagdollController?.PrepareRagdollHybridDrag(m_GripBody);

		CacheHandGripTarget(_leftHand);
		m_IsHybridDragActive = true;
		RefreshDragPreviewImmediate();
	}

	public void BeginFollow(Transform _anchor, Vector3 _gripPositionOffset, Vector3 _gripRotationEulerOffset,
		float _positionSmoothTime = -1f, float _rotationSmoothTime = -1f)
	{
		if (_anchor == null)
			return;

		LogFollow($"BeginFollow(explicit): anchor='{_anchor.name}' victim='{name}' posOffset={_gripPositionOffset} rotOffset={_gripRotationEulerOffset}");

		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();

		m_GripBody = ResolveGripRigidbody();
		m_GripReference = m_GripBody != null ? m_GripBody.transform : ResolveGripReferenceTransform();
		if (m_GripBody == null || m_GripReference == null)
		{
			Debug.LogWarning($"[UnitFallenDragVictimFollower] BeginFollow(explicit) failed: no grip rigidbody on '{name}'.", this);
			return;
		}

		m_EndFollowRequested = false;
		m_CurrentDragger = null;
		m_HandAnchor = _anchor;
		m_UseExplicitOffsets = true;
		m_ExplicitGripPositionOffset = _gripPositionOffset;
		m_ExplicitGripRotationEuler = _gripRotationEulerOffset;
		if (_positionSmoothTime > 0f || _rotationSmoothTime > 0f)
		{
			m_UseOverrideSmoothTimes = true;
			m_OverridePositionSmoothTime = _positionSmoothTime > 0f ? _positionSmoothTime : m_GripPositionSmoothTime;
			m_OverrideRotationSmoothTime = _rotationSmoothTime > 0f ? _rotationSmoothTime : m_GripRotationSmoothTime;
		}
		else
		{
			m_UseOverrideSmoothTimes = false;
		}
		m_NextRootSyncTime = Time.time;
		m_GripPositionSmoothVelocity = Vector3.zero;

		m_RagdollController?.SyncTransformsFromRigidbodies();
		AlignRagdollRigidlyToGrip(_anchor, m_GripReference, ResolveLiveGripPositionOffset());

		Quaternion desiredRotation = _anchor.rotation * Quaternion.Euler(_gripRotationEulerOffset);
		m_GripBody.MoveRotation(desiredRotation);
		m_GripLocalRotationInHand = Quaternion.identity;

		m_BakedHandSpaceOffset = Quaternion.AngleAxis(_gripRotationEulerOffset.y, _anchor.up)
		                       * Quaternion.AngleAxis(_gripRotationEulerOffset.x, _anchor.right)
		                       * Quaternion.AngleAxis(_gripRotationEulerOffset.z, _anchor.forward);

		m_RagdollController?.SyncKinematicRigidbodiesFromTransforms();
		m_StableUpperPoses.Clear();
		m_KinematicStableBodies.Clear();
		m_DynamicDragBodies.Clear();
		m_RagdollController?.PrepareRagdollHybridDrag(m_GripBody);

		CacheHandGripTarget(_anchor);
		m_IsHybridDragActive = true;
		RefreshDragPreviewImmediate();
	}

	public void EndFollow()
	{
		if (m_EndFollowRequested || (!IsBeingDragged && !m_IsHybridDragActive))
			return;

		m_EndFollowRequested = true;
		m_IsHybridDragActive = false;

		UnitRagdollController ragdollController = m_RagdollController;

		m_CurrentDragger = null;
		m_HandAnchor = null;
		m_GripReference = null;
		m_GripBody = null;
		m_GripLocalRotationInHand = Quaternion.identity;
		m_GripPositionSmoothVelocity = Vector3.zero;
		m_UseExplicitOffsets = false;
		m_ExplicitGripPositionOffset = Vector3.zero;
		m_ExplicitGripRotationEuler = Vector3.zero;
		m_BakedHandSpaceOffset = Quaternion.identity;
		m_UseOverrideSmoothTimes = false;
		m_StableUpperPoses.Clear();
		m_KinematicStableBodies.Clear();
		m_DynamicDragBodies.Clear();

		if (m_SnapToGroundOnRelease)
			SnapLowestBonesToGround();

		if (ragdollController != null)
		{
			ragdollController.SyncRootTransformToRootBone();
			ragdollController.RestoreAfterHybridDrag();
		}

		m_EndFollowRequested = false;
	}

	/// <summary>Мгновенно применить текущие offset/rotation (работает в Pause и при правке Inspector).</summary>
	public void RefreshDragPreviewImmediate()
	{
		if (!m_IsHybridDragActive || m_HandAnchor == null || m_GripBody == null)
			return;

		CacheHandGripTarget(m_HandAnchor);
		ApplyDragPose(true);
		m_RagdollController?.SyncRootTransformToRootBone();
		Physics.SyncTransforms();
	}

	public void UpdateExplicitOffsets(Vector3 _positionOffset, Vector3 _rotationEulerOffset)
	{
		m_ExplicitGripPositionOffset = _positionOffset;
		m_ExplicitGripRotationEuler = _rotationEulerOffset;

		if (m_HandAnchor != null)
		{
			m_BakedHandSpaceOffset = Quaternion.AngleAxis(_rotationEulerOffset.y, m_HandAnchor.up)
			                       * Quaternion.AngleAxis(_rotationEulerOffset.x, m_HandAnchor.right)
			                       * Quaternion.AngleAxis(_rotationEulerOffset.z, m_HandAnchor.forward);
		}
	}

	public void SetOverrideSmoothTimes(float _positionSmoothTime, float _rotationSmoothTime)
	{
		m_UseOverrideSmoothTimes = true;
		m_OverridePositionSmoothTime = _positionSmoothTime;
		m_OverrideRotationSmoothTime = _rotationSmoothTime;
	}
	#endregion

	#region Private Methods
	private void BuildDragBodyLists()
	{
		m_StableUpperPoses.Clear();
		m_KinematicStableBodies.Clear();
		m_DynamicDragBodies.Clear();

		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			if (body == null || body == m_GripBody || body.transform == transform)
				continue;

			if (IsDynamicLimbOrLowerTorsoBone(body.name))
			{
				m_DynamicDragBodies.Add(body);
				continue;
			}

			if (!IsStableUpperBone(body.name))
				continue;

			m_KinematicStableBodies.Add(body);
			m_StableUpperPoses.Add(new StableUpperPose
			{
				Body = body,
				LocalPositionInGrip = m_GripBody.transform.InverseTransformPoint(body.position),
				LocalRotationInGrip = Quaternion.Inverse(m_GripBody.rotation) * body.rotation
			});
		}
	}

	private static bool IsStableUpperBone(string _boneName)
	{
		if (string.IsNullOrEmpty(_boneName))
			return false;

		if (_boneName == "Spine_02" || _boneName == "Neck" || _boneName == "Head")
			return true;

		if (_boneName.Contains("Shoulder"))
			return true;

		return false;
	}

	private static bool IsDynamicLimbOrLowerTorsoBone(string _boneName)
	{
		if (string.IsNullOrEmpty(_boneName))
			return false;

		if (_boneName == "Hips" || _boneName == "Spine_01")
			return true;

		if (_boneName.Contains("Hand") ||
		    _boneName.Contains("UpperLeg") || _boneName.Contains("LowerLeg") ||
		    _boneName.Contains("Ball") || _boneName.Contains("Toe"))
			return true;

		return false;
	}

	private Rigidbody ResolveGripRigidbody()
	{
		if (m_GripAnchorOverride != null && m_GripAnchorOverride.TryGetComponent(out Rigidbody overrideBody))
		{
			LogFollow($"ResolveGripRigidbody: using override anchor '{m_GripAnchorOverride.name}'");
			return overrideBody;
		}

		// Порядок поиска: Spine_03 → Spine_02 → Spine_01 → Hips
		string[] preferredBones = { "Spine_03", "Spine_02", "Spine_01", "Hips" };
		for (int i = 0; i < preferredBones.Length; i++)
		{
			Rigidbody body = FindRigidbodyOnBone(preferredBones[i]);
			if (body != null)
			{
				LogFollow($"ResolveGripRigidbody: found '{preferredBones[i]}' on victim '{name}'");
				return body;
			}
		}

		LogFollowWarning($"ResolveGripRigidbody: no preferred bone with Rigidbody found on '{name}', falling back to root.");
		if (m_RagdollController != null && m_RagdollController.RootBone != null &&
			m_RagdollController.RootBone.TryGetComponent(out Rigidbody rootBody))
		{
			LogFollow($"ResolveGripRigidbody: using RootBone '{m_RagdollController.RootBone.name}' as grip");
			return rootBody;
		}

		LogFollowWarning($"ResolveGripRigidbody: FAILED to resolve any grip Rigidbody on '{name}'!");
		return null;
	}

	private Transform ResolveGripReferenceTransform()
	{
		if (m_GripAnchorOverride != null)
			return m_GripAnchorOverride;

		return FindChildTransformByName(transform, "Spine_03")
		       ?? FindChildTransformByName(transform, "Spine_02")
		       ?? (m_RagdollController != null ? m_RagdollController.RootBone : null);
	}

	private Vector3 ResolveLiveGripPositionOffset()
	{
		if (m_UseExplicitOffsets)
			return m_ExplicitGripPositionOffset;

		return m_CurrentDragger != null
			? m_CurrentDragger.VictimGripLocalOffsetInHand
			: Vector3.zero;
	}

	private Vector3 ResolveLiveGripRotationOffset()
	{
		if (m_UseExplicitOffsets)
			return m_ExplicitGripRotationEuler;

		return m_CurrentDragger != null
			? m_CurrentDragger.VictimGripLocalRotationOffsetInHand
			: Vector3.zero;
	}

	private void CacheHandGripTarget(Transform _hand)
	{
		if (_hand == null)
			return;

		Vector3 targetPosition = _hand.TransformPoint(ResolveLiveGripPositionOffset());
		Quaternion targetRotation = ComputeGripTargetRotation(_hand);
		if (!IsFiniteVector3(targetPosition) || !IsFiniteQuaternion(targetRotation))
			return;

		m_CachedGripTarget = targetPosition;
		m_CachedGripRotation = targetRotation;
	}

	private Quaternion ComputeGripTargetRotation(Transform _hand)
	{
		return _hand.rotation * m_BakedHandSpaceOffset * m_GripLocalRotationInHand;
	}

	private void ApplyDragPose(bool _instant)
	{
		if (m_GripBody == null || !IsFiniteVector3(m_CachedGripTarget) || !IsFiniteQuaternion(m_CachedGripRotation))
			return;

		bool instant = _instant || IsSimulationPaused();

		if (instant)
		{
			m_GripBody.MovePosition(m_CachedGripTarget);
			m_GripBody.MoveRotation(m_CachedGripRotation);
			m_GripPositionSmoothVelocity = Vector3.zero;
		}
		else
		{
			float posSmooth = m_UseOverrideSmoothTimes ? m_OverridePositionSmoothTime : m_GripPositionSmoothTime;
			float rotSmooth = m_UseOverrideSmoothTimes ? m_OverrideRotationSmoothTime : m_GripRotationSmoothTime;

			Vector3 nextPosition = Vector3.SmoothDamp(
				m_GripBody.position,
				m_CachedGripTarget,
				ref m_GripPositionSmoothVelocity,
				posSmooth,
				m_MaxGripFollowSpeed,
				Time.fixedDeltaTime);
			m_GripBody.MovePosition(nextPosition);

			float rotationSmooth = Mathf.Max(0.001f, rotSmooth);
			float rotationFactor = 1f - Mathf.Exp(-Time.fixedDeltaTime / rotationSmooth);
			Quaternion nextRotation = Quaternion.Slerp(m_GripBody.rotation, m_CachedGripRotation, rotationFactor);
			m_GripBody.MoveRotation(nextRotation);
		}
	}

	private void ApplyStableUpperPosesFromGrip()
	{
		Transform gripTransform = m_GripBody.transform;
		for (int i = 0; i < m_StableUpperPoses.Count; i++)
		{
			StableUpperPose pose = m_StableUpperPoses[i];
			if (pose.Body == null)
				continue;

			Vector3 worldPosition = gripTransform.TransformPoint(pose.LocalPositionInGrip);
			Quaternion worldRotation = gripTransform.rotation * pose.LocalRotationInGrip;
			pose.Body.MovePosition(worldPosition);
			pose.Body.MoveRotation(worldRotation);
		}
	}

	private static bool IsSimulationPaused()
	{
		return !Application.isPlaying || Time.timeScale <= 0.0001f;
	}

	private void SanitizeDynamicDragBodies()
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			if (body == null || body.isKinematic || body == m_GripBody)
				continue;

			if (!IsFiniteVector3(body.position) || !IsFiniteVector3(body.linearVelocity) ||
			    !IsFiniteVector3(body.angularVelocity))
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
				body.position = body.transform.position;
				body.rotation = body.transform.rotation;
				continue;
			}

			Vector3 linearVelocity = body.linearVelocity;
			if (linearVelocity.sqrMagnitude > m_MaxDynamicLinearSpeed * m_MaxDynamicLinearSpeed)
				body.linearVelocity = Vector3.ClampMagnitude(linearVelocity, m_MaxDynamicLinearSpeed);

			Vector3 angularVelocity = body.angularVelocity;
			if (angularVelocity.sqrMagnitude > m_MaxDynamicAngularSpeed * m_MaxDynamicAngularSpeed)
				body.angularVelocity = Vector3.ClampMagnitude(angularVelocity, m_MaxDynamicAngularSpeed);
		}
	}

	private static bool IsFiniteVector3(Vector3 _value)
	{
		return float.IsFinite(_value.x) && float.IsFinite(_value.y) && float.IsFinite(_value.z);
	}

	private static bool IsFiniteQuaternion(Quaternion _value)
	{
		return float.IsFinite(_value.x) && float.IsFinite(_value.y) &&
		       float.IsFinite(_value.z) && float.IsFinite(_value.w);
	}

	private void PreventFloorPenetration()
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			if (body == null || body.isKinematic)
				continue;

			Vector3 position = body.position;
			Vector3 probeOrigin = position + Vector3.up * m_FloorProbeUpMeters;
			if (!Physics.Raycast(
				    probeOrigin,
				    Vector3.down,
				    out RaycastHit hit,
				    m_FloorProbeUpMeters + m_FloorProbeDownMeters,
				    m_GroundLayers,
				    QueryTriggerInteraction.Ignore))
				continue;

			float minY = hit.point.y + m_FloorSkinMeters;
			float penetration = minY - position.y;
			if (penetration <= m_FloorPushMinPenetration)
				continue;

			body.AddForce(Vector3.up * penetration * m_FloorPushStrength, ForceMode.Acceleration);

			Vector3 velocity = body.linearVelocity;
			if (velocity.y < -m_MaxDownwardSpeed)
				body.linearVelocity = new Vector3(velocity.x, -m_MaxDownwardSpeed, velocity.z);
		}
	}

	private void AlignRagdollRigidlyToGrip(Transform _hand, Transform _gripReference, Vector3 _gripLocalOffsetInHand)
	{
		Vector3 targetGripWorld = _hand.TransformPoint(_gripLocalOffsetInHand);
		Vector3 delta = targetGripWorld - _gripReference.position;
		if (delta.sqrMagnitude < 0.000001f)
			return;

		transform.position += delta;
	}

	private void SnapLowestBonesToGround()
	{
		Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
		float maxLift = 0f;

		for (int i = 0; i < bodies.Length; i++)
		{
			Rigidbody body = bodies[i];
			if (body == null || body.transform == transform)
				continue;

			Vector3 position = body.position;
			Vector3 probeOrigin = position + Vector3.up * m_FloorProbeUpMeters;
			if (!Physics.Raycast(
				    probeOrigin,
				    Vector3.down,
				    out RaycastHit hit,
				    m_FloorProbeUpMeters + m_FloorProbeDownMeters,
				    m_GroundLayers,
				    QueryTriggerInteraction.Ignore))
				continue;

			float lift = hit.point.y + m_FloorSkinMeters - position.y;
			if (lift > maxLift)
				maxLift = lift;
		}

		if (maxLift > 0.001f)
			transform.position += Vector3.up * maxLift;
	}

	private Rigidbody FindRigidbodyOnBone(string _boneName)
	{
		Transform bone = FindChildTransformByName(transform, _boneName);
		if (bone != null && bone.TryGetComponent(out Rigidbody body))
			return body;

		return null;
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

	private void LogFollow(string _msg)
	{
		if (!m_LogDragFollow)
			return;
		Debug.Log($"[DragFollower:{name}] {_msg}", this);
	}

	private void LogFollowWarning(string _msg)
	{
		if (!m_LogDragFollow)
			return;
		Debug.LogWarning($"[DragFollower:{name}] {_msg}", this);
	}
	#endregion
}
