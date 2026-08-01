using System.Collections;

using UnityEngine;



/// <summary>

/// Подъём сражённого юнита на плечи и переноска.

/// Жертва крепится через Transform.parent к Spine несущего, на жертве играет Fireman'sCarry1 (full-body pose).

/// </summary>

[DisallowMultipleComponent]

[DefaultExecutionOrder(63)]

public sealed class UnitFiremanCarryController : MonoBehaviour

{

	#region Constants

	public const string ParamIsCarryingFallen = "IsCarryingFallen";

	public const string ParamIsBeingCarried = "IsBeingCarried";

	public const string CarriedPoseLayerName = "Carried_Pose";



	private const string c_MedkitLayerName = UnitSelfStabilizationController.MedkitHandsLayerName;

	private const float c_ApproachArriveDistance = UnitFallenApproachUtility.ArriveDistanceMeters;

	private const float c_MaxApproachSeconds = 45f;

	private const int c_AnimatorSettleFrames = 3;



	private static readonly int s_IsCarryingFallen = Animator.StringToHash(ParamIsCarryingFallen);

	private static readonly int s_IsBeingCarried = Animator.StringToHash(ParamIsBeingCarried);

	#endregion



	#region Serialized Fields

	[SerializeField] private RtsUnitMember m_RtsMember;

	[SerializeField] private UnitHealth m_Health;

	[SerializeField] private UnitConsciousness m_Consciousness;

	[SerializeField] private UnitBusyState m_BusyState;

	[SerializeField] private UnitEquipment m_UnitEquipment;

	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;

	[SerializeField] private UnitClickToMove m_ClickToMove;

	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;

	[SerializeField] private Animator m_Animator;

	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;

	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;

	[SerializeField] private UnitFallenDragController m_DragController;

	[SerializeField] private UnitWeaponReloadController m_ReloadController;



	[Header("Carry Grip")]

	[Tooltip("Оффсет жертвы относительно Spine несущего (localPosition).")]

	[SerializeField] private Vector3 m_CarryGripLocalOffset = new Vector3(0.29f, -0.26f, -0.17f);

	[Tooltip("Поворот жертвы относительно Spine несущего (localEulerAngles).")]

	[SerializeField] private Vector3 m_CarryGripRotationOffset = new Vector3(60.6f, -89.1f, 3.86f);

	[Tooltip("Куда положить жертву перед носителем при отпускании (метры вперёд по горизонтали).")]
	[SerializeField, Min(0.2f)] private float m_ReleaseForwardDistanceMeters = 0.9f;



	[Header("Presentation")]

	[SerializeField, Min(0.02f)] private float m_LayerWeightFadeSeconds = 0.2f;



	[Header("Debug")]

	[SerializeField] private bool m_LogFiremanCarry = true;

	[SerializeField] private bool m_IsCarryingFallen;

	[SerializeField] private RtsUnitMember m_DebugVictim;

	[SerializeField] private string m_DebugLastFailureReason;

	#endregion



	#region Private Fields

	private Coroutine m_SessionCoroutine;

	private RtsUnitMember m_CarriedVictim;

	private Transform m_ShoulderAnchor;

	private Transform m_VictimOriginalParent;

	private int m_MedkitHandsLayerIndex = -1;

	private float m_SmoothedLayerWeight;

	private bool m_PresentationActive;



	private Animator m_VictimAnimator;

	private UnitRagdollController m_VictimRagdoll;

	private int m_CarriedPoseLayerIndex = -1;

	#endregion



	#region Public Properties

	public bool IsCarryingFallen => m_IsCarryingFallen;

	public bool IsCarryPresentationActive => m_PresentationActive;

	public RtsUnitMember CarriedVictim => m_CarriedVictim;

	#endregion



	#region Unity Lifecycle

	private void Awake()

	{

		ResolveReferences();

		ResolveMedkitHandsLayerIndex();

	}



	private void OnEnable()

	{

		if (m_Health != null)

			m_Health.Changed += HandleHealthChanged;

		if (m_Consciousness != null)

			m_Consciousness.ConsciousnessChanged += HandleConsciousnessChanged;



		m_SmoothedLayerWeight = m_IsCarryingFallen || m_PresentationActive ? 1f : 0f;

		SyncAnimatorState();

		ApplyLayerWeightImmediate(m_SmoothedLayerWeight);

	}



	private void OnDisable()

	{

		if (m_Health != null)

			m_Health.Changed -= HandleHealthChanged;

		if (m_Consciousness != null)

			m_Consciousness.ConsciousnessChanged -= HandleConsciousnessChanged;



		ReleaseCarryImmediate();

	}



	private void Update()

	{

		SyncLayerWeight();

	}



#if UNITY_EDITOR

	private void OnValidate()

	{

		if (!Application.isPlaying || !m_IsCarryingFallen || m_CarriedVictim == null)

			return;



		m_CarriedVictim.transform.localPosition = m_CarryGripLocalOffset;

		m_CarriedVictim.transform.localRotation = Quaternion.Euler(m_CarryGripRotationOffset);

	}

#endif

	#endregion



	#region Public Methods

	public bool CanLift(RtsUnitMember _victim)

	{

		return TryValidateTarget(_victim, _allowActiveSession: false, out _, out _);

	}



	public void RequestLift(RtsUnitMember _victim)

	{

		if (m_SessionCoroutine != null && m_CarriedVictim == null)

		{



			return;

		}



		bool hasPendingSession = m_SessionCoroutine != null;

		if (!TryValidateTarget(_victim, _allowActiveSession: hasPendingSession, out string failureReason, out _))

		{

			LogWarning($"RequestLift rejected: {failureReason}");

			return;

		}



		if (m_SessionCoroutine != null)

		{

		StopCoroutine(m_SessionCoroutine);

		m_SessionCoroutine = null;

		}



		m_SessionCoroutine = StartCoroutine(CoFiremanCarrySession(_victim));

	}



	public void RequestRelease()

	{

		if (!m_IsCarryingFallen && m_SessionCoroutine == null)

			return;



		if (m_SessionCoroutine != null)

		{

			StopCoroutine(m_SessionCoroutine);

			m_SessionCoroutine = null;

		}



		ReleaseCarryImmediate();

	}

	#endregion



	#region Private Methods — Validation

	private bool TryValidateTarget(

		RtsUnitMember _victim,

		bool _allowActiveSession,

		out string _failureReason,

		out UnitRagdollController _victimRagdoll)

	{

		_failureReason = null;

		_victimRagdoll = null;



		if (_victim == null)

			return Fail("victim is null", out _failureReason);

		if (m_IsCarryingFallen)

			return Fail("already carrying", out _failureReason);

		if (!_allowActiveSession && m_SessionCoroutine != null)

			return Fail("carry session already running", out _failureReason);

		if (_victim == m_RtsMember)

			return Fail("cannot carry self", out _failureReason);



		ResolveReferences();

		if (m_Health == null || m_Health.IsDead)

			return Fail("carrier is dead", out _failureReason);

		if (m_Consciousness != null && !m_Consciousness.IsConscious)

			return Fail("carrier is unconscious", out _failureReason);

		if (m_BusyState != null && m_BusyState.IsBusy)

			return Fail($"carrier is busy: {m_BusyState.Reasons}", out _failureReason);

		if (m_SelfStabilization != null &&

		    (m_SelfStabilization.IsSelfHealing || m_SelfStabilization.IsHealPresentationActive))

			return Fail("carrier is self-stabilizing", out _failureReason);

		if (m_StabilizeOther != null &&

		    (m_StabilizeOther.IsStabilizingOther || m_StabilizeOther.IsHealPresentationActive))

			return Fail("carrier is stabilizing another", out _failureReason);

		if (m_DragController != null && m_DragController.IsDragging)

			return Fail("carrier is dragging", out _failureReason);

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)

			return Fail("carrier is reloading", out _failureReason);



		UnitFallenStateUtility.TryDescribeFallenState(_victim, out string fallenDesc);

		if (!UnitFallenStateUtility.IsFallenOrDead(_victim))

		{

			return Fail($"victim is not fallen or dead ({fallenDesc})", out _failureReason);

		}



		_victimRagdoll = _victim.GetComponentInChildren<UnitRagdollController>(true);

		if (_victimRagdoll == null)

			return Fail("victim has no ragdoll controller", out _failureReason);



		m_DebugLastFailureReason = string.Empty;

		_failureReason = null;

		return true;

	}



	private bool Fail(string _reason, out string _failureReasonOut)

	{

		_failureReasonOut = _reason;

		m_DebugLastFailureReason = _reason;

		return false;

	}

	#endregion



	#region Private Methods — Approach

	private IEnumerator CoApproachVictim(RtsUnitMember _victim)
	{
		if (m_RtsMember == null || _victim == null)
		{
			LogWarning("CoApproachVictim aborted: RTS member or victim is null.");
			yield break;
		}

		Vector3 victimFocus = UnitFallenApproachUtility.ResolveApproachFocusPosition(_victim.transform);
		float distance = UnitFallenApproachUtility.HorizontalDistance(m_RtsMember.transform.position, victimFocus);
		if (distance > UnitFallenApproachUtility.ArriveDistanceMeters)
		{
			Vector3 approachPoint = UnitFallenApproachUtility.ComputeNavMeshApproachPoint(
				m_RtsMember.transform, _victim.transform, UnitFallenApproachUtility.StandoffMeters);
			m_RtsMember.IssueMoveOrder(approachPoint, UnitClickToMove.MoveTier.Run);

			float elapsed = 0f;
			float nextRetargetTime = UnitFallenApproachUtility.RetargetIntervalSeconds;
			float stuckSeconds = 0f;
			float bestDistance = distance;
			Vector3 lastPosition = m_RtsMember.transform.position;
			Vector3 lastApproachPoint = approachPoint;

			while (elapsed < c_MaxApproachSeconds)
			{
				if (_victim == null || m_RtsMember == null)
				{
					LogWarning("CoApproachVictim interrupted: victim or carrier destroyed during approach.");
					yield break;
				}

				if (elapsed >= nextRetargetTime)
				{
					approachPoint = UnitFallenApproachUtility.ComputeNavMeshApproachPoint(
						m_RtsMember.transform, _victim.transform, UnitFallenApproachUtility.StandoffMeters);
					if (UnitFallenApproachUtility.ShouldRetargetApproach(lastApproachPoint, approachPoint))
					{
						m_RtsMember.IssueMoveOrder(approachPoint, UnitClickToMove.MoveTier.Run);
						lastApproachPoint = approachPoint;
					}

					nextRetargetTime += UnitFallenApproachUtility.RetargetIntervalSeconds;
				}

				Vector3 currentPosition = m_RtsMember.transform.position;
				victimFocus = UnitFallenApproachUtility.ResolveApproachFocusPosition(_victim.transform);
				distance = UnitFallenApproachUtility.HorizontalDistance(currentPosition, victimFocus);
				float moved = UnitFallenApproachUtility.HorizontalDistance(currentPosition, lastPosition);
				lastPosition = currentPosition;
				stuckSeconds = UnitFallenApproachUtility.UpdateStuckSeconds(
					stuckSeconds, distance, ref bestDistance, moved);

				if (UnitFallenApproachUtility.HasArrivedOrStuckCloseEnough(distance, stuckSeconds) ||
				    UnitFallenApproachUtility.ShouldAbortApproach(distance, stuckSeconds))
					break;

				elapsed += Time.deltaTime;
				yield return null;
			}

			if (!UnitFallenApproachUtility.IsWithinInteractRange(distance))
				LogWarning($"CoApproachVictim: too far after approach, distance={distance:F2}m");
			else if (distance > UnitFallenApproachUtility.ArriveDistanceMeters)
				LogWarning($"CoApproachVictim: accepted close-enough approach, distance={distance:F2}m");
		}

		m_ClickToMove?.HardStop();
		m_LocomotionDriver?.HardStop();
		yield return null;
	}



	private static Vector3 ComputeApproachPoint(Transform _carrier, Transform _victim, float _standoffMeters)

	{

		Vector3 victimPosition = _victim.position;

		Vector3 toVictim = victimPosition - _carrier.position;

		toVictim.y = 0f;



		if (toVictim.sqrMagnitude < 0.04f)

			toVictim = _victim.forward;



		toVictim.Normalize();

		return victimPosition - toVictim * _standoffMeters;

	}



	private static float HorizontalDistance(Vector3 _a, Vector3 _b)

	{

		float dx = _a.x - _b.x;

		float dz = _a.z - _b.z;

		return Mathf.Sqrt(dx * dx + dz * dz);

	}

	#endregion



	#region Private Methods — Carry Session

	private IEnumerator CoFiremanCarrySession(RtsUnitMember _victim)

	{

		if (_victim == null)

		{

			LogWarning("CoFiremanCarrySession aborted: victim is null.");

			m_SessionCoroutine = null;

			yield break;

		}



		yield return CoApproachVictim(_victim);



		if (_victim == null)

		{

			LogWarning("CoFiremanCarrySession aborted after approach: victim destroyed.");

			m_SessionCoroutine = null;

			yield break;

		}



		float approachDistance = UnitFallenApproachUtility.HorizontalDistance(
			m_RtsMember.transform.position,
			UnitFallenApproachUtility.ResolveApproachFocusPosition(_victim.transform));
		if (!UnitFallenApproachUtility.IsWithinInteractRange(approachDistance))
		{
			LogWarning($"CoFiremanCarrySession aborted: still too far ({approachDistance:F2}m).");
			m_SessionCoroutine = null;
			yield break;
		}



		if (!TryValidateTarget(_victim, _allowActiveSession: true, out string failureReason, out UnitRagdollController victimRagdoll))

		{

			LogWarning($"CoFiremanCarrySession aborted after approach: {failureReason}");

			m_SessionCoroutine = null;

			yield break;

		}



		m_CarriedVictim = _victim;

		m_DebugVictim = _victim;

		m_VictimRagdoll = victimRagdoll;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.CarryingFallen, true);

		m_PresentationActive = true;

		m_IsCarryingFallen = true;



		PrepareForCarry();

		ApplyLayerWeightImmediate(1f);

		SyncAnimatorState();



		for (int i = 0; i < c_AnimatorSettleFrames; i++)

			yield return null;



		if (_victim == null)

		{

			ReleaseCarryImmediate();

			m_SessionCoroutine = null;

			yield break;

		}



		if (m_ShoulderAnchor == null)

		{

			LogWarning("CoFiremanCarrySession: ShoulderAnchor is null — cannot attach victim.");

			ReleaseCarryImmediate();

			m_SessionCoroutine = null;

			yield break;

		}



		AttachVictim(_victim);

		m_SessionCoroutine = null;

	}



	private void PrepareForCarry()

	{

		m_ClickToMove?.HardStop();

		m_LocomotionDriver?.HardStop();

		m_ReadyHands?.SetReadyWanted(false);

		if (m_Animator != null)

		{

			AnimatorHandIk ik = m_Animator.GetComponent<AnimatorHandIk>();

			if (ik != null)

				ik.enabled = false;

		}

	}



	private void AttachVictim(RtsUnitMember _victim)

	{

		m_VictimAnimator = _victim.GetComponentInChildren<Animator>(true);



		UnitStabilizedUnconsciousPoseController victimSleepPose =
			_victim.GetComponent<UnitStabilizedUnconsciousPoseController>();
		victimSleepPose?.NotifyExternalPoseOverride(true);

		m_VictimRagdoll?.SetRagdollActive(false);



		if (m_VictimAnimator != null)

		{

			m_VictimAnimator.applyRootMotion = false;

			m_VictimAnimator.enabled = true;

			m_VictimAnimator.SetBool(s_IsBeingCarried, true);



			int carriedPoseLayerIndex = m_VictimAnimator.GetLayerIndex(CarriedPoseLayerName);

			m_CarriedPoseLayerIndex = carriedPoseLayerIndex;

			if (carriedPoseLayerIndex >= 0)

				m_VictimAnimator.SetLayerWeight(carriedPoseLayerIndex, 1f);

		}

		else

		{

			LogWarning("AttachVictim: NO Animator found on victim!");

		}



		UnityEngine.AI.NavMeshAgent victimAgent = _victim.GetComponent<UnityEngine.AI.NavMeshAgent>();

		if (victimAgent != null)

			victimAgent.enabled = false;



		m_VictimOriginalParent = _victim.transform.parent;

		_victim.transform.SetParent(m_ShoulderAnchor, true);

		_victim.transform.localPosition = m_CarryGripLocalOffset;

		_victim.transform.localRotation = Quaternion.Euler(m_CarryGripRotationOffset);



	}



	private void DetachVictim()

	{

		if (m_CarriedVictim == null)

		{



			return;

		}



		RtsUnitMember victim = m_CarriedVictim;

		if (m_VictimAnimator != null)

		{

			m_VictimAnimator.SetBool(s_IsBeingCarried, false);

			if (m_CarriedPoseLayerIndex >= 0)

				m_VictimAnimator.SetLayerWeight(m_CarriedPoseLayerIndex, 0f);

		}



		m_CarriedVictim.transform.SetParent(m_VictimOriginalParent, true);

		m_VictimOriginalParent = null;

		PlaceReleasedVictimInFront(victim);



		UnitStabilizedUnconsciousPoseController victimSleepPose =
			victim != null ? victim.GetComponent<UnitStabilizedUnconsciousPoseController>() : null;
		bool sleepPoseWillTakeOver = victimSleepPose != null &&
		                             victim.TryGetComponent(out UnitHealth victimHealth) &&
		                             victim.TryGetComponent(out UnitConsciousness victimConsciousness) &&
		                             !victimHealth.IsDead &&
		                             !victimConsciousness.IsConscious &&
		                             victimHealth.HasInjuries &&
		                             !victimHealth.HasUnstabilizedInjuries;

		if (!sleepPoseWillTakeOver)
			m_VictimRagdoll?.SetRagdollActive(true);

		victimSleepPose?.NotifyExternalPoseOverride(false);

		m_VictimRagdoll = null;

		m_VictimAnimator = null;

		m_CarriedPoseLayerIndex = -1;

	}

	private void PlaceReleasedVictimInFront(RtsUnitMember _victim)
	{
		if (_victim == null)
			return;

		Vector3 forward = transform.forward;
		forward.y = 0f;
		if (forward.sqrMagnitude < 0.0001f)
			forward = Vector3.forward;
		forward.Normalize();

		Vector3 dropPosition = transform.position + forward * m_ReleaseForwardDistanceMeters;
		dropPosition.y = _victim.transform.position.y;

		if (UnityEngine.AI.NavMesh.SamplePosition(
			    dropPosition,
			    out UnityEngine.AI.NavMeshHit hit,
			    1.5f,
			    UnityEngine.AI.NavMesh.AllAreas))
		{
			dropPosition = hit.position;
		}

		_victim.transform.position = dropPosition;
	}



	private void ReleaseCarryImmediate()

	{



		if (m_SessionCoroutine != null)

		{

			StopCoroutine(m_SessionCoroutine);

			m_SessionCoroutine = null;

		}



		DetachVictim();



		m_CarriedVictim = null;

		m_DebugVictim = null;

		m_IsCarryingFallen = false;

		m_PresentationActive = false;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.CarryingFallen, false);



		ApplyLayerWeightImmediate(0f);

		SyncAnimatorState();



		m_ClickToMove?.HardStop();

		m_LocomotionDriver?.HardStop();

		if (m_Animator != null)

		{

			AnimatorHandIk ik = m_Animator.GetComponent<AnimatorHandIk>();

			if (ik != null)

				ik.enabled = true;

		}

	}

	#endregion



	#region Private Methods — Events

	private void HandleHealthChanged()

	{

		if (!isActiveAndEnabled)

			return;

		if (m_Health != null && m_Health.IsDead)

			ReleaseCarryImmediate();

	}



	private void HandleConsciousnessChanged(bool _isConscious)

	{

		if (!_isConscious)

			RequestRelease();

	}

	#endregion



	#region Private Methods — Animator Helpers

	private void SyncAnimatorState()

	{

		if (m_Animator == null)

			return;



		m_Animator.SetBool(s_IsCarryingFallen, m_IsCarryingFallen);

		SyncLayerWeight();

	}



	private void ResolveMedkitHandsLayerIndex()

	{

		m_MedkitHandsLayerIndex = m_Animator != null

			? m_Animator.GetLayerIndex(c_MedkitLayerName)

			: -1;

	}



	private void SyncLayerWeight()

	{

		if (m_Animator == null)

			return;

		if (m_MedkitHandsLayerIndex < 0)

			ResolveMedkitHandsLayerIndex();

		if (m_MedkitHandsLayerIndex < 0)

			return;



		float targetWeight = m_PresentationActive ? 1f : 0f;

		if (!m_PresentationActive && m_BusyState != null &&

		    (m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization) ||

		     m_BusyState.HasReason(UnitBusyState.BusyReason.StabilizeOther)))

			targetWeight = 1f;

		float fadeSeconds = Mathf.Max(0.02f, m_LayerWeightFadeSeconds);

		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetWeight, Time.deltaTime / fadeSeconds);

		m_Animator.SetLayerWeight(m_MedkitHandsLayerIndex, m_SmoothedLayerWeight);

	}



	private void ApplyLayerWeightImmediate(float _weight)

	{

		if (m_Animator == null)

			return;

		if (m_MedkitHandsLayerIndex < 0)

			ResolveMedkitHandsLayerIndex();

		if (m_MedkitHandsLayerIndex < 0)

			return;



		if (_weight <= 0f && m_BusyState != null &&

		    (m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization) ||

		     m_BusyState.HasReason(UnitBusyState.BusyReason.StabilizeOther)))

			return;



		m_SmoothedLayerWeight = _weight;

		m_Animator.SetLayerWeight(m_MedkitHandsLayerIndex, m_SmoothedLayerWeight);

	}

	#endregion



	#region Private Methods — Misc

	private void ResolveReferences()

	{

		if (m_RtsMember == null)

			m_RtsMember = GetComponent<RtsUnitMember>();

		if (m_Health == null)

			m_Health = GetComponent<UnitHealth>();

		if (m_Consciousness == null)

			m_Consciousness = GetComponent<UnitConsciousness>();

		if (m_BusyState == null)

			m_BusyState = GetComponent<UnitBusyState>();

		if (m_UnitEquipment == null)

			m_UnitEquipment = GetComponent<UnitEquipment>();

		if (m_ReadyHands == null)

			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();

		if (m_ClickToMove == null)

			m_ClickToMove = GetComponent<UnitClickToMove>();

		if (m_LocomotionDriver == null)

			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();

		if (m_Animator == null)

			m_Animator = GetComponentInChildren<Animator>();

		if (m_SelfStabilization == null)

			m_SelfStabilization = GetComponent<UnitSelfStabilizationController>();

		if (m_StabilizeOther == null)

			m_StabilizeOther = GetComponent<UnitStabilizeOtherController>();

		if (m_DragController == null)

			m_DragController = GetComponent<UnitFallenDragController>();

		if (m_ReloadController == null)

			m_ReloadController = GetComponent<UnitWeaponReloadController>();

		if (m_ShoulderAnchor == null && m_Animator != null && m_Animator.isHuman)

			m_ShoulderAnchor = m_Animator.GetBoneTransform(HumanBodyBones.Spine);

	}



	private static string FormatUnit(RtsUnitMember _unit)

	{

		return _unit != null ? _unit.name : "null";

	}



	private void Log(string _message)

	{

		if (!m_LogFiremanCarry)

			return;



		Debug.Log($"[UnitFiremanCarry:{name}] {_message}", this);

	}



	private void LogWarning(string _message)

	{

		if (!m_LogFiremanCarry)

			return;



		Debug.LogWarning($"[UnitFiremanCarry:{name}] {_message}", this);

	}

	#endregion

}
