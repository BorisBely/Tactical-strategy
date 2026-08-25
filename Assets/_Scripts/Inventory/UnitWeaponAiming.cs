using UnityEngine;

/// <summary>
/// Aim solver: desired aim direction, animator <c>AimPitch</c> / layer <c>Aim_Point_U90-D90</c>, residual and AimQuality.
/// Horizontal facing is root + <see cref="UnitSpineHorizontalAim"/> (±35°, recenter at the limit).
/// Does not write equipped weapon local TRS in gameplay — BASE is <see cref="UnitEquippedWeaponPose"/>.
/// Direct <c>weaponRoot.localRotation</c> only if the pose component is missing.
/// AimQuality is visual alignment, not <c>AimProgress</c> (fire readiness).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(65)]
public sealed class UnitWeaponAiming : MonoBehaviour
{
	public enum AimSaturation
	{
		None = 0,
		ArmYaw = 1,
		ArmPitch = 2,
		Spine = 3,
		BodyRecenterRequired = 4,
	}
	#region Constants
	private const string c_ParamAimPitch = "AimPitch";
	private const string c_AimLayerName = "Aim_Point_U90-D90";
	private const string c_ObsoleteAimCrouchLayerName = "Crouch_Aim_Point_U90-D90";
	private const float c_PitchDegreesMax = 90f;
	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);
	/// <summary>Порог движения аниматора (idle &lt; 0.05, шаг &gt; 0.055) — как в <see cref="UnitAnimatorWeaponMode"/>.</summary>
	private const float c_MoveNavSpeedAnimatorThreshold = 0.055f;
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private TargetSelector m_TargetSelector;
	[Tooltip("Forward — направление юнита (корень, бёдра).")]
	[SerializeField] private Transform m_UnitForwardSource;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponFireController m_FireController;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitGrenadeThrowController m_GrenadeThrowController;
	[SerializeField] private UnitRocketLauncherOrderController m_RocketLauncherOrder;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[SerializeField] private UnitClickToMove m_ClickToMove;
	[SerializeField] private UnitNavLocomotionDriver m_LocomotionDriver;
	[SerializeField] private RtsUnitMember m_RtsMember;
	[SerializeField] private UnitSpineLean m_SpineLean;
	[SerializeField] private UnitSpineHorizontalAim m_SpineHorizontalAim;
	[SerializeField] private UnitAnimatorWeaponMode m_AnimatorWeaponMode;
	[Tooltip("Не выравнивать ствол на корпус, пока активен spine lean (иначе lean съедается).")]
	[SerializeField, Range(0.01f, 0.5f)] private float m_SkipBodyAlignWhenLeanAbove = 0.05f;

	[Header("Условия прицела")]
	[Tooltip("Only in high ready with a visible target; otherwise AimPitch and the layer go to zero.")]
	[SerializeField] private bool m_RequireReadyAndTarget = true;
	[Tooltip("Учитывать выбранную цель из TargetSelector для боевого прицела.")]
	[SerializeField] private bool m_AimAtVisibleTarget = true;

	[Header("Вертикаль (Animator)")]
	[SerializeField, Min(0f)] private float m_PitchSmoothTime = 0.08f;
	[Tooltip("При активной команде огня увеличить сглаживание AimPitch (меньше дёрганья от коллизий/анимации цели).")]
	[SerializeField] private bool m_SofterAimPitchWhileFiring = true;
	[SerializeField, Min(0f)] private float m_PitchSmoothTimeWhileFiring = 0.2f;
	[Tooltip("PointAim/Aiming walk: add to DesiredAimPitch before AimPitch = x/90. Negative lowers Aim_Point to cancel walk-clip barrel lift. Zero on idle, pose blend, reload, run/sprint, HipFire.")]
	[SerializeField, Range(-20f, 20f)] private float m_WalkPitchCompensationStandDegrees = -4.5f;
	[SerializeField, Range(-20f, 20f)] private float m_WalkPitchCompensationCrouchDegrees = -8.5f;
	[SerializeField, Min(0f)] private float m_LayerWeightSmoothSeconds = 0.08f;

	[Tooltip("Не наводить по вертикали во время смены стойки (UnitBusyState + StanceTransition).")]
	[SerializeField] private bool m_BlockAimDuringStanceTransition = true;
	[Tooltip("Не вести AimPitch на цель во время перезарядки и передёргивания затвора. Вес слоя Aim_Point_U90-D90 при этом не обнуляется — нужно для клипов перезарядки/затвора на этом слое.")]
	[SerializeField] private bool m_BlockCombatAimDuringReload = true;
	[Tooltip("После конца перезарядки/затвора не включать полноценный aim solver, пока Aim_Point кроссфейдится в pitch-blend.")]
	[SerializeField, Min(0.05f)] private float m_ReloadExitAimSettleSeconds = 0.22f;

	[Header("Aim solver")]
	[Tooltip("Сглаживание desired aim yaw (сек). Те же цифры, что у старой model-correction.")]
	[SerializeField, Min(0f)] private float m_AimYawSmoothTime = 0.04f;
	[Tooltip("Ориентир бюджета остаточного yaw рук. Residual выше — AimQuality < 1, не weapon local.")]
	[SerializeField, Min(0.5f)] private float m_MaxArmAimYawDegrees = 5f;
	[Tooltip("Ориентир бюджета остаточного pitch рук. Residual выше — AimQuality < 1, не weapon local.")]
	[SerializeField, Min(0.5f)] private float m_MaxArmAimPitchDegrees = 5f;

	[Header("Коррекция модели оружия (legacy, не применяется)")]
	[Tooltip("Устарело: PointAim больше не крутит weapon local. Флаг оставлен, чтобы не ломать префаб.")]
	[SerializeField] private bool m_EnableWeaponModelAimCorrection = true;
	[Tooltip("Максимальный локальный дововорот модели оружия по горизонту (yaw), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelYawLimitDegrees = 5f;
	[Tooltip("Максимальный локальный подъём модели оружия вверх (pitch up), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelPitchUpLimitDegrees = 18f;
	[Tooltip("Максимальный локальный увод модели оружия вниз (pitch down), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelPitchDownLimitDegrees = 10f;
	[Tooltip("Сглаживание локальной коррекции модели оружия. Больше — мягче, меньше — точнее и быстрее.")]
	[SerializeField, Min(0f)] private float m_WeaponModelCorrectionSmoothTime = 0.04f;
	[Tooltip("При стрельбе — не ниже этого smooth time для коррекции модели (гасит мелкие колебания).")]
	[SerializeField] private bool m_SofterWeaponModelCorrectionWhileFiring = true;
	[SerializeField, Min(0f)] private float m_WeaponModelCorrectionSmoothTimeWhileFiring = 0.12f;
	[Tooltip("После spine lean: макс. FromTo ствол→цель (градусы).")]
	[SerializeField, Min(1f)] private float m_LeanAimYawLimitDegrees = 36f;
	[Tooltip("Устарело: без цели больше не доворачиваем ствол к корпусу — это ломало E-cycle (Aiming/HipFire vs PointAim). Оружие = слот позы, как в тюнере.")]
	[SerializeField] private bool m_AlignBarrelToBodyWhenReadyNoTarget = true;
	[Tooltip("Отдельный yaw-лимит для body-align (ready-поза может давать ~90° ошибку; боевой лимит 5° для этого мал).")]
	[SerializeField, Range(1f, 120f)] private float m_BodyAlignYawLimitDegrees = 90f;
	[Tooltip("Сглаживание world-yaw коррекции body-align. Меньше — быстрее подтягивает ствол при стрейфе.")]
	[SerializeField, Min(0f)] private float m_BodyAlignYawSmoothTime = 0.02f;
	[Tooltip("После HighReady→PreAim и после остановки шага коррекция ствола включается не сразу, а за это время.")]
	[SerializeField, Min(0.05f)] private float m_FireRaiseAimCorrectionEaseSeconds = 0.22f;
	[Tooltip("Логировать в Console состояние body-align (сила бленда, ошибка yaw до/после).")]
	[SerializeField] private bool m_LogReadyBodyAlign;
	[SerializeField, Min(0.05f)] private float m_LogReadyBodyAlignIntervalSeconds = 0.25f;
	[Tooltip("Консоль: START/BLEND/END перехода позы и режим доворота ствола. Выкл. — facing смотри [Facing].")]
	[SerializeField] private bool m_LogPoseAimTransitions;
	[SerializeField, Min(0.05f)] private float m_LogPoseAimTransitionIntervalSeconds = 0.2f;
	[Tooltip("Консоль [ReloadAim]: перезарядка любой позы и несколько секунд после.")]
	[SerializeField] private bool m_LogReloadAimMix;
	[SerializeField, Min(0.05f)] private float m_LogReloadAimIntervalSeconds = 0.12f;
	[SerializeField, Min(0.2f)] private float m_LogReloadAimAfterSeconds = 2.5f;
	[Tooltip("Консоль [HipFireAim]: переход в/из HipFire и перезарядка от бедра. Фильтр: HipFireAim.")]
	[SerializeField] private bool m_LogHipFireAimMix;
	[SerializeField, Min(0.05f)] private float m_LogHipFireAimIntervalSeconds = 0.12f;
	[SerializeField, Min(0.2f)] private float m_LogHipFireAimAfterSeconds = 2.5f;
	[Tooltip("Консоль [WeaponSpin]: desired/residual/aimQuality/saturation. owner=base в PointAim+fire. SPIN-COMPOSE в settled PointAim/Aiming/HipFire — ошибка ownership. Фильтр: WeaponSpin. Отключено: нужен ещё глобальный master-выключатель (см. свойство LogWeaponSpinMasterEnabled).")]
	[SerializeField] private bool m_LogWeaponSpin = false;
	[SerializeField, Min(0.05f)] private float m_LogWeaponSpinIntervalSeconds = 0.1f;
	[SerializeField, Min(0.2f)] private float m_LogWeaponSpinAfterSeconds = 3f;
	[Tooltip("SPIN, если локальный поворот оружия за кадр больше этого, а кисть повернулась заметно меньше.")]
	[SerializeField, Min(1f)] private float m_WeaponSpinLocalJumpDegrees = 4f;
	[Tooltip("SPIN, если мировой ствол за кадр повернулся больше этого без такого же поворота кисти.")]
	[SerializeField, Min(1f)] private float m_WeaponSpinBarrelJumpDegrees = 10f;
	[Tooltip("TURN, если корень за кадр повернулся больше этого.")]
	[SerializeField, Min(0.5f)] private float m_WeaponSpinRootTurnDegrees = 2.5f;

	[Header("Инспектор (только отображение)")]
	[Tooltip("Сейчас реально активен боевой vertical aim: есть оружие, включён ready, есть видимая цель и стойка не заблокирована переходом.")]
	[SerializeField] private bool m_DebugCombatAimActive;
	[Tooltip("Текущая стойка на Animator: 0 = Standing, 1 = Crouch, 2 = Prone.")]
	[SerializeField] private int m_DebugCurrentStance;
	[Tooltip("Мировая точка, в которую сейчас целится vertical aim.")]
	[SerializeField] private Vector3 m_DebugAimPointWorld;
	[Tooltip("Сырые градусы pitch до сглаживания Animator.")]
	[SerializeField] private float m_DebugRawPitchDegrees;
	[Tooltip("Сырая горизонтальная ошибка (yaw) между Barrel.forward и направлением на цель.")]
	[SerializeField] private float m_DebugWeaponYawErrorDegrees;
	[Tooltip("Сырая вертикальная ошибка (pitch) между Barrel.forward и направлением на цель.")]
	[SerializeField] private float m_DebugWeaponPitchErrorDegrees;
	[Tooltip("Сколько градусов yaw-коррекции сейчас реально приложено к модели оружия.")]
	[SerializeField] private float m_DebugWeaponYawAppliedDegrees;
	[Tooltip("Сколько градусов pitch-коррекции сейчас реально приложено к модели оружия.")]
	[SerializeField] private float m_DebugWeaponPitchAppliedDegrees;
	[Tooltip("Сглаженный desired aim yaw (мир).")]
	[SerializeField] private float m_DebugDesiredAimYawDegrees;
	[Tooltip("Сглаженный desired aim pitch (горизонт/корень юнита, не грудь).")]
	[SerializeField] private float m_DebugDesiredAimPitchDegrees;
	[Tooltip("Остаточный yaw ствол↔цель после root/spine/AimPitch.")]
	[SerializeField] private float m_DebugResidualYawDegrees;
	[Tooltip("Остаточный pitch ствол↔цель после AimPitch.")]
	[SerializeField] private float m_DebugResidualPitchDegrees;
	[SerializeField, Range(0f, 1f)] private float m_DebugAimQuality01 = 1f;
	[SerializeField] private AimSaturation m_DebugAimSaturation;
	[Tooltip("Итоговое сглаженное значение AimPitch, которое уходит в Animator.")]
	[SerializeField] private float m_DebugSmoothedPitch01;
	[Tooltip("WalkPitchCompensation, добавленный к DesiredAimPitch в этом кадре (градусы).")]
	[SerializeField] private float m_DebugWalkPitchCompensationDegrees;
	[Tooltip("Текущий вес активного слоя прицела (стоя или присед).")]
	[SerializeField, Range(0f, 1f)] private float m_DebugAimLayerWeight;

	[Header("Отладка лучей")]
	[Tooltip("Scene Gizmos + Game view: куда смотрит ствол оружия (Barrel.forward).")]
	[SerializeField] private bool m_DrawBarrelForwardRay;
	[SerializeField, Min(0.1f)] private float m_BarrelForwardRayLength = 4f;
	[SerializeField] private Color m_BarrelForwardRayColor = new Color(1f, 0.85f, 0f, 0.95f);

	#endregion

	#region Private Fields
	private static readonly int s_AimPitch = Animator.StringToHash(c_ParamAimPitch);

	private ItemDefinition m_LastEquippedDefinition;
	private Quaternion m_BaseWeaponLocalRotation = Quaternion.identity;
	private Transform m_BarrelTransform;

	private int m_AimLayerIndex = -1;
	private int m_ObsoleteAimCrouchLayerIndex = -1;
	private float m_SmoothedPitch01;
	private float m_SmoothedLayerWeight;
	private float m_DesiredAimYawDegrees;
	private float m_DesiredAimPitchDegrees;
	private bool m_HasDesiredAim;
	private float m_ResidualYawDegrees;
	private float m_ResidualPitchDegrees;
	private float m_AimQuality01 = 1f;
	private AimSaturation m_AimSaturation = AimSaturation.None;
	private float m_BodyYawErrorDegrees;
	private float m_SmoothedWeaponYawDegrees;
	private float m_SmoothedWeaponPitchDegrees;
	private float m_WeaponYawVelocity;
	private float m_SmoothedPointAimDegrees;
	private Vector3 m_PointAimAxisWorld = Vector3.up;
	private float m_NextReadyBodyAlignLogTime;
	private float m_ModelAimGate01 = 1f;
	private bool m_WasLocomotionMovingForAim;
	private bool m_HasPoseAimLogBaseline;
	private WeaponPoseState m_LoggedPoseFrom;
	private WeaponPoseState m_LoggedPoseTo;
	private bool m_LoggedPoseBlending;
	private float m_NextPoseAimTransitionLogTime;
	private int m_PoseAimTransitionLogId;
	private bool m_LoggedReloadBusy;
	private float m_ReloadAimLogUntilTime = -1f;
	private float m_NextReloadAimLogTime;
	private int m_ReloadAimLogId;
	private bool m_WasReloadPresentationBusy;
	private float m_HoldWeaponModelAimUntil = -1f;
	private float m_HoldModelAimAfterFireUntil = -1f;
	private Quaternion m_LastAimHandWorld = Quaternion.identity;
	private bool m_HasLastAimHandWorld;
	private int m_LastLeanAimSign;
	private Quaternion m_LastLeanAimLocal = Quaternion.identity;
	private bool m_HasLastLeanAimLocal;
	private bool m_HasHipFirePoseLogBaseline;
	private WeaponPoseState m_HipFireLoggedFrom;
	private WeaponPoseState m_HipFireLoggedTo;
	private bool m_HipFireLoggedBlending;
	private bool m_LoggedHipFireReloadBusy;
	private bool m_HipFireReloadSession;
	private float m_HipFireAimLogUntilTime = -1f;
	private float m_NextHipFireAimLogTime;
	private int m_HipFireAimLogId;
	private bool m_HasWeaponSpinBaseline;
	private WeaponPoseState m_SpinLoggedFrom;
	private WeaponPoseState m_SpinLoggedTo;
	private bool m_SpinLoggedBlending;
	private bool m_SpinLoggedReloadBusy;
	private bool m_SpinLoggedMoving;
	private bool m_SpinLoggedFiring;
	private float m_SpinLogUntilTime = -1f;
	private float m_NextWeaponSpinLogTime;
	private int m_WeaponSpinLogId;
	private float m_SpinLastRootYaw;
	private Quaternion m_SpinLastWeaponLocal = Quaternion.identity;
	private Quaternion m_SpinLastHandWorld = Quaternion.identity;
	private Vector3 m_SpinLastBarrelFwd = Vector3.forward;
	private Vector3 m_SpinLastComposeEuler;
	private float m_SpinLastPitchErr;
	private float m_SpinLastYawErr;
	private bool m_HasSpinLastWeaponLocal;
	private bool m_HasSpinLastHandWorld;
	private bool m_HasSpinLastBarrelFwd;
	private bool m_PendingWeaponSpinLog;
	private string m_PendingWeaponSpinLine;
	private UnitWeaponRecoil m_WeaponRecoil;
	private WeaponVisualRecoilApplicator m_VisualRecoilApplicator;
	private AnimatorHandIk m_HandIk;
	private WeaponGripResolver m_GripResolver;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_TargetSelector == null)
			m_TargetSelector = GetComponent<TargetSelector>();
		if (m_UnitForwardSource == null)
			m_UnitForwardSource = transform;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();
		if (m_BusyState == null)
			m_BusyState = GetComponent<UnitBusyState>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponentInParent<UnitWeaponReloadController>();
		if (m_MagazineLoadingController == null)
			m_MagazineLoadingController = GetComponent<UnitMagazineLoadingController>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_GrenadeThrowController == null)
			m_GrenadeThrowController = GetComponent<UnitGrenadeThrowController>();
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_SpineLean == null)
			m_SpineLean = GetComponent<UnitSpineLean>();
		if (m_SpineHorizontalAim == null)
			m_SpineHorizontalAim = GetComponent<UnitSpineHorizontalAim>();
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();

		if (GetComponent<WeaponAimVisualBarrelSpinFlush>() == null)
			gameObject.AddComponent<WeaponAimVisualBarrelSpinFlush>();

		ResolveAimLayerIndices();
	}

	private void OnEnable()
	{
		if (m_ReloadController == null)
			m_ReloadController = GetComponent<UnitWeaponReloadController>();
		if (m_ReloadController == null)
			m_ReloadController = GetComponentInParent<UnitWeaponReloadController>();

		ResolveAimLayerIndices();
		m_SmoothedPitch01 = 0f;
		m_SmoothedLayerWeight = 0f;
		m_SmoothedWeaponYawDegrees = 0f;
		m_SmoothedWeaponPitchDegrees = 0f;
		m_WeaponYawVelocity = 0f;
		m_SmoothedPointAimDegrees = 0f;
		m_PointAimAxisWorld = Vector3.up;
		m_DesiredAimYawDegrees = 0f;
		m_DesiredAimPitchDegrees = 0f;
		m_HasDesiredAim = false;
		m_ResidualYawDegrees = 0f;
		m_ResidualPitchDegrees = 0f;
		m_AimQuality01 = 1f;
		m_AimSaturation = AimSaturation.None;
		m_BodyYawErrorDegrees = 0f;
		m_WasReloadPresentationBusy = false;
		m_HoldWeaponModelAimUntil = -1f;
		m_HoldModelAimAfterFireUntil = -1f;
		m_HasLastAimHandWorld = false;
		m_WasLocomotionMovingForAim = false;
		m_ModelAimGate01 = 1f;
		m_LastLeanAimSign = 0;
		m_HasLastLeanAimLocal = false;
		m_HasPoseAimLogBaseline = false;
		m_HasHipFirePoseLogBaseline = false;
		m_LoggedHipFireReloadBusy = false;
		m_HipFireReloadSession = false;
		m_HipFireAimLogUntilTime = -1f;
		m_PendingWeaponSpinLog = false;
		m_PendingWeaponSpinLine = null;
		m_HasWeaponSpinBaseline = false;
		m_SpinLoggedReloadBusy = false;
		m_SpinLoggedMoving = false;
		m_SpinLoggedFiring = false;
		m_SpinLogUntilTime = -1f;
		m_HasSpinLastWeaponLocal = false;
		m_HasSpinLastHandWorld = false;
		m_HasSpinLastBarrelFwd = false;
		m_BarrelTransform = null;
		m_LastEquippedDefinition = null;
		if (m_Animator != null)
		{
			m_Animator.SetFloat(s_AimPitch, 0f);
			SetAimLayerWeights(0f);
		}
	}

	private void Update()
	{
		if (IsBlockedByRagdoll())
			return;

		try
		{
			TickModelAimGate();
			TickReloadExitAimSettle();

		if (m_Animator != null)
		{
			bool rocketLauncherNeedsAimLayer = ShouldHoldAimLayerForRocketLauncher();
			if (m_UnitEquipment != null || rocketLauncherNeedsAimLayer)
			{
				Transform weaponRoot = m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
				ItemDefinition def = m_UnitEquipment != null ? m_UnitEquipment.EquippedDefinition : null;
				if (rocketLauncherNeedsAimLayer || (weaponRoot != null && def != null))
				{
					if (rocketLauncherNeedsAimLayer || TrySyncWeaponDefinition(weaponRoot, def))
						ApplyAnimatorAimParameters();
				}
				else
					ResetAimAnimatorParameters();
			}
		}

		if (m_UnitEquipment == null || m_UnitForwardSource == null)
			return;

		if (IsRuntimePoseTuningActive())
			return;

		if (m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return;

		Transform aimWeaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition aimDef = m_UnitEquipment.EquippedDefinition;
		if (aimWeaponRoot == null || aimDef == null)
			return;

		if (!TrySyncWeaponDefinition(aimWeaponRoot, aimDef) || m_BarrelTransform == null)
			return;

		m_HasLastLeanAimLocal = false;
		m_HasLastAimHandWorld = false;
		ResetWeaponModelCorrectionDebug();

		if (m_DrawBarrelForwardRay)
			Debug.DrawRay(m_BarrelTransform.position, m_BarrelTransform.forward * m_BarrelForwardRayLength, m_BarrelForwardRayColor);
		}
		finally
		{
			TickPoseAimTransitionLog();
			TickReloadAimMixLog();
			TickHipFireAimMixLog();
		}
	}

	private void LateUpdate()
	{
		try
		{
			TickAimResidualAfterBody();

			if (m_SpineLean == null)
				m_SpineLean = GetComponent<UnitSpineLean>();
			m_SpineLean?.TickDiagnosticsAfterAim();

			if (m_DrawBarrelForwardRay && m_BarrelTransform != null)
				Debug.DrawRay(m_BarrelTransform.position, m_BarrelTransform.forward * m_BarrelForwardRayLength, m_BarrelForwardRayColor);
		}
		finally
		{
			TickWeaponSpinLog();
		}
	}

	public float DesiredAimYawDegrees => m_DesiredAimYawDegrees;
	public float DesiredAimPitchDegrees => m_DesiredAimPitchDegrees;
	public float ResidualYawDegrees => m_ResidualYawDegrees;
	public float ResidualPitchDegrees => m_ResidualPitchDegrees;
	public float AimQuality01 => m_AimQuality01;
	public AimSaturation CurrentAimSaturation => m_AimSaturation;
	public float WalkPitchCompensationDegrees => m_DebugWalkPitchCompensationDegrees;

	public bool LogWeaponSpin
	{
		get => m_LogWeaponSpin;
		set => m_LogWeaponSpin = value;
	}

	/// <summary>
	/// Глобальный master-выключатель консоли [WeaponSpin] для всех юнитов.
	/// По умолчанию false — логи выключены (калибровка отдачи завершена, консоль занята [HeadSweep]).
	/// Лог печатается только при (m_LogWeaponSpin &amp;&amp; LogWeaponSpinMasterEnabled).
	/// Включить для разовой диагностики: UnitWeaponAiming.LogWeaponSpinMasterEnabled = true;
	/// </summary>
	public static bool LogWeaponSpinMasterEnabled { get; set; }

	/// <summary>
	/// Bore after visual recoil overlay. Call after WaitForEndOfFrame so applicator 200 has run.
	/// </summary>
	public void MeasureVisualBarrel(
		out float barrelPitchDegrees,
		out float yawErrorDegrees,
		out float residualPitchDegrees)
	{
		MeasureBarrelPitchAndTargetError(out barrelPitchDegrees, out yawErrorDegrees, out residualPitchDegrees);
	}

	/// <summary>One-line weapon-correction snapshot for <see cref="UnitFacingDebugLog"/>.</summary>
	public string FormatFacingDebugLine()
	{
		float pointAimW = GetPointAimCorrectionWeight();
		return $"weapon corr={FormatAimCorrectionMode(pointAimW)} localAim={(ShouldApplyWeaponLocalOnlyForAim() ? 1 : 0)} " +
		       $"combatAim={(m_DebugCombatAimActive ? 1 : 0)} fireBlend={GetFireCapableAimBlend01():F2} " +
		       $"desiredYaw={m_DesiredAimYawDegrees:F1}° desiredPitch={m_DesiredAimPitchDegrees:F1}° " +
		       $"residualYaw={m_ResidualYawDegrees:F1}° residualPitch={m_ResidualPitchDegrees:F1}° " +
		       $"aimQuality={m_AimQuality01:F2} sat={m_AimSaturation} " +
		       $"yawErr={m_DebugWeaponYawErrorDegrees:F1}° appliedYaw={m_DebugWeaponYawAppliedDegrees:F1}° " +
		       $"pitchErr={m_DebugWeaponPitchErrorDegrees:F1}° appliedPitch={m_DebugWeaponPitchAppliedDegrees:F1}°";
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawBarrelForwardRay || !TryGetBarrelGizmoRay(out Vector3 origin, out Vector3 direction))
			return;

		GizmoDirectionDrawUtility.DrawArrow(origin, direction, m_BarrelForwardRayLength, m_BarrelForwardRayColor, 0.1f);
	}
	#endregion

	#region Private Methods
	private bool TryGetBarrelGizmoRay(out Vector3 _origin, out Vector3 _direction)
	{
		_origin = Vector3.zero;
		_direction = Vector3.forward;

		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();

		Transform weaponRoot = m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
		if (weaponRoot == null)
			return false;

		Transform barrel = m_BarrelTransform;
		if (barrel == null)
		{
			EquippedWeapon equippedWeapon = m_UnitEquipment.EquippedWeapon;
			barrel = equippedWeapon != null ? equippedWeapon.BarrelTransform : weaponRoot;
		}

		if (barrel == null)
			return false;

		_origin = barrel.position;
		_direction = barrel.forward;
		return _direction.sqrMagnitude > 1e-8f;
	}

	private bool IsBlockedByRagdoll()
	{
		return m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts;
	}

	private bool IsRuntimePoseTuningActive()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		// Block aim overwrite for any tuner mode (incl. NotReady/Ready), not only Hands Frozen.
		return m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
	}

	/// <summary>
	/// Гранатомёт держит клипы aim/fire/reload на Aim_Point_U90-D90 — слой не должен гаситься боевым прицелом.
	/// </summary>
	private bool ShouldHoldAimLayerForRocketLauncher()
	{
		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>();
		return m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldHoldAimLayerVisible;
	}

	private bool IsRocketLauncherIkTuningActive()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponentInParent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null &&
		       m_RuntimeTuner.IsTuningActive &&
		       m_RuntimeTuner.UsesRocketLauncherContext;
	}

	private bool TryResolveAimPitchOrigin(out Vector3 _origin)
	{
		_origin = Vector3.zero;

		if (m_RocketLauncherOrder == null)
			m_RocketLauncherOrder = GetComponent<UnitRocketLauncherOrderController>();

		if (m_RocketLauncherOrder != null &&
		    m_RocketLauncherOrder.ShouldHoldAimLayerVisible &&
		    m_RocketLauncherOrder.TryGetAimPitchOrigin(out _origin, out _))
			return true;

		if (TryGetBodyAimFrame(out _, out _origin))
			return true;

		return false;
	}

	private bool TryGetBodyAimFrame(out Transform _frame, out Vector3 _origin)
	{
		_frame = null;
		_origin = Vector3.zero;

		if (m_Animator != null)
		{
			Transform head = m_Animator.GetBoneTransform(HumanBodyBones.Head);
			Transform chest = m_Animator.GetBoneTransform(HumanBodyBones.Chest);
			if (chest == null)
				chest = m_Animator.GetBoneTransform(HumanBodyBones.Spine);

			if (head != null)
			{
				_origin = head.position;
				_frame = chest != null ? chest : head;
				return true;
			}

			if (chest != null)
			{
				_origin = chest.position;
				_frame = chest;
				return true;
			}
		}

		if (m_UnitForwardSource != null)
		{
			_frame = m_UnitForwardSource;
			_origin = m_UnitForwardSource.position + Vector3.up * 1.4f;
			return true;
		}

		_frame = transform;
		_origin = transform.position + Vector3.up * 1.4f;
		return true;
	}

	private void TickDesiredAim(Vector3 _aimPointWorld, Vector3 _origin, bool _firing)
	{
		Vector3 dir = _aimPointWorld - _origin;
		if (dir.sqrMagnitude < 1e-6f)
			return;

		// Origin = head. Pitch frame = unit root, not chest/head: Aiming/PointAim already
		// rotate the chest, so chest-local elevation double-counts Aim_Point and looks up.
		Transform pitchFrame = m_UnitForwardSource != null ? m_UnitForwardSource : transform;
		Vector3 local = pitchFrame.InverseTransformDirection(dir);
		float localHoriz = Mathf.Sqrt(local.x * local.x + local.z * local.z);
		float rawPitch = Mathf.Atan2(local.y, Mathf.Max(1e-6f, localHoriz)) * Mathf.Rad2Deg;
		bool rocketAim = m_RocketLauncherOrder != null && m_RocketLauncherOrder.ShouldHoldAimLayerVisible;
		if (rocketAim)
		{
			float worldHoriz = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
			rawPitch = Mathf.Atan2(dir.y, Mathf.Max(1e-6f, worldHoriz)) * Mathf.Rad2Deg;
		}

		rawPitch = Mathf.Clamp(rawPitch, -c_PitchDegreesMax, c_PitchDegreesMax);

		Vector3 xz = dir;
		xz.y = 0f;
		float rawYaw = xz.sqrMagnitude > 1e-6f
			? Mathf.Atan2(xz.x, xz.z) * Mathf.Rad2Deg
			: m_DesiredAimYawDegrees;

		float yawSmooth = Mathf.Max(0.0001f, m_AimYawSmoothTime);
		float pitchSmooth = Mathf.Max(0.0001f, m_PitchSmoothTime);
		if (_firing)
		{
			yawSmooth = Mathf.Max(yawSmooth, m_WeaponModelCorrectionSmoothTimeWhileFiring);
			if (m_SofterAimPitchWhileFiring)
				pitchSmooth = Mathf.Max(pitchSmooth, m_PitchSmoothTimeWhileFiring);
		}

		if (!m_HasDesiredAim)
		{
			m_DesiredAimYawDegrees = rawYaw;
			m_DesiredAimPitchDegrees = rawPitch;
			m_HasDesiredAim = true;
		}
		else
		{
			m_DesiredAimYawDegrees = SmoothExpAngle(m_DesiredAimYawDegrees, rawYaw, yawSmooth);
			m_DesiredAimPitchDegrees = SmoothExp(m_DesiredAimPitchDegrees, rawPitch, pitchSmooth);
		}

		m_DebugRawPitchDegrees = rawPitch;
		m_DebugDesiredAimYawDegrees = m_DesiredAimYawDegrees;
		m_DebugDesiredAimPitchDegrees = m_DesiredAimPitchDegrees;
	}

	private void TickDesiredAimIdle(float _pitchSmooth)
	{
		m_DesiredAimPitchDegrees = SmoothExp(m_DesiredAimPitchDegrees, 0f, Mathf.Max(0.0001f, _pitchSmooth));
		m_DebugDesiredAimPitchDegrees = m_DesiredAimPitchDegrees;
		m_DebugRawPitchDegrees = 0f;
		m_DebugAimPointWorld = Vector3.zero;
	}

	private void TickAimResidualAfterBody()
	{
		m_ResidualYawDegrees = 0f;
		m_ResidualPitchDegrees = 0f;
		m_BodyYawErrorDegrees = 0f;
		m_AimQuality01 = 1f;
		m_AimSaturation = AimSaturation.None;

		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null &&
		                 m_AimAtVisibleTarget;
		if (!hasTarget || m_BarrelTransform == null)
		{
			PublishAimSolverDebug();
			return;
		}

		Vector3 aimPoint = m_TargetSelector.GetEngageableAimPointWorld();
		Vector3 barrelFwd = m_BarrelTransform.forward;
		Vector3 toTarget = aimPoint - m_BarrelTransform.position;
		if (toTarget.sqrMagnitude < 1e-6f || barrelFwd.sqrMagnitude < 1e-6f)
		{
			PublishAimSolverDebug();
			return;
		}

		Vector3 barrelXZ = barrelFwd;
		barrelXZ.y = 0f;
		Vector3 toTargetXZ = toTarget;
		toTargetXZ.y = 0f;
		if (barrelXZ.sqrMagnitude > 1e-6f && toTargetXZ.sqrMagnitude > 1e-6f)
			m_ResidualYawDegrees = Vector3.SignedAngle(barrelXZ.normalized, toTargetXZ.normalized, Vector3.up);

		Transform body = m_UnitForwardSource != null ? m_UnitForwardSource : transform;
		Vector3 bodyXZ = body.forward;
		bodyXZ.y = 0f;
		if (bodyXZ.sqrMagnitude > 1e-6f && toTargetXZ.sqrMagnitude > 1e-6f)
			m_BodyYawErrorDegrees = Vector3.SignedAngle(bodyXZ.normalized, toTargetXZ.normalized, Vector3.up);

		float barrelHoriz = Mathf.Sqrt(barrelFwd.x * barrelFwd.x + barrelFwd.z * barrelFwd.z);
		float barrelPitch = Mathf.Atan2(barrelFwd.y, Mathf.Max(1e-6f, barrelHoriz)) * Mathf.Rad2Deg;
		float targetHoriz = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
		float targetPitch = Mathf.Atan2(toTarget.y, Mathf.Max(1e-6f, targetHoriz)) * Mathf.Rad2Deg;
		m_ResidualPitchDegrees = barrelPitch - targetPitch;

		float yawBudget = Mathf.Max(0.5f, m_MaxArmAimYawDegrees);
		float pitchBudget = Mathf.Max(0.5f, m_MaxArmAimPitchDegrees);
		float yaw01 = Mathf.Clamp01(Mathf.Abs(m_ResidualYawDegrees) / yawBudget);
		float pitch01 = Mathf.Clamp01(Mathf.Abs(m_ResidualPitchDegrees) / pitchBudget);
		m_AimQuality01 = 1f - Mathf.Max(yaw01, pitch01);

		if (m_SpineHorizontalAim == null)
			m_SpineHorizontalAim = GetComponent<UnitSpineHorizontalAim>();

		if (m_SpineHorizontalAim != null && m_SpineHorizontalAim.WantsRootRecenter)
			m_AimSaturation = AimSaturation.BodyRecenterRequired;
		else if (m_SpineHorizontalAim != null && m_SpineHorizontalAim.IsSaturated)
			m_AimSaturation = AimSaturation.Spine;
		else if (Mathf.Abs(m_ResidualYawDegrees) > yawBudget)
			m_AimSaturation = AimSaturation.ArmYaw;
		else if (Mathf.Abs(m_ResidualPitchDegrees) > pitchBudget)
			m_AimSaturation = AimSaturation.ArmPitch;
		else
			m_AimSaturation = AimSaturation.None;

		PublishAimSolverDebug();
	}

	private void PublishAimSolverDebug()
	{
		m_DebugResidualYawDegrees = m_ResidualYawDegrees;
		m_DebugResidualPitchDegrees = m_ResidualPitchDegrees;
		m_DebugAimQuality01 = m_AimQuality01;
		m_DebugAimSaturation = m_AimSaturation;
	}

	/// <summary>
	/// Infantry gameplay never writes weapon local for aim. PointAim uses root + spine + AimPitch.
	/// </summary>
	private bool AllowsWeaponLocalAimCorrection()
	{
		return false;
	}

	private void SubmitWeaponLocalAimRotation(Transform _weaponRoot, Quaternion _localRotation)
	{
		if (m_EquippedWeaponPose != null)
		{
			m_EquippedWeaponPose.ComposeAimLocalRotation(
				_localRotation,
				UnitEquippedWeaponPose.WeaponLocalComposeLayer.AimCorrection);
			return;
		}

		if (_weaponRoot != null)
			_weaponRoot.localRotation = _localRotation;
	}

	private bool ShouldApplyWeaponLocalOnlyForAim()
	{
		if (!AllowsWeaponLocalAimCorrection())
			return false;

		if (!m_RequireReadyAndTarget)
			return false;

		if (GetModelAimAlignStrength() <= 0.001f)
			return false;

		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;
		if (!hasTarget || !m_AimAtVisibleTarget)
			return false;

		return true;
	}

	/// <summary>
	/// Spine lean крутит торс в LateUpdate после обычного aim. Доворот ствола — тоже после lean,
	/// иначе roll съедает 8–13° и лимит pitch-down 10° не хватает.
	/// </summary>
	private bool ShouldApplyLeanTargetAim()
	{
		if (!AllowsWeaponLocalAimCorrection())
			return false;
		if (!m_EnableWeaponModelAimCorrection)
			return false;
		if (!IsSpineLeanActiveForBodyAlignSkip())
			return false;
		if (m_TargetSelector == null || m_TargetSelector.SelectedTarget == null || !m_AimAtVisibleTarget)
			return false;
		if (IsBlockedByRagdoll())
			return false;
		if (IsAimBlockedByStanceOrReload())
			return false;
		if (IsHoldingWeaponModelAimAfterReload())
			return false;
		if (GetHipFirePoseWeight() >= 0.999f)
			return false;
		if (GetShoulderedAimingPoseWeight() >= 0.999f)
			return false;
		if (IsLocomotionMovingNow())
			return false;
		return true;
	}

	private Quaternion ResolveAimBaseLocalRotation()
	{
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.BaseWeaponLocalRotation;
		return m_BaseWeaponLocalRotation;
	}

	/// <summary>
	/// Combat aim overlay follows FireCapableBlend01 (0 in HighReady/PreAim, 1 in HipFire/PointAim/Aiming).
	/// </summary>
	private float GetFireCapableAimBlend01()
	{
		if (m_EquippedWeaponPose != null)
			return Mathf.Clamp01(m_EquippedWeaponPose.FireCapableBlend01);

		return m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady() ? 1f : 0f;
	}

	private void TickModelAimGate()
	{
		// After stop: ease how complete aiming animation/quality may be. Does not rotate weapon local.
		if (IsLocomotionMovingNow())
		{
			if (ShouldKeepCombatWalkBodyAim())
			{
				m_ModelAimGate01 = 1f;
				return;
			}

			m_ModelAimGate01 = 0f;
			m_WasLocomotionMovingForAim = true;
			m_HasLastAimHandWorld = false;
			m_HoldModelAimAfterFireUntil = -1f;
			return;
		}

		if (!m_WasLocomotionMovingForAim)
		{
			m_ModelAimGate01 = 1f;
			return;
		}

		float ease = Mathf.Max(0.05f, m_FireRaiseAimCorrectionEaseSeconds);
		m_ModelAimGate01 = Mathf.MoveTowards(m_ModelAimGate01, 1f, Time.deltaTime / ease);
		if (m_ModelAimGate01 >= 0.999f)
		{
			m_ModelAimGate01 = 1f;
			m_WasLocomotionMovingForAim = false;
		}
	}

	/// <summary>
	/// Recoil rotates Hand_R. Recomputing PointAim FromTo in that parent space twists the gun in the fingers.
	/// Hold the last good local until the hand settles. Never across locomotion — walk changes the parent.
	/// </summary>
	private bool ShouldHoldWeaponModelAim(Transform _hand)
	{
		if (IsLocomotionMovingNow())
			return false;

		if (IsFiringForSteadyAim())
		{
			m_HoldModelAimAfterFireUntil = Time.time + 0.22f;
			return true;
		}

		if (Time.time < m_HoldModelAimAfterFireUntil)
			return true;

		if (_hand != null && m_HasLastAimHandWorld)
		{
			float handDelta = Quaternion.Angle(m_LastAimHandWorld, _hand.rotation);
			if (handDelta >= 2.5f)
				return true;
		}

		return false;
	}

	private bool IsLocomotionMovingNow()
	{
		if (m_Animator != null && m_Animator.GetFloat(s_NavSpeed) >= c_MoveNavSpeedAnimatorThreshold)
			return true;
		if (IsRunOrSprintMoveNow())
			return true;
		return false;
	}

	/// <summary>
	/// Fixed Aim_Point offset for PointAim/Aiming walk clips. Not a closed loop on residual.
	/// Pose weight lerps with overlay poses; stance offset lerps with grip StanceBlend01
	/// so crouch xfade does not snap stand −4.5 → crouch −6.5 while arms are still blending.
	/// </summary>
	private float ResolveWalkPitchCompensationDegrees()
	{
		if (IsRunOrSprintMoveNow() || !IsLocomotionMovingNow())
			return 0f;
		if (m_ReloadController != null && m_ReloadController.IsReloadBusy)
			return 0f;
		if (m_GrenadeThrowController != null && m_GrenadeThrowController.IsThrowAnimPlaying)
			return 0f;
		if (m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine)
			return 0f;
		if (m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return 0f;

		float poseWeight = ResolveWalkCompensationPoseWeight();
		if (poseWeight <= 0.001f)
			return 0f;

		return poseWeight * ResolveWalkCompensationStanceDegrees();
	}

	private float ResolveWalkCompensationPoseWeight()
	{
		if (m_EquippedWeaponPose == null)
		{
			WeaponPoseState pose = m_ReadyHands != null
				? m_ReadyHands.EffectivePoseState
				: WeaponPoseState.LowReady;
			return PoseWantsAimPointOverlay(pose) ? 1f : 0f;
		}

		float from = PoseWantsAimPointOverlay(m_EquippedWeaponPose.CurrentPose) ? 1f : 0f;
		float to = PoseWantsAimPointOverlay(m_EquippedWeaponPose.TargetPose) ? 1f : 0f;
		if (!m_EquippedWeaponPose.IsPoseBlendAnimating)
			return to;

		return Mathf.Lerp(from, to, m_EquippedWeaponPose.PoseBlend01);
	}

	private float ResolveWalkCompensationStanceDegrees()
	{
		if (m_Animator != null && m_Animator.GetInteger(s_Stance) == (int)LocomotionStance.Prone)
			return 0f;

		if (m_GripResolver == null)
			m_GripResolver = GetComponent<WeaponGripResolver>();
		if (m_GripResolver != null)
		{
			WeaponHoldContext hold = m_GripResolver.HoldContext;
			float from = CompensationForWeaponStance(hold.StanceFrom);
			float to = CompensationForWeaponStance(hold.StanceTo);
			if (!hold.IsStanceBlending)
				return to;
			return Mathf.Lerp(from, to, hold.StanceBlend01);
		}

		int stance = m_Animator != null ? m_Animator.GetInteger(s_Stance) : 0;
		if (stance == (int)LocomotionStance.Crouch)
			return m_WalkPitchCompensationCrouchDegrees;
		return m_WalkPitchCompensationStandDegrees;
	}

	private float CompensationForWeaponStance(WeaponStance _stance)
	{
		if (_stance == WeaponStance.Crouching)
			return m_WalkPitchCompensationCrouchDegrees;
		if (_stance == WeaponStance.Standing)
			return m_WalkPitchCompensationStandDegrees;
		return 0f;
	}

	private bool IsRunOrSprintMoveNow()
	{
		if (m_ClickToMove != null && m_ClickToMove.isActiveAndEnabled &&
		    (m_ClickToMove.IsRunMoveMode || m_ClickToMove.IsSprintMoveMode))
			return true;
		if (m_LocomotionDriver != null && m_LocomotionDriver.isActiveAndEnabled &&
		    (m_LocomotionDriver.IsRunMoveMode || m_LocomotionDriver.IsSprintMoveMode))
			return true;
		return false;
	}

	/// <summary>
	/// HipFire / PointAim / Aiming walk keeps bodyAim (root barrel facing). Run/sprint still zeroes the gate.
	/// Does not write weapon local.
	/// </summary>
	private bool ShouldKeepCombatWalkBodyAim()
	{
		if (IsRunOrSprintMoveNow())
			return false;
		if (GetHipFirePoseWeight() >= 0.999f)
			return true;
		WeaponPoseState pose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.LowReady;
		return pose == WeaponPoseState.PointAim || pose == WeaponPoseState.Aiming;
	}

	private bool IsMoveHoldAimMix()
	{
		if (ShouldKeepCombatWalkBodyAim())
			return false;
		return IsLocomotionMovingNow() || m_WasLocomotionMovingForAim;
	}

	private float GetModelAimAlignStrength() =>
		GetFireCapableAimBlend01()
		* GetPointAimCorrectionWeight()
		* (1f - GetHipFirePoseWeight())
		* m_ModelAimGate01;

	/// <summary>
	/// 1 = HipFire (authored hip slot, no local barrel twist).
	/// 0 = not HipFire.
	/// </summary>
	private float GetHipFirePoseWeight()
	{
		if (m_EquippedWeaponPose == null)
		{
			return m_ReadyHands != null && m_ReadyHands.EffectivePoseState.IsHipFireHold()
				? 1f
				: 0f;
		}

		float from = m_EquippedWeaponPose.CurrentPose.IsHipFireHold() ? 1f : 0f;
		float to = m_EquippedWeaponPose.TargetPose.IsHipFireHold() ? 1f : 0f;
		if (!m_EquippedWeaponPose.IsPoseBlendAnimating)
			return to;

		return Mathf.Lerp(from, to, m_EquippedWeaponPose.PoseBlend01);
	}

	/// <summary>
	/// 1 = Aiming (authored shoulder slot + AimPitch). FromTo in Hand_R twists the rifle and IK-yanks the support arm.
	/// </summary>
	private float GetShoulderedAimingPoseWeight()
	{
		if (m_EquippedWeaponPose == null)
		{
			return m_ReadyHands != null && m_ReadyHands.EffectivePoseState == WeaponPoseState.Aiming
				? 1f
				: 0f;
		}

		float from = m_EquippedWeaponPose.CurrentPose == WeaponPoseState.Aiming ? 1f : 0f;
		float to = m_EquippedWeaponPose.TargetPose == WeaponPoseState.Aiming ? 1f : 0f;
		if (!m_EquippedWeaponPose.IsPoseBlendAnimating)
			return to;

		return Mathf.Lerp(from, to, m_EquippedWeaponPose.PoseBlend01);
	}

	private static bool PoseWantsAimPointOverlay(WeaponPoseState _pose) =>
		_pose == WeaponPoseState.Aiming
		|| _pose == WeaponPoseState.PointAim;

	private static bool PoseWantsBarrelFromToCorrection(WeaponPoseState _pose) =>
		_pose == WeaponPoseState.PointAim;

	/// <summary>
	/// 1 = FromTo in the barrel→target plane (PointAim only, standing).
	/// Aiming keeps the authored slot; the Aim_Point layer still runs via <see cref="PoseWantsAimPointOverlay"/>.
	/// </summary>
	private float GetPointAimCorrectionWeight()
	{
		if (m_EquippedWeaponPose == null)
		{
			WeaponPoseState pose = m_ReadyHands != null
				? m_ReadyHands.EffectivePoseState
				: WeaponPoseState.NotReady;
			return PoseWantsBarrelFromToCorrection(pose) ? 1f : 0f;
		}

		float from = PoseWantsBarrelFromToCorrection(m_EquippedWeaponPose.CurrentPose) ? 1f : 0f;
		float to = PoseWantsBarrelFromToCorrection(m_EquippedWeaponPose.TargetPose) ? 1f : 0f;
		if (!m_EquippedWeaponPose.IsPoseBlendAnimating)
			return to;

		return Mathf.Max(from, to);
	}

	private WeaponPoseState ResolveAimPointPose()
	{
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.TargetPose;
		if (m_ReadyHands != null)
			return m_ReadyHands.EffectivePoseState;
		return WeaponPoseState.LowReady;
	}

	private float ResolveAimPointLayerWeight()
	{
		if (m_EquippedWeaponPose == null || !m_EquippedWeaponPose.IsPoseBlendAnimating)
			return 1f;

		bool fromWants = PoseWantsAimPointOverlay(m_EquippedWeaponPose.CurrentPose);
		bool toWants = PoseWantsAimPointOverlay(m_EquippedWeaponPose.TargetPose);
		return Mathf.Lerp(fromWants ? 1f : 0f, toWants ? 1f : 0f, m_EquippedWeaponPose.PoseBlend01);
	}

	private bool TryGetReadyBodyAlignContext(out float _strength)
	{
		_strength = 0f;

		if (!m_AlignBarrelToBodyWhenReadyNoTarget || !m_EnableWeaponModelAimCorrection)
			return false;

		if (IsSpineLeanActiveForBodyAlignSkip())
			return false;

		if (IsManualBarrelFacingOverrideActive())
			return false;

		float fireBlend = GetFireCapableAimBlend01();
		if (fireBlend <= 0.001f)
			return false;

		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;
		if (hasTarget && m_AimAtVisibleTarget)
			return false;

		if (IsAimBlockedByStanceOrReload())
			return false;

		if (m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating)
			return false;

		if (GetPointAimCorrectionWeight() > 0.001f)
			return false;

		_strength = GetReadyBodyAlignStrength() * m_ModelAimGate01;
		return _strength > 0.001f;
	}

	private float GetReadyBodyAlignStrength()
	{
		if (m_EquippedWeaponPose == null)
			return 1f;

		return Mathf.Clamp01(m_EquippedWeaponPose.ReadyPoseBlend01);
	}

	private bool IsAimBlockedByStanceOrReload()
	{
		if (m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return true;

		if (m_BlockCombatAimDuringReload &&
		    m_ReloadController != null &&
		    m_ReloadController.IsReloadBusy)
			return true;

		return false;
	}

	private bool IsSpineLeanActiveForBodyAlignSkip()
	{
		if (m_SpineLean == null)
			m_SpineLean = GetComponent<UnitSpineLean>();
		if (m_SpineLean == null)
			return false;

		return Mathf.Abs(m_SpineLean.CurrentLean01) >= m_SkipBodyAlignWhenLeanAbove
		       || Mathf.Abs(m_SpineLean.CurrentLeanDegrees) >= 1f;
	}

	private bool IsManualBarrelFacingOverrideActive()
	{
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_RtsMember != null && m_RtsMember.ShouldYieldRouteFacingToCombatTarget)
			return false;
		if (m_RtsMember != null && m_RtsMember.IsManualBarrelFacingActive)
			return true;

		if (m_ClickToMove == null)
			m_ClickToMove = GetComponent<UnitClickToMove>();
		if (m_ClickToMove != null && m_ClickToMove.OverrideFacingAngle.HasValue)
			return true;

		if (m_LocomotionDriver == null)
			m_LocomotionDriver = GetComponent<UnitNavLocomotionDriver>();
		if (m_LocomotionDriver != null && m_LocomotionDriver.OverrideFacingAngle.HasValue)
			return true;

		return false;
	}

	private Vector3 GetBodyForwardXZ()
	{
		Transform forwardSource = m_UnitForwardSource != null ? m_UnitForwardSource : transform;
		return ProjectOnHorizontalPlane(forwardSource.forward);
	}

	/// <summary>
	/// PointAim, no target: ease leftover FromTo to zero. Do not apply upright yaw/pitch
	/// (FromTo-weight 0 means BASE, not a reduced upright limit).
	/// </summary>
	private void ApplyNoTargetAuthoredWeaponRotation(Transform _weaponRoot, Quaternion _baseLocalRotation)
	{
		DampWeaponModelCorrectionToZero();

		float pointAimWeight = GetPointAimCorrectionWeight();
		if (pointAimWeight <= 0.001f)
			return;

		Quaternion finalLocal = _baseLocalRotation;
		if (_weaponRoot != null && _weaponRoot.parent != null && Mathf.Abs(m_SmoothedPointAimDegrees) > 0.0001f)
		{
			Transform parent = _weaponRoot.parent;
			Quaternion pointAimCorrection = Quaternion.AngleAxis(
				m_SmoothedPointAimDegrees,
				ToParentAxis(parent, m_PointAimAxisWorld));
			Quaternion localCorrection = Quaternion.Slerp(Quaternion.identity, pointAimCorrection, pointAimWeight);
			finalLocal = localCorrection * _baseLocalRotation;
		}

		SubmitWeaponLocalAimRotation(_weaponRoot, finalLocal);

		m_DebugWeaponYawErrorDegrees = 0f;
		m_DebugWeaponPitchErrorDegrees = 0f;
		m_DebugWeaponYawAppliedDegrees = 0f;
		m_DebugWeaponPitchAppliedDegrees = m_SmoothedPointAimDegrees * pointAimWeight;
	}

	private void DampWeaponModelCorrectionToZero()
	{
		float smoothTime = Mathf.Max(0.0001f, m_WeaponModelCorrectionSmoothTime);
		if (smoothTime <= 0.0001f)
		{
			ClearWeaponModelCorrectionVelocities();
			m_SmoothedWeaponYawDegrees = 0f;
			m_SmoothedWeaponPitchDegrees = 0f;
			m_SmoothedPointAimDegrees = 0f;
			return;
		}

		m_SmoothedWeaponYawDegrees = SmoothExpAngle(m_SmoothedWeaponYawDegrees, 0f, smoothTime);
		m_SmoothedWeaponPitchDegrees = SmoothExpAngle(m_SmoothedWeaponPitchDegrees, 0f, smoothTime);
		m_SmoothedPointAimDegrees = SmoothExp(m_SmoothedPointAimDegrees, 0f, smoothTime);
		ClearWeaponModelCorrectionVelocities();

		if (Mathf.Abs(m_SmoothedWeaponYawDegrees) < 0.01f)
			m_SmoothedWeaponYawDegrees = 0f;
		if (Mathf.Abs(m_SmoothedWeaponPitchDegrees) < 0.01f)
			m_SmoothedWeaponPitchDegrees = 0f;
		if (Mathf.Abs(m_SmoothedPointAimDegrees) < 0.01f)
			m_SmoothedPointAimDegrees = 0f;
	}

	private void ApplyWorldBodyBarrelYawAlignment(
		Transform _weaponRoot,
		Quaternion _baseLocalRotation,
		float _alignStrength)
	{
		if (_weaponRoot == null || m_BarrelTransform == null || _weaponRoot.parent == null)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		// Compute as if weapon were at base pose — do not write localRotation (PoseController owns that).
		Transform parent = _weaponRoot.parent;
		Quaternion baseWorld = parent.rotation * _baseLocalRotation;
		Vector3 barrelLocalDir = Quaternion.Inverse(_weaponRoot.rotation) * m_BarrelTransform.forward;
		Vector3 barrelFwd = ProjectOnHorizontalPlane(baseWorld * barrelLocalDir);
		Vector3 bodyFwd = GetBodyForwardXZ();
		if (bodyFwd.sqrMagnitude < 1e-6f || barrelFwd.sqrMagnitude < 1e-6f)
		{
			ResetWeaponModelCorrectionDebug();
			if (AllowsWeaponLocalAimCorrection())
				SubmitWeaponLocalAimRotation(_weaponRoot, _baseLocalRotation);
			return;
		}

		float alignStrength = Mathf.Clamp01(_alignStrength);
		float rawWorldYawError = Vector3.SignedAngle(barrelFwd, bodyFwd, Vector3.up);
		float targetYaw = Mathf.Clamp(rawWorldYawError * alignStrength, -m_BodyAlignYawLimitDegrees, m_BodyAlignYawLimitDegrees);

		float smoothTime = Mathf.Max(0.0001f, m_BodyAlignYawSmoothTime);
		if (m_BodyAlignYawSmoothTime <= 0.0001f)
		{
			m_SmoothedWeaponYawDegrees = targetYaw;
			m_SmoothedWeaponPitchDegrees = 0f;
			m_WeaponYawVelocity = 0f;
			m_SmoothedPointAimDegrees = 0f;
		}
		else
		{
			m_SmoothedWeaponYawDegrees = Mathf.SmoothDampAngle(
				m_SmoothedWeaponYawDegrees,
				targetYaw,
				ref m_WeaponYawVelocity,
				smoothTime,
				Mathf.Infinity,
				Time.deltaTime);
			m_SmoothedWeaponPitchDegrees = 0f;
			m_SmoothedPointAimDegrees = 0f;
		}

		Quaternion finalWorld = baseWorld;
		if (Mathf.Abs(m_SmoothedWeaponYawDegrees) > 0.0001f)
			finalWorld = Quaternion.AngleAxis(m_SmoothedWeaponYawDegrees, Vector3.up) * baseWorld;

		Quaternion finalLocal = Quaternion.Inverse(parent.rotation) * finalWorld;
		if (!AllowsWeaponLocalAimCorrection())
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		SubmitWeaponLocalAimRotation(_weaponRoot, finalLocal);

		m_DebugWeaponYawErrorDegrees = rawWorldYawError;
		m_DebugWeaponPitchErrorDegrees = 0f;
		m_DebugWeaponYawAppliedDegrees = m_SmoothedWeaponYawDegrees;
		m_DebugWeaponPitchAppliedDegrees = 0f;
	}

	private static Vector3 ProjectOnHorizontalPlane(Vector3 _vector)
	{
		Vector3 projected = _vector;
		projected.y = 0f;
		if (projected.sqrMagnitude < 1e-6f)
			return Vector3.zero;

		return projected.normalized;
	}

	private void LogReadyBodyAlignIfNeeded(float _alignStrength)
	{
		if (!m_LogReadyBodyAlign || Time.unscaledTime < m_NextReadyBodyAlignLogTime)
			return;
		if (m_BarrelTransform == null)
			return;

		Transform forwardSource = m_UnitForwardSource != null ? m_UnitForwardSource : transform;
		Vector3 bodyFwd = forwardSource.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
			return;
		bodyFwd.Normalize();

		Vector3 barrelFwd = m_BarrelTransform.forward;
		barrelFwd.y = 0f;
		if (barrelFwd.sqrMagnitude < 1e-6f)
			return;
		barrelFwd.Normalize();

		float bodyBarrelDelta = Vector3.SignedAngle(bodyFwd, barrelFwd, Vector3.up);
		float blend = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.ReadyPoseBlend01 : 1f;

		m_NextReadyBodyAlignLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogReadyBodyAlignIntervalSeconds);
		Debug.Log(
			$"[ReadyBodyAlign] unit={name} blend={blend:F2} strength={_alignStrength:F2} " +
			$"body↔barrel={bodyBarrelDelta:F1}° worldYawErr={m_DebugWeaponYawErrorDegrees:F1}° " +
			$"appliedWorldYaw={m_DebugWeaponYawAppliedDegrees:F1}° limit={m_BodyAlignYawLimitDegrees:F0}°",
			this);
	}

	private void TickPoseAimTransitionLog()
	{
		if (!m_LogPoseAimTransitions)
			return;
		if (m_EquippedWeaponPose == null)
			return;

		WeaponPoseState from = m_EquippedWeaponPose.CurrentPose;
		WeaponPoseState to = m_EquippedWeaponPose.TargetPose;
		bool blending = m_EquippedWeaponPose.IsPoseBlendAnimating;
		if (!m_HasPoseAimLogBaseline)
		{
			m_HasPoseAimLogBaseline = true;
			m_LoggedPoseFrom = from;
			m_LoggedPoseTo = to;
			m_LoggedPoseBlending = blending;
			return;
		}

		bool pairChanged = from != m_LoggedPoseFrom || to != m_LoggedPoseTo;
		bool blendEnded = m_LoggedPoseBlending && !blending;
		string label = null;
		if (blendEnded)
			label = "END";
		else if (pairChanged && blending && m_LoggedPoseBlending)
			label = "INVERT";
		else if (pairChanged && blending)
			label = "START";
		else if (pairChanged)
			label = "SNAP";
		else if (blending && Time.unscaledTime >= m_NextPoseAimTransitionLogTime)
			label = "BLEND";

		if (label != null)
		{
			WeaponPoseState logFrom = from;
			WeaponPoseState logTo = to;
			if (blendEnded)
			{
				logFrom = m_LoggedPoseFrom;
				logTo = m_LoggedPoseTo;
			}
			else if (label == "SNAP")
			{
				logFrom = m_LoggedPoseFrom;
				logTo = from;
			}

			LogPoseAimTransition(label, logFrom, logTo, blending);
		}

		m_LoggedPoseFrom = from;
		m_LoggedPoseTo = to;
		m_LoggedPoseBlending = blending;
	}

	private void LogPoseAimTransition(string _label, WeaponPoseState _from, WeaponPoseState _to, bool _blending)
	{
		if (!m_LogPoseAimTransitions)
			return;
		if (m_RtsMember == null)
			m_RtsMember = GetComponent<RtsUnitMember>();
		if (m_RtsMember != null && !m_RtsMember.IsSelected)
			return;

		if (_label == "START" || _label == "SNAP" || _label == "INVERT")
			m_PoseAimTransitionLogId++;

		float pointAimW = GetPointAimCorrectionWeight();
		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;
		float barrelPitch = 0f;
		if (m_BarrelTransform != null)
		{
			Vector3 f = m_BarrelTransform.forward;
			float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
			barrelPitch = Mathf.Atan2(f.y, horiz) * Mathf.Rad2Deg;
		}

		m_NextPoseAimTransitionLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogPoseAimTransitionIntervalSeconds);
		Debug.Log(
			$"[PoseAim #{m_PoseAimTransitionLogId}] {_label} unit={name} {_from}→{_to} " +
			$"t={m_EquippedWeaponPose.PoseBlend01:F3} blending={_blending} " +
			$"fireBlend={m_EquippedWeaponPose.FireCapableBlend01:F3} " +
			$"raisedBlend={m_EquippedWeaponPose.ReadyPoseBlend01:F3} " +
			$"corr={FormatAimCorrectionMode(pointAimW)} pointAimW={pointAimW:F2} " +
			$"hasTarget={hasTarget} modelAim={ShouldApplyWeaponLocalOnlyForAim()} " +
			$"aimGate={m_ModelAimGate01:F3} " +
			$"yawErr={m_DebugWeaponYawErrorDegrees:F1}° pitchErr={m_DebugWeaponPitchErrorDegrees:F1}° " +
			$"appliedYaw={m_DebugWeaponYawAppliedDegrees:F1}° appliedPitch={m_DebugWeaponPitchAppliedDegrees:F1}° " +
			$"fromTo={m_SmoothedPointAimDegrees:F1}° barrelPitch={barrelPitch:F1}° " +
			$"AimPitch={m_SmoothedPitch01:F3} layerW={m_SmoothedLayerWeight:F3}",
			this);
	}

	private string FormatAimCorrectionMode(float _pointAimWeight)
	{
		if (IsHoldingWeaponModelAimAfterReload())
			return "reloadSettle";
		if (!ShouldKeepCombatWalkBodyAim() &&
		    (IsLocomotionMovingNow() || (m_WasLocomotionMovingForAim && m_ModelAimGate01 < 0.999f)))
			return "moveHold";
		if (GetShoulderedAimingPoseWeight() >= 0.999f)
			return "authoredAiming";
		if (m_DebugCombatAimActive)
			return "bodyAim";
		if (IsFiringForSteadyAim() || Time.time < m_HoldModelAimAfterFireUntil)
			return "recoilHold";

		if (ShouldApplyWeaponLocalOnlyForAim())
		{
			if (_pointAimWeight >= 0.999f)
				return "barrel-FromTo";
			if (_pointAimWeight <= 0.001f)
				return "upright-yaw/pitch";
			return "blend-upright/FromTo";
		}

		return Mathf.Abs(m_SmoothedWeaponYawDegrees) > 0.01f
		       || Mathf.Abs(m_SmoothedWeaponPitchDegrees) > 0.01f
		       || Mathf.Abs(m_SmoothedPointAimDegrees) > 0.01f
			? "ease-out"
			: "none";
	}

	private void TickReloadAimMixLog()
	{
		if (!m_LogReloadAimMix)
			return;

		bool reloadBusy = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool boltHeld = m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
		bool magLoad = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool busy = reloadBusy || boltHeld || magLoad;
		string edge = null;
		if (busy && !m_LoggedReloadBusy)
		{
			m_ReloadAimLogId++;
			m_ReloadAimLogUntilTime = float.PositiveInfinity;
			edge = "START";
		}
		else if (!busy && m_LoggedReloadBusy)
		{
			m_ReloadAimLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogReloadAimAfterSeconds);
			edge = "END";
		}

		m_LoggedReloadBusy = busy;
		if (!busy && Time.unscaledTime > m_ReloadAimLogUntilTime)
			return;

		if (edge == null && Time.unscaledTime < m_NextReloadAimLogTime)
			return;

		m_NextReloadAimLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogReloadAimIntervalSeconds);
		LogAimMixSnapshot("ReloadAim", m_ReloadAimLogId, edge ?? (busy ? "RELOAD" : "AFTER"));
	}

	private void TickHipFireAimMixLog()
	{
		if (!m_LogHipFireAimMix)
			return;

		WeaponPoseState poseFrom = m_EquippedWeaponPose != null
			? m_EquippedWeaponPose.CurrentPose
			: WeaponPoseState.NotReady;
		WeaponPoseState poseTo = m_EquippedWeaponPose != null
			? m_EquippedWeaponPose.TargetPose
			: poseFrom;
		bool blending = m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;
		WeaponPoseState effective = m_ReadyHands != null ? m_ReadyHands.EffectivePoseState : poseTo;
		bool hipPose = InvolvesHipFire(poseFrom, poseTo) || effective.IsHipFireHold();

		bool reloadBusy = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool boltHeld = m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
		bool magLoad = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool busy = reloadBusy || boltHeld || magLoad;

		string edge = null;
		if (!m_HasHipFirePoseLogBaseline)
		{
			m_HasHipFirePoseLogBaseline = true;
			m_HipFireLoggedFrom = poseFrom;
			m_HipFireLoggedTo = poseTo;
			m_HipFireLoggedBlending = blending;
		}
		else
		{
			bool pairChanged = poseFrom != m_HipFireLoggedFrom || poseTo != m_HipFireLoggedTo;
			bool blendEnded = m_HipFireLoggedBlending && !blending;
			bool loggedHip = InvolvesHipFire(m_HipFireLoggedFrom, m_HipFireLoggedTo);
			bool nowHip = InvolvesHipFire(poseFrom, poseTo);
			if (blendEnded && loggedHip)
			{
				edge = "END";
				m_HipFireAimLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogHipFireAimAfterSeconds);
			}
			else if (pairChanged && blending && m_HipFireLoggedBlending && (loggedHip || nowHip))
			{
				edge = "INVERT";
				if (!nowHip)
					m_HipFireAimLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogHipFireAimAfterSeconds);
			}
			else if (pairChanged && blending && nowHip)
			{
				m_HipFireAimLogId++;
				edge = "START";
			}
			else if (pairChanged && nowHip)
			{
				m_HipFireAimLogId++;
				m_HipFireAimLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogHipFireAimAfterSeconds);
				edge = "SNAP";
			}
			else if (blending && nowHip && Time.unscaledTime >= m_NextHipFireAimLogTime)
				edge = "BLEND";

			m_HipFireLoggedFrom = poseFrom;
			m_HipFireLoggedTo = poseTo;
			m_HipFireLoggedBlending = blending;
		}

		if (busy && !m_LoggedHipFireReloadBusy)
		{
			if (hipPose)
			{
				m_HipFireReloadSession = true;
				m_HipFireAimLogId++;
				edge = "START";
			}
		}
		else if (!busy && m_LoggedHipFireReloadBusy && m_HipFireReloadSession)
		{
			m_HipFireAimLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogHipFireAimAfterSeconds);
			edge = "END";
		}

		m_LoggedHipFireReloadBusy = busy && (m_HipFireReloadSession || hipPose);
		if (!busy)
			m_HipFireReloadSession = false;

		bool poseWindow = blending && InvolvesHipFire(poseFrom, poseTo);
		bool afterWindow = Time.unscaledTime <= m_HipFireAimLogUntilTime;
		if (edge == null && !poseWindow && !afterWindow && !(busy && hipPose))
			return;

		if (edge == null && Time.unscaledTime < m_NextHipFireAimLogTime)
			return;

		m_NextHipFireAimLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogHipFireAimIntervalSeconds);
		string phase = edge;
		if (phase == null)
		{
			if (busy && (hipPose || m_LoggedHipFireReloadBusy))
				phase = "RELOAD";
			else if (poseWindow)
				phase = "BLEND";
			else
				phase = "AFTER";
		}

		LogAimMixSnapshot("HipFireAim", m_HipFireAimLogId, phase);
	}

	private static bool InvolvesHipFire(WeaponPoseState _from, WeaponPoseState _to) =>
		_from.IsHipFireHold() || _to.IsHipFireHold();

	private void TickWeaponSpinLog()
	{
		if (!m_LogWeaponSpin || !LogWeaponSpinMasterEnabled)
			return;

		WeaponPoseState pose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.NotReady;
		WeaponPoseState poseFrom = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.CurrentPose : pose;
		WeaponPoseState poseTo = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.TargetPose : pose;
		bool blending = m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;
		float poseT = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.PoseBlend01 : 1f;

		bool reloadBusy = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool mag = m_ReloadController != null && m_ReloadController.IsReloadingWeapon;
		bool bolt = m_ReloadController != null && m_ReloadController.IsCyclingBolt;
		bool boltHeld = m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
		bool magLoad = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool busy = reloadBusy || boltHeld || magLoad;
		bool moving = IsLocomotionMovingNow();
		bool firing = IsFiringForSteadyAim();
		bool lean = IsSpineLeanActiveForBodyAlignSkip();

		Transform weaponRoot = m_UnitEquipment != null ? m_UnitEquipment.MainWeaponRoot : null;
		Transform hand = weaponRoot != null ? weaponRoot.parent : null;
		float rootYaw = transform.eulerAngles.y;
		float rootDelta = 0f;
		float weaponLocalDelta = 0f;
		float handDelta = 0f;
		float barrelDelta = 0f;
		Quaternion weaponLocal = weaponRoot != null ? weaponRoot.localRotation : Quaternion.identity;
		Quaternion handWorld = hand != null ? hand.rotation : Quaternion.identity;
		Vector3 barrelFwd = m_BarrelTransform != null ? m_BarrelTransform.forward : Vector3.forward;

		Vector3 composeEuler = Vector3.zero;
		if (m_EquippedWeaponPose != null)
		{
			Quaternion delta = Quaternion.Inverse(m_EquippedWeaponPose.CurrentBaseWeaponLocalRotation) *
			                   m_EquippedWeaponPose.ComposedAimLocalRotation;
			composeEuler = WrapEuler180(delta.eulerAngles);
		}

		if (m_HasWeaponSpinBaseline)
			rootDelta = Mathf.DeltaAngle(m_SpinLastRootYaw, rootYaw);
		if (m_HasSpinLastWeaponLocal)
			weaponLocalDelta = Quaternion.Angle(m_SpinLastWeaponLocal, weaponLocal);
		if (m_HasSpinLastHandWorld)
			handDelta = Quaternion.Angle(m_SpinLastHandWorld, handWorld);
		if (m_HasSpinLastBarrelFwd && barrelFwd.sqrMagnitude > 1e-8f && m_SpinLastBarrelFwd.sqrMagnitude > 1e-8f)
			barrelDelta = Vector3.Angle(m_SpinLastBarrelFwd, barrelFwd);

		string edge = null;
		if (!m_HasWeaponSpinBaseline)
		{
			m_HasWeaponSpinBaseline = true;
			m_SpinLoggedFrom = poseFrom;
			m_SpinLoggedTo = poseTo;
			m_SpinLoggedBlending = blending;
			m_SpinLoggedReloadBusy = busy;
			m_SpinLoggedMoving = moving;
			m_SpinLoggedFiring = firing;
			StoreWeaponSpinSample(rootYaw, weaponLocal, handWorld, barrelFwd, composeEuler);
			return;
		}

		bool pairChanged = poseFrom != m_SpinLoggedFrom || poseTo != m_SpinLoggedTo;
		bool blendEnded = m_SpinLoggedBlending && !blending;
		if (blendEnded)
			edge = "POSE-END";
		else if (pairChanged && blending)
		{
			m_WeaponSpinLogId++;
			edge = "POSE-START";
		}
		else if (pairChanged)
		{
			m_WeaponSpinLogId++;
			edge = "POSE-SNAP";
		}

		if (busy && !m_SpinLoggedReloadBusy)
		{
			m_WeaponSpinLogId++;
			edge = "RELOAD-START";
		}
		else if (!busy && m_SpinLoggedReloadBusy)
			edge = edge ?? "RELOAD-END";

		if (moving && !m_SpinLoggedMoving)
		{
			m_WeaponSpinLogId++;
			edge = edge ?? "MOVE-START";
		}
		else if (!moving && m_SpinLoggedMoving)
			edge = edge ?? "MOVE-STOP";

		if (firing && !m_SpinLoggedFiring)
			edge = edge ?? "FIRE-START";
		else if (!firing && m_SpinLoggedFiring)
			edge = edge ?? "FIRE-STOP";

		if (Mathf.Abs(rootDelta) >= m_WeaponSpinRootTurnDegrees)
			edge = edge ?? "TURN";

		float composeJump = m_HasSpinLastWeaponLocal
			? Quaternion.Angle(Quaternion.Euler(m_SpinLastComposeEuler), Quaternion.Euler(composeEuler))
			: 0f;
		bool authoredDrive = blending || busy;
		bool spinInHand = !authoredDrive &&
		                  weaponLocalDelta >= m_WeaponSpinLocalJumpDegrees &&
		                  weaponLocalDelta > handDelta + 3f;
		bool spinBarrel = !authoredDrive &&
		                  barrelDelta >= m_WeaponSpinBarrelJumpDegrees &&
		                  barrelDelta > handDelta + 5f;
		bool spinCompose = !authoredDrive &&
		                   composeJump >= m_WeaponSpinLocalJumpDegrees &&
		                   composeJump > handDelta + 3f;
		float pitchErrDelta = m_HasWeaponSpinBaseline
			? Mathf.Abs(m_DebugWeaponPitchErrorDegrees - m_SpinLastPitchErr)
			: 0f;
		float yawErrDelta = m_HasWeaponSpinBaseline
			? Mathf.Abs(m_DebugWeaponYawErrorDegrees - m_SpinLastYawErr)
			: 0f;
		bool spinErrSpike = !authoredDrive && (pitchErrDelta >= 10f || yawErrDelta >= 10f);
		bool atYawLimit = Mathf.Abs(m_DebugWeaponYawAppliedDegrees) >= m_WeaponModelYawLimitDegrees - 0.15f;
		bool atPitchLimit = m_DebugWeaponPitchAppliedDegrees >= m_WeaponModelPitchUpLimitDegrees - 0.15f
		                    || m_DebugWeaponPitchAppliedDegrees <= -m_WeaponModelPitchDownLimitDegrees + 0.15f;
		bool leftover = Mathf.Abs(m_DebugWeaponYawErrorDegrees) > m_WeaponModelYawLimitDegrees + 4f
		                || Mathf.Abs(m_DebugWeaponPitchErrorDegrees) > 12f;
		bool spinAttractor = !authoredDrive && atYawLimit && atPitchLimit && leftover;
		if (spinInHand || spinBarrel || spinCompose || spinErrSpike || spinAttractor)
		{
			string spinTag = spinInHand ? "SPIN-HAND"
				: spinBarrel ? "SPIN-BARREL"
				: spinCompose ? "SPIN-COMPOSE"
				: spinErrSpike ? "SPIN-ERR"
				: "SPIN-ATTRACTOR";
			if (edge == null)
			{
				m_WeaponSpinLogId++;
				edge = spinTag;
			}
			else if (edge.IndexOf("SPIN", System.StringComparison.Ordinal) < 0)
				edge = edge + "+" + spinTag;
		}

		if (edge != null)
			m_SpinLogUntilTime = Time.unscaledTime + Mathf.Max(0.2f, m_LogWeaponSpinAfterSeconds);

		bool live = blending || busy || moving || firing;
		bool afterWindow = Time.unscaledTime <= m_SpinLogUntilTime;
		bool quiet = weaponLocalDelta < 0.2f
		             && handDelta < 0.2f
		             && barrelDelta < 0.2f
		             && composeJump < 0.2f
		             && Mathf.Abs(rootDelta) < 0.2f
		             && pitchErrDelta < 1f;
		if (edge == null && (quiet || (!live && !afterWindow)))
		{
			m_SpinLoggedFrom = poseFrom;
			m_SpinLoggedTo = poseTo;
			m_SpinLoggedBlending = blending;
			m_SpinLoggedReloadBusy = busy;
			m_SpinLoggedMoving = moving;
			m_SpinLoggedFiring = firing;
			StoreWeaponSpinSample(rootYaw, weaponLocal, handWorld, barrelFwd, composeEuler);
			return;
		}

		bool isSpin = edge != null && edge.StartsWith("SPIN", System.StringComparison.Ordinal);
		if (edge == null && !isSpin && Time.unscaledTime < m_NextWeaponSpinLogTime)
		{
			m_SpinLoggedFrom = poseFrom;
			m_SpinLoggedTo = poseTo;
			m_SpinLoggedBlending = blending;
			m_SpinLoggedReloadBusy = busy;
			m_SpinLoggedMoving = moving;
			m_SpinLoggedFiring = firing;
			StoreWeaponSpinSample(rootYaw, weaponLocal, handWorld, barrelFwd, composeEuler);
			return;
		}

		m_NextWeaponSpinLogTime = Time.unscaledTime + Mathf.Max(0.05f, m_LogWeaponSpinIntervalSeconds);
		string phase = edge ?? (busy ? "RELOAD" : moving ? "MOVE" : blending ? "BLEND" : firing ? "FIRE" : "TICK");
		LogWeaponSpinLine(
			phase,
			pose,
			poseFrom,
			poseTo,
			poseT,
			blending,
			reloadBusy,
			mag,
			bolt,
			boltHeld,
			busy,
			moving,
			firing,
			lean,
			rootYaw,
			rootDelta,
			weaponLocalDelta,
			handDelta,
			barrelDelta,
			composeJump,
			weaponLocal,
			composeEuler);

		m_SpinLoggedFrom = poseFrom;
		m_SpinLoggedTo = poseTo;
		m_SpinLoggedBlending = blending;
		m_SpinLoggedReloadBusy = busy;
		m_SpinLoggedMoving = moving;
		m_SpinLoggedFiring = firing;
		StoreWeaponSpinSample(rootYaw, weaponLocal, handWorld, barrelFwd, composeEuler);
	}

	private void StoreWeaponSpinSample(
		float _rootYaw,
		Quaternion _weaponLocal,
		Quaternion _handWorld,
		Vector3 _barrelFwd,
		Vector3 _composeEuler)
	{
		m_SpinLastRootYaw = _rootYaw;
		m_SpinLastWeaponLocal = _weaponLocal;
		m_SpinLastHandWorld = _handWorld;
		m_SpinLastBarrelFwd = _barrelFwd;
		m_SpinLastComposeEuler = _composeEuler;
		m_SpinLastPitchErr = m_DebugWeaponPitchErrorDegrees;
		m_SpinLastYawErr = m_DebugWeaponYawErrorDegrees;
		m_HasSpinLastWeaponLocal = true;
		m_HasSpinLastHandWorld = true;
		m_HasSpinLastBarrelFwd = _barrelFwd.sqrMagnitude > 1e-8f;
	}

	private void LogWeaponSpinLine(
		string _phase,
		WeaponPoseState _pose,
		WeaponPoseState _from,
		WeaponPoseState _to,
		float _poseT,
		bool _blending,
		bool _reloadBusy,
		bool _mag,
		bool _bolt,
		bool _boltHeld,
		bool _busy,
		bool _moving,
		bool _firing,
		bool _lean,
		float _rootYaw,
		float _rootDelta,
		float _weaponLocalDelta,
		float _handDelta,
		float _barrelDelta,
		float _composeJump,
		Quaternion _weaponLocal,
		Vector3 _composeEuler)
	{
		bool stanceBusy = m_BusyState != null && m_BusyState.IsBusy &&
		                  (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;
		bool modelAim = ShouldApplyWeaponLocalOnlyForAim();
		float pointAimW = GetPointAimCorrectionWeight();
		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;
		float nav = m_Animator != null ? m_Animator.GetFloat(s_NavSpeed) : 0f;
		Vector3 weaponEu = WrapEuler180(_weaponLocal.eulerAngles);

		float barrelPitch = 0f;
		float barrelYawErr = 0f;
		if (m_BarrelTransform != null)
		{
			Vector3 f = m_BarrelTransform.forward;
			float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
			barrelPitch = Mathf.Atan2(f.y, horiz) * Mathf.Rad2Deg;
			if (hasTarget)
			{
				Vector3 toTarget = m_TargetSelector.GetEngageableAimPointWorld() - m_BarrelTransform.position;
				toTarget.y = 0f;
				Vector3 barrelXZ = f;
				barrelXZ.y = 0f;
				if (toTarget.sqrMagnitude > 1e-6f && barrelXZ.sqrMagnitude > 1e-6f)
					barrelYawErr = Vector3.SignedAngle(barrelXZ.normalized, toTarget.normalized, Vector3.up);
			}
		}

		if (m_SpineHorizontalAim == null)
			m_SpineHorizontalAim = GetComponent<UnitSpineHorizontalAim>();
		float spineYaw = m_SpineHorizontalAim != null ? m_SpineHorizontalAim.CurrentAbsorbedYawDegrees : 0f;
		float spinePitch = m_SpineHorizontalAim != null ? m_SpineHorizontalAim.CurrentAbsorbedPitchDegrees : 0f;
		bool spineRecenter = m_SpineHorizontalAim != null && m_SpineHorizontalAim.WantsRootRecenter;

		float? arrowYaw = null;
		if (m_ClickToMove != null && m_ClickToMove.OverrideFacingAngle.HasValue)
			arrowYaw = m_ClickToMove.OverrideFacingAngle;
		else if (m_LocomotionDriver != null && m_LocomotionDriver.OverrideFacingAngle.HasValue)
			arrowYaw = m_LocomotionDriver.OverrideFacingAngle;

		string mix = BuildReloadAimMixTag(
			_busy,
			modelAim,
			m_DebugCombatAimActive,
			_blending,
			IsHoldingWeaponModelAimAfterReload(),
			IsMoveHoldAimMix());

		string line =
			$"[WeaponSpin #{m_WeaponSpinLogId}] {_phase} unit={name} pose={_pose} {_from}→{_to} " +
			$"t={_poseT:F2} blending={(_blending ? 1 : 0)} " +
			$"reload={(_reloadBusy ? 1 : 0)} mag={(_mag ? 1 : 0)} bolt={(_bolt ? 1 : 0)} boltHeld={(_boltHeld ? 1 : 0)} " +
			$"settle={(IsHoldingWeaponModelAimAfterReload() ? 1 : 0)} stance={(stanceBusy ? 1 : 0)} " +
			$"nav={nav:F2} move={(_moving ? 1 : 0)} fire={(_firing ? 1 : 0)} lean={(_lean ? 1 : 0)} " +
			$"rootYaw={_rootYaw:F1} rootΔ={_rootDelta:F1}° spine={spineYaw:F1} spinePitch={spinePitch:F1} recenter={(spineRecenter ? 1 : 0)} " +
			$"arrow={(arrowYaw.HasValue ? arrowYaw.Value.ToString("F0") : "-")} " +
			$"handΔ={_handDelta:F1}° wpnLocalΔ={_weaponLocalDelta:F1}° barrelΔ={_barrelDelta:F1}° " +
			$"localVsHand={(_weaponLocalDelta - _handDelta):F1}° composeJump={_composeJump:F1}° " +
			$"wpnLocal=({weaponEu.x:F1},{weaponEu.y:F1},{weaponEu.z:F1}) " +
			$"mix={mix} combatAim={(m_DebugCombatAimActive ? 1 : 0)} modelAim={(modelAim ? 1 : 0)} " +
			$"fireBlend={GetFireCapableAimBlend01():F2} gate={m_ModelAimGate01:F2} corr={FormatAimCorrectionMode(pointAimW)} " +
			$"AimPitch={m_SmoothedPitch01:F2} walkComp={m_DebugWalkPitchCompensationDegrees:F1} layerW={m_SmoothedLayerWeight:F2} " +
			$"yawErr={m_DebugWeaponYawErrorDegrees:F1}° appliedYaw={m_DebugWeaponYawAppliedDegrees:F1}° " +
			$"pitchErr={m_DebugWeaponPitchErrorDegrees:F1}° appliedPitch={m_DebugWeaponPitchAppliedDegrees:F1}° " +
			$"fromTo={m_SmoothedPointAimDegrees:F1}° aimBarrelPitch={barrelPitch:F1}° barrelYawErr={barrelYawErr:F1}° " +
			$"desiredYaw={m_DesiredAimYawDegrees:F1} desiredPitch={m_DesiredAimPitchDegrees:F1} " +
			$"bodyYawError={m_BodyYawErrorDegrees:F1} spineYaw={spineYaw:F1} " +
			$"residualYaw={m_ResidualYawDegrees:F1} residualPitch={m_ResidualPitchDegrees:F1} " +
			$"aimResidualPitch={m_ResidualPitchDegrees:F1} aimQuality={m_AimQuality01:F2} saturation={m_AimSaturation} " +
			$"{FormatVisualRecoilSpinTag()} {FormatHandIkSpinTag()} " +
			$"owner={ResolveWeaponLocalOwnerTag()} composeΔ=({_composeEuler.x:F1},{_composeEuler.y:F1},{_composeEuler.z:F1}) " +
			$"hasTarget={(hasTarget ? 1 : 0)}";

		m_PendingWeaponSpinLine = line;
		m_PendingWeaponSpinLog = true;
	}

	/// <summary>
	/// Completes a deferred WeaponSpin line after visual recoil overlay (order 201).
	/// AimBarrel was captured at 65; VisualBarrel is the bore now.
	/// </summary>
	public void FlushWeaponSpinLogAfterVisualRecoil()
	{
		if (!m_PendingWeaponSpinLog)
			return;

		if (!m_LogWeaponSpin || !LogWeaponSpinMasterEnabled)
		{
			m_PendingWeaponSpinLog = false;
			m_PendingWeaponSpinLine = null;
			return;
		}

		m_PendingWeaponSpinLog = false;
		string line = m_PendingWeaponSpinLine ?? string.Empty;
		m_PendingWeaponSpinLine = null;

		MeasureBarrelPitchAndTargetError(out float visualBarrelPitch, out float visualYawErr, out float visualResidualPitch);
		float visualError = visualResidualPitch;
		Debug.Log(
			$"{line} visualBarrelPitch={visualBarrelPitch:F1}° visualYawErr={visualYawErr:F1}° visualError={visualError:F1}°",
			this);
	}

	private void MeasureBarrelPitchAndTargetError(out float _barrelPitch, out float _yawErr, out float _residualPitch)
	{
		_barrelPitch = 0f;
		_yawErr = 0f;
		_residualPitch = 0f;
		if (m_BarrelTransform == null)
			return;

		Vector3 f = m_BarrelTransform.forward;
		float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
		_barrelPitch = Mathf.Atan2(f.y, Mathf.Max(1e-6f, horiz)) * Mathf.Rad2Deg;

		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;
		if (!hasTarget)
			return;

		Vector3 toTarget = m_TargetSelector.GetEngageableAimPointWorld() - m_BarrelTransform.position;
		Vector3 toTargetXZ = toTarget;
		toTargetXZ.y = 0f;
		Vector3 barrelXZ = f;
		barrelXZ.y = 0f;
		if (toTargetXZ.sqrMagnitude > 1e-6f && barrelXZ.sqrMagnitude > 1e-6f)
			_yawErr = Vector3.SignedAngle(barrelXZ.normalized, toTargetXZ.normalized, Vector3.up);

		float targetHoriz = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
		float targetPitch = Mathf.Atan2(toTarget.y, Mathf.Max(1e-6f, targetHoriz)) * Mathf.Rad2Deg;
		_residualPitch = _barrelPitch - targetPitch;
	}

	private string ResolveWeaponLocalOwnerTag()
	{
		if (IsRuntimePoseTuningActive())
			return "tuner";
		if (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle)
			return "bolt";
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.GetWeaponLocalOwnerTag();
		return AllowsWeaponLocalAimCorrection() &&
		       (ShouldApplyWeaponLocalOnlyForAim() || ShouldApplyLeanTargetAim())
			? "pointAimCorr"
			: "base";
	}

	private string FormatVisualRecoilSpinTag()
	{
		if (m_WeaponRecoil == null)
			m_WeaponRecoil = GetComponent<UnitWeaponRecoil>();
		if (m_VisualRecoilApplicator == null)
			m_VisualRecoilApplicator = GetComponent<WeaponVisualRecoilApplicator>();

		WeaponVisualRecoilState state = m_WeaponRecoil != null
			? m_WeaponRecoil.CurrentState
			: default;
		float recoilDelta = m_WeaponRecoil != null ? m_WeaponRecoil.RecoilRotationDeltaDegrees : 0f;
		float handRecoilDelta = 0f;
		if (m_VisualRecoilApplicator != null)
		{
			handRecoilDelta = Quaternion.Angle(
				m_VisualRecoilApplicator.LastHandBaseLocalRotation,
				m_VisualRecoilApplicator.LastHandFinalLocalRotation);
		}

		return $"recoilPitch={state.punchPitch:F2} recoilYaw={state.punchYaw:F2} " +
		       $"recoilBack={state.backOffset:F4} recoilUp={state.upOffset:F4} " +
		       $"climbPitch={state.climbPitch:F2} recoilΔ={recoilDelta:F2}° handRecoilΔ={handRecoilDelta:F2}°";
	}

	private string FormatHandIkSpinTag()
	{
		if (m_HandIk == null)
			m_HandIk = GetComponent<AnimatorHandIk>();
		if (m_HandIk == null)
			m_HandIk = GetComponentInChildren<AnimatorHandIk>();
		if (m_GripResolver == null)
			m_GripResolver = GetComponent<WeaponGripResolver>();

		WeaponHoldContext hold = m_GripResolver != null ? m_GripResolver.HoldContext : default;

		if (m_HandIk == null)
		{
			return $"ikState=- leftIkW=0/0 rightIkW=0/0 poseBlend={hold.PoseBlend01:F2} stanceBlend={hold.StanceBlend01:F2} " +
			       $"gripError=0 gripReachable=1";
		}

		GripValidity gripV = m_HandIk.LastGripValidity;
		return $"ikState={m_HandIk.CurrentMode} leftIkW={m_HandIk.CurrentLeftIkWeight:F2}/{m_HandIk.TargetLeftIkWeight:F2} " +
		       $"rightIkW={m_HandIk.CurrentRightIkWeight:F2}/{m_HandIk.TargetRightIkWeight:F2} " +
		       $"poseBlend={hold.PoseBlend01:F2} stanceBlend={hold.StanceBlend01:F2} " +
		       $"gripError={gripV.DistanceError:F3} gripReachable={(gripV.IsReachable ? 1 : 0)}";
	}

	private static Vector3 WrapEuler180(Vector3 _euler)
	{
		if (_euler.x > 180f) _euler.x -= 360f;
		if (_euler.y > 180f) _euler.y -= 360f;
		if (_euler.z > 180f) _euler.z -= 360f;
		return _euler;
	}

	private void LogAimMixSnapshot(string _tag, int _id, string _phase)
	{
		WeaponPoseState pose = m_ReadyHands != null
			? m_ReadyHands.EffectivePoseState
			: WeaponPoseState.NotReady;
		WeaponPoseState poseFrom = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.CurrentPose : pose;
		WeaponPoseState poseTo = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.TargetPose : pose;
		bool blending = m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;
		float poseT = m_EquippedWeaponPose != null ? m_EquippedWeaponPose.PoseBlend01 : 1f;

		bool reloadBusy = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool mag = m_ReloadController != null && m_ReloadController.IsReloadingWeapon;
		bool bolt = m_ReloadController != null && m_ReloadController.IsCyclingBolt;
		bool boltHeld = m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle;
		bool belt = m_ReloadController != null && m_ReloadController.IsLoadingLmgBelt;
		bool magLoad = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;
		bool stanceBusy = m_BusyState != null && m_BusyState.IsBusy &&
		                  (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;
		bool modelAim = ShouldApplyWeaponLocalOnlyForAim();
		float pointAimW = GetPointAimCorrectionWeight();
		bool hasTarget = m_TargetSelector != null &&
		                 m_TargetSelector.HasSelectedAimPoint &&
		                 m_TargetSelector.SelectedTarget != null;

		float barrelPitch = 0f;
		float barrelYawErr = 0f;
		if (m_BarrelTransform != null)
		{
			Vector3 f = m_BarrelTransform.forward;
			float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
			barrelPitch = Mathf.Atan2(f.y, horiz) * Mathf.Rad2Deg;
			if (hasTarget)
			{
				Vector3 toTarget = m_TargetSelector.GetEngageableAimPointWorld() - m_BarrelTransform.position;
				toTarget.y = 0f;
				Vector3 barrelXZ = f;
				barrelXZ.y = 0f;
				if (toTarget.sqrMagnitude > 1e-6f && barrelXZ.sqrMagnitude > 1e-6f)
					barrelYawErr = Vector3.SignedAngle(barrelXZ.normalized, toTarget.normalized, Vector3.up);
			}
		}

		Vector3 composedDeltaEuler = Vector3.zero;
		if (m_EquippedWeaponPose != null)
		{
			Quaternion delta = Quaternion.Inverse(m_EquippedWeaponPose.CurrentBaseWeaponLocalRotation) *
			                   m_EquippedWeaponPose.ComposedAimLocalRotation;
			composedDeltaEuler = delta.eulerAngles;
			if (composedDeltaEuler.x > 180f) composedDeltaEuler.x -= 360f;
			if (composedDeltaEuler.y > 180f) composedDeltaEuler.y -= 360f;
			if (composedDeltaEuler.z > 180f) composedDeltaEuler.z -= 360f;
		}

		string mix = BuildReloadAimMixTag(
			reloadBusy || boltHeld,
			modelAim,
			m_DebugCombatAimActive,
			blending,
			IsHoldingWeaponModelAimAfterReload(),
			IsMoveHoldAimMix());

		float nav = m_Animator != null ? m_Animator.GetFloat(s_NavSpeed) : 0f;
		Debug.Log(
			$"[{_tag} #{_id}] {_phase} unit={name} pose={pose} {poseFrom}→{poseTo} " +
			$"t={poseT:F2} blending={(blending ? 1 : 0)} " +
			$"reload={(reloadBusy ? 1 : 0)} mag={(mag ? 1 : 0)} bolt={(bolt ? 1 : 0)} boltHeld={(boltHeld ? 1 : 0)} belt={(belt ? 1 : 0)} " +
			$"magLoad={(magLoad ? 1 : 0)} stance={(stanceBusy ? 1 : 0)} settle={(IsHoldingWeaponModelAimAfterReload() ? 1 : 0)} " +
			$"nav={nav:F2} move={(IsLocomotionMovingNow() ? 1 : 0)} gate={m_ModelAimGate01:F2} " +
			$"mix={mix} combatAim={ (m_DebugCombatAimActive ? 1 : 0)} modelAim={ (modelAim ? 1 : 0)} " +
			$"fireBlend={GetFireCapableAimBlend01():F2} corr={FormatAimCorrectionMode(pointAimW)} " +
			$"AimPitch={m_SmoothedPitch01:F2} rawPitch={m_DebugRawPitchDegrees:F1}° layerW={m_SmoothedLayerWeight:F2} " +
			$"yawErr={m_DebugWeaponYawErrorDegrees:F1}° appliedYaw={m_DebugWeaponYawAppliedDegrees:F1}° " +
			$"pitchErr={m_DebugWeaponPitchErrorDegrees:F1}° appliedPitch={m_DebugWeaponPitchAppliedDegrees:F1}° " +
			$"fromTo={m_SmoothedPointAimDegrees:F1}° barrelPitch={barrelPitch:F1}° barrelYawErr={barrelYawErr:F1}° " +
			$"composeΔ=({composedDeltaEuler.x:F1},{composedDeltaEuler.y:F1},{composedDeltaEuler.z:F1}) " +
			$"hasTarget={ (hasTarget ? 1 : 0)}",
			this);
	}

	private static string BuildReloadAimMixTag(
		bool _reloadBusy,
		bool _modelAim,
		bool _combatAim,
		bool _poseBlending,
		bool _reloadSettle,
		bool _moveHold)
	{
		string mix = _reloadBusy ? "reloadClip" : "";
		if (_combatAim)
			mix = string.IsNullOrEmpty(mix) ? "animAimPitch" : mix + "+animAimPitch";
		mix = string.IsNullOrEmpty(mix)
			? (_modelAim ? "modelCorr" : "authoredEase")
			: mix + (_modelAim ? "+modelCorr" : "+authoredEase");
		if (_reloadSettle)
			mix += "+reloadSettle";
		if (_moveHold)
			mix += "+moveHold";
		if (_poseBlending)
			mix += "+poseBlend";
		return mix;
	}

	private void TickReloadExitAimSettle()
	{
		bool busy = (m_ReloadController != null && m_ReloadController.IsReloadBusy)
		            || (m_UnitEquipment != null && m_UnitEquipment.IsWeaponHeldForBoltCycle);
		if (m_WasReloadPresentationBusy && !busy)
		{
			m_HoldWeaponModelAimUntil = Time.time + Mathf.Max(0.05f, m_ReloadExitAimSettleSeconds);
			ClearWeaponModelCorrectionVelocities();
		}

		m_WasReloadPresentationBusy = busy;
	}

	private bool IsHoldingWeaponModelAimAfterReload() =>
		Time.time < m_HoldWeaponModelAimUntil;

	private void ClearWeaponModelCorrectionVelocities()
	{
		m_WeaponYawVelocity = 0f;
	}

	private void ResetAimAnimatorParameters()
	{
		m_LastEquippedDefinition = null;
		m_BarrelTransform = null;
		m_SmoothedLayerWeight = 0f;
		m_SmoothedPitch01 = 0f;
		if (m_Animator != null)
		{
			m_Animator.SetFloat(s_AimPitch, 0f);
			SetAimLayerWeights(0f);
		}
		m_DebugCombatAimActive = false;
		m_DebugCurrentStance = 0;
		m_DebugAimPointWorld = Vector3.zero;
		m_DebugRawPitchDegrees = 0f;
		ResetWeaponModelCorrectionDebug();
		m_DebugSmoothedPitch01 = 0f;
		m_DebugAimLayerWeight = 0f;
		m_HasDesiredAim = false;
		m_DesiredAimYawDegrees = 0f;
		m_DesiredAimPitchDegrees = 0f;
		m_ResidualYawDegrees = 0f;
		m_ResidualPitchDegrees = 0f;
		m_AimQuality01 = 1f;
		m_AimSaturation = AimSaturation.None;
		PublishAimSolverDebug();
	}

	/// <summary>
	/// Мгновенно снять вес Aim-слоя (после гранатомёта/броска), чтобы не залипать в override-позе.
	/// </summary>
	public void SnapAimLayerWeightOff()
	{
		if (m_Animator != null && m_AimLayerIndex < 0)
			ResolveAimLayerIndices();

		m_SmoothedLayerWeight = 0f;
		m_SmoothedPitch01 = 0f;
		if (m_Animator != null)
			m_Animator.SetFloat(s_AimPitch, 0f);
		SetAimLayerWeights(0f);
		m_DebugAimLayerWeight = 0f;
	}

	private void ResolveAimLayerIndices()
	{
		if (m_Animator == null)
		{
			m_AimLayerIndex = -1;
			m_ObsoleteAimCrouchLayerIndex = -1;
			return;
		}

		m_AimLayerIndex = m_Animator.GetLayerIndex(c_AimLayerName);
		m_ObsoleteAimCrouchLayerIndex = m_Animator.GetLayerIndex(c_ObsoleteAimCrouchLayerName);
	}

	private void SetAimLayerWeights(float _weight)
	{
		if (m_Animator == null)
			return;

		if (m_AimLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_AimLayerIndex, _weight);
		if (m_ObsoleteAimCrouchLayerIndex >= 0)
			m_Animator.SetLayerWeight(m_ObsoleteAimCrouchLayerIndex, 0f);
	}

	private void ResolveBarrelTransform(Transform _weaponRoot)
	{
		EquippedWeapon w = m_UnitEquipment != null ? m_UnitEquipment.EquippedWeapon : null;
		if (w != null)
		{
			m_BarrelTransform = w.BarrelTransform != null ? w.BarrelTransform : _weaponRoot;
			return;
		}

		m_BarrelTransform = _weaponRoot;
	}

	private bool TrySyncWeaponDefinition(Transform _weaponRoot, ItemDefinition _def)
	{
		if (_def != m_LastEquippedDefinition)
		{
			m_LastEquippedDefinition = _def;
			ResolveBarrelTransform(_weaponRoot);
		}

		m_BaseWeaponLocalRotation = ResolveBaseWeaponLocalRotation(_def);
		return m_BarrelTransform != null;
	}

	private Quaternion ResolveBaseWeaponLocalRotation(ItemDefinition _def)
	{
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.CurrentBaseWeaponLocalRotation;

		return _def != null ? _def.RightHandLocalRotation : Quaternion.identity;
	}

	private void ApplyAnimatorAimParameters()
	{
		if (m_Animator != null && m_AimLayerIndex < 0)
			ResolveAimLayerIndices();

		bool logicalReady = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		float fireBlend = GetFireCapableAimBlend01();
		Transform target = m_TargetSelector != null ? m_TargetSelector.SelectedTarget : null;
		bool hasTarget = target != null;

		bool stanceBlocks = m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		                    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;

		bool reloadBlocks = m_BlockCombatAimDuringReload &&
		                    m_ReloadController != null &&
		                    m_ReloadController.IsReloadBusy;

		bool throwBlocks = m_GrenadeThrowController != null && m_GrenadeThrowController.IsThrowAnimPlaying;

		bool magazineLoadingBlocks = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;

		bool rocketLauncherNeedsAimLayer = ShouldHoldAimLayerForRocketLauncher();
		bool rocketLauncherTuningAimPose = IsRocketLauncherIkTuningActive();
		// Гранатомёт: подъём AimPitch к цели уже на фазе aim (не только fire).
		bool rocketLauncherCombatAim = rocketLauncherNeedsAimLayer &&
		                              !rocketLauncherTuningAimPose &&
		                              hasTarget &&
		                              m_AimAtVisibleTarget &&
		                              !stanceBlocks &&
		                              !throwBlocks &&
		                              !magazineLoadingBlocks;

		bool combatAim = (m_RequireReadyAndTarget && fireBlend > 0.001f && hasTarget && m_AimAtVisibleTarget && !stanceBlocks && !reloadBlocks && !throwBlocks && !magazineLoadingBlocks)
		                 || rocketLauncherCombatAim;
		bool keepAimPitchDuringReload = reloadBlocks &&
		                                fireBlend > 0.001f &&
		                                hasTarget &&
		                                m_AimAtVisibleTarget &&
		                                !throwBlocks &&
		                                !magazineLoadingBlocks;
		// Hold DesiredAimPitch / AimPitch float across StanceTransition. Layer weight may still drop.
		bool keepAimPitchDuringStance = stanceBlocks &&
		                                fireBlend > 0.001f &&
		                                hasTarget &&
		                                m_AimAtVisibleTarget &&
		                                !throwBlocks &&
		                                !magazineLoadingBlocks;
		bool aimPitchActive = combatAim || keepAimPitchDuringReload || keepAimPitchDuringStance;
		int currentStance = m_Animator != null ? m_Animator.GetInteger(s_Stance) : 0;

		bool canUseAimLayerForStance = currentStance == (int)LocomotionStance.Standing || currentStance == (int)LocomotionStance.Crouch;
		bool reloadNeedsAimLayerClips = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool throwNeedsAimLayerClips = m_GrenadeThrowController != null && m_GrenadeThrowController.IsThrowAnimPlaying;
		bool poseWantsAimPoint = PoseWantsAimPointOverlay(ResolveAimPointPose());
		if (m_AnimatorWeaponMode == null)
			m_AnimatorWeaponMode = GetComponent<UnitAnimatorWeaponMode>();
		// Layer weight may drop during StanceTransition. DesiredAimPitch / AimPitch float must not
		// (keepAimPitchDuringStance). Coupling them caused +0.18 → 0 → +0.18 after stand↔crouch.
		bool aimLayerHoldForCombat = m_RequireReadyAndTarget && hasTarget && m_AimAtVisibleTarget && poseWantsAimPoint
		                             && !stanceBlocks && !magazineLoadingBlocks && !throwBlocks;
		bool poseBlendDrivesAimLayer = m_EquippedWeaponPose != null && m_EquippedWeaponPose.IsPoseBlendAnimating;
		float targetLayer = 0f;
		if (canUseAimLayerForStance)
		{
			if (reloadNeedsAimLayerClips || throwNeedsAimLayerClips || rocketLauncherNeedsAimLayer)
				targetLayer = 1f;
			else if (aimLayerHoldForCombat)
				targetLayer = ResolveAimPointLayerWeight();
		}

		if (reloadNeedsAimLayerClips || throwNeedsAimLayerClips || rocketLauncherNeedsAimLayer)
		{
			// Клипы перезарядки/затвора/броска/гранатомёта на Aim_Point_U90-D90; при весе 0 animation events не приходят.
			m_SmoothedLayerWeight = 1f;
			SetAimLayerWeights(1f);

			// Not-ready reload / бросок / тюнер IK: нейтральный pitch.
			// Гранатомёт с целью — не обнулять: AimPitch поднимает трубу уже на aim.
			bool forceNeutralPitch = throwNeedsAimLayerClips ||
			                         rocketLauncherTuningAimPose ||
			                         (!logicalReady && !rocketLauncherCombatAim);
			if (forceNeutralPitch)
			{
				m_SmoothedPitch01 = 0f;
				m_Animator.SetFloat(s_AimPitch, 0f);
			}
		}
		else if (poseBlendDrivesAimLayer)
		{
			m_SmoothedLayerWeight = targetLayer;
			SetAimLayerWeights(m_SmoothedLayerWeight);
		}
		else
		{
			float wSmooth = Mathf.Max(0.0001f, m_LayerWeightSmoothSeconds);
			m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetLayer, Time.deltaTime / wSmooth);
			SetAimLayerWeights(m_SmoothedLayerWeight);
		}

		float targetPitch01 = 0f;
		float pitchSmoothUse = m_PitchSmoothTime;
		if (m_SofterAimPitchWhileFiring && combatAim && IsFiringForSteadyAim())
			pitchSmoothUse = Mathf.Max(pitchSmoothUse, m_PitchSmoothTimeWhileFiring);

		if (aimPitchActive && TryResolveAimPitchOrigin(out Vector3 pitchOrigin))
		{
			if (fireBlend < 0.08f)
				m_SmoothedPitch01 = ReadBarrelPitch01();

			Vector3 aimPoint = GetTargetAimPointWorld(target);
			m_DebugAimPointWorld = aimPoint;
			TickDesiredAim(aimPoint, pitchOrigin, combatAim && IsFiringForSteadyAim());
			float walkComp = ResolveWalkPitchCompensationDegrees();
			m_DebugWalkPitchCompensationDegrees = walkComp;
			targetPitch01 = Mathf.Clamp(
				(m_DesiredAimPitchDegrees + walkComp) / c_PitchDegreesMax, -1f, 1f);
		}
		else
		{
			m_DebugWalkPitchCompensationDegrees = 0f;
			TickDesiredAimIdle(pitchSmoothUse);
		}

		if (pitchSmoothUse <= 0.0001f)
			m_SmoothedPitch01 = targetPitch01;
		else
			m_SmoothedPitch01 = SmoothExp(m_SmoothedPitch01, targetPitch01, pitchSmoothUse);

		m_Animator.SetFloat(s_AimPitch, m_SmoothedPitch01);

		m_DebugCombatAimActive = combatAim;
		m_DebugCurrentStance = currentStance;
		m_DebugSmoothedPitch01 = m_SmoothedPitch01;
		m_DebugAimLayerWeight = m_SmoothedLayerWeight;

		if (m_EquippedWeaponPose != null && m_EquippedWeaponPose.ShouldLogHighReadyToPreAim)
		{
			bool weaponReady = m_Animator != null && m_Animator.GetBool(UnitAnimatorWeaponMode.ParamWeaponReady);
			float barrelPitch = 0f;
			if (m_BarrelTransform != null)
			{
				Vector3 f = m_BarrelTransform.forward;
				float horiz = Mathf.Sqrt(f.x * f.x + f.z * f.z);
				barrelPitch = Mathf.Atan2(f.y, horiz) * Mathf.Rad2Deg;
			}

			Debug.Log(
				$"[HR→PreAim AIM] unit={name} fireBlend={fireBlend:F3} combatAim={combatAim} " +
				$"logicalReady={logicalReady} layerW={m_SmoothedLayerWeight:F3} targetLayer={targetLayer:F3} " +
				$"poseDrivesLayer={poseBlendDrivesAimLayer} aimPitch01={m_SmoothedPitch01:F3} " +
				$"rawPitchDeg={m_DebugRawPitchDegrees:F1} barrelPitch={barrelPitch:F1}° " +
				$"WeaponReady={weaponReady} corrYaw={m_DebugWeaponYawAppliedDegrees:F1} " +
				$"corrPitch={m_DebugWeaponPitchAppliedDegrees:F1} hasTarget={hasTarget} " +
				$"modelAim={ShouldApplyWeaponLocalOnlyForAim()} aimGate={m_ModelAimGate01:F3} " +
				$"pose={m_EquippedWeaponPose.CurrentPose}→{m_EquippedWeaponPose.TargetPose} " +
				$"t={m_EquippedWeaponPose.PoseBlend01:F3}",
				this);
		}
	}

	private float ReadBarrelPitch01()
	{
		if (m_BarrelTransform == null)
			return 0f;

		Vector3 dir = m_BarrelTransform.forward;
		float horiz = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
		if (horiz < 1e-6f && Mathf.Abs(dir.y) < 1e-6f)
			return 0f;

		float pitchDeg = Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
		pitchDeg = Mathf.Clamp(pitchDeg, -c_PitchDegreesMax, c_PitchDegreesMax);
		return pitchDeg / c_PitchDegreesMax;
	}

	private Vector3 GetTargetAimPointWorld(Transform _targetRoot)
	{
		if (m_TargetSelector != null &&
		    _targetRoot != null &&
		    _targetRoot == m_TargetSelector.SelectedTarget &&
		    m_TargetSelector.HasSelectedAimPoint)
			return m_TargetSelector.GetEngageableAimPointWorld();

		if (_targetRoot != null && _targetRoot.TryGetComponent(out UnitVision uv))
		{
			if (UnitBodyHitZoneVisionUtility.TryGetCombinedBounds(uv.BodyHitZones, out Bounds combined))
				return combined.center;

			if (uv.BodyCollider != null)
				return uv.BodyCollider.bounds.center;
		}

		return _targetRoot != null ? _targetRoot.position + Vector3.up * 1.2f : Vector3.zero;
	}

	private bool IsFiringForSteadyAim()
	{
		return m_FireController != null && m_FireController.IsFiringCommandActive;
	}

	/// <summary>
	/// После lean: FromTo оставшейся ошибки ствол→цель в пространстве руки (уже с roll).
	/// Считаем от текущего ствола и доворачиваем текущий local, иначе лимит от authored-позы
	/// оставляет 10°+ навсегда, а world yaw/pitch выпрямляет винтовку против наклона.
	/// </summary>
	private void ApplyLeanParentSpaceFromToCorrection(Transform _weaponRoot, Vector3 _aimPointWorld)
	{
		if (!m_EnableWeaponModelAimCorrection || _weaponRoot == null || _weaponRoot.parent == null || m_BarrelTransform == null)
			return;
		if (!AllowsWeaponLocalAimCorrection())
			return;

		int leanSign = 0;
		if (m_SpineLean != null)
			leanSign = m_SpineLean.CurrentLean01 < -0.05f ? -1 : (m_SpineLean.CurrentLean01 > 0.05f ? 1 : 0);
		if (leanSign != m_LastLeanAimSign)
		{
			m_LastLeanAimSign = leanSign;
			m_SmoothedWeaponYawDegrees = 0f;
			m_SmoothedWeaponPitchDegrees = 0f;
			m_SmoothedPointAimDegrees = 0f;
		}

		Transform parent = _weaponRoot.parent;
		if (ShouldHoldWeaponModelAim(parent) && m_HasLastLeanAimLocal)
		{
			SubmitWeaponLocalAimRotation(_weaponRoot, m_LastLeanAimLocal);
			return;
		}

		Vector3 origin = m_BarrelTransform.position;
		Vector3 barrelWorld = m_BarrelTransform.forward;
		Vector3 desiredWorld = _aimPointWorld - origin;
		if (desiredWorld.sqrMagnitude < 1e-6f || barrelWorld.sqrMagnitude < 1e-6f)
			return;

		Vector3 barrelParent = parent.InverseTransformDirection(barrelWorld.normalized);
		Vector3 desiredParent = parent.InverseTransformDirection(desiredWorld.normalized);
		if (barrelParent.sqrMagnitude < 1e-8f || desiredParent.sqrMagnitude < 1e-8f)
			return;
		barrelParent.Normalize();
		desiredParent.Normalize();

		Quaternion remainingQ = Quaternion.FromToRotation(barrelParent, desiredParent);
		float remainingDeg = Quaternion.Angle(Quaternion.identity, remainingQ);
		float maxDeg = Mathf.Max(1f, m_LeanAimYawLimitDegrees);
		if (remainingDeg > maxDeg)
			remainingQ = Quaternion.Slerp(Quaternion.identity, remainingQ, maxDeg / remainingDeg);

		Quaternion currentLocal = _weaponRoot.localRotation;
		Quaternion finalLocal = remainingDeg < 0.12f ? currentLocal : remainingQ * currentLocal;

		m_LastLeanAimLocal = finalLocal;
		m_HasLastLeanAimLocal = true;

		SubmitWeaponLocalAimRotation(_weaponRoot, finalLocal);

		m_DebugWeaponYawErrorDegrees = remainingDeg;
		m_DebugWeaponPitchErrorDegrees = Vector3.Angle(barrelWorld, desiredWorld.normalized);
		m_DebugWeaponYawAppliedDegrees = 0f;
		m_DebugWeaponPitchAppliedDegrees = remainingDeg < 0.12f ? 0f : Mathf.Min(remainingDeg, maxDeg);
	}

	private void ApplyWeaponModelAimCorrection(
		Transform _weaponRoot,
		Vector3 _aimPointWorld,
		bool _useFiringStability,
		Quaternion _baseLocalRotation,
		float _yawLimitOverride = -1f,
		float _pitchUpOverride = -1f,
		float _pitchDownOverride = -1f,
		float _smoothTimeOverride = -1f,
		float _alignStrength = 1f,
		bool _measureFromBasePose = false)
	{
		if (!m_EnableWeaponModelAimCorrection || _weaponRoot == null || _weaponRoot.parent == null)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		if (!AllowsWeaponLocalAimCorrection() || GetPointAimCorrectionWeight() <= 0.001f)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		float yawLimit = _yawLimitOverride >= 0f ? _yawLimitOverride : m_WeaponModelYawLimitDegrees;
		float pitchUpLimit = _pitchUpOverride >= 0f ? _pitchUpOverride : m_WeaponModelPitchUpLimitDegrees;
		float pitchDownLimit = _pitchDownOverride >= 0f ? _pitchDownOverride : m_WeaponModelPitchDownLimitDegrees;
		float alignStrength = Mathf.Clamp01(_alignStrength);

		Transform parent = _weaponRoot.parent;
		Quaternion savedLocal = _weaponRoot.localRotation;
		// Measure barrel from BASE only; restore immediately so this is not a FINAL write.
		if (_measureFromBasePose)
			_weaponRoot.localRotation = _baseLocalRotation;

		Vector3 aimOrigin = m_BarrelTransform.position;
		Vector3 barrelWorld = m_BarrelTransform.forward.normalized;

		if (_measureFromBasePose)
			_weaponRoot.localRotation = savedLocal;

		Vector3 desiredWorldDir = _aimPointWorld - aimOrigin;
		if (desiredWorldDir.sqrMagnitude < 1e-6f)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		desiredWorldDir.Normalize();
		Vector3 worldUpParent = ToParentAxis(parent, Vector3.up);
		Vector3 desiredDirParent = parent.InverseTransformDirection(desiredWorldDir);
		Vector3 currentForwardParent = parent.InverseTransformDirection(barrelWorld);

		float rawYawError = SignedAngleOnPlane(currentForwardParent, desiredDirParent, worldUpParent);
		float targetYaw = Mathf.Clamp(rawYawError * alignStrength, -yawLimit, yawLimit);

		Quaternion yawRotation = Quaternion.AngleAxis(targetYaw, worldUpParent);
		Vector3 yawedForwardParent = yawRotation * currentForwardParent;
		Vector3 pitchAxisParent = ResolveWorldHorizontalPitchAxisParent(parent, parent.TransformDirection(yawedForwardParent));
		float rawPitchError = SignedAngleOnPlane(yawedForwardParent, desiredDirParent, pitchAxisParent);
		float targetPitch = Mathf.Clamp(rawPitchError * alignStrength, -pitchDownLimit, pitchUpLimit);

		float maxFromTo = Mathf.Sqrt(
			yawLimit * yawLimit
			+ Mathf.Max(pitchUpLimit, pitchDownLimit) * Mathf.Max(pitchUpLimit, pitchDownLimit));
		float rawFromTo = Vector3.Angle(barrelWorld, desiredWorldDir);
		Vector3 fromToAxisWorld = Vector3.Cross(barrelWorld, desiredWorldDir);
		if (fromToAxisWorld.sqrMagnitude > 1e-10f)
			m_PointAimAxisWorld = fromToAxisWorld.normalized;
		else if (m_PointAimAxisWorld.sqrMagnitude < 1e-8f)
			m_PointAimAxisWorld = Vector3.up;

		float targetFromTo = Mathf.Min(rawFromTo * alignStrength, maxFromTo);

		float baselineSmooth = _smoothTimeOverride >= 0f
			? _smoothTimeOverride
			: m_WeaponModelCorrectionSmoothTime;
		if (_smoothTimeOverride < 0f && m_SofterWeaponModelCorrectionWhileFiring && _useFiringStability)
			baselineSmooth = Mathf.Max(baselineSmooth, m_WeaponModelCorrectionSmoothTimeWhileFiring);
		float smoothTime = Mathf.Max(0.0001f, baselineSmooth);
		if (baselineSmooth <= 0.0001f)
		{
			m_SmoothedWeaponYawDegrees = targetYaw;
			m_SmoothedWeaponPitchDegrees = targetPitch;
			m_SmoothedPointAimDegrees = targetFromTo;
			ClearWeaponModelCorrectionVelocities();
		}
		else
		{
			m_SmoothedWeaponYawDegrees = SmoothExpAngle(m_SmoothedWeaponYawDegrees, targetYaw, smoothTime);
			m_SmoothedWeaponPitchDegrees = SmoothExpAngle(m_SmoothedWeaponPitchDegrees, targetPitch, smoothTime);
			m_SmoothedPointAimDegrees = SmoothExp(m_SmoothedPointAimDegrees, targetFromTo, smoothTime);
			ClearWeaponModelCorrectionVelocities();
		}

		Quaternion appliedYawRotation = Quaternion.AngleAxis(m_SmoothedWeaponYawDegrees, worldUpParent);
		Vector3 appliedPitchAxisParent = ResolveWorldHorizontalPitchAxisParent(
			parent,
			parent.TransformDirection(appliedYawRotation * currentForwardParent));
		Quaternion uprightCorrection =
			Quaternion.AngleAxis(m_SmoothedWeaponPitchDegrees, appliedPitchAxisParent) * appliedYawRotation;

		Vector3 pointAimAxisParent = ToParentAxis(parent, m_PointAimAxisWorld);
		Quaternion pointAimCorrection = Mathf.Abs(m_SmoothedPointAimDegrees) > 0.0001f
			? Quaternion.AngleAxis(m_SmoothedPointAimDegrees, pointAimAxisParent)
			: Quaternion.identity;

		float pointAimWeight = GetPointAimCorrectionWeight();
		if (pointAimWeight >= 0.999f)
		{
			m_SmoothedWeaponYawDegrees = 0f;
			m_SmoothedWeaponPitchDegrees = 0f;
		}

		Quaternion localCorrection = Quaternion.Slerp(uprightCorrection, pointAimCorrection, pointAimWeight);
		Quaternion finalLocal = localCorrection * _baseLocalRotation;
		SubmitWeaponLocalAimRotation(_weaponRoot, finalLocal);

		m_DebugWeaponYawErrorDegrees = rawYawError;
		m_DebugWeaponPitchErrorDegrees = pointAimWeight > 0.5f ? rawFromTo : rawPitchError;
		m_DebugWeaponYawAppliedDegrees = Mathf.Lerp(m_SmoothedWeaponYawDegrees, 0f, pointAimWeight);
		m_DebugWeaponPitchAppliedDegrees = Mathf.Lerp(m_SmoothedWeaponPitchDegrees, m_SmoothedPointAimDegrees, pointAimWeight);
	}

	private static Vector3 ToParentAxis(Transform _parent, Vector3 _worldAxis)
	{
		Vector3 parentAxis = _parent.InverseTransformDirection(_worldAxis);
		return parentAxis.sqrMagnitude < 1e-8f ? Vector3.up : parentAxis.normalized;
	}

	private static Vector3 ResolveWorldHorizontalPitchAxisParent(Transform _parent, Vector3 _yawedBarrelWorld)
	{
		Vector3 horiz = ProjectOnHorizontalPlane(_yawedBarrelWorld);
		Vector3 pitchAxisWorld = Vector3.Cross(Vector3.up, horiz);
		if (pitchAxisWorld.sqrMagnitude < 1e-8f)
			pitchAxisWorld = Vector3.Cross(Vector3.up, Vector3.forward);
		if (pitchAxisWorld.sqrMagnitude < 1e-8f)
			pitchAxisWorld = Vector3.right;

		return ToParentAxis(_parent, pitchAxisWorld);
	}

	private void ResetWeaponModelCorrectionDebug()
	{
		m_SmoothedWeaponYawDegrees = 0f;
		m_SmoothedWeaponPitchDegrees = 0f;
		m_WeaponYawVelocity = 0f;
		m_SmoothedPointAimDegrees = 0f;
		m_PointAimAxisWorld = Vector3.up;
		m_DebugWeaponYawErrorDegrees = 0f;
		m_DebugWeaponPitchErrorDegrees = 0f;
		m_DebugWeaponYawAppliedDegrees = 0f;
		m_DebugWeaponPitchAppliedDegrees = 0f;
	}

	private static float SignedAngleOnPlane(Vector3 _from, Vector3 _to, Vector3 _planeNormal)
	{
		Vector3 fromProjected = Vector3.ProjectOnPlane(_from, _planeNormal);
		Vector3 toProjected = Vector3.ProjectOnPlane(_to, _planeNormal);
		if (fromProjected.sqrMagnitude < 1e-6f || toProjected.sqrMagnitude < 1e-6f)
			return 0f;

		fromProjected.Normalize();
		toProjected.Normalize();
		return Vector3.SignedAngle(fromProjected, toProjected, _planeNormal);
	}

	private static float SmoothExp(float _current, float _target, float _smoothTime)
	{
		if (_smoothTime <= 0.0001f)
			return _target;
		float t = 1f - Mathf.Exp(-Time.deltaTime / _smoothTime);
		return Mathf.Lerp(_current, _target, t);
	}

	private static float SmoothExpAngle(float _current, float _target, float _smoothTime)
	{
		if (_smoothTime <= 0.0001f)
			return _target;
		float t = 1f - Mathf.Exp(-Time.deltaTime / _smoothTime);
		return Mathf.LerpAngle(_current, _target, t);
	}
	#endregion
}
