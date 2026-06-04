using UnityEngine;

/// <summary>
/// Вертикальное наведение: параметр <c>AimPitch</c> и слой <c>Aim_Point_U90-D90</c>.
/// Горизонталь — корень юнита (<see cref="UnitClickToMove"/>). При «готов» и видимой цели корень оружия только локальный из <see cref="ItemDefinition"/>, вертикаль даёт анимация.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(55)]
public sealed class UnitWeaponAiming : MonoBehaviour
{
	#region Constants
	private const string c_ParamAimPitch = "AimPitch";
	private const string c_AimLayerName = "Aim_Point_U90-D90";
	private const string c_ObsoleteAimCrouchLayerName = "Crouch_Aim_Point_U90-D90";
	private const float c_PitchDegreesMax = 90f;
	private static readonly int s_Stance = Animator.StringToHash(UnitAnimatorWeaponMode.ParamStance);
	#endregion

	#region Serialized Fields
	[SerializeField] private UnitEquipment m_UnitEquipment;
	[SerializeField] private UnitVision m_Vision;
	[Tooltip("Forward — направление юнита (корень, бёдра).")]
	[SerializeField] private Transform m_UnitForwardSource;

	[SerializeField] private Animator m_Animator;
	[SerializeField] private UnitWeaponReadyHandsLayer m_ReadyHands;
	[SerializeField] private UnitBusyState m_BusyState;
	[SerializeField] private UnitWeaponReloadController m_ReloadController;
	[SerializeField] private UnitMagazineLoadingController m_MagazineLoadingController;
	[SerializeField] private UnitWeaponFireController m_FireController;

	[Header("Условия прицела")]
	[Tooltip("Только при «готов» и видимой цели; иначе AimPitch и слой в ноль.")]
	[SerializeField] private bool m_RequireReadyAndTarget = true;
	[Tooltip("Учитывать видимую цель из UnitVision для боевого прицела.")]
	[SerializeField] private bool m_AimAtVisibleTarget = true;

	[Header("Вертикаль (Animator)")]
	[SerializeField, Min(0f)] private float m_PitchSmoothTime = 0.08f;
	[Tooltip("При активной команде огня увеличить сглаживание AimPitch (меньше дёрганья от коллизий/анимации цели).")]
	[SerializeField] private bool m_SofterAimPitchWhileFiring = true;
	[SerializeField, Min(0f)] private float m_PitchSmoothTimeWhileFiring = 0.2f;
	[SerializeField, Min(0f)] private float m_LayerWeightSmoothSeconds = 0.08f;

	[Tooltip("Не наводить по вертикали во время смены стойки (UnitBusyState + StanceTransition).")]
	[SerializeField] private bool m_BlockAimDuringStanceTransition = true;
	[Tooltip("Не вести оружие на цель (AimPitch, локальная коррекция модели) во время перезарядки и передёргивания затвора. Вес слоя Aim_Point_U90-D90 при этом не обнуляется — нужно для клипов перезарядки/затвора на этом слое.")]
	[SerializeField] private bool m_BlockCombatAimDuringReload = true;

	[Header("Коррекция модели оружия")]
	[Tooltip("Если включено, после Animator-aim модель оружия локально доворачивается к центру цели. В пределах лимитов линия Barrel -> цель будет точной.")]
	[SerializeField] private bool m_EnableWeaponModelAimCorrection = true;
	[Tooltip("Максимальный локальный дововорот модели оружия по горизонту (yaw), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelYawLimitDegrees = 20f;
	[Tooltip("Максимальный локальный подъём модели оружия вверх (pitch up), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelPitchUpLimitDegrees = 18f;
	[Tooltip("Максимальный локальный увод модели оружия вниз (pitch down), в градусах.")]
	[SerializeField, Min(0f)] private float m_WeaponModelPitchDownLimitDegrees = 10f;
	[Tooltip("Сглаживание локальной коррекции модели оружия. Больше — мягче, меньше — точнее и быстрее.")]
	[SerializeField, Min(0f)] private float m_WeaponModelCorrectionSmoothTime = 0.04f;
	[Tooltip("При стрельбе — не ниже этого smooth time для коррекции модели (гасит мелкие колебания).")]
	[SerializeField] private bool m_SofterWeaponModelCorrectionWhileFiring = true;
	[SerializeField, Min(0f)] private float m_WeaponModelCorrectionSmoothTimeWhileFiring = 0.12f;

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
	[Tooltip("Итоговое сглаженное значение AimPitch, которое уходит в Animator.")]
	[SerializeField] private float m_DebugSmoothedPitch01;
	[Tooltip("Текущий вес активного слоя прицела (стоя или присед).")]
	[SerializeField, Range(0f, 1f)] private float m_DebugAimLayerWeight;

	[Header("Отладка лучей")]
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
	private float m_PitchVelocity;
	private float m_SmoothedLayerWeight;
	private float m_SmoothedWeaponYawDegrees;
	private float m_SmoothedWeaponPitchDegrees;
	private float m_WeaponYawVelocity;
	private float m_WeaponPitchVelocity;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_UnitEquipment == null)
			m_UnitEquipment = GetComponent<UnitEquipment>();
		if (m_Vision == null)
			m_Vision = GetComponent<UnitVision>();
		if (m_UnitForwardSource == null)
			m_UnitForwardSource = transform;
		if (m_Animator == null)
			m_Animator = GetComponentInChildren<Animator>();
		if (m_ReadyHands == null)
			m_ReadyHands = GetComponent<UnitWeaponReadyHandsLayer>();
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
		m_PitchVelocity = 0f;
		m_SmoothedLayerWeight = 0f;
		m_SmoothedWeaponYawDegrees = 0f;
		m_SmoothedWeaponPitchDegrees = 0f;
		m_WeaponYawVelocity = 0f;
		m_WeaponPitchVelocity = 0f;
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
		if (m_UnitEquipment == null || m_Animator == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
		{
			ResetAimAnimatorParameters();
			return;
		}

		if (!TrySyncWeaponDefinition(weaponRoot, def))
			return;

		ApplyAnimatorAimParameters();
	}

	private void LateUpdate()
	{
		if (m_UnitEquipment == null || m_UnitForwardSource == null)
			return;

		Transform weaponRoot = m_UnitEquipment.MainWeaponRoot;
		ItemDefinition def = m_UnitEquipment.EquippedDefinition;
		if (weaponRoot == null || def == null)
			return;

		if (!TrySyncWeaponDefinition(weaponRoot, def) || m_BarrelTransform == null)
			return;

		Quaternion baseForAim = m_BaseWeaponLocalRotation;
		weaponRoot.localRotation = baseForAim;
		if (ShouldApplyWeaponLocalOnlyForAim())
		{
			Vector3 aimPoint = GetTargetAimPointWorld(m_Vision != null ? m_Vision.VisibleTarget : null);
			ApplyWeaponModelAimCorrection(weaponRoot, aimPoint, IsFiringForSteadyAim(), baseForAim);
		}
		else
		{
			ResetWeaponModelCorrectionDebug();
		}

		if (m_DrawBarrelForwardRay)
			Debug.DrawRay(m_BarrelTransform.position, m_BarrelTransform.forward * m_BarrelForwardRayLength, m_BarrelForwardRayColor);
	}
	#endregion

	#region Private Methods
	private bool ShouldApplyWeaponLocalOnlyForAim()
	{
		if (!m_RequireReadyAndTarget)
			return false;

		bool ready = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		bool hasTarget = m_Vision != null && m_Vision.VisibleTarget != null;
		if (!ready || !hasTarget || !m_AimAtVisibleTarget)
			return false;

		if (m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0)
			return false;

		if (m_BlockCombatAimDuringReload &&
			m_ReloadController != null &&
			m_ReloadController.IsReloadBusy)
			return false;

		return true;
	}

	private void ResetAimAnimatorParameters()
	{
		m_LastEquippedDefinition = null;
		m_BarrelTransform = null;
		m_SmoothedLayerWeight = 0f;
		m_SmoothedPitch01 = 0f;
		m_PitchVelocity = 0f;
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
			m_BaseWeaponLocalRotation = _def.RightHandLocalRotation;
			ResolveBarrelTransform(_weaponRoot);
			_weaponRoot.localRotation = m_BaseWeaponLocalRotation;
		}

		return m_BarrelTransform != null;
	}

	private void ApplyAnimatorAimParameters()
	{
		if (m_Animator != null && m_AimLayerIndex < 0)
			ResolveAimLayerIndices();

		bool ready = m_ReadyHands != null && m_ReadyHands.IsWeaponEquippedAndReady();
		Transform target = m_Vision != null ? m_Vision.VisibleTarget : null;
		bool hasTarget = target != null;

		bool stanceBlocks = m_BlockAimDuringStanceTransition && m_BusyState != null && m_BusyState.IsBusy &&
		                    (m_BusyState.Reasons & UnitBusyState.BusyReason.StanceTransition) != 0;

		bool reloadBlocks = m_BlockCombatAimDuringReload &&
		                    m_ReloadController != null &&
		                    m_ReloadController.IsReloadBusy;

		bool magazineLoadingBlocks = m_MagazineLoadingController != null && m_MagazineLoadingController.IsLoadingMagazine;

		bool combatAim = m_RequireReadyAndTarget && ready && hasTarget && m_AimAtVisibleTarget && !stanceBlocks && !reloadBlocks && !magazineLoadingBlocks;
		int currentStance = m_Animator != null ? m_Animator.GetInteger(s_Stance) : 0;

		bool canUseAimLayerForStance = currentStance == (int)LocomotionStance.Standing || currentStance == (int)LocomotionStance.Crouch;
		bool reloadNeedsAimLayerClips = m_ReloadController != null && m_ReloadController.IsReloadBusy;
		bool aimLayerHoldForCombat = m_RequireReadyAndTarget && ready && hasTarget && m_AimAtVisibleTarget && !stanceBlocks && !magazineLoadingBlocks;
		float targetLayer = canUseAimLayerForStance && (reloadNeedsAimLayerClips || aimLayerHoldForCombat) ? 1f : 0f;

		if (reloadNeedsAimLayerClips)
		{
			// Клипы перезарядки/затвора на Aim_Point_U90-D90; при весе 0 animation events не приходят.
			m_SmoothedLayerWeight = 1f;
			SetAimLayerWeights(1f);

			// Not-ready reload: pitch-blend не должен влиять даже на доли кадра до Play(relaxed idle).
			if (!ready)
			{
				m_SmoothedPitch01 = 0f;
				m_PitchVelocity = 0f;
				m_Animator.SetFloat(s_AimPitch, 0f);
			}
		}
		else
		{
			float wSmooth = Mathf.Max(0.0001f, m_LayerWeightSmoothSeconds);
			m_SmoothedLayerWeight = Mathf.MoveTowards(m_SmoothedLayerWeight, targetLayer, Time.deltaTime / wSmooth);
			SetAimLayerWeights(m_SmoothedLayerWeight);
		}

		float targetPitch01 = 0f;
		if (combatAim && m_BarrelTransform != null)
		{
			Vector3 aimPoint = GetTargetAimPointWorld(target);
			m_DebugAimPointWorld = aimPoint;
			Vector3 dir = aimPoint - m_BarrelTransform.position;
			if (dir.sqrMagnitude > 1e-6f)
			{
				dir.Normalize();
				float horiz = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
				float pitchDeg = Mathf.Atan2(dir.y, horiz) * Mathf.Rad2Deg;
				m_DebugRawPitchDegrees = pitchDeg;
				pitchDeg = Mathf.Clamp(pitchDeg, -c_PitchDegreesMax, c_PitchDegreesMax);
				targetPitch01 = pitchDeg / c_PitchDegreesMax;
			}
		}
		else
		{
			m_DebugAimPointWorld = Vector3.zero;
			m_DebugRawPitchDegrees = 0f;
		}

		float pitchSmoothUse = m_PitchSmoothTime;
		if (m_SofterAimPitchWhileFiring && combatAim && IsFiringForSteadyAim())
			pitchSmoothUse = Mathf.Max(pitchSmoothUse, m_PitchSmoothTimeWhileFiring);

		if (pitchSmoothUse <= 0.0001f)
		{
			m_SmoothedPitch01 = targetPitch01;
			m_PitchVelocity = 0f;
		}
		else
		{
			m_SmoothedPitch01 = Mathf.SmoothDamp(m_SmoothedPitch01, targetPitch01, ref m_PitchVelocity, pitchSmoothUse,
				Mathf.Infinity, Time.deltaTime);
		}

		m_Animator.SetFloat(s_AimPitch, m_SmoothedPitch01);

		m_DebugCombatAimActive = combatAim;
		m_DebugCurrentStance = currentStance;
		m_DebugSmoothedPitch01 = m_SmoothedPitch01;
		m_DebugAimLayerWeight = m_SmoothedLayerWeight;
	}

	private Vector3 GetTargetAimPointWorld(Transform _targetRoot)
	{
		if (_targetRoot != null && _targetRoot.TryGetComponent(out UnitVision uv) && uv.BodyCollider != null)
			return uv.BodyCollider.bounds.center;

		return _targetRoot != null ? _targetRoot.position + Vector3.up * 1.2f : Vector3.zero;
	}

	private bool IsFiringForSteadyAim()
	{
		return m_FireController != null && m_FireController.IsFiringCommandActive;
	}

	private void ApplyWeaponModelAimCorrection(Transform _weaponRoot, Vector3 _aimPointWorld, bool _useFiringStability, Quaternion _baseLocalRotation)
	{
		if (!m_EnableWeaponModelAimCorrection || _weaponRoot == null || _weaponRoot.parent == null)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		Vector3 desiredWorldDir = _aimPointWorld - m_BarrelTransform.position;
		if (desiredWorldDir.sqrMagnitude < 1e-6f)
		{
			ResetWeaponModelCorrectionDebug();
			return;
		}

		Transform parent = _weaponRoot.parent;
		Vector3 desiredDirParent = parent.InverseTransformDirection(desiredWorldDir.normalized);
		Vector3 currentForwardParent = parent.InverseTransformDirection(m_BarrelTransform.forward).normalized;
		Vector3 currentRightParent = parent.InverseTransformDirection(m_BarrelTransform.right).normalized;

		float rawYawError = SignedAngleOnPlane(currentForwardParent, desiredDirParent, Vector3.up);
		float targetYaw = Mathf.Clamp(rawYawError, -m_WeaponModelYawLimitDegrees, m_WeaponModelYawLimitDegrees);

		Quaternion yawRotation = Quaternion.AngleAxis(targetYaw, Vector3.up);
		Vector3 yawedForwardParent = yawRotation * currentForwardParent;
		Vector3 yawedRightParent = (yawRotation * currentRightParent).normalized;
		float rawPitchError = SignedAngleOnPlane(yawedForwardParent, desiredDirParent, yawedRightParent);
		float targetPitch = Mathf.Clamp(rawPitchError, -m_WeaponModelPitchDownLimitDegrees, m_WeaponModelPitchUpLimitDegrees);

		float baselineSmooth = m_WeaponModelCorrectionSmoothTime;
		if (m_SofterWeaponModelCorrectionWhileFiring && _useFiringStability)
			baselineSmooth = Mathf.Max(baselineSmooth, m_WeaponModelCorrectionSmoothTimeWhileFiring);
		float smoothTime = Mathf.Max(0.0001f, baselineSmooth);
		if (m_WeaponModelCorrectionSmoothTime <= 0.0001f)
		{
			m_SmoothedWeaponYawDegrees = targetYaw;
			m_SmoothedWeaponPitchDegrees = targetPitch;
			m_WeaponYawVelocity = 0f;
			m_WeaponPitchVelocity = 0f;
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

			m_SmoothedWeaponPitchDegrees = Mathf.SmoothDampAngle(
				m_SmoothedWeaponPitchDegrees,
				targetPitch,
				ref m_WeaponPitchVelocity,
				smoothTime,
				Mathf.Infinity,
				Time.deltaTime);
		}

		Quaternion appliedYawRotation = Quaternion.AngleAxis(m_SmoothedWeaponYawDegrees, Vector3.up);
		Vector3 appliedPitchAxis = (appliedYawRotation * currentRightParent).normalized;
		Quaternion appliedPitchRotation = Quaternion.AngleAxis(m_SmoothedWeaponPitchDegrees, appliedPitchAxis);
		Quaternion localCorrection = appliedPitchRotation * appliedYawRotation;
		_weaponRoot.localRotation = localCorrection * _baseLocalRotation;

		m_DebugWeaponYawErrorDegrees = rawYawError;
		m_DebugWeaponPitchErrorDegrees = rawPitchError;
		m_DebugWeaponYawAppliedDegrees = m_SmoothedWeaponYawDegrees;
		m_DebugWeaponPitchAppliedDegrees = m_SmoothedWeaponPitchDegrees;
	}

	private void ResetWeaponModelCorrectionDebug()
	{
		m_SmoothedWeaponYawDegrees = 0f;
		m_SmoothedWeaponPitchDegrees = 0f;
		m_WeaponYawVelocity = 0f;
		m_WeaponPitchVelocity = 0f;
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
	#endregion
}
