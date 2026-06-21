using System.Collections;

using UnityEngine;



/// <summary>

/// Автоматическая самостабилизация: сознательный юнит с IFAK приседает и стабилизирует травмы подряд в одной сессии.

/// </summary>

[DisallowMultipleComponent]

[DefaultExecutionOrder(62)]

public sealed class UnitSelfStabilizationController : MonoBehaviour

{

	#region Constants

	public const string ParamIsSelfHealing = "IsSelfHealing";

	public const string MedkitHandsLayerName = "Medkit_Hands";



	private static readonly int s_IsSelfHealing = Animator.StringToHash(ParamIsSelfHealing);

	private static readonly int s_StateHealStart = Animator.StringToHash("healStart");

	private static readonly int s_StateHeal = Animator.StringToHash("heal");

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



	[Header("Healing Presentation")]

	[SerializeField, Min(0.02f)] private float m_LayerWeightFadeSeconds = 0.2f;



	[Header("Debug")]

	[SerializeField] private bool m_IsSelfHealing;

	[SerializeField] private int m_DebugCurrentInjuryIndex = -1;

	[SerializeField] private int m_DebugRequiredHealCycles;

	[SerializeField] private int m_DebugCompletedHealCycles;

	[SerializeField] private string m_DebugLastFailureReason;

	[SerializeField] private float m_DebugSmoothedInjuryProgress01;

	[SerializeField] private float m_DebugCurrentInjuryProgressTotalDuration;

	#endregion



	#region Private Fields

	private Coroutine m_SelfHealCoroutine;

	private GameObject m_LeftHandMedkitVisualInstance;

	private int m_MedkitHandsLayerIndex = -1;

	private float m_SmoothedLayerWeight;

	private bool m_StopRequested;

	private bool m_HealPresentationActive;

	private bool m_CurrentInjuryUsesHealStart;

	private bool m_SuppressAutoSelfStabilization;

	private int m_LastKnownInjuryCount;

	private float m_CurrentInjuryProgressStartTime;

	private float m_CurrentInjuryProgressTotalDuration;

	#endregion



	#region Public Properties

	public bool IsSelfHealing => m_IsSelfHealing;

	public bool IsHealPresentationActive => m_HealPresentationActive;

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
		{
			m_Health.Changed += HandleHealthChanged;
			m_LastKnownInjuryCount = m_Health.InjuryCount;
		}

		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged += HandleConsciousnessChanged;



		m_SmoothedLayerWeight = m_IsSelfHealing || m_HealPresentationActive ? 1f : 0f;

		SyncAnimatorState();

		ApplyLayerWeightImmediate(m_SmoothedLayerWeight);

	}



	private void OnDisable()

	{

		if (m_Health != null)

			m_Health.Changed -= HandleHealthChanged;

		if (m_Consciousness != null)
			m_Consciousness.ConsciousnessChanged -= HandleConsciousnessChanged;



		StopSelfStabilization(true, false);

	}



	private void Update()

	{

		SyncLayerWeight();

		if (m_HealPresentationActive && !CanContinueCurrentTreatment())
		{
			StopSelfStabilizationWithoutUserCancel();
			return;
		}

		UpdateHealProgressInHealthCell();



		if (!m_SuppressAutoSelfStabilization &&

		    !m_IsSelfHealing && !m_HealPresentationActive && m_Health != null && m_Health.HasUnstabilizedInjuries)

			TryBeginSelfStabilization();

	}

	#endregion



	#region Public Methods

	public void StopSelfStabilization()

	{

		StopSelfStabilization(true, true);

	}



	public void StopSelfStabilizationWithoutUserCancel()

	{

		StopSelfStabilization(true, false);

	}



	public bool CanRequestSelfStabilization()

	{

		return CanStartSelfStabilization(out _, out _, out _);

	}



	public bool RequestSelfStabilization()

	{

		m_SuppressAutoSelfStabilization = false;

		return TryBeginSelfStabilization();

	}



	public void AnimationEvent_SelfHealShowMedkitInHand()

	{

		if (!m_HealPresentationActive)

			return;

		if (m_LeftHandMedkitVisualInstance == null)

			AttachMedkitVisualToLeftHand();

	}



	public void AnimationEvent_SelfHealHideMedkitFromHand()

	{

		if (!m_HealPresentationActive)

			return;

		ClearLeftHandMedkitVisual();

	}



	public void AnimationEvent_SelfHealCycleCompleted()

	{

		if (!m_HealPresentationActive)

			return;



		m_DebugCompletedHealCycles++;

	}



	public void AnimationEvent_SelfHealStartLoop()

	{

	}



	public void AnimationEvent_SelfHealStartEnd()

	{

	}

	#endregion



	#region Private Methods

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

	}



	private void HandleHealthChanged()

	{

		if (!isActiveAndEnabled)

			return;

		if (m_Health != null && m_Health.IsDead)
		{
			StopSelfStabilizationWithoutUserCancel();
			return;
		}



		if (m_Health != null && m_Health.InjuryCount > m_LastKnownInjuryCount)

			m_SuppressAutoSelfStabilization = false;

		if (m_Health != null)

			m_LastKnownInjuryCount = m_Health.InjuryCount;



		TryBeginSelfStabilization();

	}

	private void HandleConsciousnessChanged(bool _isConscious)
	{
		if (!_isConscious)
			StopSelfStabilizationWithoutUserCancel();
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

		return true;
	}



	private bool TryBeginSelfStabilization()

	{

		if (m_SuppressAutoSelfStabilization)

			return false;

		if (m_IsSelfHealing || m_HealPresentationActive || m_SelfHealCoroutine != null)

			return false;

		if (!CanStartSelfStabilization(out _, out _, out _))

			return false;



		m_SelfHealCoroutine = StartCoroutine(SelfStabilizationSessionRoutine());

		return true;

	}



	private bool CanStartSelfStabilization(

		out int _injuryIndex,

		out InventorySlotRuntimeData _medkitSlot,

		out int _medkitBagIndex)

	{

		return CanTreatNextInjury(out _injuryIndex, out _medkitSlot, out _medkitBagIndex, false);

	}



	private bool TryResolveNextInjuryTarget(

		out int _injuryIndex,

		out InventorySlotRuntimeData _medkitSlot,

		out int _medkitBagIndex,

		out InjuryUiEntry _injury)

	{

		_injury = default;

		return CanTreatNextInjury(out _injuryIndex, out _medkitSlot, out _medkitBagIndex, true) &&

		       m_Health.TryGetInjury(_injuryIndex, out _injury);

	}



	private bool CanTreatNextInjury(

		out int _injuryIndex,

		out InventorySlotRuntimeData _medkitSlot,

		out int _medkitBagIndex,

		bool _allowDuringSelfStabilizationSession)

	{

		_injuryIndex = -1;

		_medkitSlot = default;

		_medkitBagIndex = -1;



		ResolveReferences();

		if (m_Health == null || m_Health.IsDead)

			return Fail("нет UnitHealth или юнит погиб");

		if (m_Consciousness != null && !m_Consciousness.IsConscious)

			return Fail("юнит без сознания");

		if (!_allowDuringSelfStabilizationSession && m_BusyState != null && m_BusyState.IsBusy)

			return Fail("юнит занят");

		if (!m_Health.TryGetWorstUnstabilizedInjury(out InjuryUiEntry injury, out _injuryIndex))

			return Fail("нет нестабилизированных травм");

		if (!TryFindUsableMedkit(injury, out _medkitSlot, out _medkitBagIndex))

			return Fail("нет IFAK с достаточным ресурсом");



		m_DebugLastFailureReason = string.Empty;

		return true;

	}



	private bool Fail(string _reason)

	{

		m_DebugLastFailureReason = _reason;

		return false;

	}



	private bool TryFindUsableMedkit(

		in InjuryUiEntry _injury,

		out InventorySlotRuntimeData _slot,

		out int _bagIndex)

	{

		_slot = default;

		_bagIndex = -1;

		if (m_CharacterInventory == null)

			return false;



		for (int i = 0; i < m_CharacterInventory.BagCount; i++)

		{

			InventorySlotRuntimeData slot = m_CharacterInventory.BagItems[i];

			MedkitRuntimeState medkitState = slot.InstanceState != null ? slot.InstanceState.MedkitState : null;

			if (medkitState == null || medkitState.Definition == null || !medkitState.CanTreatInjury(_injury))

				continue;



			_slot = slot;

			_bagIndex = i;

			return true;

		}



		return false;

	}



	private IEnumerator SelfStabilizationSessionRoutine()

	{

		m_HealPresentationActive = true;

		m_StopRequested = false;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.SelfStabilization, true);



		PrepareForHealing();

		ApplyLayerWeightImmediate(1f);



		bool sessionStarted = false;

		bool treatedAny = false;



		while (!m_StopRequested && TryResolveNextInjuryTarget(

			       out int injuryIndex,

			       out InventorySlotRuntimeData medkitSlot,

			       out int medkitBagIndex,

			       out InjuryUiEntry injury))

		{

			if (!CanContinueCurrentTreatment())

				break;

			yield return TreatSingleInjuryRoutine(

				injuryIndex,

				medkitSlot,

				medkitBagIndex,

				injury,

				!sessionStarted);



			sessionStarted = true;

			if (!m_StopRequested && CanContinueCurrentTreatment())

				treatedAny = true;

		}



		if (treatedAny && !m_StopRequested && CanContinueCurrentTreatment())

		{

			float healEndTimeoutSeconds = SelfHealPresentationTiming.GetHealEndTimeoutSeconds();

			m_IsSelfHealing = false;

			SyncAnimatorState();

			yield return WaitForMedkitState(s_StateHealEnd, healEndTimeoutSeconds);

		}



		FinishSelfHealPresentation(false);

	}



	private IEnumerator TreatSingleInjuryRoutine(

		int _injuryIndex,

		InventorySlotRuntimeData _medkitSlot,

		int _medkitBagIndex,

		InjuryUiEntry _injury,

		bool _playHealStart)

	{

		m_DebugCurrentInjuryIndex = _injuryIndex;

		m_DebugCompletedHealCycles = 0;

		m_DebugSmoothedInjuryProgress01 = 0f;

		m_DebugRequiredHealCycles = SelfHealPresentationTiming.ResolveHealCycles(_injury.SortPriority);

		m_CurrentInjuryUsesHealStart = _playHealStart;

		m_CurrentInjuryProgressTotalDuration = m_CurrentInjuryUsesHealStart

			? SelfHealPresentationTiming.HealStartDuration +

			  m_DebugRequiredHealCycles * SelfHealPresentationTiming.HealLoopCycleDuration

			: m_DebugRequiredHealCycles * SelfHealPresentationTiming.HealLoopCycleDuration;

		m_DebugCurrentInjuryProgressTotalDuration = m_CurrentInjuryProgressTotalDuration;

		m_CurrentInjuryProgressStartTime = Time.time;

		HealthStatusHealProgressBridge.Report(m_Health, _injuryIndex, 0f);



		if (_playHealStart)

		{

			float healStartTimeoutSeconds = SelfHealPresentationTiming.GetHealStartTimeoutSeconds();

			m_IsSelfHealing = true;

			SyncAnimatorState();

			yield return WaitForMedkitState(s_StateHeal, healStartTimeoutSeconds);

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



		if (!m_StopRequested && CanContinueCurrentTreatment() && m_Health.TryGetInjury(_injuryIndex, out InjuryUiEntry injury))

		{

			ApplyStabilization(_injuryIndex, _medkitSlot, _medkitBagIndex, injury);

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

		in InjuryUiEntry _injury)

	{

		MedkitRuntimeState medkitState = _medkitSlot.InstanceState != null ? _medkitSlot.InstanceState.MedkitState : null;

		if (medkitState == null || !medkitState.TryConsumeForInjury(_injury, out _))

			return;



		m_CharacterInventory?.TrySetBagItemAt(_medkitBagIndex, _medkitSlot);

		m_Health.TryMarkInjuryStabilized(_injuryIndex);

	}



	private void UpdateHealProgressInHealthCell()
	{
		if (!m_HealPresentationActive || m_Health == null || m_DebugCurrentInjuryIndex < 0)
			return;

		if (!CanContinueCurrentTreatment())
		{
			HealthStatusHealProgressBridge.Clear(m_Health);
			return;
		}

		HealthStatusHealProgressBridge.Report(
			m_Health,
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



	private bool TryGetMedkitLayerStateInfo(out AnimatorStateInfo _stateInfo)

	{

		_stateInfo = default;

		if (m_Animator == null)

			return false;

		if (m_MedkitHandsLayerIndex < 0)

			ResolveMedkitHandsLayerIndex();

		if (m_MedkitHandsLayerIndex < 0)

			return false;



		_stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_MedkitHandsLayerIndex);

		return m_Animator.GetLayerWeight(m_MedkitHandsLayerIndex) > 0.01f;

	}



	private void StopSelfStabilization(bool _restorePresentation, bool _userCancelled)

	{

		if (!m_IsSelfHealing && !m_HealPresentationActive && m_SelfHealCoroutine == null)

		{

			if (_userCancelled)

			{

				m_SuppressAutoSelfStabilization = true;

				HealthStatusHealProgressBridge.Clear(m_Health);

			}

			ClearLeftHandMedkitVisual();

			return;

		}



		if (_userCancelled)

		{

			m_SuppressAutoSelfStabilization = true;

		}



		m_StopRequested = true;

		if (m_SelfHealCoroutine != null)

		{

			StopCoroutine(m_SelfHealCoroutine);

			m_SelfHealCoroutine = null;

		}



		if (_restorePresentation)

			FinishSelfHealPresentation(false, _userCancelled);

	}



	private IEnumerator WaitForMedkitState(int _stateHash, float _timeoutSeconds)

	{

		float endTime = Time.time + Mathf.Max(0.01f, _timeoutSeconds);

		while (!m_StopRequested && Time.time < endTime)

		{

			if (IsMedkitLayerInState(_stateHash))

				yield break;



			yield return null;

		}

	}



	private bool IsMedkitLayerInState(int _stateHash)

	{

		if (m_Animator == null)

			return false;

		if (m_MedkitHandsLayerIndex < 0)

			ResolveMedkitHandsLayerIndex();

		if (m_MedkitHandsLayerIndex < 0)

			return false;



		AnimatorStateInfo stateInfo = m_Animator.GetCurrentAnimatorStateInfo(m_MedkitHandsLayerIndex);

		return stateInfo.shortNameHash == _stateHash;

	}



	private void FinishSelfHealPresentation(bool _tryContinue, bool _snapMedkitLayerOff = false)

	{

		HealthStatusHealProgressBridge.Clear(m_Health);

		m_IsSelfHealing = false;

		m_HealPresentationActive = false;

		m_StopRequested = false;

		m_SelfHealCoroutine = null;

		m_DebugCurrentInjuryIndex = -1;

		m_CurrentInjuryUsesHealStart = false;

		m_DebugCompletedHealCycles = 0;

		m_DebugSmoothedInjuryProgress01 = 0f;

		m_CurrentInjuryProgressStartTime = 0f;

		m_CurrentInjuryProgressTotalDuration = 0f;

		m_DebugCurrentInjuryProgressTotalDuration = 0f;

		m_BusyState?.SetReasonActive(UnitBusyState.BusyReason.SelfStabilization, false);

		if (_snapMedkitLayerOff)

			ApplyLayerWeightImmediate(0f);

		SyncAnimatorState();

		if (_snapMedkitLayerOff)

			ApplyLayerWeightImmediate(0f);

		ClearLeftHandMedkitVisual();

		m_UnitEquipment?.SetMainWeaponVisualActive(true);



		if (m_Stance != null)

			m_Stance.RequestStance(LocomotionStance.Standing);



		if (_tryContinue && isActiveAndEnabled)

			TryBeginSelfStabilization();

	}



	private void AttachMedkitVisualToLeftHand()

	{

		ClearLeftHandMedkitVisual();

		if (m_LeftHandAnchor == null || m_CharacterInventory == null)

			return;

		if (!TryFindAnyMedkitSlot(out InventorySlotRuntimeData medkitSlot))

			return;



		ItemDefinition definition = medkitSlot.Definition;

		if (definition == null || definition.EquippedVisualPrefab == null)

			return;



		m_LeftHandMedkitVisualInstance = Instantiate(definition.EquippedVisualPrefab, m_LeftHandAnchor);

		m_LeftHandMedkitVisualInstance.transform.localPosition = definition.RightHandLocalPosition;

		m_LeftHandMedkitVisualInstance.transform.localRotation = definition.RightHandLocalRotation;

		DisablePhysicsOnHealingVisual(m_LeftHandMedkitVisualInstance);

	}



	private bool TryFindAnyMedkitSlot(out InventorySlotRuntimeData _slot)

	{

		_slot = default;

		if (m_CharacterInventory == null)

			return false;



		for (int i = 0; i < m_CharacterInventory.BagCount; i++)

		{

			InventorySlotRuntimeData slot = m_CharacterInventory.BagItems[i];

			if (slot.InstanceState?.MedkitState?.Definition == null)

				continue;



			_slot = slot;

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



	private void SyncAnimatorState()

	{

		if (m_Animator == null)

			return;



		m_Animator.SetBool(s_IsSelfHealing, m_IsSelfHealing);

		SyncLayerWeight();

	}



	private void ResolveMedkitHandsLayerIndex()

	{

		m_MedkitHandsLayerIndex = m_Animator != null

			? m_Animator.GetLayerIndex(MedkitHandsLayerName)

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



		float targetWeight = m_HealPresentationActive ? 1f : 0f;

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



		m_SmoothedLayerWeight = _weight;

		m_Animator.SetLayerWeight(m_MedkitHandsLayerIndex, m_SmoothedLayerWeight);

	}

	#endregion

}


