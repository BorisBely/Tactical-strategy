using UnityEngine;

/// <summary>
/// Визуальная отдача оружия и правой кисти. Читает RecoilVisualState из UnitWeaponRecoilController
/// (VisualPitch/Yaw/Back/Up — отдельно от SpreadHalfAngle) и добавляет короткий shot-импульс на PenaltyDelta.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(62)]
public sealed class UnitWeaponRecoil : MonoBehaviour
{
	#region Cached Pose
	private struct CachedWeaponVisualPose
	{
		public Quaternion Rotation;
		public Vector3 Position;
	}
	#endregion

	#region Serialized Fields
	[Tooltip("Снаряжение: корень оружия в руке.")]
	[SerializeField] private UnitEquipment m_Equipment;
	[Tooltip("Геймплейный контроллер отдачи — источник RecoilVisualState.")]
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Редко: явная цель kick.")]
	[SerializeField] private Transform m_KickTransformOverride;
	[Tooltip("Базовая поза оружия (relaxed↔ready).")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Gameplay Pose")]
	[Tooltip("Максимальный визуальный pitch (градусы).")]
	[SerializeField, Min(0f)] private float m_MaxVisualPitch = 12f;
	[Tooltip("Амплитуда Perlin-шума бокового покачивания относительно pitch.")]
	[SerializeField, Range(0f, 1f)] private float m_YawNoiseScale = 0.25f;
	[Tooltip("Скорость Perlin-шума для бокового покачивания.")]
	[SerializeField, Min(0f)] private float m_YawNoiseSpeed = 0.7f;

	[Header("Shot Impulse")]
	[Tooltip("Импульсный pitch на единицу delta penalty.")]
	[SerializeField, Min(0f)] private float m_ImpulsePitchScale = 0.1f;
	[Tooltip("Импульсный back на единицу delta penalty.")]
	[SerializeField, Min(0f)] private float m_ImpulseBackScale = 0.0005f;
	[Tooltip("Импульсный up на единицу delta penalty.")]
	[SerializeField, Min(0f)] private float m_ImpulseUpScale = 0.0002f;
	[Tooltip("SmoothDamp время импульса.")]
	[SerializeField, Min(0.01f)] private float m_ImpulseSmoothTime = 0.04f;

	[Header("Hand Kick")]
	[Tooltip("Множитель pitch-вращения кисти.")]
	[SerializeField, Range(0f, 2f)] private float m_HandPitch = 0.45f;
	[Tooltip("Множитель yaw-вращения кисти.")]
	[SerializeField, Range(0f, 2f)] private float m_HandYaw = 0.35f;
	[Tooltip("Множитель сдвига кисти назад.")]
	[SerializeField, Range(0f, 2f)] private float m_HandBack = 0.5f;
	[Tooltip("Множитель сдвига кисти вверх.")]
	[SerializeField, Range(0f, 2f)] private float m_HandUp = 0.4f;
	#endregion

	#region Private Fields
	private Transform m_KickTarget;
	private CachedWeaponVisualPose m_CachedTotalPose;
	private float m_RecoilTime;
	private float m_YawSeed;

	// Shot impulse
	private float m_ShotImpulsePitch;
	private float m_ShotImpulseYaw;
	private float m_ShotImpulseBack;
	private float m_ShotImpulseUp;
	private float m_ShotImpulseVelocityPitch;
	private float m_ShotImpulseVelocityYaw;
	private float m_ShotImpulseVelocityBack;
	private float m_ShotImpulseVelocityUp;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Equipment == null)
			m_Equipment = GetComponent<UnitEquipment>();
		if (m_RecoilController == null)
			m_RecoilController = GetComponent<UnitWeaponRecoilController>();
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
		if (m_RecoilController != null)
			m_RecoilController.PenaltyDelta += HandlePenaltyDelta;
		RefreshKickTarget(true);
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_RecoilController != null)
			m_RecoilController.PenaltyDelta -= HandlePenaltyDelta;
		if (m_KickTarget != null)
			ResetVisualKick();
		m_KickTarget = null;
		ResetImpulseState();
	}

	private void LateUpdate()
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

		if (m_KickTarget == null)
			return;

		if (!IsNearCameraForVisualDetail())
		{
			ResetVisualKick();
			return;
		}

		bool hasWeapon = m_Equipment != null && m_Equipment.MainWeaponRoot != null;
		if (hasWeapon)
			m_RecoilTime += Time.deltaTime;

		// 1) Decay shot impulse
		m_ShotImpulsePitch = Mathf.SmoothDamp(m_ShotImpulsePitch, 0f, ref m_ShotImpulseVelocityPitch, m_ImpulseSmoothTime);
		m_ShotImpulseYaw = Mathf.SmoothDamp(m_ShotImpulseYaw, 0f, ref m_ShotImpulseVelocityYaw, m_ImpulseSmoothTime);
		m_ShotImpulseBack = Mathf.SmoothDamp(m_ShotImpulseBack, 0f, ref m_ShotImpulseVelocityBack, m_ImpulseSmoothTime);
		m_ShotImpulseUp = Mathf.SmoothDamp(m_ShotImpulseUp, 0f, ref m_ShotImpulseVelocityUp, m_ImpulseSmoothTime);

		// 2) Read current visual state from gameplay controller
		UnitWeaponRecoilController.RecoilVisualState state =
			m_RecoilController != null ? m_RecoilController.CurrentVisualState : default;

		// 3) GameplayPose — контроллер даёт готовые визуальные значения
		float gameplayPitch = Mathf.Min(state.VisualPitch, m_MaxVisualPitch);
		float gameplayYaw = state.VisualYaw + ComputePerlinYaw(gameplayPitch);
		float gameplayBack = state.VisualBack;
		float gameplayUp = state.VisualUp;

		// 4) Total
		float totalPitch = gameplayPitch + m_ShotImpulsePitch;
		float totalYaw = gameplayYaw + m_ShotImpulseYaw;
		float totalBack = gameplayBack + m_ShotImpulseBack;
		float totalUp = gameplayUp + m_ShotImpulseUp;

		Quaternion baseRot = ResolveBaseWeaponLocalRotation();
		Vector3 basePos = ResolveBaseWeaponLocalPosition();

		if (!HasActiveVisual(totalPitch, totalYaw, totalBack, totalUp))
		{
			m_KickTarget.localRotation = baseRot;
			m_KickTarget.localPosition = basePos;
			m_CachedTotalPose = default;
			return;
		}

		Quaternion kickRot = Quaternion.Euler(-totalPitch, totalYaw, 0f);
		Vector3 kickPos = new Vector3(0f, totalUp, -totalBack);

		m_CachedTotalPose.Rotation = kickRot;
		m_CachedTotalPose.Position = kickPos;

		m_KickTarget.localRotation = baseRot * kickRot;
		m_KickTarget.localPosition = basePos + kickPos;
	}
	#endregion

	#region Public API
	public void ApplyHandKick(ref Vector3 _localPosition, ref Quaternion _localRotation)
	{
		if (m_CachedTotalPose.Rotation == Quaternion.identity
		    && m_CachedTotalPose.Position == Vector3.zero)
			return;

		Quaternion kickRot = Quaternion.Euler(
			m_CachedTotalPose.Rotation.eulerAngles.x * m_HandPitch,
			m_CachedTotalPose.Rotation.eulerAngles.y * m_HandYaw,
			0f);
		Vector3 kickPos = new Vector3(
			0f,
			m_CachedTotalPose.Position.y * m_HandUp,
			m_CachedTotalPose.Position.z * m_HandBack);

		_localPosition = kickRot * _localPosition + kickPos;
		_localRotation = kickRot * _localRotation;
	}

	public void ResetVisualKick()
	{
		ResetImpulseState();
		m_CachedTotalPose = default;
		if (m_KickTarget == null)
			return;
		Quaternion baseRot = ResolveBaseWeaponLocalRotation();
		Vector3 basePos = ResolveBaseWeaponLocalPosition();
		m_KickTarget.localRotation = baseRot;
		m_KickTarget.localPosition = basePos;
	}
	#endregion

	#region Private Methods
	private bool IsRuntimePoseTuningActive()
	{
		if (m_RuntimeTuner == null)
			m_RuntimeTuner = GetComponent<UnitEquippedWeaponPoseRuntimeTuner>();
		return m_RuntimeTuner != null && m_RuntimeTuner.ShouldSkipWeaponPoseWrite;
	}

	private Quaternion ResolveBaseWeaponLocalRotation()
	{
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.BaseWeaponLocalRotation;
		if (m_KickTarget != null)
			return m_KickTarget.localRotation;
		return Quaternion.identity;
	}

	private Vector3 ResolveBaseWeaponLocalPosition()
	{
		if (m_EquippedWeaponPose != null)
			return m_EquippedWeaponPose.BaseWeaponLocalPosition;
		if (m_KickTarget != null)
			return m_KickTarget.localPosition;
		return Vector3.zero;
	}

	private float ComputePerlinYaw(float _visualPitch)
	{
		if (_visualPitch <= 0.0001f || m_YawNoiseScale <= 0.0001f)
			return 0f;
		float noise = Mathf.PerlinNoise(m_YawSeed, m_RecoilTime * m_YawNoiseSpeed) * 2f - 1f;
		return noise * _visualPitch * m_YawNoiseScale;
	}

	private static bool HasActiveVisual(float _pitch, float _yaw, float _back, float _up)
	{
		return Mathf.Abs(_pitch) > 0.001f
			|| Mathf.Abs(_yaw) > 0.001f
			|| Mathf.Abs(_back) > 0.00001f
			|| Mathf.Abs(_up) > 0.00001f;
	}

	private void HandlePenaltyDelta(float _delta)
	{
		if (Mathf.Abs(_delta) <= 0.0001f)
			return;
		if (!IsNearCameraForVisualDetail())
			return;

		Transform kickTarget = ResolveKickTarget();
		if (kickTarget == null)
			return;
		if (kickTarget != m_KickTarget)
			RefreshKickTarget(true);
		if (m_KickTarget == null)
			return;

		float absDelta = Mathf.Abs(_delta);
		float sign = _delta > 0f ? 1f : -1f;

		m_ShotImpulsePitch += absDelta * m_ImpulsePitchScale * sign;
		m_ShotImpulseBack += absDelta * m_ImpulseBackScale * sign;
		m_ShotImpulseUp += absDelta * m_ImpulseUpScale * sign;
		m_ShotImpulseYaw += Random.Range(-1f, 1f) * absDelta * m_ImpulsePitchScale * 0.3f;
	}

	private void HandleEquipmentChanged()
	{
		RefreshKickTarget(true);
	}

	private Transform ResolveKickTarget()
	{
		if (m_KickTransformOverride != null)
			return m_KickTransformOverride;
		EquippedWeapon equipped = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		if (equipped != null && equipped.VisualRecoilKickPivot != null)
			return equipped.VisualRecoilKickPivot;
		return m_Equipment != null ? m_Equipment.MainWeaponRoot : null;
	}

	private void RefreshKickTarget(bool _resetKick)
	{
		Transform newTarget = ResolveKickTarget();
		bool targetChanged = newTarget != m_KickTarget;
		m_KickTarget = newTarget;

		if (m_KickTarget == null)
		{
			ResetImpulseState();
			return;
		}

		if (_resetKick || targetChanged)
		{
			ResetImpulseState();
			m_RecoilTime = 0f;
		}
	}

	private bool IsNearCameraForVisualDetail()
	{
		WeaponVfxProfile profile = WeaponVfxUtility.GetCurrentProfile(null);
		EquippedWeapon weapon = m_Equipment != null ? m_Equipment.EquippedWeapon : null;
		Vector3 samplePosition;
		if (weapon != null && WeaponVfxUtility.TryGetShellEjectionPose(weapon, out Vector3 shellPos, out _))
			samplePosition = shellPos;
		else if (m_KickTarget != null)
			samplePosition = m_KickTarget.position;
		else if (m_Equipment != null && m_Equipment.MainWeaponRoot != null)
			samplePosition = m_Equipment.MainWeaponRoot.position;
		else
			samplePosition = transform.position;
		return WeaponVfxUtility.IsWithinNearCameraDetailDistance(profile, samplePosition);
	}

	private void ResetImpulseState()
	{
		m_ShotImpulsePitch = 0f;
		m_ShotImpulseYaw = 0f;
		m_ShotImpulseBack = 0f;
		m_ShotImpulseUp = 0f;
		m_ShotImpulseVelocityPitch = 0f;
		m_ShotImpulseVelocityYaw = 0f;
		m_ShotImpulseVelocityBack = 0f;
		m_ShotImpulseVelocityUp = 0f;
	}
	#endregion
}
