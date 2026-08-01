using UnityEngine;

/// <summary>
/// Плавный переход локальной позы экипированного оружия между relaxed (low ready) и ready (high ready)
/// по <see cref="UnitWeaponReadyHandsLayer"/>. Единственная точка установки localPosition/localRotation на <see cref="UnitEquipment.MainWeaponRoot"/>.
/// Координаты берутся из ItemDefinition. Для ручной настройки в Play Mode см. <see cref="UnitEquippedWeaponPoseRuntimeTuner"/>
/// (Polygone → Weapons → Add Weapon Pose Runtime Tuner To Unit).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45)]
public sealed class UnitEquippedWeaponPose : MonoBehaviour
{
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
	[Tooltip("Состояние пассажира в машине. На fire-capable месте — Vehicle поля ItemDefinition; Ready blend от WantsReadyPose.")]
	[SerializeField] private VehiclePassengerState m_VehiclePassengerState;

	[Header("Переход Ready / Relaxed")]
	[SerializeField, Min(0f)] private float m_ReadyPoseBlendDuration = 0.28f;
	[Tooltip("Кривая веса ready-позы. Пустая — SmoothStep.")]
	[SerializeField] private AnimationCurve m_ReadyPoseBlendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
	#endregion

	#region Private Fields
	private float m_ReadyBlend01;
	private float m_BlendStartReady01;
	private float m_TargetReadyBlend01;
	private bool m_IsReadyBlendAnimating;
	private float m_ReadyBlendElapsed;
	private int m_LastReadyBlendAdvanceFrame = -1;

	private Vector3 m_CurrentBaseWeaponLocalPosition;
	private Quaternion m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
	private VehiclePassengerState m_SubscribedVehiclePassengerState;
	#endregion

	#region Public Properties
	/// <summary>Текущий вес ready-позы (0 = relaxed, 1 = ready).</summary>
	public float ReadyPoseBlend01 => m_ReadyBlend01;

	/// <summary>Локальная позиция оружия после бленда relaxed/ready (без aim-correction).</summary>
	public Vector3 CurrentBaseWeaponLocalPosition => m_CurrentBaseWeaponLocalPosition;

	/// <summary>Локальный поворот оружия после бленда relaxed/ready (без aim-correction).</summary>
	public Quaternion CurrentBaseWeaponLocalRotation => m_CurrentBaseWeaponLocalRotation;

	/// <summary>Базовая локальная позиция оружия (relaxed↔ready blend), без визуальной отдачи. Источник истины для IK и VisualRecoil.</summary>
	public Vector3 BaseWeaponLocalPosition => m_CurrentBaseWeaponLocalPosition;

	/// <summary>Базовый локальный поворот оружия (relaxed↔ready blend), без визуальной отдачи. Источник истины для IK и VisualRecoil.</summary>
	public Quaternion BaseWeaponLocalRotation => m_CurrentBaseWeaponLocalRotation;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		ResolveReferences();
	}

	private void OnEnable()
	{
		SubscribeEquipmentEvents();
		SubscribeVehiclePassengerEvents();
		SyncReadyTargetImmediate();
		m_ReadyBlend01 = m_TargetReadyBlend01;
		m_IsReadyBlendAnimating = false;
		ApplyWeaponLocalPose();
	}

	private void OnDisable()
	{
		UnsubscribeEquipmentEvents();
		UnsubscribeVehiclePassengerEvents();
		StopReadyBlend();
	}

	private void Update()
	{
		if (IsBlockedByRagdoll())
			return;

		// VehiclePassengerState is often added at mount time — keep subscription + target in sync.
		EnsureVehiclePassengerSubscription();
		float desiredTarget = ComputeReadyTarget01();
		if (!Mathf.Approximately(desiredTarget, m_TargetReadyBlend01))
		{
			m_TargetReadyBlend01 = desiredTarget;
			BeginReadyBlendTowardTarget();
		}

		AdvanceReadyBlend();
		ApplyWeaponLocalPose();
	}
	#endregion

	#region Public Methods
	/// <summary>Вызывать при смене WeaponReady (E / ИИ / vehicle passenger ready).</summary>
	public void OnWeaponReadyStateChanged()
	{
		EnsureVehiclePassengerSubscription();
		SyncReadyTargetImmediate();
		BeginReadyBlendTowardTarget();
		ApplyWeaponLocalPose();
	}

	/// <summary>Мгновенно выставить позу по текущему ready-состоянию (например после экипировки).</summary>
	public void ApplyImmediateFromEquipment()
	{
		SyncReadyTargetImmediate();
		m_ReadyBlend01 = m_TargetReadyBlend01;
		StopReadyBlend();
		ApplyWeaponLocalPose();
	}
	#endregion

	#region Private Methods
	private void ResolveReferences()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponentInParent<UnitEquipment>();

		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponentInParent<UnitWeaponReadyHandsLayer>();

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
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponentInParent<UnitRocketLauncherOrderController>();

		if (m_Stance == null)
			m_Stance = GetComponent<UnitAnimatorStance>();
		if (m_Stance == null)
			m_Stance = GetComponentInParent<UnitAnimatorStance>();

		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();

		EnsureVehiclePassengerState();
	}

	private bool IsBlockedByRagdoll()
	{
		return m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts;
	}

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

	private void SubscribeVehiclePassengerEvents()
	{
		EnsureVehiclePassengerSubscription();
	}

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

	private void HandleEquipmentChanged()
	{
		ApplyImmediateFromEquipment();
	}

	private void HandleVehicleReadyIntentChanged()
	{
		OnWeaponReadyStateChanged();
	}

	private void SyncReadyTargetImmediate()
	{
		m_TargetReadyBlend01 = ComputeReadyTarget01();
	}

	private float ComputeReadyTarget01()
	{
		// In a fire-capable vehicle seat, ready blend is driven by VehiclePassengerState —
		// foot high-ready keyboard input is disabled while mounted.
		if (IsVehiclePassengerFireCapable())
			return m_VehiclePassengerState.WantsReadyPose ? 1f : 0f;

		return m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady() ? 1f : 0f;
	}

	private void BeginReadyBlendTowardTarget()
	{
		m_BlendStartReady01 = m_ReadyBlend01;

		if (m_ReadyPoseBlendDuration <= 0f)
		{
			m_ReadyBlend01 = m_TargetReadyBlend01;
			StopReadyBlend();
			return;
		}

		m_IsReadyBlendAnimating = true;
		m_ReadyBlendElapsed = 0f;
		m_LastReadyBlendAdvanceFrame = -1;
	}

	private void StopReadyBlend()
	{
		m_IsReadyBlendAnimating = false;
		m_ReadyBlendElapsed = 0f;
		m_LastReadyBlendAdvanceFrame = -1;
	}

	private void AdvanceReadyBlend()
	{
		if (!m_IsReadyBlendAnimating)
			return;

		if (m_LastReadyBlendAdvanceFrame != Time.frameCount)
		{
			m_LastReadyBlendAdvanceFrame = Time.frameCount;
			m_ReadyBlendElapsed += Time.deltaTime;
		}

		float duration = Mathf.Max(0.0001f, m_ReadyPoseBlendDuration);
		float normalizedTime = Mathf.Clamp01(m_ReadyBlendElapsed / duration);
		float curveT = m_ReadyPoseBlendCurve != null && m_ReadyPoseBlendCurve.length > 0
			? m_ReadyPoseBlendCurve.Evaluate(normalizedTime)
			: Mathf.SmoothStep(0f, 1f, normalizedTime);

		m_ReadyBlend01 = Mathf.Lerp(m_BlendStartReady01, m_TargetReadyBlend01, curveT);

		if (normalizedTime >= 1f)
		{
			m_ReadyBlend01 = m_TargetReadyBlend01;
			StopReadyBlend();
		}
	}

	private void ApplyWeaponLocalPose()
	{
		bool useRocketLauncher = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldDriveWeaponPose;
		if (m_UnitEquipment == null && !useRocketLauncher)
			return;

		Transform weaponRoot = useRocketLauncher
			? m_RocketLauncherOrder.HandLauncherRoot
			: m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = useRocketLauncher
			? m_RocketLauncherOrder.ActiveLauncherDefinition
			: m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
		{
			m_CurrentBaseWeaponLocalPosition = Vector3.zero;
			m_CurrentBaseWeaponLocalRotation = Quaternion.identity;
			return;
		}

		bool inVehicle = IsVehiclePassengerFireCapable();
		Vector3 relaxedPosition;
		Quaternion relaxedRotation;
		Vector3 readyPosition;
		Quaternion readyRotation;

		if (inVehicle)
		{
			relaxedPosition = def.ResolveVehicleRightHandLocalPosition();
			relaxedRotation = def.ResolveVehicleRightHandLocalRotation();
			readyPosition = def.ResolveVehicleRightHandReadyLocalPosition();
			readyRotation = def.ResolveVehicleRightHandReadyLocalRotation();
		}
		else
		{
			relaxedPosition = def.ResolveRightHandLocalPosition(GetCurrentStance());
			relaxedRotation = def.ResolveRightHandLocalRotation(GetCurrentStance());
			readyPosition = def.ResolveRightHandReadyLocalPosition(GetCurrentStance());
			readyRotation = def.ResolveRightHandReadyLocalRotation(GetCurrentStance());
		}

		float blend01 = m_ReadyBlend01;

		if (m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive)
		{
			m_RuntimeTuner.GetOverridePoses(
				out relaxedPosition,
				out relaxedRotation,
				out readyPosition,
				out readyRotation,
				out blend01);
		}
		else if (!inVehicle && ShouldInheritReadyPoseFromNotReady(def, GetCurrentStance()))
		{
			readyPosition = relaxedPosition;
			readyRotation = relaxedRotation;
		}

		m_CurrentBaseWeaponLocalPosition = Vector3.Lerp(relaxedPosition, readyPosition, blend01);
		m_CurrentBaseWeaponLocalRotation = Quaternion.Slerp(relaxedRotation, readyRotation, blend01);

		// While runtime tuning: leave MainWeaponRoot free so user can move it in Hierarchy/Scene.
		if (IsRuntimeTuningSkipWrite())
			return;

		// Болтовое передёргивание: временный якорь держит оружие, не перезаписывать local правой позой.
		if (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return;

		weaponRoot.localPosition = m_CurrentBaseWeaponLocalPosition;
		weaponRoot.localRotation = m_CurrentBaseWeaponLocalRotation;
	}

	private bool IsRuntimeTuningSkipWrite()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.ShouldSkipWeaponPoseWrite;
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

	private static bool ShouldInheritReadyPoseFromNotReady(ItemDefinition _def, LocomotionStance _stance)
	{
		if (_def == null || ItemDefinition.UsesCrouchHandPose(_stance))
			return false;

		Vector3 notReadyPosition = _def.ResolveRightHandLocalPosition(_stance);
		Vector3 notReadyEuler = _def.RightHandLocalEulerAngles;
		Vector3 readyPosition = _def.ResolveRightHandReadyLocalPosition(_stance);
		Vector3 readyEuler = _def.RightHandReadyLocalEulerAngles;

		if (readyPosition == Vector3.zero && readyEuler == Vector3.zero)
			return true;

		const float positionTolerance = 0.02f;
		const float angleTolerance = 0.75f;
		Vector3 templatePosition = new Vector3(0.05f, 0.02f, 0.08f);
		Vector3 templateEuler = new Vector3(-10f, 90f, 90f);

		bool readyStillTemplate = Approximately(readyPosition, templatePosition, positionTolerance)
		                          && ApproximatelyEuler(readyEuler, templateEuler, angleTolerance);
		if (!readyStillTemplate)
			return false;

		return !Approximately(notReadyPosition, templatePosition, positionTolerance)
		       || !ApproximatelyEuler(notReadyEuler, templateEuler, angleTolerance);
	}

	private static bool Approximately(Vector3 _a, Vector3 _b, float _tolerance)
	{
		return (_a - _b).sqrMagnitude <= _tolerance * _tolerance;
	}

	private static bool ApproximatelyEuler(Vector3 _a, Vector3 _b, float _tolerance)
	{
		return Mathf.Abs(Mathf.DeltaAngle(_a.x, _b.x)) <= _tolerance
		       && Mathf.Abs(Mathf.DeltaAngle(_a.y, _b.y)) <= _tolerance
		       && Mathf.Abs(Mathf.DeltaAngle(_a.z, _b.z)) <= _tolerance;
	}
	#endregion
}
