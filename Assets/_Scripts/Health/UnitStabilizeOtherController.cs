using System.Collections;

using UnityEngine;



/// <summary>

/// Стабилизация другого юнита: сознательный юнит подходит к бессознательному,

/// приседает и стабилизирует его травмы, расходуя медикаменты сначала жертвы, затем свои.

/// </summary>

[DisallowMultipleComponent]

[DefaultExecutionOrder(63)]

public sealed class UnitStabilizeOtherController : MonoBehaviour

{

	#region Constants

	public const string ParamIsStabilizingOther = "IsStabilizingOther";

	public const string HealOtherHandsLayerName = UnitSelfStabilizationController.MedkitHandsLayerName;



	private const float c_ApproachArriveDistance = 1f;

	private const float c_MaxApproachSeconds = 45f;



	private static readonly int s_IsStabilizingOther = Animator.StringToHash(ParamIsStabilizingOther);

	private static readonly int s_StateHealStart = Animator.StringToHash("healStart");

	private static readonly int s_StateHeal2 = Animator.StringToHash("heal2");

	private static readonly int s_StateHealEnd = Animator.StringToHash("healEnd");

	#endregion



	#region Serialized Fields

	[SerializeField] private UnitHealth m_Health;

	[SerializeField] private CharacterInventory m_CharacterInventory;

	[SerializeField] private UnitConsciousness m_Consciousness;

	[SerializeField] private UnitBusyState m_BusyState;

	[SerializeField] private UnitAnimatorStance m_Stance;

	[SerializeField] private UnitEquipment m_UnitEquipment;

	[SerializeField] private UnitClickToMove m_ClickToMove;

	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;

	[SerializeField] private Animator m_Animator;

	[SerializeField] private Transform m_LeftHandAnchor;

	[SerializeField] private RtsUnitMember m_RtsMember;



	[Header("Healing Presentation")]

	[SerializeField, Min(0.02f)] private float m_LayerWeightFadeSeconds = 0.2f;



	[Header("Debug")]

	[SerializeField] private bool m_IsStabilizingOther;

	[SerializeField] private RtsUnitMember m_DebugVictim;

	[SerializeField] private int m_DebugCurrentInjuryIndex = -1;

	[SerializeField] private int m_DebugRequiredHealCycles;

	[SerializeField] private int m_DebugCompletedHealCycles;

	[SerializeField] private string m_DebugLastFailureReason;

	[SerializeField] private float m_DebugSmoothedInjuryProgress01;

	[SerializeField] private float m_DebugCurrentInjuryProgressTotalDuration;

	#endregion



	#region Private Fields

	private Coroutine m_StabilizeOtherCoroutine;

	private RtsUnitMember m_CurrentVictim;

	private UnitHealth m_VictimHealth;

	private CharacterInventory m_VictimInventory;

	private UnitConsciousness m_VictimConsciousness;

	private GameObject m_LeftHandMedkitVisualInstance;

	private int m_HealOtherHandsLayerIndex = -1;

	private float m_SmoothedLayerWeight;

	private bool m_StopRequested;

	private bool m_HealPresentationActive;

	private bool m_CurrentInjuryUsesHealStart;

	private int m_ActiveMedkitBagIndex;

	private CharacterInventory m_ActiveMedkitInventory;

	private float m_CurrentInjuryProgressStartTime;

	private float m_CurrentInjuryProgressTotalDuration;

	#endregion



	#region Public Properties

	public bool IsStabilizingOther => m_IsStabilizingOther;

	public bool IsHealPresentationActive => m_HealPresentationActive;

	public RtsUnitMember CurrentVictim => m_CurrentVictim;

	#endregion



	#region Unity Lifecycle

	private void Awake()

	{

		ResolveReferences();

		ResolveHealOtherHandsLayerIndex();

	}



	private void OnEnable()

	{

		if (m_Health != null)

			m_Health.Changed += HandleHealthChanged;

		if (m_Consciousness != null)

			m_Consciousness.ConsciousnessChanged += HandleConsciousnessChanged;



		m_SmoothedLayerWeight = m_IsStabilizingOther || m_HealPresentationActive ? 1f : 0f;

		SyncAnimatorState();

		ApplyLayerWeightImmediate(m_SmoothedLayerWeight);

	}



	private void OnDisable()

	{

		if (m_Health != null)

			m_Health.Changed -= HandleHealthChanged;

		if (m_Consciousness != null)

			m_Consciousness.ConsciousnessChanged -= HandleConsciousnessChanged;



		UnsubscribeVictimEvents();

		StopStabilizeOther(true, false);

	}



	private void Update()

	{

		SyncLayerWeight();

		if (m_HealPresentationActive && !CanContinueCurrentTreatment())

		{

			StopStabilizeOtherWithoutUserCancel();

			return;

		}

		UpdateHealProgressInHealthCell();

	}

	#endregion



	#region Public Methods

	public bool CanStabilizeOther(RtsUnitMember _victim)

	{

		return TryValidateTarget(_victim, _allowActiveSession: false, out _, out _, out _, out _);

	}



	public void RequestStabilizeOther(RtsUnitMember _victim)

	{

		Debug.Log($"[UnitStabilizeOther:{name}] RequestStabilizeOther called. target='{FormatUnit(_victim)}'");



		bool hasPendingSession = m_StabilizeOtherCoroutine != null;

		if (!TryValidateTarget(_victim, _allowActiveSession: hasPendingSession, out _, out _, out _, out string failureReason))

		{

			Debug.LogWarning($"[UnitStabilizeOther:{name}] RequestStabilizeOther rejected: {failureReason}");

			return;

		}



		if (m_StabilizeOtherCoroutine != null)

		{

			Debug.Log($"[UnitStabilizeOther:{name}] RequestStabilizeOther: restarting pending session.");

			StopCoroutine(m_StabilizeOtherCoroutine);

			m_StabilizeOtherCoroutine = null;

		}



		m_StabilizeOtherCoroutine = StartCoroutine(CoStabilizeOtherSession(_victim));

	}



	public void StopStabilizeOther()

	{

		StopStabilizeOther(true, true);

	}



	public void StopStabilizeOtherWithoutUserCancel()

	{

		StopStabilizeOther(true, false);

	}



	public void AnimationEvent_StabilizeOtherShowMedkitInHand()

	{

		if (!m_HealPresentationActive)

			return;

		if (m_LeftHandMedkitVisualInstance == null)

			AttachMedkitVisualToLeftHand();

	}



	public void AnimationEvent_StabilizeOtherHideMedkitFromHand()

	{

		if (!m_HealPresentationActive)

			return;

		ClearLeftHandMedkitVisual();

	}



	public void AnimationEvent_StabilizeOtherCycleCompleted()

	{

		if (!m_HealPresentationActive)

			return;



		m_DebugCompletedHealCycles++;

	}



	public void AnimationEvent_StabilizeOtherStartLoop()

	{

	}



	public void AnimationEvent_StabilizeOtherStartEnd()

	{

	}

	#endregion



	#region Private Methods — Validation

	private bool TryValidateTarget(

		RtsUnitMember _victim,

		bool _allowActiveSession,

		out UnitHealth _victimHealth,

		out CharacterInventory _victimInventory,

		out UnitConsciousness _victimConsciousness,

		out string _failureReason)

	{

		_failureReason = null;

		_victimHealth = null;

		_victimInventory = null;

		_victimConsciousness = null;



		if (_victim == null)

			return Fail("victim is null", out _failureReason);

		if (m_IsStabilizingOther)

			return Fail("already stabilizing another", out _failureReason);

		if (!_allowActiveSession && m_StabilizeOtherCoroutine != null)

			return Fail("stabilize-other session already running", out _failureReason);

		if (_victim == m_RtsMember)

			return Fail("cannot stabilize self", out _failureReason);



		ResolveReferences();

		if (m_Health == null || m_Health.IsDead)

			return Fail("helper is dead", out _failureReason);

		if (m_Consciousness != null && !m_Consciousness.IsConscious)

			return Fail("helper is unconscious", out _failureReason);

		if (m_BusyState != null && m_BusyState.IsBusy)

			return Fail($"helper is busy: {m_BusyState.Reasons}", out _failureReason);



		_victimHealth = _victim.GetComponent<UnitHealth>();

		if (_victimHealth == null || _victimHealth.IsDead)

			return Fail("victim is null or dead", out _failureReason);



		_victimConsciousness = _victim.GetComponent<UnitConsciousness>();

		if (_victimConsciousness == null || _victimConsciousness.IsConscious)

			return Fail("victim is conscious", out _failureReason);



		if (!_victimHealth.HasUnstabilizedInjuries)

			return Fail("victim has no unstabilized injuries", out _failureReason);



		_victimInventory = _victim.GetComponent<CharacterInventory>();

		if (!TryFindUsableMedkitForVictim(_victimInventory, _victimHealth, out _, out _, out _))

			return Fail("no usable medkit in victim or helper inventory", out _failureReason);



		m_DebugLastFailureReason = string.Empty;

		_failureReason = null;

		return true;

	}



	private bool CanContinueCurrentTreatment()

	{

		if (!isActiveAndEnabled)

			return false;

		if (m_StopRequested)

			return false;

		if (m_Health == null || m_Health.IsDead)

			return false;

		if (m_Consciousness != null && !m_Consciousness.IsConscious)

			return false;

		if (m_CurrentVictim == null || m_VictimHealth == null || m_VictimHealth.IsDead)

			return false;

		if (m_VictimConsciousness != null && m_VictimConsciousness.IsConscious)

			return false;

		return true;

	}



	private bool Fail(string _reason, out string _failureReasonOut)

	{

		_failureReasonOut = _reason;

		m_DebugLastFailureReason = _reason;

		return false;

	}

	#endregion



	#region Private Methods — Medkit Resolution

	private bool TryFindUsableMedkitForVictim(

		CharacterInventory _victimInventory,

		UnitHealth _victimHealth,

		out CharacterInventory _resolvedInventory,

		out InventorySlotRuntimeData _slot,

		out int _bagIndex)

	{

		_slot = default;

		_bagIndex = -1;

		_resolvedInventory = null;



		if (!_victimHealth.TryGetWorstUnstabilizedInjury(out InjuryUiEntry injury, out _))

			return false;



		if (_victimInventory != null && TryFindUsableMedkitInInventory(_victimInventory, injury, out _slot, out _bagIndex))

		{

			_resolvedInventory = _victimInventory;

			return true;

		}



		if (m_CharacterInventory != null && TryFindUsableMedkitInInventory(m_CharacterInventory, injury, out _slot, out _bagIndex))

		{

			_resolvedInventory = m_CharacterInventory;

			return true;

		}



		return false;

	}



	private static bool TryFindUsableMedkitInInventory(

		CharacterInventory _inventory,

		in InjuryUiEntry _injury,

		out InventorySlotRuntimeData _slot,

		out int _bagIndex)

	{

		_slot = default;

		_bagIndex = -1;

		if (_inventory == null)

			return false;



		for (int i = 0; i < _inventory.BagCount; i++)

		{

			InventorySlotRuntimeData slot = _inventory.BagItems[i];

			MedkitRuntimeState medkitState = slot.InstanceState != null ? slot.InstanceState.MedkitState : null;

			if (medkitState == null || medkitState.Definition == null || !medkitState.CanTreatInjury(_injury))

				continue;



			_slot = slot;

			_bagIndex = i;

			return true;

		}



		return false;

	}



	private bool TryResolveNextInjuryTarget(

		out int _injuryIndex,

		out InventorySlotRuntimeData _medkitSlot,

		out int _medkitBagIndex,

		out CharacterInventory _medkitInventory,

		out InjuryUiEntry _injury)

	{

		_injuryIndex = -1;

		_medkitSlot = default;

		_medkitBagIndex = -1;

		_medkitInventory = null;

		_injury = default;



		if (m_VictimHealth == null || m_VictimHealth.IsDead)

			return false;

		if (!m_VictimHealth.TryGetWorstUnstabilizedInjury(out _injury, out _injuryIndex))

			return false;



		if (!TryFindUsableMedkitForVictim(m_VictimInventory, m_VictimHealth, out _medkitInventory, out _medkitSlot, out _medkitBagIndex))

			return false;



		return true;

	}

	#endregion



	#region Private Methods — Approach

	private IEnumerator CoApproachVictim(RtsUnitMember _victim)

	{

		if (m_RtsMember == null || _victim == null)

		{

			Debug.LogWarning($"[UnitStabilizeOther:{name}] CoApproachVictim aborted: RTS member or victim is null.");

			yield break;

		}



		float distance = Vector3.Distance(m_RtsMember.transform.position, _victim.transform.position);

		Debug.Log($"[UnitStabilizeOther:{name}] CoApproachVictim: initial distance={distance:F2}m (arrive<={c_ApproachArriveDistance:F2}m)");

		if (distance > c_ApproachArriveDistance)

		{

			Vector3 approachPoint = ComputeApproachPoint(m_RtsMember.transform, _victim.transform, c_ApproachArriveDistance * 0.85f);

			Debug.Log($"[UnitStabilizeOther:{name}] CoApproachVictim: issuing move order to {approachPoint}");

			m_RtsMember.IssueMoveOrder(approachPoint, UnitClickToMove.MoveTier.Walk);



			float elapsed = 0f;

			while (elapsed < c_MaxApproachSeconds)

			{

				if (_victim == null || m_RtsMember == null)

				{

					Debug.LogWarning($"[UnitStabilizeOther:{name}] CoApproachVictim interrupted: victim or helper destroyed during approach.");

					yield break;

				}



				distance = Vector3.Distance(m_RtsMember.transform.position, _victim.transform.position);

				if (distance <= c_ApproachArriveDistance)

					break;



				elapsed += Time.deltaTime;

				yield return null;

			}



			Debug.Log($"[UnitStabilizeOther:{name}] CoApproachVictim finished waiting after {elapsed:F1}s, distance={distance:F2}m");

		}



		m_ClickToMove?.HardStop();

		m_LocomotionDriver?.HardStop();

		yield return null;

	}



	private static Vector3 ComputeApproachPoint(Transform _helper, Transform _victim, float _standoffMeters)

	{

		Vector3 victimPosition = _victim.position;

		Vector3 toVictim = victimPosition - _helper.position;

		toVictim.y = 0f;



		if (toVictim.sqrMagnitude < 0.04f)

			toVictim = _victim.forward;



		toVictim.Normalize();

		return victimPosition - toVictim * _standoffMeters;

	}

	#endregion



	#region Private Methods — Stabilization Session

	private IEnumerator CoStabilizeOtherSession(RtsUnitMember _victim)

	{

		if (_victim == null)

		{

			Debug.LogWarning($"[UnitStabilizeOther:{name}] CoStabilizeOtherSession aborted: victim is null.");

			m_StabilizeOtherCoroutine = null;

			yield break;

		}



		Debug.Log($"[UnitStabilizeOther:{name}] CoStabilizeOtherSession: approaching victim='{FormatUnit(_victim)}'");

		yield return CoApproachVictim(_victim);



		if (_victim == null)

		{

			Debug.LogWarning($"[UnitStabilizeOther:{name}] CoStabilizeOtherSession aborted after approach: victim destroyed.");

			m_StabilizeOtherCoroutine = null;

			yield break;

		}



		if (!TryValidateTarget(_victim, _allowActiveSession: true, out UnitHealth victimHealth, out CharacterInventory victimInventory, out UnitConsciousness victimConsciousness, out string failureReason))

		{

			Debug.LogWarning($"[UnitStabilizeOther:{name}] CoStabilizeOtherSession aborted after approach: {failureReason}");

			m_StabilizeOtherCoroutine = null;

			yield break;

		}



		Debug.Log($"[UnitStabilizeOther:{name}] CoStabilizeOtherSession: approach complete, starting stabilization for victim='{FormatUnit(_victim)}'");



		m_HealPresentationActive = true;

		m_StopRequested = false;

		m_CurrentVictim = _victim;

		m_VictimHealth = victimHealth;

		m_VictimInventory = victimInventory;

		m_VictimConsciousness = victimConsciousness;

		m_DebugVictim = _victim;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.StabilizeOther, true);



		SubscribeVictimEvents();

		PrepareForHealing();

		ApplyLayerWeightImmediate(1f);



		bool sessionStarted = false;

		bool treatedAny = false;



		while (!m_StopRequested && TryResolveNextInjuryTarget(

			       out int injuryIndex,

			       out InventorySlotRuntimeData medkitSlot,

			       out int medkitBagIndex,

			       out CharacterInventory medkitInventory,

			       out InjuryUiEntry injury))

		{

			if (!CanContinueCurrentTreatment())

				break;

			yield return TreatSingleInjuryRoutine(

				injuryIndex,

				medkitSlot,

				medkitBagIndex,

				medkitInventory,

				injury,

				!sessionStarted);



			sessionStarted = true;

			if (!m_StopRequested && CanContinueCurrentTreatment())

				treatedAny = true;

		}



		if (treatedAny && !m_StopRequested && CanContinueCurrentTreatment())

		{

			float healEndTimeoutSeconds = SelfHealPresentationTiming.GetHealEndTimeoutSeconds();

			m_IsStabilizingOther = false;

			SyncAnimatorState();

			yield return WaitForHealOtherState(s_StateHealEnd, healEndTimeoutSeconds);

		}



		FinishHealOtherPresentation();

	}



	private IEnumerator TreatSingleInjuryRoutine(

		int _injuryIndex,

		InventorySlotRuntimeData _medkitSlot,

		int _medkitBagIndex,

		CharacterInventory _medkitInventory,

		InjuryUiEntry _injury,

		bool _playHealStart)

	{

		m_DebugCurrentInjuryIndex = _injuryIndex;

		m_DebugCompletedHealCycles = 0;

		m_DebugSmoothedInjuryProgress01 = 0f;

		m_DebugRequiredHealCycles = SelfHealPresentationTiming.ResolveHealCycles(_injury.SortPriority);

		m_CurrentInjuryUsesHealStart = _playHealStart;

		m_ActiveMedkitBagIndex = _medkitBagIndex;

		m_ActiveMedkitInventory = _medkitInventory;

		m_CurrentInjuryProgressTotalDuration = m_CurrentInjuryUsesHealStart

			? SelfHealPresentationTiming.HealStartDuration +

			  m_DebugRequiredHealCycles * SelfHealPresentationTiming.HealLoopCycleDuration

			: m_DebugRequiredHealCycles * SelfHealPresentationTiming.HealLoopCycleDuration;

		m_DebugCurrentInjuryProgressTotalDuration = m_CurrentInjuryProgressTotalDuration;

		m_CurrentInjuryProgressStartTime = Time.time;

		HealthStatusHealProgressBridge.Report(m_VictimHealth, _injuryIndex, 0f);



		if (_playHealStart)

		{

			float healStartTimeoutSeconds = SelfHealPresentationTiming.GetHealStartTimeoutSeconds();

			m_IsStabilizingOther = true;

			SyncAnimatorState();

			yield return WaitForHealOtherState(s_StateHeal2, healStartTimeoutSeconds);

			if (!CanContinueCurrentTreatment())

				yield break;

		}



		float healLoopsTimeoutSeconds =

			SelfHealPresentationTiming.GetHealLoopsTimeoutSeconds(m_DebugRequiredHealCycles);



		float healCyclesEndTime = Time.time + healLoopsTimeoutSeconds;

		while (!m_StopRequested &&

		       m_DebugCompletedHealCycles < m_DebugRequiredHealCycles &&

		       Time.time < healCyclesEndTime)

		{

			if (!CanContinueCurrentTreatment())

				yield break;

			yield return null;

		}



		if (!m_StopRequested && CanContinueCurrentTreatment() && m_VictimHealth.TryGetInjury(_injuryIndex, out InjuryUiEntry injuryAfter))

		{

			ApplyStabilization(_injuryIndex, _medkitSlot, _medkitBagIndex, _medkitInventory, injuryAfter);

		}

	}



	private void PrepareForHealing()

	{

		m_ClickToMove?.HardStop();

		m_LocomotionDriver?.HardStop();

		if (m_Stance != null)

			m_Stance.RequestStance(LocomotionStance.Crouch);



		m_UnitEquipment?.SetMainWeaponVisualActive(false);

	}



	private void ApplyStabilization(

		int _injuryIndex,

		InventorySlotRuntimeData _medkitSlot,

		int _medkitBagIndex,

		CharacterInventory _medkitInventory,

		in InjuryUiEntry _injury)

	{

		MedkitRuntimeState medkitState = _medkitSlot.InstanceState != null ? _medkitSlot.InstanceState.MedkitState : null;

		if (medkitState == null || !medkitState.TryConsumeForInjury(_injury, out _))

			return;



		_medkitInventory?.TrySetBagItemAt(_medkitBagIndex, _medkitSlot);

		m_VictimHealth.TryMarkInjuryStabilized(_injuryIndex);

	}
	#endregion



	#region Private Methods — Progress

	private void UpdateHealProgressInHealthCell()

	{

		if (!m_HealPresentationActive || m_VictimHealth == null || m_DebugCurrentInjuryIndex < 0)

			return;



		if (!CanContinueCurrentTreatment())

		{

			HealthStatusHealProgressBridge.Clear(m_VictimHealth);

			return;

		}



		HealthStatusHealProgressBridge.Report(

			m_VictimHealth,

			m_DebugCurrentInjuryIndex,

			CalculateCurrentInjuryProgress01());

	}



	private float CalculateCurrentInjuryProgress01()

	{

		if (m_CurrentInjuryProgressTotalDuration <= 0f)

			return m_DebugSmoothedInjuryProgress01;



		float elapsed = Time.time - m_CurrentInjuryProgressStartTime;

		float rawProgress = Mathf.Clamp01(elapsed / m_CurrentInjuryProgressTotalDuration);

		if (m_DebugCompletedHealCycles >= m_DebugRequiredHealCycles)

			rawProgress = 1f;

		else if (!m_StopRequested)

			rawProgress = Mathf.Min(rawProgress, 0.99f);



		m_DebugSmoothedInjuryProgress01 = Mathf.Max(m_DebugSmoothedInjuryProgress01, rawProgress);

		return m_DebugSmoothedInjuryProgress01;

	}

	#endregion



	#region Private Methods — Presentation Lifecycle

	private void FinishHealOtherPresentation()

	{

		HealthStatusHealProgressBridge.Clear(m_VictimHealth);

		m_IsStabilizingOther = false;

		m_HealPresentationActive = false;

		m_StopRequested = false;

		m_StabilizeOtherCoroutine = null;

		m_CurrentVictim = null;

		m_DebugVictim = null;

		m_DebugCurrentInjuryIndex = -1;

		m_CurrentInjuryUsesHealStart = false;

		m_DebugCompletedHealCycles = 0;

		m_DebugSmoothedInjuryProgress01 = 0f;

		m_CurrentInjuryProgressStartTime = 0f;

		m_CurrentInjuryProgressTotalDuration = 0f;

		m_DebugCurrentInjuryProgressTotalDuration = 0f;

		m_ActiveMedkitBagIndex = -1;

		m_ActiveMedkitInventory = null;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.StabilizeOther, false);



		UnsubscribeVictimEvents();

		m_VictimHealth = null;

		m_VictimInventory = null;

		m_VictimConsciousness = null;



		SyncAnimatorState();

		ApplyLayerWeightImmediate(0f);

		ClearLeftHandMedkitVisual();

		m_UnitEquipment?.SetMainWeaponVisualActive(true);



		if (m_Stance != null)

			m_Stance.RequestStance(LocomotionStance.Standing);

	}



	private void StopStabilizeOther(bool _restorePresentation, bool _userCancelled)

	{

		if (!m_IsStabilizingOther && !m_HealPresentationActive && m_StabilizeOtherCoroutine == null)

		{

			if (_userCancelled)

				HealthStatusHealProgressBridge.Clear(m_VictimHealth);

			ClearLeftHandMedkitVisual();

			return;

		}



		m_StopRequested = true;

		if (m_StabilizeOtherCoroutine != null)

		{

			StopCoroutine(m_StabilizeOtherCoroutine);

			m_StabilizeOtherCoroutine = null;

		}



		if (_restorePresentation)

			FinishHealOtherPresentation();

	}

	#endregion



	#region Private Methods — Animation

	private IEnumerator WaitForHealOtherState(int _stateHash, float _timeoutSeconds)

	{

		float endTime = Time.time + Mathf.Max(0.01f, _timeoutSeconds);

		while (!m_StopRequested && Time.time < endTime)

		{

			if (IsHealOtherLayerInState(_stateHash))

				yield break;



			yield return null;

		}

	}



	private bool IsHealOtherLayerInState(int _stateHash)

	{

		if (m_Animator == null)

			return false;

		if (m_HealOtherHandsLayerIndex < 0)

			ResolveHealOtherHandsLayerIndex();

		if (m_HealOtherHandsLayerIndex < 0)

			return false;



		AnimatorStateInfo stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_HealOtherHandsLayerIndex);

		return stateInfo.shortNameHash == _stateHash;

	}



	private bool TryGetHealOtherLayerStateInfo(out AnimatorStateInfo _stateInfo)

	{

		_stateInfo = default;

		if (m_Animator == null)

			return false;

		if (m_HealOtherHandsLayerIndex < 0)

			ResolveHealOtherHandsLayerIndex();

		if (m_HealOtherHandsLayerIndex < 0)

			return false;



		_stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_HealOtherHandsLayerIndex);

		return m_Animator.GetLayerWeight(m_HealOtherHandsLayerIndex) > 0.01f;

	}

	#endregion



	#region Private Methods — Victim Events

	private void SubscribeVictimEvents()

	{

		if (m_CurrentVictim == null)

			return;



		m_VictimHealth = m_CurrentVictim.GetComponent<UnitHealth>();

		if (m_VictimHealth != null)

			m_VictimHealth.Changed += HandleVictimHealthChanged;



		m_VictimConsciousness = m_CurrentVictim.GetComponent<UnitConsciousness>();

		if (m_VictimConsciousness != null)

			m_VictimConsciousness.ConsciousnessChanged += HandleVictimConsciousnessChanged;

	}



	private void UnsubscribeVictimEvents()

	{

		if (m_VictimHealth != null)

		{

			m_VictimHealth.Changed -= HandleVictimHealthChanged;

			m_VictimHealth = null;

		}

		if (m_VictimConsciousness != null)

		{

			m_VictimConsciousness.ConsciousnessChanged -= HandleVictimConsciousnessChanged;

			m_VictimConsciousness = null;

		}

	}



	private void HandleVictimHealthChanged()

	{

		if (m_VictimHealth != null && m_VictimHealth.IsDead)

			StopStabilizeOtherWithoutUserCancel();

	}



	private void HandleVictimConsciousnessChanged(bool _isConscious)

	{

		if (_isConscious)

			StopStabilizeOtherWithoutUserCancel();

	}

	#endregion



	#region Private Methods — Helper Events

	private void HandleHealthChanged()

	{

		if (!isActiveAndEnabled)

			return;

		if (m_Health != null && m_Health.IsDead)

			StopStabilizeOtherWithoutUserCancel();

	}



	private void HandleConsciousnessChanged(bool _isConscious)

	{

		if (!_isConscious)

			StopStabilizeOtherWithoutUserCancel();

	}

	#endregion



	#region Private Methods — Visual

	private void AttachMedkitVisualToLeftHand()

	{

		ClearLeftHandMedkitVisual();

		if (m_LeftHandAnchor == null)

			return;



		CharacterInventory activeInventory = m_ActiveMedkitInventory;

		if (activeInventory == null)

			activeInventory = m_CharacterInventory;



		InventorySlotRuntimeData medkitSlot = default;

		int bagIndex = m_ActiveMedkitBagIndex;

		if (bagIndex >= 0 && activeInventory != null && bagIndex < activeInventory.BagCount)

			medkitSlot = activeInventory.BagItems[bagIndex];



		if (medkitSlot.IsEmpty && !TryFindAnyMedkitSlot(activeInventory, out medkitSlot, out _))

			return;



		ItemDefinition definition = medkitSlot.Definition;

		if (definition == null || definition.EquippedVisualPrefab == null)

			return;



		m_LeftHandMedkitVisualInstance = Instantiate(definition.EquippedVisualPrefab, m_LeftHandAnchor);

		m_LeftHandMedkitVisualInstance.transform.localPosition = definition.RightHandLocalPosition;

		m_LeftHandMedkitVisualInstance.transform.localRotation = definition.RightHandLocalRotation;

		DisablePhysicsOnHealingVisual(m_LeftHandMedkitVisualInstance);

	}



	private bool TryFindAnyMedkitSlot(CharacterInventory _inventory, out InventorySlotRuntimeData _slot, out int _bagIndex)

	{

		_slot = default;

		_bagIndex = -1;

		if (_inventory == null)

			return false;



		for (int i = 0; i < _inventory.BagCount; i++)

		{

			InventorySlotRuntimeData slot = _inventory.BagItems[i];

			if (slot.InstanceState?.MedkitState?.Definition == null)

				continue;



			_slot = slot;

			_bagIndex = i;

			return true;

		}



		return false;

	}



	private void ClearLeftHandMedkitVisual()

	{

		if (m_LeftHandMedkitVisualInstance == null)

			return;



		Destroy(m_LeftHandMedkitVisualInstance);

		m_LeftHandMedkitVisualInstance = null;

	}



	private static void DisablePhysicsOnHealingVisual(GameObject _root)

	{

		Rigidbody[] bodies = _root.GetComponentsInChildren<Rigidbody>(true);

		for (int i = 0; i < bodies.Length; i++)

		{

			bodies[i].isKinematic = true;

			bodies[i].detectCollisions = false;

		}



		Collider[] colliders = _root.GetComponentsInChildren<Collider>(true);

		for (int i = 0; i < colliders.Length; i++)

			colliders[i].enabled = false;



		WorldPickupItem[] pickups = _root.GetComponentsInChildren<WorldPickupItem>(true);

		for (int i = 0; i < pickups.Length; i++)

			pickups[i].enabled = false;

	}

	#endregion



	#region Private Methods — Animator Helpers

	private void SyncAnimatorState()

	{

		if (m_Animator == null)

			return;



		m_Animator.SetBool(s_IsStabilizingOther, m_IsStabilizingOther);

		SyncLayerWeight();

	}



	private void ResolveHealOtherHandsLayerIndex()

	{

		m_HealOtherHandsLayerIndex = m_Animator != null

			? m_Animator.GetLayerIndex(HealOtherHandsLayerName)

			: -1;

	}



	private void SyncLayerWeight()

	{

		if (m_Animator == null)

			return;

		if (m_HealOtherHandsLayerIndex < 0)

			ResolveHealOtherHandsLayerIndex();

		if (m_HealOtherHandsLayerIndex < 0)

			return;



		float targetWeight = m_HealPresentationActive ? 1f : 0f;

		float fadeSeconds = Mathf.Max(0.02f, m_LayerWeightFadeSeconds);

		m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetWeight, Time.deltaTime / fadeSeconds);

		m_Animator.SetLayerWeight(m_HealOtherHandsLayerIndex, m_SmoothedLayerWeight);

	}



	private void ApplyLayerWeightImmediate(float _weight)

	{

		if (m_Animator == null)

			return;

		if (m_HealOtherHandsLayerIndex < 0)

			ResolveHealOtherHandsLayerIndex();

		if (m_HealOtherHandsLayerIndex < 0)

			return;



		m_SmoothedLayerWeight = _weight;

		m_Animator.SetLayerWeight(m_HealOtherHandsLayerIndex, m_SmoothedLayerWeight);

	}

	#endregion



	#region Private Methods — Misc

	private void ResolveReferences()

	{

		if (m_Health == null)

			m_Health = GetComponent<UnitHealth>();

		if (m_CharacterInventory == null)

			m_CharacterInventory = GetComponent<CharacterInventory>();

		if (m_Consciousness == null)

			m_Consciousness = GetComponent<UnitConsciousness>();

		if (m_BusyState == null)

			m_BusyState = GetComponent<UnitBusyState>();

		if (m_Stance == null)

			m_Stance = GetComponent<UnitAnimatorStance>();

		if (m_UnitEquipment == null)

			m_UnitEquipment = GetComponent<UnitEquipment>();

		if (m_ClickToMove == null)

			m_ClickToMove = GetComponent<UnitClickToMove>();

		if (m_LocomotionDriver == null)

			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();

		if (m_Animator == null)

			m_Animator = GetComponentInChildren<Animator>();

		if (m_LeftHandAnchor == null && m_Animator != null && m_Animator.isHuman)

			m_LeftHandAnchor = m_Animator.GetBoneTransform(HumanBodyBones.LeftHand);

		if (m_RtsMember == null)

			m_RtsMember = GetComponent<RtsUnitMember>();

	}



	private static string FormatUnit(RtsUnitMember _unit)

	{

		return _unit != null ? _unit.name : "null";

	}

	#endregion

}
