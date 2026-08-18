using UnityEngine;

/// <summary>
/// Sole author of equipped weapon BASE local TRS under <c>Hand_R</c>.
/// Blends <see cref="WeaponPoseState"/> slots from <see cref="WeaponPoseDefinition"/>.
/// <para>
/// BASE = <see cref="CurrentBaseWeaponLocalPosition"/> / <see cref="CurrentBaseWeaponLocalRotation"/> (authored blend).
/// Compose aim-correction is rejected in gameplay (PointAim / Aiming / HipFire and other normal poses).
/// Tuner and bolt write the transform directly, not BASE.
/// FINAL commit = BASE when compose is empty or rejected.
/// </para>
/// Recoil must not call <see cref="ComposeRecoilLocalPosition"/> — visual recoil is a Hand_R overlay after animation.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(64)]
public sealed class UnitEquippedWeaponPose : MonoBehaviour
{
	public enum WeaponLocalComposeLayer
	{
		None = 0,
		AimCorrection = 1,
	}

	private const float c_SettledPoseOwnershipAssertDegrees = 0.15f;

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoading;
	[SerializeField] private UnitWeaponReloadController m_WeaponReload;
	[SerializeField] private UnitSelfStabilizationController m_SelfStabilization;
	[SerializeField] private UnitStabilizeOtherController m_StabilizeOther;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[SerializeField] private UnitRocketLauncherOrderController m_RocketLauncherOrder;
	[SerializeField] private UnitAnimatorStance m_Stance;
	[SerializeField] private Animator m_Animator;
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;

	[Header("Переход позы")]
	[SerializeField, Min(0f)] private float m_FallbackBlendDuration = 0.28f;
	[SerializeField] private AnimationCurve m_PoseBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	[SerializeField] private bool m_LogHighReadyToPreAim;
	[Tooltip("Консоль: стоячий E-cycle — слоты оружия vs записанный TRS vs IK. Только выбранный юнит.")]
	[SerializeField] private bool m_LogStandingPoseSwitch;
	[Tooltip("Editor/development: warn if settled PointAim/Aiming/HipFire FINAL rotation drifts from BASE.")]
	[SerializeField] private bool m_LogPoseOwnership;
	#endregion

	#region Private Fields
	private WeaponPoseState m_CurrentPose = WeaponPoseState.LowReady;
	private WeaponPoseState m_TargetPose = WeaponPoseState.LowReady;
	private float m_PoseBlend01 = 1f;
	private bool m_IsPoseBlendAnimating;
	private float m_PoseBlendElapsed;
	private float m_PoseBlendDuration = 0.28f;
	private int m_LastPoseBlendAdvanceFrame = -1;

	private Vector3 m_CurrentBaseWeaponLocalPosition;
	private Quaternion m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
	private VehiclePassengerState m_SubscribedVehiclePassengerState;

	private bool m_HasComposedAimRotation;
	private Quaternion m_ComposedAimLocalRotation = Quaternion.identity;
	private WeaponLocalComposeLayer m_ComposedAimLayer = WeaponLocalComposeLayer.None;
	private bool m_HasComposedRecoilPosition;
	private Vector3 m_ComposedRecoilLocalPosition;
	private Transform m_PendingWeaponRoot;
	private WeaponPoseState m_LastIkPoseSide;
	private bool m_HasIkPoseSide;
	private bool m_HasCapturedBlendFrom;
	private Vector3 m_CapturedBlendFromPos;
	private Quaternion m_CapturedBlendFromRot = Quaternion.identity;
	private int m_HighReadyPreAimLogId;
	private int m_StandingPoseLogId;
	private bool m_PendingStandingPoseEndLog;
	private string m_LastPoseApplyPath;
	private bool m_HighReadyPreAimLogActive;
	#endregion

	#region Public Properties
	public WeaponPoseState CurrentPose => m_CurrentPose;
	public WeaponPoseState TargetPose => m_TargetPose;
	public float PoseBlend01 => m_PoseBlend01;
	public bool IsPoseBlendAnimating => m_IsPoseBlendAnimating;

	/// <summary>0 = LowReady/NotReady/NotReadyPatrol, 1 = fire pose (compat for Aiming/IK readers).</summary>
	public float ReadyPoseBlend01
	{
		get
		{
			float fromRaised = RaisedAmount(m_CurrentPose);
			float toRaised = RaisedAmount(m_TargetPose);
			return Mathf.Lerp(fromRaised, toRaised, m_PoseBlend01);
		}
	}

	/// <summary>
	/// 0 = cannot shoot (HighReady/PreAim/LowReady/NotReady), 1 = HipFire/PointAim/Aiming.
	/// Follows the weapon blend so Aim layer / barrel correction do not race ahead.
	/// </summary>
	public float FireCapableBlend01
	{
		get
		{
			float fromFire = m_CurrentPose.CanShootFromPose() ? 1f : 0f;
			float toFire = m_TargetPose.CanShootFromPose() ? 1f : 0f;
			return Mathf.Lerp(fromFire, toFire, m_PoseBlend01);
		}
	}

	private static float RaisedAmount(WeaponPoseState _pose) =>
		_pose.IsWeaponRaised() ? 1f : 0f;

	public Vector3 CurrentBaseWeaponLocalPosition => m_CurrentBaseWeaponLocalPosition;
	public Quaternion CurrentBaseWeaponLocalRotation => m_CurrentBaseWeaponLocalRotation;
	public Vector3 BaseWeaponLocalPosition => m_CurrentBaseWeaponLocalPosition;
	public Quaternion BaseWeaponLocalRotation => m_CurrentBaseWeaponLocalRotation;
	public Quaternion ComposedAimLocalRotation =>
		m_HasComposedAimRotation ? m_ComposedAimLocalRotation : m_CurrentBaseWeaponLocalRotation;
	public bool HasComposedAimRotation => m_HasComposedAimRotation;
	public WeaponLocalComposeLayer ComposedAimLayer => m_ComposedAimLayer;

	public event System.Action ReadyPoseBlendChanged;
	public event System.Action PoseChanged;

	public bool ShouldLogHighReadyToPreAim
	{
		get
		{
			if (!m_LogHighReadyToPreAim || !m_HighReadyPreAimLogActive)
				return false;
			RtsUnitMember member = GetComponent<RtsUnitMember>();
			return member == null || member.IsSelected;
		}
	}

	public bool ShouldLogStandingPoseSwitch
	{
		get
		{
			if (!m_LogStandingPoseSwitch)
				return false;
			if (m_ReadyHands != null && !m_ReadyHands.IsStandingIdleNow())
				return false;
			RtsUnitMember member = GetComponent<RtsUnitMember>();
			return member == null || member.IsSelected;
		}
	}

	public static bool IsHighReadyPreAimPair(WeaponPoseState _from, WeaponPoseState _to) =>
		(_from == WeaponPoseState.HighReady && _to == WeaponPoseState.PreAim)
		|| (_from == WeaponPoseState.PreAim && _to == WeaponPoseState.HighReady);
	#endregion

	#region Unity Lifecycle
	private void Awake() => ResolveReferences();

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
		SubscribeVehiclePassengerEvents();
		SyncTargetPoseImmediate();
		m_CurrentPose = m_TargetPose;
		m_PoseBlend01 = 1f;
		m_IsPoseBlendAnimating = false;
		ApplyWeaponLocalPose();
		CommitFinalWeaponTransform();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		UnsubscribeVehiclePassengerEvents();
		StopPoseBlend();
	}

	private void Update()
	{
		if (IsBlockedByRagdoll())
			return;

		EnsureVehiclePassengerSubscription();
		WeaponPoseState desired = ComputeDesiredPose();
		if (desired != m_TargetPose)
		{
			BeginPoseTransition(desired);
		}

		AdvancePoseBlend();
		NotifyIkPoseSideIfChanged();
		ClearCompositionOverrides();
		ApplyWeaponLocalPose();
	}

	public void CommitWeaponTransformForFrame()
	{
		if (IsBlockedByRagdoll())
			return;
		CommitFinalWeaponTransform();
	}
	#endregion

	#region Public Methods
	public void OnWeaponReadyStateChanged()
	{
		EnsureVehiclePassengerSubscription();
		WeaponPoseState desired = ComputeDesiredPose();
		BeginPoseTransition(desired);
		ApplyWeaponLocalPose();
		CommitFinalWeaponTransform();
	}

	public void ApplyImmediateFromEquipment()
	{
		SyncTargetPoseImmediate();
		m_CurrentPose = m_TargetPose;
		m_PoseBlend01 = 1f;
		StopPoseBlend();
		ClearCompositionOverrides();
		ApplyWeaponLocalPose();
		CommitFinalWeaponTransform();
		ReadyPoseBlendChanged?.Invoke();
		PoseChanged?.Invoke();
	}

	public void ComposeAimLocalRotation(Quaternion _localRotation) =>
		ComposeAimLocalRotation(_localRotation, WeaponLocalComposeLayer.AimCorrection);

	/// <summary>
	/// Request a temporary weapon-local overlay. Gameplay aim-correction is always rejected;
	/// FINAL stays BASE. Tuner and bolt write the transform directly.
	/// </summary>
	public void ComposeAimLocalRotation(Quaternion _localRotation, WeaponLocalComposeLayer _layer)
	{
		m_ComposedAimLayer = _layer;
		if (_layer == WeaponLocalComposeLayer.None || !AcceptsAimCorrectionCompose())
		{
			m_HasComposedAimRotation = false;
			m_ComposedAimLayer = WeaponLocalComposeLayer.None;
			return;
		}

		m_ComposedAimLocalRotation = _localRotation;
		m_HasComposedAimRotation = true;
	}

	/// <summary>
	/// Unused. Visual recoil is a Hand_R overlay via <see cref="WeaponVisualRecoilApplicator"/>,
	/// not a BASE compose and not a weapon-local punch.
	/// </summary>
	public void ComposeRecoilLocalPosition(Vector3 _localPosition)
	{
		m_ComposedRecoilLocalPosition = _localPosition;
		m_HasComposedRecoilPosition = true;
	}

	/// <summary>
	/// Who last owned Equipped_* local this frame: base / pointAimCorr / bolt / tuner.
	/// Visual recoil does not write weapon local; <c>recoilPunch</c> only if unused
	/// <see cref="ComposeRecoilLocalPosition"/> was called.
	/// </summary>
	public string GetWeaponLocalOwnerTag()
	{
		if (IsRuntimeTuningSkipWrite())
			return "tuner";
		if (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return "bolt";
		if (m_HasComposedRecoilPosition)
			return "recoilPunch";
		if (m_HasComposedAimRotation)
			return "pointAimCorr";
		return "base";
	}

	/// <summary>
	/// During a raise into a fire pose, animator follows the destination immediately
	/// so WeaponReady CrossFade runs with the blend instead of snapping at the end.
	/// Run/sprint suppress follows the target immediately so locomotion is not stuck on the old fire pose.
	/// Standing idle never snaps IK to the target via a leftover restore flag.
	/// </summary>
	public WeaponPoseState GetEffectivePoseForIk()
	{
		if (m_IsPoseBlendAnimating && m_PoseBlend01 < 0.999f)
		{
			if (m_ReadyHands != null && m_ReadyHands.ShouldIkFollowPoseTargetImmediately())
				return m_TargetPose;
			if (!m_CurrentPose.CanFireFromPose() && m_TargetPose.CanFireFromPose())
				return m_TargetPose;
			return m_CurrentPose;
		}

		return m_TargetPose;
	}

	/// <summary>Authored or derived weapon local TRS for a pose slot (Hand_R space).</summary>
	public bool TryGetWeaponLocalPose(WeaponPoseState _pose, out Vector3 _position, out Quaternion _rotation)
	{
		_position = Vector3.zero;
		_rotation = Quaternion.identity;

		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
		if (m_UnitEquipment == null && !useRocketLauncher)
			return false;

		ItemDefinition def = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment.EquippedDefinition;
		if (def == null)
			return false;

		bool inVehicle = IsVehiclePassengerFireCapable();
		WeaponStance poseStance = inVehicle
			? WeaponStance.Vehicle
			: (GetCurrentStance() == LocomotionStance.Crouch ? WeaponStance.Crouching : WeaponStance.Standing);
		ResolveTargetLocalPose(def, poseStance, _pose, inVehicle, out _position, out _rotation);
		return true;
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>() ?? GetComponentInParent<UnitEquipment>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>() ?? GetComponentInParent<UnitWeaponReadyHandsLayer>();
		if (m_MagazineLoading == null)
			m_MagazineLoading = GetComponentInParent<UnitMagazineLoadingController>();
		if (m_WeaponReload == null)
			m_WeaponReload = GetComponentInParent<UnitWeaponReloadController>();
		if (m_SelfStabilization == null)
			m_SelfStabilization = GetComponentInParent<UnitSelfStabilizationController>();
		if (m_StabilizeOther == null)
			m_StabilizeOther = GetComponentInParent<UnitStabilizeOtherController>();
		if (m_BusyState == null)
			m_BusyState = GetComponentInParent<UnitBusyState>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponentInParent<UnitRagdollController>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>()
			                ?? GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>()
			                       ?? GetComponentInParent<UnitRocketLauncherOrderController>();
		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>() ?? GetComponentInParent<UnitAnimatorStance>();
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		EnsureVehiclePassengerState();
	}

	private bool IsBlockedByRagdoll() =>
		m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts;

	private void SubscribeEquipmentEvents()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged += HandleEquipmentChanged;
	}

	private void UnsubscribeEquipmentEvents()
	{
		if (m_UnitEquipment != null)
			m_UnitEquipment.EquipmentChanged -= HandleEquipmentChanged;
	}

	private void SubscribeVehiclePassengerEvents() => EnsureVehiclePassengerSubscription();

	private void UnsubscribeVehiclePassengerEvents()
	{
		if (m_SubscribedVehiclePassengerState != null)
		{
			m_SubscribedVehiclePassengerState.ReadyIntentChanged -= HandleVehicleReadyIntentChanged;
			m_SubscribedVehiclePassengerState = null;
		}
	}

	private void EnsureVehiclePassengerSubscription()
	{
		VehiclePassengerState state = EnsureVehiclePassengerState();
		if (state == m_SubscribedVehiclePassengerState)
			return;
		if (m_SubscribedVehiclePassengerState != null)
			m_SubscribedVehiclePassengerState.ReadyIntentChanged -= HandleVehicleReadyIntentChanged;
		m_SubscribedVehiclePassengerState = state;
		if (m_SubscribedVehiclePassengerState != null)
			m_SubscribedVehiclePassengerState.ReadyIntentChanged += HandleVehicleReadyIntentChanged;
	}

	private void HandleEquipmentChanged() => ApplyImmediateFromEquipment();
	private void HandleVehicleReadyIntentChanged() => OnWeaponReadyStateChanged();

	private void SyncTargetPoseImmediate() => m_TargetPose = ComputeDesiredPose();

	private WeaponPoseState ComputeDesiredPose()
	{
		if (IsVehiclePassengerFireCapable())
			return m_VehiclePassengerState.WantsReadyPose ? WeaponPoseState.PointAim : WeaponPoseState.LowReady;

		if (m_ReadyHands != null)
			return m_ReadyHands.EffectivePoseState;

		return WeaponPoseState.LowReady;
	}

	private void BeginPoseTransition(WeaponPoseState _desired)
	{
		if (_desired == m_TargetPose && m_IsPoseBlendAnimating)
			return;

		if (m_IsPoseBlendAnimating && m_PoseBlend01 < 1f)
		{
			if (_desired == m_CurrentPose)
			{
				InvertActiveBlendTo(_desired);
				PoseChanged?.Invoke();
				return;
			}

			CaptureCurrentVisualAsBlendFrom();
			if (m_PoseBlend01 >= 0.5f)
				m_CurrentPose = m_TargetPose;
		}
		else if (m_PoseBlend01 >= 1f)
			m_CurrentPose = m_TargetPose;

		CaptureCurrentVisualAsBlendFrom();
		m_TargetPose = _desired;
		m_PoseBlend01 = 0f;
		m_PoseBlendDuration = ResolveTransitionDuration(m_CurrentPose, m_TargetPose);
		if (m_HasCapturedBlendFrom && m_CurrentPose == m_TargetPose)
			m_PoseBlendDuration = Mathf.Max(0.12f, m_PoseBlendDuration);
		if (m_PoseBlendDuration <= 0f || (m_CurrentPose == m_TargetPose && !m_HasCapturedBlendFrom))
		{
			m_PoseBlend01 = 1f;
			m_CurrentPose = m_TargetPose;
			StopPoseBlend();
			LogHighReadyPreAim("SNAP no-blend, target applied immediately");
			LogStandingPoseSwitch("SNAP no-blend");
			PoseChanged?.Invoke();
			ReadyPoseBlendChanged?.Invoke();
			return;
		}

		m_IsPoseBlendAnimating = true;
		m_PoseBlendElapsed = 0f;
		m_LastPoseBlendAdvanceFrame = -1;
		LogHighReadyPreAimStart();
		LogStandingPoseSwitch("START");
		PoseChanged?.Invoke();
	}

	private void InvertActiveBlendTo(WeaponPoseState _desired)
	{
		float oldDuration = Mathf.Max(0.0001f, m_PoseBlendDuration);
		float oldNorm = Mathf.Clamp01(m_PoseBlendElapsed / oldDuration);
		WeaponPoseState previousTarget = m_TargetPose;
		m_TargetPose = _desired;
		m_CurrentPose = previousTarget;
		m_HasCapturedBlendFrom = false;
		m_PoseBlendDuration = ResolveTransitionDuration(m_CurrentPose, m_TargetPose);
		m_PoseBlendElapsed = (1f - oldNorm) * Mathf.Max(0.0001f, m_PoseBlendDuration);
		m_PoseBlend01 = EvaluateBlendCurve(Mathf.Clamp01(m_PoseBlendElapsed / Mathf.Max(0.0001f, m_PoseBlendDuration)));
		m_IsPoseBlendAnimating = true;
		m_LastPoseBlendAdvanceFrame = -1;
	}

	private void CaptureCurrentVisualAsBlendFrom()
	{
		m_HasCapturedBlendFrom = true;
		m_CapturedBlendFromPos = m_CurrentBaseWeaponLocalPosition;
		m_CapturedBlendFromRot = m_CurrentBaseWeaponLocalRotation;
	}

	private float EvaluateBlendCurve(float _normalizedTime)
	{
		float t = Mathf.Clamp01(_normalizedTime);
		// HighReady → PreAim (and any !fire → fire): EaseInOut leaves the weapon parked
		// while Aim_Point already pulls the muzzle down. Linear keeps one arc to PreAim.
		if (!m_CurrentPose.CanFireFromPose() && m_TargetPose.CanFireFromPose())
			return t;
		if (m_PoseBlendCurve != null && m_PoseBlendCurve.length > 0)
			return m_PoseBlendCurve.Evaluate(t);
		return Mathf.SmoothStep(0f, 1f, t);
	}

	private float ResolveTransitionDuration(WeaponPoseState _from, WeaponPoseState _to)
	{
		if (m_ReadyHands != null && m_ReadyHands.PoseCapabilityCache.IsValid)
			return m_ReadyHands.PoseCapabilityCache.GetTransitionSeconds(_from, _to);
		if (m_FallbackBlendDuration > 0f)
			return m_FallbackBlendDuration;
		return WeaponPoseAutoCapabilityCache.DefaultTransitionSeconds(_from, _to);
	}

	private void StopPoseBlend()
	{
		m_IsPoseBlendAnimating = false;
		m_PoseBlendElapsed = 0f;
		m_LastPoseBlendAdvanceFrame = -1;
		m_HasCapturedBlendFrom = false;
	}

	private void AdvancePoseBlend()
	{
		if (!m_IsPoseBlendAnimating)
			return;

		if (m_LastPoseBlendAdvanceFrame != Time.frameCount)
		{
			m_LastPoseBlendAdvanceFrame = Time.frameCount;
			m_PoseBlendElapsed += Time.deltaTime;
		}

		float duration = Mathf.Max(0.0001f, m_PoseBlendDuration);
		float normalizedTime = Mathf.Clamp01(m_PoseBlendElapsed / duration);
		m_PoseBlend01 = EvaluateBlendCurve(normalizedTime);
		if (normalizedTime >= 1f)
		{
			m_PoseBlend01 = 1f;
			LogHighReadyPreAim("END blend complete");
			m_CurrentPose = m_TargetPose;
			StopPoseBlend();
			m_PendingStandingPoseEndLog = m_LogStandingPoseSwitch;
			LogStandingPoseSwitch("END blend complete");
			ReadyPoseBlendChanged?.Invoke();
		}
	}

	private void NotifyIkPoseSideIfChanged()
	{
		WeaponPoseState side = GetEffectivePoseForIk();
		if (m_HasIkPoseSide && side == m_LastIkPoseSide)
			return;
		m_HasIkPoseSide = true;
		m_LastIkPoseSide = side;
		ReadyPoseBlendChanged?.Invoke();
		m_ReadyHands?.RefreshAnimatorPoseParameters();
	}

	private void ApplyWeaponLocalPose()
	{
		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
		if (m_UnitEquipment == null && !useRocketLauncher)
			return;

		bool operatingTurret = !useRocketLauncher && m_UnitEquipment.IsOperatingVehicleTurret;
		Transform weaponRoot = useRocketLauncher
			? m_RocketLauncherOrder.HandLauncherRoot
			: operatingTurret
				? m_UnitEquipment.EffectiveWeaponRoot
				: m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment.EquippedDefinition;
		m_PendingWeaponRoot = weaponRoot;
		if (weaponRoot == null || def == null)
		{
			m_CurrentBaseWeaponLocalPosition = Vector3.zero;
			m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
			return;
		}

		if (operatingTurret)
			return;

		bool inVehicle = IsVehiclePassengerFireCapable();
		WeaponStance poseStance = inVehicle
			? WeaponStance.Vehicle
			: (GetCurrentStance() == LocomotionStance.Crouch ? WeaponStance.Crouching : WeaponStance.Standing);

		WeaponPoseState fromPose = m_CurrentPose;
		WeaponPoseState toPose = m_TargetPose;
		float blend01 = m_PoseBlend01;

		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			m_RuntimeTuner.GetOverridePoses(
				out Vector3 relaxedPosition,
				out Quaternion relaxedRotation,
				out Vector3 readyPosition,
				out Quaternion readyRotation,
				out blend01);
			m_CurrentBaseWeaponLocalPosition = Vector3.Lerp(relaxedPosition, readyPosition, blend01);
			m_CurrentBaseWeaponLocalRotation = Quaternion.Slerp(relaxedRotation, readyRotation, blend01);
			return;
		}

		if (m_HasCapturedBlendFrom)
		{
			ResolveTargetLocalPose(def, poseStance, toPose, inVehicle, out Vector3 toPos, out Quaternion toRot);
			float t = Mathf.Clamp01(blend01);
			m_CurrentBaseWeaponLocalPosition = Vector3.Lerp(m_CapturedBlendFromPos, toPos, t);
			m_CurrentBaseWeaponLocalRotation = Quaternion.Slerp(m_CapturedBlendFromRot, toRot, t);
			m_LastPoseApplyPath = "captured→target";
			LogHighReadyPreAimApply(poseStance, def, inVehicle, toPos, toRot);
			return;
		}

		if (def.WeaponPoseDefinition != null)
		{
			def.WeaponPoseDefinition.GetBlended(
				poseStance, fromPose, toPose, blend01,
				out m_CurrentBaseWeaponLocalPosition,
				out m_CurrentBaseWeaponLocalRotation);
			m_LastPoseApplyPath = "GetBlended slots";
			ResolveTargetLocalPose(def, poseStance, toPose, inVehicle, out Vector3 toPos, out Quaternion toRot);
			LogHighReadyPreAimApply(poseStance, def, inVehicle, toPos, toRot);
			return;
		}

		// Legacy flat fields: LowReady ↔ PointAim
		Vector3 lowPos;
		Quaternion lowRot;
		Vector3 pointPos;
		Quaternion pointRot;
		if (inVehicle)
		{
			lowPos = def.ResolveVehicleRightHandLocalPosition();
			lowRot = def.ResolveVehicleRightHandLocalRotation();
			pointPos = def.ResolveVehicleRightHandReadyLocalPosition();
			pointRot = def.ResolveVehicleRightHandReadyLocalRotation();
		}
		else
		{
			lowPos = def.ResolveRightHandLocalPosition(GetCurrentStance());
			lowRot = def.ResolveRightHandLocalRotation(GetCurrentStance());
			pointPos = def.ResolveRightHandReadyLocalPosition(GetCurrentStance());
			pointRot = def.ResolveRightHandReadyLocalRotation(GetCurrentStance());
		}

		float raisedT = ReadyPoseBlend01;
		m_CurrentBaseWeaponLocalPosition = Vector3.Lerp(lowPos, pointPos, raisedT);
		m_CurrentBaseWeaponLocalRotation = Quaternion.Slerp(lowRot, pointRot, raisedT);
	}

	private void ResolveTargetLocalPose(
		ItemDefinition _def,
		WeaponStance _stance,
		WeaponPoseState _pose,
		bool _inVehicle,
		out Vector3 _position,
		out Quaternion _rotation)
	{
		if (_def.WeaponPoseDefinition != null)
		{
			_def.WeaponPoseDefinition.ResolveLocalPose(_stance, _pose, out _position, out _rotation);
			return;
		}

		if (_inVehicle)
		{
			if (_pose.IsWeaponRaised())
			{
				_position = _def.ResolveVehicleRightHandReadyLocalPosition();
				_rotation = _def.ResolveVehicleRightHandReadyLocalRotation();
			}
			else
			{
				_position = _def.ResolveVehicleRightHandLocalPosition();
				_rotation = _def.ResolveVehicleRightHandLocalRotation();
			}

			return;
		}

		if (_pose.IsWeaponRaised())
		{
			_position = _def.ResolveRightHandReadyLocalPosition(GetCurrentStance());
			_rotation = _def.ResolveRightHandReadyLocalRotation(GetCurrentStance());
		}
		else
		{
			_position = _def.ResolveRightHandLocalPosition(GetCurrentStance());
			_rotation = _def.ResolveRightHandLocalRotation(GetCurrentStance());
		}
	}

	private void ClearCompositionOverrides()
	{
		m_HasComposedAimRotation = false;
		m_HasComposedRecoilPosition = false;
		m_ComposedAimLayer = WeaponLocalComposeLayer.None;
	}

	/// <summary>
	/// Gameplay never accepts weapon-local aim compose. Tuner and bolt write the transform directly.
	/// </summary>
	private bool AcceptsAimCorrectionCompose()
	{
		return false;
	}

	private static bool IsAuthoredOnlyWeaponLocalPose(WeaponPoseState _pose) =>
		_pose == WeaponPoseState.Aiming
		|| _pose.IsHipFireHold()
		|| _pose == WeaponPoseState.PointAim;

	private bool IsReloadOrBoltBusy()
	{
		if (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return true;
		if (m_WeaponReload != null && m_WeaponReload.IsReloadBusy)
			return true;
		if (m_MagazineLoading != null && m_MagazineLoading.IsLoadingMagazine)
			return true;
		return false;
	}

	private void CommitFinalWeaponTransform()
	{
		if (IsRuntimeTuningSkipWrite())
			return;
		if (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return;

		Transform weaponRoot = m_PendingWeaponRoot;
		if (weaponRoot == null)
			return;

		bool useComposedAim = m_HasComposedAimRotation && AcceptsAimCorrectionCompose();
		weaponRoot.localPosition = m_HasComposedRecoilPosition
			? m_ComposedRecoilLocalPosition
			: m_CurrentBaseWeaponLocalPosition;
		weaponRoot.localRotation = useComposedAim
			? m_ComposedAimLocalRotation
			: m_CurrentBaseWeaponLocalRotation;

		AssertSettledAuthoredPoseMatchesBase(weaponRoot);

		if (ShouldLogHighReadyToPreAim)
			LogHighReadyPreAimCommit(weaponRoot);
		if (m_PendingStandingPoseEndLog)
		{
			m_PendingStandingPoseEndLog = false;
			LogStandingPoseSwitchCommit(weaponRoot);
		}
		if (!m_IsPoseBlendAnimating)
			m_HighReadyPreAimLogActive = false;
	}

	private void AssertSettledAuthoredPoseMatchesBase(Transform _weaponRoot)
	{
		if (_weaponRoot == null)
			return;
		if (m_IsPoseBlendAnimating)
			return;
		if (!IsAuthoredOnlyWeaponLocalPose(m_TargetPose))
			return;
		if (IsRuntimeTuningSkipWrite() || IsReloadOrBoltBusy())
			return;

		float angle = Quaternion.Angle(_weaponRoot.localRotation, m_CurrentBaseWeaponLocalRotation);
		bool drifted = angle >= c_SettledPoseOwnershipAssertDegrees;
		if (m_LogPoseOwnership && drifted)
		{
			Debug.LogWarning(
				$"[PoseOwnership] settled {m_TargetPose} FINAL≠BASE ang={angle:F3}° " +
				$"(limit {c_SettledPoseOwnershipAssertDegrees:F2}°) owner={GetWeaponLocalOwnerTag()}",
				this);
		}

#if UNITY_EDITOR
		Debug.Assert(
			!drifted,
			$"[PoseOwnership] {name} settled {m_TargetPose}: Angle(FINAL, BASE)={angle:F3}° must be < {c_SettledPoseOwnershipAssertDegrees:F2}°");
#endif
	}

	private bool IsRuntimeTuningSkipWrite()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.ShouldSkipWeaponPoseWrite;
	}

	private void LogStandingPoseSwitch(string _label)
	{
		if (!ShouldLogStandingPoseSwitch)
			return;
		if (_label == "START" || _label.StartsWith("SNAP", System.StringComparison.Ordinal))
			m_StandingPoseLogId++;

		ItemDefinition def = m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
		bool inVehicle = IsVehiclePassengerFireCapable();
		WeaponStance stance = inVehicle
			? WeaponStance.Vehicle
			: (GetCurrentStance() == LocomotionStance.Crouch ? WeaponStance.Crouching : WeaponStance.Standing);

		string slotLines = "  no ItemDefinition";
		if (def != null)
		{
			ResolveTargetLocalPose(def, stance, m_CurrentPose, inVehicle, out Vector3 fromPos, out Quaternion fromRot);
			ResolveTargetLocalPose(def, stance, m_TargetPose, inVehicle, out Vector3 toPos, out Quaternion toRot);
			bool slotsIdentical = Vector3.Distance(fromPos, toPos) < 0.0001f
			                      && Quaternion.Angle(fromRot, toRot) < 0.05f;
			slotLines =
				$"  captured {FmtPose(m_CapturedBlendFromPos, m_CapturedBlendFromRot)}\n" +
				$"  fromSlot {FmtPose(fromPos, fromRot)}\n" +
				$"  toSlot   {FmtPose(toPos, toRot)}\n" +
				$"  nowBase  {FmtPose(m_CurrentBaseWeaponLocalPosition, m_CurrentBaseWeaponLocalRotation)}\n" +
				$"  slotsIdentical={slotsIdentical} " +
				$"captured→to pos={Vector3.Distance(m_CapturedBlendFromPos, toPos):F3} " +
				$"ang={Quaternion.Angle(m_CapturedBlendFromRot, toRot):F1}°";
		}

		bool weaponReady = m_Animator != null && m_Animator.GetBool(UnitAnimatorWeaponMode.ParamWeaponReady);
		int standIdle = m_Animator != null ? m_Animator.GetInteger(UnitAnimatorWeaponMode.ParamWeaponStandIdle) : -1;
		string hands = m_ReadyHands != null ? m_ReadyHands.FormatStandingPoseDebug() : "hands=null";

		Debug.Log(
			$"[PoseStand #{m_StandingPoseLogId}] {_label} unit={name} {m_CurrentPose}→{m_TargetPose} " +
			$"t={m_PoseBlend01:F3} dur={m_PoseBlendDuration:F3}s path={m_LastPoseApplyPath} " +
			$"ikSide={GetEffectivePoseForIk()} ikFollowTarget={m_ReadyHands != null && m_ReadyHands.ShouldIkFollowPoseTargetImmediately()} " +
			$"fireBlend={FireCapableBlend01:F3} raisedBlend={ReadyPoseBlend01:F3} " +
			$"composedAim={m_HasComposedAimRotation} WeaponReady={weaponReady} StandIdle={standIdle} " +
			$"stance={stance}\n  {hands}\n{slotLines}",
			this);
	}

	private void LogStandingPoseSwitchCommit(Transform _weaponRoot)
	{
		if (!m_LogStandingPoseSwitch)
			return;
		RtsUnitMember member = GetComponent<RtsUnitMember>();
		if (member != null && !member.IsSelected)
			return;

		Transform barrel = null;
		if (m_UnitEquipment != null && m_UnitEquipment.EquippedWeapon != null)
			barrel = m_UnitEquipment.EquippedWeapon.BarrelTransform;
		float barrelPitch = BarrelWorldPitch(barrel != null ? barrel : _weaponRoot);
		float weaponPitch = BarrelWorldPitch(_weaponRoot);
		bool weaponReady = m_Animator != null && m_Animator.GetBool(UnitAnimatorWeaponMode.ParamWeaponReady);

		Debug.Log(
			$"[PoseStand #{m_StandingPoseLogId}] COMMIT unit={name} {m_CurrentPose}/{m_TargetPose} " +
			$"writtenLocal {FmtPose(_weaponRoot.localPosition, _weaponRoot.localRotation)} " +
			$"composedAim={m_HasComposedAimRotation} composedRecoil={m_HasComposedRecoilPosition} " +
			$"weaponFwdPitch={weaponPitch:F1}° barrelFwdPitch={barrelPitch:F1}° " +
			$"WeaponReady={weaponReady} parent={_weaponRoot.parent?.name} " +
			$"deltaWrittenVsBase pos={Vector3.Distance(_weaponRoot.localPosition, m_CurrentBaseWeaponLocalPosition):F4} " +
			$"ang={Quaternion.Angle(_weaponRoot.localRotation, m_CurrentBaseWeaponLocalRotation):F2}°",
			this);
	}

	private void LogHighReadyPreAimStart()
	{
		if (!m_LogHighReadyToPreAim || !IsHighReadyPreAimPair(m_CurrentPose, m_TargetPose))
			return;
		RtsUnitMember member = GetComponent<RtsUnitMember>();
		if (member != null && !member.IsSelected)
			return;

		m_HighReadyPreAimLogActive = true;
		m_HighReadyPreAimLogId++;

		ItemDefinition def = m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
		bool inVehicle = IsVehiclePassengerFireCapable();
		if (def == null)
		{
			Debug.Log(
				$"[HR→PreAim #{m_HighReadyPreAimLogId}] START unit={name} {m_CurrentPose}→{m_TargetPose} NO ItemDefinition",
				this);
			return;
		}
		WeaponStance stance = inVehicle
			? WeaponStance.Vehicle
			: (GetCurrentStance() == LocomotionStance.Crouch ? WeaponStance.Crouching : WeaponStance.Standing);

		ResolveTargetLocalPose(def, stance, WeaponPoseState.HighReady, inVehicle, out Vector3 hrPos, out Quaternion hrRot);
		ResolveTargetLocalPose(def, stance, WeaponPoseState.PreAim, inVehicle, out Vector3 paPos, out Quaternion paRot);
		ResolveTargetLocalPose(def, stance, WeaponPoseState.LowReady, inVehicle, out Vector3 lowPos, out Quaternion lowRot);
		ResolveTargetLocalPose(def, stance, WeaponPoseState.Aiming, inVehicle, out Vector3 aimPos, out Quaternion aimRot);

		bool hasHrSlot = def != null && def.WeaponPoseDefinition != null
			&& def.WeaponPoseDefinition.TryGetPose(stance, WeaponPoseState.HighReady, out _);
		float cacheSec = m_ReadyHands != null && m_ReadyHands.PoseCapabilityCache.IsValid
			? m_ReadyHands.PoseCapabilityCache.GetTransitionSeconds(m_CurrentPose, m_TargetPose)
			: -1f;

		Debug.Log(
			$"[HR→PreAim #{m_HighReadyPreAimLogId}] START unit={name} " +
			$"{m_CurrentPose}→{m_TargetPose} duration={m_PoseBlendDuration:F3}s cacheSec={cacheSec:F3} " +
			$"fallback={m_FallbackBlendDuration:F3} linearFireRaise=true captured={m_HasCapturedBlendFrom} " +
			$"handsEffective={m_ReadyHands?.EffectivePoseState} ikSide={GetEffectivePoseForIk()} " +
			$"highReadySlotExists={hasHrSlot} stance={stance} " +
			$"slotsIdentical={Vector3.Distance(hrPos, paPos) < 0.0001f && Quaternion.Angle(hrRot, paRot) < 0.05f}\n" +
			$"  captured {FmtPose(m_CapturedBlendFromPos, m_CapturedBlendFromRot)}\n" +
			$"  HighReady {FmtPose(hrPos, hrRot)}\n" +
			$"  PreAim    {FmtPose(paPos, paRot)}\n" +
			$"  LowReady  {FmtPose(lowPos, lowRot)}\n" +
			$"  Aiming    {FmtPose(aimPos, aimRot)}\n" +
			$"  dist captured→PreAim pos={Vector3.Distance(m_CapturedBlendFromPos, paPos):F3} " +
			$"ang={Quaternion.Angle(m_CapturedBlendFromRot, paRot):F1}° " +
			$"captured→HighReady pos={Vector3.Distance(m_CapturedBlendFromPos, hrPos):F3} " +
			$"ang={Quaternion.Angle(m_CapturedBlendFromRot, hrRot):F1}°",
			this);
	}

	private void LogHighReadyPreAimApply(
		WeaponStance _stance,
		ItemDefinition _def,
		bool _inVehicle,
		Vector3 _toPos,
		Quaternion _toRot)
	{
		if (!ShouldLogHighReadyToPreAim)
			return;

		Debug.Log(
			$"[HR→PreAim #{m_HighReadyPreAimLogId}] APPLY t={m_PoseBlend01:F3} elapsed={m_PoseBlendElapsed:F3}/{m_PoseBlendDuration:F3} " +
			$"path={m_LastPoseApplyPath} current={m_CurrentPose} target={m_TargetPose} " +
			$"fireBlend={FireCapableBlend01:F3} raisedBlend={ReadyPoseBlend01:F3} ikSide={GetEffectivePoseForIk()}\n" +
			$"  from {FmtPose(m_CapturedBlendFromPos, m_CapturedBlendFromRot)}\n" +
			$"  to   {FmtPose(_toPos, _toRot)}\n" +
			$"  now  {FmtPose(m_CurrentBaseWeaponLocalPosition, m_CurrentBaseWeaponLocalRotation)}",
			this);
	}

	private void LogHighReadyPreAimCommit(Transform _weaponRoot)
	{
		Transform barrel = null;
		if (m_UnitEquipment != null && m_UnitEquipment.EquippedWeapon != null)
			barrel = m_UnitEquipment.EquippedWeapon.BarrelTransform;
		float barrelPitch = BarrelWorldPitch(barrel != null ? barrel : _weaponRoot);
		float weaponPitch = BarrelWorldPitch(_weaponRoot);
		bool weaponReady = m_Animator != null && m_Animator.GetBool(UnitAnimatorWeaponMode.ParamWeaponReady);
		int standIdle = m_Animator != null ? m_Animator.GetInteger(UnitAnimatorWeaponMode.ParamWeaponStandIdle) : -1;

		Debug.Log(
			$"[HR→PreAim #{m_HighReadyPreAimLogId}] COMMIT writtenLocal {FmtPose(_weaponRoot.localPosition, _weaponRoot.localRotation)} " +
			$"composedAim={m_HasComposedAimRotation} composedRecoil={m_HasComposedRecoilPosition} " +
			$"weaponFwdPitch={weaponPitch:F1}° barrelFwdPitch={barrelPitch:F1}° " +
			$"WeaponReady={weaponReady} StandIdle={standIdle} parent={_weaponRoot.parent?.name}",
			this);
	}

	private void LogHighReadyPreAim(string _label)
	{
		if (!ShouldLogHighReadyToPreAim)
			return;
		Debug.Log(
			$"[HR→PreAim #{m_HighReadyPreAimLogId}] {_label} t={m_PoseBlend01:F3} " +
			$"{m_CurrentPose}→{m_TargetPose} fireBlend={FireCapableBlend01:F3} ikSide={GetEffectivePoseForIk()}",
			this);
	}

	private static string FmtPose(Vector3 _pos, Quaternion _rot)
	{
		Vector3 e = _rot.eulerAngles;
		return $"pos=({_pos.x:F3},{_pos.y:F3},{_pos.z:F3}) euler=({e.x:F1},{e.y:F1},{e.z:F1})";
	}

	private static float BarrelWorldPitch(Transform _t)
	{
		if (_t == null)
			return 0f;
		Vector3 f = _t.forward;
		float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
		return Mathf.Atan2(f.y, horiz) * Mathf.Rad2Deg;
	}

	private LocomotionStance GetCurrentStance()
	{
		if (m_Stance != null)
			return m_Stance.CurrentStance;
		if (m_Animator != null)
		{
			int stance = m_Animator.GetInteger(Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance));
			if (stance == (int)LocomotionStance.Crouch)
				return LocomotionStance.Crouch;
			if (stance == (int)LocomotionStance.Prone)
				return LocomotionStance.Prone;
		}

		return LocomotionStance.Standing;
	}

	private bool IsVehiclePassengerFireCapable()
	{
		if (m_UnitEquipment != null && m_UnitEquipment.IsOperatingVehicleTurret)
			return false;
		EnsureVehiclePassengerState();
		return m_VehiclePassengerState != null && m_VehiclePassengerState.IsFireCapable;
	}

	private VehiclePassengerState EnsureVehiclePassengerState()
	{
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponent<VehiclePassengerState>();
		if (m_VehiclePassengerState == null)
			m_VehiclePassengerState = GetComponentInParent<VehiclePassengerState>();
		return m_VehiclePassengerState;
	}
	#endregion
}





