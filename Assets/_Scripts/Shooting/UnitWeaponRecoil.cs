using UnityEngine;

/// <summary>
/// Визуальная отдача оружия и правой кисти.
///
/// Две независимые величины:
///   1. VisualRecoil = PitchCurve(RecoilPenalty) — положение оружия, вычисляется каждый кадр заново.
///   2. ShotImpulse  — резкий удар после выстрела, SmoothDamp → 0.
///
/// Всё остальное (Back, Up, Yaw) — производные от этих двух.
/// Никаких recovery-интерполяций, отдельных режимов стрельбы, зависимости от SpreadAngle.
/// Подписывается на FireController.ShotFired напрямую, без PenaltyDelta-посредника.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(66)]
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
	[Tooltip("Геймплейный контроллер отдачи — источник RecoilPenalty.")]
	[SerializeField] private UnitWeaponRecoilController m_RecoilController;
	[Tooltip("Контроллер стрельбы — источник ShotFired.")]
	[SerializeField] private UnitWeaponFireController m_FireController;
	[Tooltip("Runtime оружия — чтение VisualRecoilKickScale.")]
	[SerializeField] private UnitWeaponRuntime m_WeaponRuntime;
	[SerializeField] private UnitRagdollController m_RagdollController;
	[SerializeField] private UnitEquippedWeaponPoseRuntimeTuner m_RuntimeTuner;
	[Tooltip("Редко: явная цель kick.")]
	[SerializeField] private Transform m_KickTransformOverride;
	[Tooltip("Базовая поза оружия (relaxed↔ready).")]
	[SerializeField] private UnitEquippedWeaponPose m_EquippedWeaponPose;

	[Header("Offset — положение от накопленной отдачи")]
	[Tooltip("Кривая: ось X = RecoilPenalty (абсолютный), ось Y = визуальный pitch (градусы).")]
	[SerializeField] private AnimationCurve m_PitchCurve = AnimationCurve.EaseInOut(0f, 0f, 60f, 5f);
		[Tooltip("Сдвиг назад (метры) на градус Pitch.")]
	[SerializeField, Min(0f)] private float m_BackScale = 0.012f;
	[Tooltip("Сдвиг вверх (метры) на градус Pitch.")]
	[SerializeField, Min(0f)] private float m_UpScale = 0.004f;
	[Tooltip("Множитель offset-смещения (не влияет на импульс).")]
	[SerializeField, Min(0f)] private float m_VisualOffsetScale = 1f;

	[Header("Shot Impulse — резкий удар выстрела")]
	[Tooltip("Базовый градус импульса на единицу RecoilPerShot. Умножается на VisualRecoilKickScale из WeaponDefinition.")]
	[SerializeField, Min(0f)] private float m_ShotPitch = 2f;
	[Tooltip("Доля pitch для амплитуды yaw-импульса.")]
	[SerializeField, Range(0f, 1f)] private float m_ShotYawScale = 0.3f;
	[Tooltip("SmoothDamp время импульса (сек).")]
	[SerializeField, Min(0.01f)] private float m_ShotSmoothTime = 0.05f;

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

	// Единственное состояние: импульс (2 float)
	private float m_ShotImpulsePitch;
	private float m_ShotImpulseYaw;
	private float m_ShotImpulseVelocityPitch;
	private float m_ShotImpulseVelocityYaw;

	// Детерминированный шум yaw по номеру выстрела
	private int m_ShotIndex;
	private float m_YawSeed;
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
		RefreshKickTarget(true);
	}

	private void OnDisable()
	{
		if (m_Equipment != null)
			m_Equipment.EquipmentChanged -= HandleEquipmentChanged;
		if (m_FireController != null)
			m_FireController.ShotFired -= HandleShotFired;
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

		bool nearCam = IsNearCameraForVisualDetail();
		if (!nearCam)
		{
			ResetVisualKick();
			return;
		}

		// 1) Decay impulse
		m_ShotImpulsePitch = Mathf.SmoothDamp(m_ShotImpulsePitch, 0f, ref m_ShotImpulseVelocityPitch, m_ShotSmoothTime);
		m_ShotImpulseYaw   = Mathf.SmoothDamp(m_ShotImpulseYaw,   0f, ref m_ShotImpulseVelocityYaw,   m_ShotSmoothTime);

		// 2) Offset — fresh every frame from penalty, no interpolation
		float penalty = m_RecoilController != null ? m_RecoilController.RecoilPenalty : 0f;
		float offsetPitch = m_PitchCurve.Evaluate(penalty);

		// 3) Combine — offset and impulse scaled independently
		float kickScale = ResolveVisualRecoilKickScale();
		float totalPitch = offsetPitch * m_VisualOffsetScale + m_ShotImpulsePitch * kickScale;
		float totalYaw   = m_ShotImpulseYaw * kickScale;
		float totalBack  = totalPitch * m_BackScale;
		float totalUp    = totalPitch * m_UpScale;

		// 4) Apply — ONLY position (Aiming handles rotation)
		Vector3 basePos = ResolveBaseWeaponLocalPosition();

		if (Mathf.Abs(totalPitch) < 0.001f && Mathf.Abs(totalYaw) < 0.001f)
		{
			m_CachedTotalPose = default;
			return;
		}

		Vector3 kickPos = new Vector3(0f, totalUp, -totalBack);

		m_CachedTotalPose.Rotation = Quaternion.identity;
		m_CachedTotalPose.Position = kickPos;

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

	private static bool HasActiveVisual(float _pitch, float _yaw, float _back, float _up)
	{
		return Mathf.Abs(_pitch) > 0.001f
			|| Mathf.Abs(_yaw) > 0.001f
			|| Mathf.Abs(_back) > 0.00001f
			|| Mathf.Abs(_up) > 0.00001f;
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
		Transform kickTarget = ResolveKickTarget();
		if (kickTarget == null)
			return;
		if (kickTarget != m_KickTarget)
			RefreshKickTarget(true);
		if (m_KickTarget == null)
			return;

		float recoilPerShot = m_RecoilController != null
			? m_RecoilController.ComputeRecoilAddedPerShot(_ammoDefinition)
			: 1f;
		float kickScale = ResolveVisualRecoilKickScale();
		float shotPitchAmount = recoilPerShot * kickScale * m_ShotPitch;

		m_ShotImpulsePitch += shotPitchAmount;

		float yawNoise = Mathf.PerlinNoise(m_YawSeed, m_ShotIndex * 0.73f) * 2f - 1f;
		m_ShotImpulseYaw += yawNoise * shotPitchAmount * m_ShotYawScale;
		m_ShotIndex++;
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
			ResetImpulseState();
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
		m_ShotImpulseVelocityPitch = 0f;
		m_ShotImpulseVelocityYaw = 0f;
		m_ShotIndex = 0;
	}
	#endregion
}
