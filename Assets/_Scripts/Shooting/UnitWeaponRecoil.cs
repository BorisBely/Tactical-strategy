using UnityEngine;

/// <summary>
/// Computes visual recoil state only. Does not author weapon BASE pose and does not write bones.
///
/// Roles of the visual channels:
///   Back  = primary translation
///   Up    = secondary translation
///   Pitch = secondary rotation
///   Climb = PitchCurve(RecoilPenalty) — sustained secondary rotation (rotation only, no translation)
///   Yaw   = small variation
///
/// Punch — per-shot value: one shared visual impulse (normalized visual recoil strength,
/// not a physical impulse) decomposed into pitch / back / up.
///
/// <see cref="WeaponVisualRecoilApplicator"/> applies the state to Hand_R after animation, before left IK.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class UnitWeaponRecoil : MonoBehaviour
{
	#region Serialized Fields
	[Tooltip("Снаряжение: корень оружия в руке.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Геймплейный контроллер отдачи — источник RecoilPenalty.")]
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[Tooltip("Контроллер стрельбы — источник ShotFired.")]
	[SerializeField] private UnitWeaponFireController m_FireController;
	[Tooltip("Runtime оружия — чтение VisualRecoilKickScale.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Редко: явная точка выборки дистанции до камеры.")]
	[SerializeField] private Transform m_KickTransformOverride;
	[Tooltip("Базовая поза оружия (relaxed↔ready).")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Climb — устойчивый подъём от накопленной отдачи")]
	[Tooltip("Кривая: ось X = RecoilPenalty (абсолютный), ось Y = визуальный pitch (градусы). Первые выстрелы почти не поднимают ствол — очередь уводит выше постепенно.")]
	[SerializeField] private AnimationCurve m_PitchCurve = new AnimationCurve(
		new Keyframe(0f, 0f),
		new Keyframe(15f, 0.6f),
		new Keyframe(30f, 1.7f),
		new Keyframe(60f, 4.5f));
	[Tooltip("Множитель climb-подъёма (не влияет на punch).")]
	[SerializeField, Min(0f)] private float m_VisualOffsetScale = 1f;

	[Header("Punch — удар выстрела (единый impulse → pitch/back/up)")]
	[Tooltip("Градусы pitch на единицу визуальной силы отдачи. Impulse = RecoilAddedPerShot x VisualRecoilKickScale — нормализованная визуальная сила, не физический импульс.")]
	[SerializeField, Min(0f)] private float m_ShotPitch = 2.5f;
	[Tooltip("Доля pitch для амплитуды yaw-импульса.")]
	[SerializeField, Range(0f, 1f)] private float m_ShotYawScale = 0.3f;
	[Tooltip("Смещение yaw вправо (>0) / влево (<0). Шум не переворачивает сторону каждый выстрел.")]
	[SerializeField, Range(-1f, 1f)] private float m_YawBias = 0.45f;
	[Tooltip("Постоянная времени затухания punch (сек). Без перелёта за ноль.")]
	[SerializeField, Min(0.01f)] private float m_ShotSmoothTime = 0.08f;
	[Tooltip("Во время очереди punch гасится медленнее — ствол не ныряет между выстрелами.")]
	[SerializeField, Min(1f)] private float m_DecayWhileFiringMultiplier = 1.75f;
	[Tooltip("Страховочный потолок накопленной визуальной силы отдачи (impulse cap), чтобы автоочередь не улетала.")]
	[SerializeField, Min(1f)] private float m_MaxShotImpulse = 6f;
	[Tooltip("Отдельный потолок бокового yaw (градусы). Независим от impulse cap.")]
	[SerializeField, Min(1f)] private float m_MaxShotYawDegrees = 6f;
	[Tooltip("Сдвиг назад (метры) на единицу визуальной силы отдачи. Главное направление recoil. Climb сдвигом не едет.")]
	[SerializeField, Min(0f)] private float m_BackScale = 0.035f;
	[Tooltip("Сдвиг вверх (метры) на единицу визуальной силы отдачи. Вторичное направление.")]
	[SerializeField, Min(0f)] private float m_UpScale = 0.008f;

	[Header("Hand Kick")]
	[Tooltip("Множитель pitch-вращения кисти. 1 = полный визуальный kick через руку.")]
	[SerializeField, Range(0f, 2f)] private float m_HandPitch = 0.8f;
	[Tooltip("Множитель yaw-вращения кисти.")]
	[SerializeField, Range(0f, 2f)] private float m_HandYaw = 0.85f;
	[Tooltip("Множитель сдвига кисти назад.")]
	[SerializeField, Range(0f, 2f)] private float m_HandBack = 1f;
	[Tooltip("Множитель сдвига кисти вверх.")]
	[SerializeField, Range(0f, 2f)] private float m_HandUp = 0.75f;
	#endregion

	#region Private Fields
	private WeaponVisualRecoilState m_CurrentState;
	private float m_ShotImpulse;
	private float m_ShotImpulseYaw;
	private int m_ShotIndex;
	private float m_YawSeed;
	#endregion

	#region Public API
	public WeaponVisualRecoilState CurrentState => m_CurrentState;
	public bool HasVisualKick => m_CurrentState.isActive;

	/// <summary>
	/// RecoilSweep: keep punch/climb even if the camera is outside the VFX near-detail radius.
	/// </summary>
	public bool IgnoreCameraDistanceCull { get; set; }

	public bool IsCameraNearForVisualKick() => IsNearCameraForVisualDetail();

	public Quaternion BuildHandRotationOffset()
	{
		return Quaternion.Euler(
			-(m_CurrentState.climbPitch + m_CurrentState.punchPitch) * m_HandPitch,
			m_CurrentState.punchYaw * m_HandYaw,
			0f);
	}

	/// <summary>
	/// World-space direction of visual recoil (back along the barrel).
	/// </summary>
	public Vector3 GetCurrentRecoilDirectionWorld()
	{
		Transform fireOrigin = ResolveFireOriginTransform();
		if (fireOrigin == null)
			return -transform.forward;
		return -fireOrigin.forward;
	}

	/// <summary>
	/// Same world translation the applicator writes onto Hand_R (up + back along the barrel).
	/// </summary>
	public Vector3 GetCurrentKickTranslationWorld()
	{
		Transform fireOrigin = ResolveFireOriginTransform();
		Vector3 barrelForward = fireOrigin != null ? fireOrigin.forward : transform.forward;
		return Vector3.up * m_CurrentState.upOffset - barrelForward * m_CurrentState.backOffset;
	}

	/// <summary>
	/// Translation recoil в пространстве родителя кисти (parent-space delta для Hand_R.localPosition).
	/// Back идёт строго назад вдоль реального ствола (FireOriginTransform.forward),
	/// up — по мировой вертикали (roll оружия не уводит recoil вбок).
	/// Оси кости Hand_R НЕ используются: они не совпадают с продольной осью оружия.
	/// </summary>
	public Vector3 BuildHandParentSpaceTranslation(Transform hand)
	{
		Transform fireOrigin = ResolveFireOriginTransform();

		if (fireOrigin == null || hand == null || hand.parent == null)
			return Vector3.zero;

		Vector3 worldDelta =
			Vector3.up * m_CurrentState.upOffset -
			fireOrigin.forward * m_CurrentState.backOffset;

		return hand.parent.InverseTransformVector(worldDelta);
	}

	public float RecoilRotationDeltaDegrees =>
		Quaternion.Angle(Quaternion.identity, BuildHandRotationOffset());

	public float ShotPitchDegrees => m_ShotPitch;
	public float ShotYawScale => m_ShotYawScale;
	public float YawBias => m_YawBias;
	public float ShotSmoothTime => m_ShotSmoothTime;
	public float DecayWhileFiringMultiplier => m_DecayWhileFiringMultiplier;
	public float MaxShotImpulse => m_MaxShotImpulse;
	public float MaxShotYawDegrees => m_MaxShotYawDegrees;
	public float ShotImpulse => m_ShotImpulse;
	public float LastAddedVisualImpulse { get; private set; }
	public float BackScale => m_BackScale;
	public float UpScale => m_UpScale;
	public float HandPitch => m_HandPitch;
	public float HandYaw => m_HandYaw;
	public float HandBack => m_HandBack;
	public float HandUp => m_HandUp;

	public bool ShouldApplyOverlayThisFrame()
	{
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return false;
		if (IsRuntimePoseTuningActive())
			return false;
		if (m_Equipment != null && m_Equipment.IsWeaponHeldForBoltCycle)
			return false;
		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
			return false;
		return HasEquippedWeaponForVisualKick() && ResolveRightHandTransform() != null;
	}

	public void ResetVisualKick()
	{
		ResetImpulseState();
		m_CurrentState = default;
	}
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
		if (m_FireController == null)
			m_FireController = GetComponent<UnitWeaponFireController>();
		if (m_WeaponRuntime == null)
			m_WeaponRuntime = GetComponent<UnitWeaponRuntime>();
		if (m_RagdollController == null)
			m_RagdollController = GetComponent<UnitRagdollController>();
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponent<UnitEquippedWeaponPose>();
		if (m_EquippedWeaponPose == null)
			m_EquippedWeaponPose = GetComponentInParent<UnitEquippedWeaponPose>();

		m_YawSeed = Random.value * 100f;
	}

	private void OnEnable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged += HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired += HandleShotFired;
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
		ResetVisualKick();
	}

	private void Update()
	{
		if (m_RagdollController != null && m_RagdollController.ShouldBlockWeaponPoseScripts)
			return;

		if (IsRuntimePoseTuningActive())
			return;

		if (m_Equipment != null && m_Equipment.IsWeaponHeldForBoltCycle)
		{
			ResetVisualKick();
			return;
		}

		if (m_Equipment != null && m_Equipment.IsOperatingVehicleTurret)
		{
			ResetVisualKick();
			return;
		}

		if (!HasEquippedWeaponForVisualKick())
		{
			ResetVisualKick();
			return;
		}

		if (!IgnoreCameraDistanceCull && !IsNearCameraForVisualDetail())
		{
			ResetVisualKick();
			return;
		}

		float tau = Mathf.Max(0.01f, m_ShotSmoothTime);
		if (m_FireController != null && m_FireController.IsFiringCommandActive)
			tau *= Mathf.Max(1f, m_DecayWhileFiringMultiplier);
		float decay = Mathf.Exp(-Time.deltaTime / tau);
		m_ShotImpulse *= decay;
		m_ShotImpulseYaw *= decay;
		if (Mathf.Abs(m_ShotImpulse) < 0.001f)
			m_ShotImpulse = 0f;
		if (Mathf.Abs(m_ShotImpulseYaw) < 0.001f)
			m_ShotImpulseYaw = 0f;

		RebuildCurrentState();
	}

	private void LateUpdate()
	{
		if (!ShouldApplyOverlayThisFrame())
			return;

		RebuildCurrentState();
	}
	#endregion

	#region Private Methods
	private void RebuildCurrentState()
	{
		float kickScale = ResolveVisualRecoilKickScale();
		float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
		float climbPitch = m_PitchCurve.Evaluate(penalty) * m_VisualOffsetScale * kickScale;

		float impulse = m_ShotImpulse;
		float punchPitch = impulse * m_ShotPitch;
		float punchYaw = m_ShotImpulseYaw;
		float backOffset = impulse * m_BackScale * m_HandBack;
		float upOffset = impulse * m_UpScale * m_HandUp;

		float totalPitch = climbPitch + punchPitch;
		bool isActive = Mathf.Abs(totalPitch) >= 0.001f
		                || Mathf.Abs(punchYaw) >= 0.001f
		                || Mathf.Abs(backOffset) >= 0.000001f
		                || Mathf.Abs(upOffset) >= 0.000001f;

		if (!isActive)
		{
			m_CurrentState = default;
			return;
		}

		m_CurrentState = new WeaponVisualRecoilState
		{
			punchPitch = punchPitch,
			punchYaw = punchYaw,
			climbPitch = climbPitch,
			backOffset = backOffset,
			upOffset = upOffset,
			isActive = true
		};
	}

	private Transform ResolveRightHandTransform()
	{
		return m_Equipment != null ? m_Equipment.RightHandAnchor : null;
	}

	private Transform ResolveFireOriginTransform()
	{
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (weapon != null && weapon.FireOriginTransform != null)
			return weapon.FireOriginTransform;
		return m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	}

	private bool HasEquippedWeaponForVisualKick()
	{
		return m_Equipment != null && m_Equipment.MainWeaponRoot != null;
	}

	private bool IsRuntimePoseTuningActive()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.IsTuningActive;
	}

	private float ResolveVisualRecoilKickScale()
	{
		WeaponDefinition definition = m_WeaponRuntime != null
			? m_WeaponRuntime.CurrentWeaponDefinition
			: null;
		return definition != null ? definition.VisualRecoilKickScale : 1f;
	}

	private void HandleShotFired(AmmoDefinition _ammoDefinition)
	{
		LastAddedVisualImpulse = 0f;
		if (!HasEquippedWeaponForVisualKick())
			return;

		float recoilPerShot = m_RecoilController != null
			? m_RecoilController.ComputeRecoilAddedPerShot(_ammoDefinition)
			: 1f;
		float kickScale = ResolveVisualRecoilKickScale();
		float impulse = recoilPerShot * kickScale;
		LastAddedVisualImpulse = impulse;
		float shotPitch = impulse * m_ShotPitch;

		m_ShotImpulse = Mathf.Min(m_ShotImpulse + impulse, m_MaxShotImpulse);

		float yawNoise = Mathf.PerlinNoise(m_YawSeed, m_ShotIndex * 0.73f) * 2f - 1f;
		float yawDir = Mathf.Clamp(m_YawBias + yawNoise * (1f - Mathf.Abs(m_YawBias)), -1f, 1f);
		m_ShotImpulseYaw += yawDir * shotPitch * m_ShotYawScale;
		m_ShotImpulseYaw = Mathf.Clamp(m_ShotImpulseYaw, -m_MaxShotYawDegrees, m_MaxShotYawDegrees);
		m_ShotIndex++;
		RebuildCurrentState();
	}

	private void HandleEquipmentChanged()
	{
		ResetVisualKick();
	}

	private bool IsNearCameraForVisualDetail()
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(null);
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Vector3 samplePosition;
		if (weapon != null && WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 shellPos, out _))
			samplePosition = shellPos;
		else if (m_KickTransformOverride != null)
			samplePosition = m_KickTransformOverride.position;
		else if (m_Equipment != null && m_Equipment.MainWeaponRoot != null)
			samplePosition = m_Equipment.MainWeaponRoot.position;
		else
			samplePosition = transform.position;
		return WeaponVfxUtility.IsWithinNearCameraDetailDistance(profile, samplePosition);
	}

	private void ResetImpulseState()
	{
		m_ShotImpulse = 0f;
		m_ShotImpulseYaw = 0f;
		m_ShotIndex = 0;
		LastAddedVisualImpulse = 0f;
	}
	#endregion
}
