using System.Collections;
using UnityEngine;

/// <summary>
/// Оттаскивание бессознательного или мёртвого юнита: подход, присед, overlay левой руки, движение спиной вперёд.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(63)]
public sealed class UnitFallenDragController : MonoBehaviour
{
	#region Constants
	public const string ParamIsDraggingFallen = "IsDraggingFallen";
	public const string DragLeftHandLayerName = "Drag_LeftHand";

	private const float c_ApproachArriveDistance = 1f;
	private const float c_MaxApproachSeconds = 45f;
	private const float c_StanceSettleTimeoutSeconds = 4f;

	private static readonly int s_IsDraggingFallen = Animator.StringToHash(ParamIsDraggingFallen);
	#endregion

	#region Serialized Fields
	[SerializeField] private RtsUnitMember m_RtsMember;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private UnitConsciousness m_Consciousness;
	[SerializeField] private UnitHealth m_Health;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private AnimatorHandIk m_LeftHandIk;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private Transform m_LeftHandAnchor;

	[Header("Presentation")]
	[SerializeField, Min(0.02f)] private float m_LayerWeightFadeSeconds = 0.2f;
	[Tooltip("Локальный offset точки хвата жертвы относительно левой кисти тащущего (в пространстве кисти).")]
	[SerializeField] private Vector3 m_VictimGripLocalOffsetInHand = new Vector3(0f, -0.04f, 0.08f);
	[Tooltip("Доп. поворот Spine_03 в локальных Euler-градусах кисти (X=pitch, Y=yaw, Z=roll).")]
	[SerializeField] private Vector3 m_VictimGripLocalRotationOffsetInHand;
	[SerializeField, Min(0)] private int m_AttachAnimatorSettleFrames = 2;

	[Header("Debug")]
	[SerializeField] private bool m_LogFallenDrag = true;
	[SerializeField] private string m_DebugLastFailureReason;
	#endregion

	#region Private Fields
	private Coroutine m_SessionCoroutine;
	private RtsUnitMember m_DraggedVictim;
	private UnitFallenDragVictimFollower m_VictimFollower;
	private int m_DragLeftHandLayerIndex = -1;
	private float m_SmoothedLayerWeight;
	private bool m_IsDraggingFallen;
	private bool m_PresentationActive;
	#endregion

	#region Public Properties
	public bool IsDragging => m_IsDraggingFallen;
	public bool IsBackwardDragLocomotion => m_IsDraggingFallen;
	public RtsUnitMember DraggedVictim => m_DraggedVictim;
	public Transform LeftHandAnchor => m_LeftHandAnchor;
	public Vector3 VictimGripLocalOffsetInHand
	{
		get => m_VictimGripLocalOffsetInHand;
		set => m_VictimGripLocalOffsetInHand = value;
	}

	public Vector3 VictimGripLocalRotationOffsetInHand
	{
		get => m_VictimGripLocalRotationOffsetInHand;
		set => m_VictimGripLocalRotationOffsetInHand = value;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
		ResolveDragLayerIndex();
	}

	private void OnEnable()
	{
		SubscribeEvents();
	}

	private void OnDisable()
	{
		UnsubscribeEvents();
		if (m_IsDraggingFallen)
			ReleaseDragImmediate(_skipLocomotionStop: true);
	}

	private void Update()
	{
		SyncLayerWeight();
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (!Application.isPlaying || !m_IsDraggingFallen || m_VictimFollower == null)
			return;

		if (m_VictimFollower.IsHybridDragActive)
			m_VictimFollower.RefreshDragPreviewImmediate();
	}
#endif
	#endregion

	#region Public Methods
	public bool CanBeginDrag(RtsUnitMember _victim)
	{
		return TryValidateDragTarget(_victim, _allowActiveSession: false, out _);
	}

	public void RequestBeginDrag(RtsUnitMember _victim)
	{
		LogDrag($"RequestBeginDrag called. dragger='{name}', victim='{FormatUnit(_victim)}'");

		bool hasPendingSession = m_SessionCoroutine != null;
		if (!TryValidateDragTarget(_victim, _allowActiveSession: hasPendingSession, out string failureReason))
		{
			LogDragWarning($"RequestBeginDrag rejected: {failureReason}");
			return;
		}

		if (m_SessionCoroutine != null)
		{
			LogDrag("RequestBeginDrag: restarting pending drag session.");
			StopCoroutine(m_SessionCoroutine);
			m_SessionCoroutine = null;
		}

		LogDrag($"Starting drag session coroutine toward victim='{FormatUnit(_victim)}'");
		m_SessionCoroutine = StartCoroutine(CoBeginDragSession(_victim));
	}

	public void RequestReleaseDrag()
	{
		if (!m_IsDraggingFallen && m_SessionCoroutine == null)
			return;

		if (m_SessionCoroutine != null)
		{
			StopCoroutine(m_SessionCoroutine);
			m_SessionCoroutine = null;
		}

		ReleaseDragImmediate();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_Consciousness == null)
			m_Consciousness = GetComponent<UnitConsciousness>();
		if (m_Health == null)
			m_Health = GetComponent<UnitHealth>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponent<UnitStabilizeOtherController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_LeftHandIk == null && m_Animator != null)
			m_LeftHandIk = m_Animator.GetComponent<AnimatorHandIk>();
		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)
			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);
	}

	private void SubscribeEvents()
	{
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged += HandleDraggerConsciousnessChanged;
		if (m_Health != null)
			m_Health.Changed += HandleDraggerHealthChanged;
	}

	private void UnsubscribeEvents()
	{
		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged -= HandleDraggerConsciousnessChanged;
		if (m_Health != null)
			m_Health.Changed -= HandleDraggerHealthChanged;

		UnsubscribeVictimEvents();
	}

	private void SubscribeVictimEvents()
	{
		if (m_DraggedVictim == null)
			return;

		UnitConsciousness victimConsciousness = m_DraggedVictim.GetComponent<UnitConsciousness>();
		if (victimConsciousness != null)
			victimConsciousness.ConsciousnessChanged += HandleVictimConsciousnessChanged;
	}

	private void UnsubscribeVictimEvents()
	{
		if (m_DraggedVictim == null)
			return;

		UnitConsciousness victimConsciousness = m_DraggedVictim.GetComponent<UnitConsciousness>();
		if (victimConsciousness != null)
			victimConsciousness.ConsciousnessChanged -= HandleVictimConsciousnessChanged;
	}

	private void HandleDraggerConsciousnessChanged(bool _isConscious)
	{
		if (!_isConscious)
			RequestReleaseDrag();
	}

	private void HandleDraggerHealthChanged()
	{
		if (m_Health != null && m_Health.IsDead)
			RequestReleaseDrag();
	}

	private void HandleVictimConsciousnessChanged(bool _isConscious)
	{
		if (_isConscious)
			RequestReleaseDrag();
	}

	private bool IsDraggerOperational()
	{
		if (m_Consciousness != null && !m_Consciousness.IsConscious)
			return false;
		if (m_Health != null && m_Health.IsDead)
			return false;
		return true;
	}

	private IEnumerator CoBeginDragSession(RtsUnitMember _victim)
	{
		if (_victim == null)
		{
			LogDragWarning("CoBeginDragSession aborted: victim is null.");
			m_SessionCoroutine = null;
			yield break;
		}

		LogDrag($"CoBeginDragSession: approaching victim='{FormatUnit(_victim)}'");
		yield return CoApproachVictim(_victim);

		if (_victim == null)
		{
			LogDragWarning("CoBeginDragSession aborted after approach: victim destroyed.");
			m_SessionCoroutine = null;
			yield break;
		}

		if (!TryValidateDragTarget(_victim, _allowActiveSession: true, out string failureReason))
		{
			LogDragWarning($"CoBeginDragSession aborted after approach: {failureReason}");
			m_SessionCoroutine = null;
			yield break;
		}

		LogDrag($"CoBeginDragSession: approach complete, starting drag presentation for victim='{FormatUnit(_victim)}'");
		BeginDragPresentation(_victim);
		m_SessionCoroutine = null;
	}

	private IEnumerator CoApproachVictim(RtsUnitMember _victim)
	{
		if (m_RtsMember == null || _victim == null)
		{
			LogDragWarning("CoApproachVictim aborted: dragger RTS member or victim is null.");
			yield break;
		}

		float distance = Vector3.Distance(m_RtsMember.transform.position, _victim.transform.position);
		LogDrag($"CoApproachVictim: initial distance={distance:F2}m (arrive<={c_ApproachArriveDistance:F2}m)");
		if (distance > c_ApproachArriveDistance)
		{
			Vector3 approachPoint = ComputeApproachPoint(m_RtsMember.transform, _victim.transform, c_ApproachArriveDistance * 0.85f);
			LogDrag($"CoApproachVictim: issuing move order to {approachPoint}");
			m_RtsMember.IssueMoveOrder(approachPoint, UnitClickToMove.MoveTier.Walk);

			float elapsed = 0f;
			while (elapsed < c_MaxApproachSeconds)
			{
				if (_victim == null || m_RtsMember == null)
				{
					LogDragWarning("CoApproachVictim interrupted: victim or dragger destroyed during approach.");
					yield break;
				}

				distance = Vector3.Distance(m_RtsMember.transform.position, _victim.transform.position);
				if (distance <= c_ApproachArriveDistance)
					break;

				elapsed += Time.deltaTime;
				yield return null;
			}

			LogDrag($"CoApproachVictim finished waiting after {elapsed:F1}s, distance={distance:F2}m");
		}

		m_ClickToMove?.HardStop();
		m_LocomotionDriver?.HardStop();
		yield return null;
	}

	private static Vector3 ComputeApproachPoint(Transform _dragger, Transform _victim, float _standoffMeters)
	{
		Vector3 victimPosition = _victim.position;
		Vector3 toVictim = victimPosition - _dragger.position;
		toVictim.y = 0f;

		if (toVictim.sqrMagnitude < 0.04f)
			toVictim = _victim.forward;

		toVictim.Normalize();
		return victimPosition - toVictim * _standoffMeters;
	}

	private void BeginDragPresentation(RtsUnitMember _victim)
	{
		m_DraggedVictim = _victim;
		m_VictimFollower = _victim.GetComponentInChildren<UnitFallenDragVictimFollower>(true);
		if (m_VictimFollower == null)
			m_VictimFollower = _victim.gameObject.AddComponent<UnitFallenDragVictimFollower>();

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.DraggingFallen, true);
		m_PresentationActive = true;
		m_IsDraggingFallen = true;

		m_LeftHandIk?.RequestClearLeftHandIk();

		m_ClickToMove?.HardStop();
		m_LocomotionDriver?.HardStop();

		if (m_Stance != null)
			m_Stance.RequestStance(LocomotionStance.Crouch);

		LogDrag($"BeginDragPresentation: crouch requested, dragLayerIndex={m_DragLeftHandLayerIndex}");
		StartCoroutine(CoWaitForStanceAndAttach());
	}

	private IEnumerator CoWaitForStanceAndAttach()
	{
		float endTime = Time.time + c_StanceSettleTimeoutSeconds;
		while (Time.time < endTime)
		{
			if (!m_IsDraggingFallen || m_DraggedVictim == null)
			{
				LogDragWarning("CoWaitForStanceAndAttach aborted: drag cancelled before stance settled.");
				yield break;
			}

			bool inCrouch = m_Stance == null || m_Stance.CurrentStance == LocomotionStance.Crouch;
			bool stanceBusy = m_BusyState != null && m_BusyState.HasReason(UnitBusyState.BusyReason.StanceTransition);
			if (inCrouch && !stanceBusy)
				break;

			yield return null;
		}

		if (!m_IsDraggingFallen || m_DraggedVictim == null || m_VictimFollower == null)
		{
			LogDragWarning("CoWaitForStanceAndAttach aborted: invalid state before attach.");
			yield break;
		}

		if (m_LeftHandAnchor == null)
			LogDragWarning("CoWaitForStanceAndAttach: LeftHand anchor is null — victim follow may look wrong.");

		if (m_DragLeftHandLayerIndex < 0)
			LogDragWarning($"CoWaitForStanceAndAttach: animator layer '{DragLeftHandLayerName}' not found. Run Polygone/Animation/Setup Drag Left Hand Layer.");

		ApplyLayerWeightImmediate(1f);
		SyncAnimatorState();

		for (int i = 0; i < m_AttachAnimatorSettleFrames; i++)
			yield return null;

		if (!m_IsDraggingFallen || m_DraggedVictim == null || m_VictimFollower == null)
		{
			LogDragWarning("CoWaitForStanceAndAttach aborted: drag cancelled before attach.");
			yield break;
		}

		SubscribeVictimEvents();
		m_VictimFollower.BeginFollow(this, m_LeftHandAnchor);
		LogDrag($"CoWaitForStanceAndAttach: victim attached, IsDraggingFallen={m_IsDraggingFallen}, layerWeight=1, gripOffset={m_VictimGripLocalOffsetInHand}, gripRotOffset={m_VictimGripLocalRotationOffsetInHand}");
	}

	private void ReleaseDragImmediate(bool _skipLocomotionStop = false)
	{
		LogDrag("ReleaseDragImmediate");
		UnsubscribeVictimEvents();

		if (m_VictimFollower != null)
		{
			m_VictimFollower.EndFollow();
			m_VictimFollower = null;
		}

		m_DraggedVictim = null;
		m_IsDraggingFallen = false;
		m_PresentationActive = false;
		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.DraggingFallen, false);

		ApplyLayerWeightImmediate(0f);
		SyncAnimatorState();

		if (!_skipLocomotionStop)
		{
			m_ClickToMove?.HardStop();
			m_LocomotionDriver?.HardStop();
		}

		if (m_Stance != null && isActiveAndEnabled)
			m_Stance.RequestStance(LocomotionStance.Standing);
	}

	private bool IsWeaponEquipped()
	{
		ItemDefinition current = m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
		return current != null && current.IsEquipment && current.EquipmentKind == EquipmentKind.Weapon;
	}

	private void SyncAnimatorState()
	{
		if (m_Animator == null)
			return;

		m_Animator.SetBool(s_IsDraggingFallen, m_IsDraggingFallen);
		SyncLayerWeight();
	}

	private void ResolveDragLayerIndex()
	{
		m_DragLeftHandLayerIndex = m_Animator != null
			? m_Animator.GetLayerIndex(DragLeftHandLayerName)
			: -1;
	}

	private void SyncLayerWeight()
	{
		if (m_Animator == null)
			return;

		if (m_DragLeftHandLayerIndex < 0)
			ResolveDragLayerIndex();
		if (m_DragLeftHandLayerIndex < 0)
			return;

		float targetWeight = m_PresentationActive && m_IsDraggingFallen ? 1f : 0f;
		float fadeSeconds = Mathf.Max(0.02f, m_LayerWeightFadeSeconds);
		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetWeight, Time.deltaTime / fadeSeconds);
		m_Animator.SetLayerWeight(m_DragLeftHandLayerIndex, m_SmoothedLayerWeight);
	}

	private void ApplyLayerWeightImmediate(float _weight)
	{
		if (m_Animator == null)
			return;

		if (m_DragLeftHandLayerIndex < 0)
			ResolveDragLayerIndex();
		if (m_DragLeftHandLayerIndex < 0)
			return;

		m_SmoothedLayerWeight = _weight;
		m_Animator.SetLayerWeight(m_DragLeftHandLayerIndex, _weight);
	}

	private bool TryValidateDragTarget(
		RtsUnitMember _victim,
		bool _allowActiveSession,
		out string _failureReason)
	{
		_failureReason = null;

		if (_victim == null)
			return FailValidation("victim is null", out _failureReason);

		if (m_IsDraggingFallen)
			return FailValidation("already dragging", out _failureReason);

		if (!_allowActiveSession && m_SessionCoroutine != null)
			return FailValidation("drag session already running", out _failureReason);

		if (!IsDraggerOperational())
			return FailValidation("dragger is unconscious or dead", out _failureReason);

		if (m_BusyState != null &&
		    (m_BusyState.HasReason(UnitBusyState.BusyReason.Reload) ||
		     m_BusyState.HasReason(UnitBusyState.BusyReason.SelfStabilization) ||
		     m_BusyState.HasReason(UnitBusyState.BusyReason.DraggingFallen) ||
		     m_BusyState.HasReason(UnitBusyState.BusyReason.StabilizeOther)))
			return FailValidation($"dragger busy: {m_BusyState.Reasons}", out _failureReason);

		if (m_SelfStabilization != null &&
		    (m_SelfStabilization.IsSelfHealing || m_SelfStabilization.IsHealPresentationActive))
			return FailValidation("dragger is self-stabilizing", out _failureReason);

		if (m_StabilizeOther != null &&
		    (m_StabilizeOther.IsStabilizingOther || m_StabilizeOther.IsHealPresentationActive))
			return FailValidation("dragger is stabilizing another unit", out _failureReason);

		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return FailValidation("dragger is reloading", out _failureReason);

		if (!UnitFallenDragVictimFollower.IsDraggableTarget(_victim))
		{
			UnitFallenStateUtility.TryDescribeFallenState(_victim, out string state);
			return FailValidation($"victim '{FormatUnit(_victim)}' is not draggable ({state})", out _failureReason);
		}

		UnitFallenDragVictimFollower follower = _victim.GetComponentInChildren<UnitFallenDragVictimFollower>(true);
		if (follower != null && !follower.CanBeDraggedBy(this))
			return FailValidation($"victim '{FormatUnit(_victim)}' is already dragged by another unit", out _failureReason);

		if (_victim == m_RtsMember)
			return FailValidation("cannot drag self", out _failureReason);

		m_DebugLastFailureReason = string.Empty;
		return true;
	}

	private bool FailValidation(string _reason, out string _failureReason)
	{
		_failureReason = _reason;
		m_DebugLastFailureReason = _reason;
		return false;
	}

	private void LogDrag(string _message)
	{
		if (!m_LogFallenDrag)
			return;

		Debug.Log($"[UnitFallenDrag:{name}] {_message}", this);
	}

	private void LogDragWarning(string _message)
	{
		if (!m_LogFallenDrag)
			return;

		Debug.LogWarning($"[UnitFallenDrag:{name}] {_message}", this);
	}

	private static string FormatUnit(RtsUnitMember _unit)
	{
		return _unit != null ? _unit.name : "null";
	}
	#endregion
}
