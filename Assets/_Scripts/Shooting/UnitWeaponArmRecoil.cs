using UnityEngine;

/// <summary>
/// Rebuilds RightShoulder → UpperArm → LowerArm around the post-recoil Hand_R pose.
/// Does not author weapon TRS or aim. Left IK (order 250) still follows the weapon child.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UnitWeaponRecoil))]
[DefaultExecutionOrder(220)]
public sealed class UnitWeaponArmRecoil : MonoBehaviour
{
	#region Constants
	private const float c_ImpulseEpsilon = 0.001f;
	private const float c_SolverEpsilon = 0.001f;
	private const float c_PoleEpsilonSqr = 1e-8f;
	private const float c_HandErrorCorrectionMeters = 0.005f;
	/// <summary>
	/// Recoil must have at least this much of its unit length in the IK plane
	/// (sin of the angle vs the arm). Below that the leftover is noise and looks lateral.
	/// </summary>
	private const float c_MinRecoilOnPlaneSqr = 0.04f;
	private const string c_LogTag = "[ArmRecoil]";
	#endregion

	#region Serialized Fields
	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitEquipment m_Equipment;
	[SerializeField] private UnitWeaponRecoil m_WeaponRecoil;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;

	[Header("Impulse")]
	[SerializeField, Min(0.1f)] private float m_MaxArmImpulse = 2.5f;
	[SerializeField, Min(0.01f)] private float m_ArmResponseTau = 0.13f;
	[SerializeField, Min(0.01f)] private float m_ShoulderResponseTau = 0.18f;
	[SerializeField, Min(0.01f)] private float m_RecoveryTau = 0.13f;
	[Tooltip("Fraction of each shot's impulse applied to the arm the same frame. Tau alone leaves the first shot near zero.")]
	[SerializeField, Range(0f, 1f)] private float m_ArmShotCatchup = 0.9f;
	[Tooltip("Smaller than arm catchup so the shoulder lags the elbow.")]
	[SerializeField, Range(0f, 1f)] private float m_ShoulderShotCatchup = 0.5f;

	[Header("Shoulder")]
	[SerializeField, Min(0f)] private float m_ShoulderStrength = 1.5f;
	[SerializeField, Min(0f)] private float m_ShoulderPitch = 2.5f;
	[SerializeField, Min(0f)] private float m_ShoulderYaw = 0.8f;
	[SerializeField, Min(0f)] private float m_ShoulderRoll = 0.45f;
	[SerializeField, Min(0.1f)] private float m_MaxShoulderPitch = 3f;
	[SerializeField, Min(0.1f)] private float m_MaxShoulderYaw = 2f;
	[SerializeField, Min(0.1f)] private float m_MaxShoulderRoll = 2f;

	[Header("Arm carry")]
	[Tooltip("Fraction of the gun kick the shoulder follows. Keep below Elbow Carry.")]
	[SerializeField, Range(0f, 1f)] private float m_ShoulderCarry = 0.22f;
	[Tooltip("Fraction of the gun kick the elbow follows. Between shoulder and the hand (1).")]
	[SerializeField, Range(0f, 1f)] private float m_ElbowCarry = 0.5f;

	[Header("Elbow pole")]
	[SerializeField, Min(0f)] private float m_ElbowStrength = 1.5f;
	[Tooltip("Unused while Arm Carry is 1: pole orbit around the arm is sideways in aim.")]
	[SerializeField, Min(0f)] private float m_ElbowBackward = 0f;
	[SerializeField, Min(0f)] private float m_ElbowDown = 0f;
	[SerializeField, Min(1f)] private float m_MaxPoleRotation = 18f;

	[Header("Quality")]
	[SerializeField, Min(0.1f)] private float m_FullQualityDistanceMeters = 12f;
	[SerializeField, Min(0.1f)] private float m_LightQualityDistanceMeters = 25f;
	[SerializeField, Min(0.05f)] private float m_QualityCheckIntervalSeconds = 0.25f;

	[Header("Debug")]
	[SerializeField] private bool m_DebugDraw;
	[SerializeField] private bool m_LogOnShot;
	#endregion

	#region Private Fields
	private Transform m_RightShoulder;
	private Transform m_RightUpperArm;
	private Transform m_RightLowerArm;
	private Transform m_RightHand;

	private float m_TargetImpulse;
	private float m_ArmImpulse;
	private float m_ShoulderImpulse;
	private WeaponArmRecoilState m_CurrentState;
	private ArmRecoilQuality m_Quality = ArmRecoilQuality.Off;
	private float m_NextQualityCheckTime;
	private bool m_PendingShotLog;

	private Vector3 m_HandTargetPosition;
	private Quaternion m_HandTargetRotation = Quaternion.identity;
	private bool m_HasHandTarget;

	private Vector3 m_DebugBasePole;
	private Vector3 m_DebugDesiredPole;
	private Vector3 m_DebugTargetElbow;
	private Vector3 m_DebugShoulderPosition;
	private bool m_HasDebugGeometry;
	private float m_DebugShoulderDegrees;
	private float m_DebugPoleDeltaDegrees;
	private float m_DebugHandErrorMeters;
	private float m_LastSolveHandErrorMeters;
	private float m_LastRestoreHandErrorMeters;
	private float m_LastHandRotationErrorDegrees;
	private float m_LastElbowMoveMeters;
	private float m_LastElbowBackMeters;
	private float m_LastElbowSideMeters;
	private float m_LastElbowUpMeters;
	private float m_LastCarryMeters;
	private float m_LastShoulderMoveMeters;
	private float m_LastRecoilOnPlane;
	private float m_LastArmBarrelAngleDegrees;
	private string m_LastPoleMode = "Keep";
	#endregion

	#region Public Properties
	public WeaponArmRecoilState CurrentState => m_CurrentState;
	public ArmRecoilQuality Quality => m_Quality;
	public float CurrentImpulse => m_ArmImpulse;
	public float TargetImpulse => m_TargetImpulse;
	public float ShoulderImpulse => m_ShoulderImpulse;
	public bool AppliedThisFrame { get; private set; }
	public float LastHandErrorMeters => m_LastSolveHandErrorMeters;
	public float LastSolveHandErrorMeters => m_LastSolveHandErrorMeters;
	public float LastRestoreHandErrorMeters => m_LastRestoreHandErrorMeters;
	public float LastHandRotationErrorDegrees => m_LastHandRotationErrorDegrees;
	public float LastShoulderDegrees => m_DebugShoulderDegrees;
	public float LastPoleDeltaDegrees => m_DebugPoleDeltaDegrees;
	public float LastElbowMoveMeters => m_LastElbowMoveMeters;
	/// <summary>Elbow delta along world recoil (−barrel). Positive = back with the gun.</summary>
	public float LastElbowBackMeters => m_LastElbowBackMeters;
	/// <summary>Elbow delta along character right. Near zero means no sideways flare.</summary>
	public float LastElbowSideMeters => m_LastElbowSideMeters;
	public float LastElbowUpMeters => m_LastElbowUpMeters;
	public float LastCarryMeters => m_LastCarryMeters;
	public float LastShoulderMoveMeters => m_LastShoulderMoveMeters;
	public float LastRecoilOnPlane => m_LastRecoilOnPlane;
	public float LastArmBarrelAngleDegrees => m_LastArmBarrelAngleDegrees;
	public string LastPoleMode => m_LastPoleMode;

	/// <summary>
	/// L-sweep: keep Full quality even if the camera is outside the near/mid radii.
	/// </summary>
	public bool ForceFullQuality { get; set; }
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Animator == null)
			m_Animator = GetComponent<Animator>();
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_WeaponRecoil == null)
			m_WeaponRecoil = GetComponent<UnitWeaponRecoil>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();

		CacheArmBones();
	}

	private void OnEnable()
	{
		CacheArmBones();
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
		m_NextQualityCheckTime = 0f;
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
		ResetRuntimeState();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		m_ElbowCarry = Mathf.Max(m_ElbowCarry, m_ShoulderCarry);
	}
#endif

	private void LateUpdate()
	{
		AppliedThisFrame = false;
		m_HasDebugGeometry = false;
		m_CurrentState = default;
		m_LastSolveHandErrorMeters = 0f;
		m_LastRestoreHandErrorMeters = 0f;
		m_LastHandRotationErrorDegrees = 0f;
		m_LastElbowMoveMeters = 0f;
		m_LastElbowBackMeters = 0f;
		m_LastElbowSideMeters = 0f;
		m_LastElbowUpMeters = 0f;
		m_LastCarryMeters = 0f;
		m_LastShoulderMoveMeters = 0f;
		m_LastRecoilOnPlane = 0f;
		m_LastArmBarrelAngleDegrees = 0f;
		m_LastPoleMode = "Keep";
		m_DebugShoulderDegrees = 0f;
		m_DebugPoleDeltaDegrees = 0f;

		if (!CanRunArmRecoil())
		{
			ResetRuntimeState();
			return;
		}

		RefreshQualityTierIfDue();
		bool hasImpulse = m_TargetImpulse >= c_ImpulseEpsilon
		                  || m_ArmImpulse >= c_ImpulseEpsilon
		                  || m_ShoulderImpulse >= c_ImpulseEpsilon;
		if (!hasImpulse || m_Quality == ArmRecoilQuality.Off)
		{
			TickImpulseWithoutApply(Time.deltaTime);
			return;
		}

		DampTowardTarget(Time.deltaTime);
		if (m_ArmImpulse < c_ImpulseEpsilon && m_ShoulderImpulse < c_ImpulseEpsilon)
		{
			DecayTargetImpulse(Time.deltaTime);
			return;
		}

		CaptureHandTarget();
		if (!m_HasHandTarget)
		{
			TickImpulseWithoutApply(Time.deltaTime);
			return;
		}

		Vector3 recoilDirection = GetRecoilDirection();
		float shoulder01 = Saturate(m_ShoulderImpulse, m_ShoulderStrength);
		float elbow01 = Saturate(m_ArmImpulse, m_ElbowStrength);

		m_CurrentState = new WeaponArmRecoilState
		{
			impulse = m_ArmImpulse,
			recoilDirectionWorld = recoilDirection,
			shoulderAmount = shoulder01,
			upperArmAmount = elbow01,
			elbowAmount = elbow01,
			isActive = true
		};

		Vector3 shoulderBeforeApply = m_RightShoulder != null ? m_RightShoulder.position : Vector3.zero;
		Vector3 elbowBeforeApply = m_RightLowerArm.position;
		bool applied = m_Quality == ArmRecoilQuality.Full
			? ApplyFull(recoilDirection, shoulder01, elbow01)
			: ApplyLight(recoilDirection, shoulder01);

		if (applied)
		{
			if (m_RightShoulder != null)
				m_LastShoulderMoveMeters = Vector3.Distance(m_RightShoulder.position, shoulderBeforeApply);
			if (m_RightLowerArm != null)
			{
				Vector3 elbowDelta = m_RightLowerArm.position - elbowBeforeApply;
				m_LastElbowMoveMeters = elbowDelta.magnitude;
				m_LastElbowBackMeters = Vector3.Dot(elbowDelta, recoilDirection);
				m_LastElbowSideMeters = Vector3.Dot(elbowDelta, transform.right);
				m_LastElbowUpMeters = Vector3.Dot(elbowDelta, Vector3.up);
			}
			MeasureHandError();
			m_LastSolveHandErrorMeters = m_DebugHandErrorMeters;
			RestoreHandPose();
			MeasureHandError();
			m_LastRestoreHandErrorMeters = m_DebugHandErrorMeters;
			m_LastHandRotationErrorDegrees = m_HasHandTarget && m_RightHand != null
				? Quaternion.Angle(m_RightHand.rotation, m_HandTargetRotation)
				: 0f;
			AppliedThisFrame = true;
			TryLogShot();
		}

		DecayTargetImpulse(Time.deltaTime);
	}

	private void OnDrawGizmosSelected()
	{
		if (!m_DebugDraw || !m_HasDebugGeometry)
			return;

		Gizmos.color = Color.green;
		Gizmos.DrawRay(m_DebugShoulderPosition, m_DebugBasePole * 0.25f);
		Gizmos.color = Color.yellow;
		Gizmos.DrawRay(m_DebugShoulderPosition, m_DebugDesiredPole * 0.25f);
		Gizmos.color = Color.red;
		Gizmos.DrawSphere(m_DebugTargetElbow, 0.015f);
		Gizmos.color = Color.cyan;
		Gizmos.DrawSphere(m_HandTargetPosition, 0.015f);
		if (m_RightUpperArm != null && m_RightLowerArm != null && m_RightHand != null)
		{
			Gizmos.color = Color.white;
			Gizmos.DrawLine(m_RightUpperArm.position, m_RightLowerArm.position);
			Gizmos.DrawLine(m_RightLowerArm.position, m_RightHand.position);
		}
	}
	#endregion

	#region Private Methods
	private void CacheArmBones()
	{
		if (m_Animator == null)
			return;

		m_RightShoulder = m_Animator.GetBoneTransform(HumanBodyBones.RightShoulder);
		m_RightUpperArm = m_Animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
		m_RightLowerArm = m_Animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
		m_RightHand = m_Equipment != null && m_Equipment.RightHandAnchor != null
			? m_Equipment.RightHandAnchor
			: m_Animator.GetBoneTransform(HumanBodyBones.RightHand);
	}

	private bool CanRunArmRecoil()
	{
		if (m_WeaponRecoil == null || !m_WeaponRecoil.isActiveAndEnabled)
			return false;
		if (!m_WeaponRecoil.ShouldApplyOverlayThisFrame())
			return false;
		if (m_RightUpperArm == null || m_RightLowerArm == null || m_RightHand == null)
			CacheArmBones();
		return m_RightUpperArm != null && m_RightLowerArm != null && m_RightHand != null;
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		float added = m_WeaponRecoil != null ? m_WeaponRecoil.LastAddedVisualImpulse : 0f;
		if (added <= 0f)
			return;

		float multiplier = 1f;
		WeaponDefinition definition = m_WeaponRuntime != null
			? m_WeaponRuntime.CurrentWeaponDefinition
			: null;
		if (definition != null)
			multiplier = definition.ArmRecoilMultiplier;

		float addedScaled = added * multiplier;
		m_TargetImpulse = Mathf.Min(m_TargetImpulse + addedScaled, m_MaxArmImpulse);
		m_ArmImpulse = Mathf.Min(
			m_ArmImpulse + addedScaled * Mathf.Clamp01(m_ArmShotCatchup),
			m_TargetImpulse);
		m_ShoulderImpulse = Mathf.Min(
			m_ShoulderImpulse + addedScaled * Mathf.Clamp01(m_ShoulderShotCatchup),
			m_TargetImpulse);
		m_PendingShotLog = m_LogOnShot;
	}

	private void HandleEquipmentChanged()
	{
		CacheArmBones();
		ResetRuntimeState();
	}

	private void ResetRuntimeState()
	{
		m_TargetImpulse = 0f;
		m_ArmImpulse = 0f;
		m_ShoulderImpulse = 0f;
		m_CurrentState = default;
		m_HasHandTarget = false;
		m_PendingShotLog = false;
		AppliedThisFrame = false;
	}

	private void RefreshQualityTierIfDue()
	{
		if (ForceFullQuality)
		{
			m_Quality = ArmRecoilQuality.Full;
			return;
		}

		if (Time.time < m_NextQualityCheckTime)
			return;

		m_NextQualityCheckTime = Time.time + m_QualityCheckIntervalSeconds;
		m_Quality = ResolveQualityTier();
	}

	private ArmRecoilQuality ResolveQualityTier()
	{
		if (ForceFullQuality)
			return ArmRecoilQuality.Full;

		Vector3 sample = m_RightHand != null ? m_RightHand.position : transform.position;
		if (!WeaponVfxUtility.TryGetEffectViewerPosition(out Vector3 viewer))
			return ArmRecoilQuality.Off;

		float sqr = (sample - viewer).sqrMagnitude;
		float full = m_FullQualityDistanceMeters;
		float light = Mathf.Max(m_LightQualityDistanceMeters, full);
		if (sqr <= full * full)
			return ArmRecoilQuality.Full;
		if (sqr <= light * light)
			return ArmRecoilQuality.Light;
		return ArmRecoilQuality.Off;
	}

	private void DampTowardTarget(float _deltaTime)
	{
		m_ArmImpulse = Damp(m_ArmImpulse, m_TargetImpulse, m_ArmResponseTau, _deltaTime);
		m_ShoulderImpulse = Damp(m_ShoulderImpulse, m_TargetImpulse, m_ShoulderResponseTau, _deltaTime);
		if (m_ArmImpulse < c_ImpulseEpsilon)
			m_ArmImpulse = 0f;
		if (m_ShoulderImpulse < c_ImpulseEpsilon)
			m_ShoulderImpulse = 0f;
	}

	private void TickImpulseWithoutApply(float _deltaTime)
	{
		DecayTargetImpulse(_deltaTime);
		DampTowardTarget(_deltaTime);
	}

	private void DecayTargetImpulse(float _deltaTime)
	{
		float tau = Mathf.Max(0.01f, m_RecoveryTau);
		m_TargetImpulse *= Mathf.Exp(-_deltaTime / tau);
		if (m_TargetImpulse < c_ImpulseEpsilon)
			m_TargetImpulse = 0f;
	}

	private void CaptureHandTarget()
	{
		m_HandTargetPosition = m_RightHand.position;
		m_HandTargetRotation = m_RightHand.rotation;
		m_HasHandTarget = true;
	}

	private Vector3 GetRecoilDirection()
	{
		if (m_WeaponRecoil != null)
		{
			Vector3 direction = m_WeaponRecoil.GetCurrentRecoilDirectionWorld();
			if (direction.sqrMagnitude > c_PoleEpsilonSqr)
				return direction.normalized;
		}

		return -transform.forward;
	}

	private bool ApplyFull(Vector3 _recoilDirection, float _shoulder01, float _elbow01)
	{
		CaptureArmDiagnostics(_recoilDirection);
		m_LastPoleMode = "Falloff";
		m_DebugPoleDeltaDegrees = 0f;
		return ApplyFalloffCarry();
	}

	private bool ApplyLight(Vector3 _recoilDirection, float _shoulder01)
	{
		CaptureArmDiagnostics(_recoilDirection);
		m_LastPoleMode = "Falloff";
		m_DebugPoleDeltaDegrees = 0f;
		return ApplyFalloffCarry();
	}

	private void CaptureArmDiagnostics(Vector3 _recoilDirection)
	{
		m_LastRecoilOnPlane = 0f;
		m_LastArmBarrelAngleDegrees = 0f;
		if (m_RightUpperArm == null)
			return;

		Vector3 armAxis = m_HandTargetPosition - m_RightUpperArm.position;
		if (armAxis.sqrMagnitude < c_PoleEpsilonSqr)
			return;

		armAxis.Normalize();
		m_LastArmBarrelAngleDegrees = Vector3.Angle(armAxis, -_recoilDirection);
		m_LastRecoilOnPlane = Vector3.ProjectOnPlane(_recoilDirection, armAxis).magnitude;
	}

	private bool ApplyFalloffCarry()
	{
		m_LastCarryMeters = 0f;
		if (m_RightShoulder == null || m_RightUpperArm == null || m_RightLowerArm == null || m_WeaponRecoil == null)
			return false;

		Vector3 kickWorld = m_WeaponRecoil.GetCurrentKickTranslationWorld();
		m_LastCarryMeters = kickWorld.magnitude;
		if (kickWorld.sqrMagnitude < 1e-10f)
			return true;

		float shoulderCarry = Mathf.Clamp01(m_ShoulderCarry);
		float elbowCarry = Mathf.Clamp(Mathf.Max(m_ElbowCarry, shoulderCarry), 0f, 1f);

		Vector3 elbowRest = m_RightLowerArm.position;
		m_RightShoulder.position += kickWorld * shoulderCarry;

		Vector3 desiredElbow = elbowRest + kickWorld * elbowCarry;
		m_DebugTargetElbow = desiredElbow;
		m_DebugShoulderPosition = m_RightUpperArm.position;
		m_HasDebugGeometry = true;

		Vector3 upperPos = m_RightUpperArm.position;
		Vector3 currentUpper = m_RightLowerArm.position - upperPos;
		Vector3 desiredUpper = desiredElbow - upperPos;
		if (currentUpper.sqrMagnitude > c_PoleEpsilonSqr && desiredUpper.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightUpperArm.rotation = Quaternion.FromToRotation(currentUpper, desiredUpper) * m_RightUpperArm.rotation;

		m_RightLowerArm.position = desiredElbow;

		Vector3 currentLower = m_RightHand.position - m_RightLowerArm.position;
		Vector3 desiredLower = m_HandTargetPosition - m_RightLowerArm.position;
		if (currentLower.sqrMagnitude > c_PoleEpsilonSqr && desiredLower.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightLowerArm.rotation = Quaternion.FromToRotation(currentLower, desiredLower) * m_RightLowerArm.rotation;

		return true;
	}

	private void ApplyShoulderOffset(Vector3 _recoilDirection, float _strength01)
	{
		m_DebugShoulderDegrees = 0f;
		if (m_RightShoulder == null || _strength01 < 0.001f)
			return;

		float pitch = Mathf.Min(_strength01 * m_ShoulderPitch, m_MaxShoulderPitch);
		float yaw = Mathf.Min(_strength01 * m_ShoulderYaw, m_MaxShoulderYaw);
		float roll = Mathf.Min(_strength01 * m_ShoulderRoll, m_MaxShoulderRoll);
		if (pitch < 0.001f && yaw < 0.001f && roll < 0.001f)
			return;

		Vector3 pitchAxis = Vector3.Cross(m_RightShoulder.up, _recoilDirection);
		if (pitchAxis.sqrMagnitude < c_PoleEpsilonSqr)
			pitchAxis = m_RightShoulder.right;
		else
			pitchAxis.Normalize();

		Quaternion offset = Quaternion.AngleAxis(pitch, pitchAxis);
		if (yaw >= 0.001f)
			offset = Quaternion.AngleAxis(yaw, m_RightShoulder.up) * offset;
		if (roll >= 0.001f)
			offset = Quaternion.AngleAxis(roll, m_RightShoulder.forward) * offset;

		m_RightShoulder.rotation = offset * m_RightShoulder.rotation;
		m_DebugShoulderDegrees = pitch;
	}

	private bool TryCalculateBaseElbowPole(out Vector3 _pole)
	{
		_pole = Vector3.zero;
		Vector3 shoulder = m_RightUpperArm.position;
		Vector3 elbow = m_RightLowerArm.position;
		Vector3 hand = m_RightHand.position;
		Vector3 armAxis = hand - shoulder;
		if (armAxis.sqrMagnitude < c_PoleEpsilonSqr)
			return false;

		armAxis.Normalize();
		Vector3 elbowOffset = elbow - shoulder;
		_pole = elbowOffset - armAxis * Vector3.Dot(elbowOffset, armAxis);
		if (_pole.sqrMagnitude < c_PoleEpsilonSqr)
			return false;

		_pole.Normalize();
		return true;
	}

	private Vector3 CalculateRecoilPole(
		Vector3 _basePole,
		Vector3 _recoilDirection,
		Vector3 _handTarget,
		float _strength)
	{
		Vector3 armAxis = _handTarget - m_RightUpperArm.position;
		if (armAxis.sqrMagnitude < c_PoleEpsilonSqr)
			return _basePole;
		armAxis.Normalize();

		// Elbow can only travel on the circle ⊥ arm axis. Push that circle along recoil,
		// not along body-right (inward) — that was the sideways flare.
		Vector3 backOnPlane = Vector3.ProjectOnPlane(_recoilDirection, armAxis);
		if (backOnPlane.sqrMagnitude >= c_MinRecoilOnPlaneSqr)
		{
			backOnPlane.Normalize();
		}
		else
		{
			backOnPlane = Vector3.ProjectOnPlane(-transform.forward, armAxis);
			backOnPlane -= transform.right * Vector3.Dot(backOnPlane, transform.right);
			if (backOnPlane.sqrMagnitude < c_PoleEpsilonSqr)
				backOnPlane = Vector3.zero;
			else
				backOnPlane.Normalize();
		}

		Vector3 downOnPlane = Vector3.ProjectOnPlane(Vector3.down, armAxis);
		if (downOnPlane.sqrMagnitude > c_PoleEpsilonSqr)
			downOnPlane.Normalize();
		else
			downOnPlane = Vector3.zero;

		Vector3 desired = _basePole
		                  + backOnPlane * (m_ElbowBackward * _strength)
		                  + downOnPlane * (m_ElbowDown * _strength);
		desired = Vector3.ProjectOnPlane(desired, armAxis);
		if (desired.sqrMagnitude < c_PoleEpsilonSqr)
			return _basePole;

		desired.Normalize();
		float maxRadians = m_MaxPoleRotation * Mathf.Deg2Rad * Mathf.Clamp01(_strength);
		return Vector3.RotateTowards(_basePole, desired, maxRadians, 0f);
	}

	private bool SolveArmPosition(Vector3 _handTarget, Vector3 _pole, bool _allowCorrection)
	{
		if (!TrySolveTwoBone(_handTarget, _pole))
			return false;

		if (_allowCorrection)
		{
			float error = Vector3.Distance(m_RightHand.position, _handTarget);
			if (error >= c_HandErrorCorrectionMeters)
				TrySolveTwoBone(_handTarget, _pole);
		}

		return true;
	}

	private bool TrySolveTwoBone(Vector3 _handTarget, Vector3 _pole)
	{
		Vector3 shoulder = m_RightUpperArm.position;
		float lenUpper = Vector3.Distance(shoulder, m_RightLowerArm.position);
		float lenLower = Vector3.Distance(m_RightLowerArm.position, m_RightHand.position);
		if (lenUpper < c_SolverEpsilon || lenLower < c_SolverEpsilon)
			return false;

		Vector3 toTarget = _handTarget - shoulder;
		float distance = toTarget.magnitude;
		if (distance < c_SolverEpsilon)
			return false;

		float maxReach = lenUpper + lenLower - c_SolverEpsilon;
		float minReach = Mathf.Abs(lenUpper - lenLower) + c_SolverEpsilon;
		if (distance > maxReach + 0.05f)
			return false;

		distance = Mathf.Clamp(distance, minReach, maxReach);
		Vector3 dir = toTarget / Mathf.Max(toTarget.magnitude, c_SolverEpsilon);

		Vector3 pole = _pole;
		pole -= dir * Vector3.Dot(pole, dir);
		if (pole.sqrMagnitude < c_PoleEpsilonSqr)
			return false;
		pole.Normalize();

		Vector3 side = Vector3.Cross(pole, dir);
		if (side.sqrMagnitude < c_PoleEpsilonSqr)
			return false;
		side.Normalize();
		Vector3 bend = Vector3.Cross(dir, side);
		if (bend.sqrMagnitude < c_PoleEpsilonSqr)
			return false;
		bend.Normalize();

		float cosUpper = (lenUpper * lenUpper + distance * distance - lenLower * lenLower)
		                 / (2f * lenUpper * distance);
		cosUpper = Mathf.Clamp(cosUpper, -1f, 1f);
		float upperAngle = Mathf.Acos(cosUpper);
		float along = Mathf.Cos(upperAngle) * lenUpper;
		float height = Mathf.Sin(upperAngle) * lenUpper;
		Vector3 targetElbow = shoulder + dir * along + bend * height;

		m_DebugShoulderPosition = shoulder;
		m_DebugTargetElbow = targetElbow;
		m_HasDebugGeometry = true;

		Vector3 currentUpper = m_RightLowerArm.position - shoulder;
		Vector3 desiredUpper = targetElbow - shoulder;
		if (currentUpper.sqrMagnitude > c_PoleEpsilonSqr && desiredUpper.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightUpperArm.rotation = Quaternion.FromToRotation(currentUpper, desiredUpper) * m_RightUpperArm.rotation;

		Vector3 currentLower = m_RightHand.position - m_RightLowerArm.position;
		Vector3 desiredLower = _handTarget - m_RightLowerArm.position;
		if (currentLower.sqrMagnitude > c_PoleEpsilonSqr && desiredLower.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightLowerArm.rotation = Quaternion.FromToRotation(currentLower, desiredLower) * m_RightLowerArm.rotation;

		return true;
	}

	private void AimBonesAtHandTarget()
	{
		Vector3 currentUpper = m_RightLowerArm.position - m_RightUpperArm.position;
		Vector3 desiredUpper = m_HandTargetPosition - m_RightUpperArm.position;
		if (currentUpper.sqrMagnitude > c_PoleEpsilonSqr && desiredUpper.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightUpperArm.rotation = Quaternion.FromToRotation(currentUpper, desiredUpper) * m_RightUpperArm.rotation;

		Vector3 currentLower = m_RightHand.position - m_RightLowerArm.position;
		Vector3 desiredLower = m_HandTargetPosition - m_RightLowerArm.position;
		if (currentLower.sqrMagnitude > c_PoleEpsilonSqr && desiredLower.sqrMagnitude > c_PoleEpsilonSqr)
			m_RightLowerArm.rotation = Quaternion.FromToRotation(currentLower, desiredLower) * m_RightLowerArm.rotation;
	}

	private void RestoreHandPose()
	{
		if (!m_HasHandTarget || m_RightHand == null)
			return;

		m_RightHand.SetPositionAndRotation(m_HandTargetPosition, m_HandTargetRotation);
	}

	private void MeasureHandError()
	{
		if (!m_HasHandTarget || m_RightHand == null)
		{
			m_DebugHandErrorMeters = 0f;
			return;
		}

		m_DebugHandErrorMeters = Vector3.Distance(m_RightHand.position, m_HandTargetPosition);
	}

	private void TryLogShot()
	{
		if (!m_PendingShotLog)
			return;

		m_PendingShotLog = false;
		Debug.Log(
			$"{c_LogTag} impulse={m_ArmImpulse:F3} shoulder={m_DebugShoulderDegrees:F2} " +
			$"pole={m_LastPoleMode} handKick={m_LastCarryMeters:F4} elbow={m_LastElbowMoveMeters:F4} " +
			$"shoulder={m_LastShoulderMoveMeters:F4} onPlane={m_LastRecoilOnPlane:F2} armBarrel={m_LastArmBarrelAngleDegrees:F1} " +
			$"elbowBack={m_LastElbowBackMeters:F4} elbowSide={m_LastElbowSideMeters:F4} elbowUp={m_LastElbowUpMeters:F4} " +
			$"solveErr={m_LastSolveHandErrorMeters:F4} " +
			$"restoreErr={m_LastRestoreHandErrorMeters:F4} rotError={m_LastHandRotationErrorDegrees:F2} " +
			$"quality={m_Quality}",
			this);
	}

	private static float Saturate(float _impulse, float _strength)
	{
		return 1f - Mathf.Exp(-Mathf.Max(0f, _impulse) * Mathf.Max(0f, _strength));
	}

	private static float Damp(float _current, float _target, float _tau, float _deltaTime)
	{
		float t = 1f - Mathf.Exp(-_deltaTime / Mathf.Max(0.001f, _tau));
		return Mathf.Lerp(_current, _target, t);
	}
	#endregion
}
