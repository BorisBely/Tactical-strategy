using UnityEngine;

/// <summary>
/// Трёхосное наведение турели: Turret Y 360°, основание Y ±10°, ствол X (elevation).
/// Velocity-based физика с разгоном, торможением, люфтом и допуском точности.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class VehicleTurretAimController : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleTurretHierarchyBinder m_Hierarchy;
	[SerializeField] private TurretWeaponVariant m_ActiveVariant = TurretWeaponVariant.None;

	[Header("Limits")]
	[SerializeField, Range(0f, 45f)] private float m_BaseYawLimitDegrees = 10f;
	[SerializeField, Range(0f, 90f)] private float m_PitchUpLimitDegrees = 45f;
	[SerializeField, Range(0f, 90f)] private float m_PitchDownLimitDegrees = 10f;

	[Header("Pitch")]
	[SerializeField, Min(1f)] private float m_PitchRate = 150f;
	[SerializeField, Min(1f)] private float m_ReloadPitchReturnMaxRate = 90f;

	[Header("Drive Profiles")]
	[SerializeField] private TurretDriveProfile m_MechanicalProfile = new TurretDriveProfile();
	[SerializeField] private TurretDriveProfile m_ElectricProfile = new TurretDriveProfile();
	[SerializeField] private TurretDriveType m_DriveType = TurretDriveType.Mechanical;

	[Header("Debug")]
	[SerializeField] private bool m_LogOscillation = true;

	[Header("Rest local euler (captured on bind)")]
	[SerializeField] private Vector3 m_TurretRestEuler;
	[SerializeField] private Vector3 m_GunBase127RestEuler;
	[SerializeField] private Vector3 m_Gun127RestEuler;
	[SerializeField] private Vector3 m_Mk19BaseRestEuler;
	[SerializeField] private Vector3 m_Mk19RestEuler;
	[SerializeField] private bool m_RestCaptured;
	#endregion

	#region Private Fields
	private bool m_Active;
	private bool m_HasAimPoint;
	private Vector3 m_AimPoint;
	private TurretDriveProfile m_ActiveProfile;
	private bool m_ReloadPitchOverrideActive;
	private bool m_ReloadPitchReturning;
	private float m_ReloadPitchTargetX;
	private float m_SavedPitchBeforeReloadX;
	private float m_ReloadPitchMoveRate;

	private float m_TurretYawVelocity;
	private float m_BaseYawVelocity;
	private float m_TurretBacklashRemaining;
	private float m_BaseBacklashRemaining;
	private float m_TurretBacklashRefAngle;
	private float m_BaseBacklashRefAngle;
	private int m_TurretEngagedDir;
	private int m_BaseEngagedDir;
	private int m_TurretDirChangeCount;
	private float m_TurretDirChangeElapsed;
	private bool m_OscillationReported;
	#endregion

	#region Public Properties
	public TurretWeaponVariant ActiveVariant => m_ActiveVariant;
	public bool IsAimActive => m_Active;
	public bool HasAimPoint => m_HasAimPoint;
	public Vector3 AimPoint => m_AimPoint;
	public Transform FireOrigin => ResolveFireOrigin();
	public TurretDriveType DriveType => m_DriveType;
	public bool IsReloadPitchOverrideActive => m_ReloadPitchOverrideActive;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Hierarchy == null)
			TryGetComponent(out m_Hierarchy);
		m_Hierarchy?.EnsureBound();
		CaptureRestPosesIfNeeded();
		InitializeProfiles();
	}

	private void LateUpdate()
	{
		TickAim(Time.deltaTime);
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleTurretHierarchyBinder _hierarchy)
	{
		m_Hierarchy = _hierarchy;
		m_Hierarchy?.EnsureBound();
		CaptureRestPosesIfNeeded(_force: true);
	}

	public void SetActiveVariant(TurretWeaponVariant _variant)
	{
		m_ActiveVariant = _variant;
	}

	public void SetDriveType(TurretDriveType _type)
	{
		if (m_DriveType == _type)
			return;
		m_DriveType = _type;
		m_ActiveProfile = _type == TurretDriveType.Mechanical ? m_MechanicalProfile : m_ElectricProfile;
		ResetDriveState();
	}

	public void SetActive(bool _active)
	{
		m_Active = _active;
		if (!_active)
			ClearAim();
	}

	public void SetAimPoint(Vector3 _worldPoint)
	{
		m_AimPoint = _worldPoint;
		m_HasAimPoint = true;
	}

	public void ClearAim()
	{
		m_HasAimPoint = false;
	}

	public void BeginReloadPitchOverride(float _pitchUpDegrees, float _durationSeconds)
	{
		Transform pitchPivot = m_Hierarchy != null ? m_Hierarchy.GetActiveWeaponPitch(m_ActiveVariant) : null;
		Vector3 rest = m_ActiveVariant == TurretWeaponVariant.Mk19 ? m_Mk19RestEuler : m_Gun127RestEuler;
		m_SavedPitchBeforeReloadX = pitchPivot != null ? pitchPivot.localEulerAngles.x : rest.x;
		m_ReloadPitchTargetX = m_SavedPitchBeforeReloadX - _pitchUpDegrees;
		m_ReloadPitchOverrideActive = true;
		m_ReloadPitchReturning = false;

		float currentX = pitchPivot != null ? pitchPivot.localEulerAngles.x : m_SavedPitchBeforeReloadX;
		float delta = Mathf.Abs(Mathf.DeltaAngle(currentX, m_ReloadPitchTargetX));
		float duration = Mathf.Max(0.05f, _durationSeconds);
		m_ReloadPitchMoveRate = Mathf.Max(5f, delta / duration);
	}

	public void EndReloadPitchOverride(float _durationSeconds)
	{
		Transform pitchPivot = m_Hierarchy != null ? m_Hierarchy.GetActiveWeaponPitch(m_ActiveVariant) : null;
		Vector3 rest = m_ActiveVariant == TurretWeaponVariant.Mk19 ? m_Mk19RestEuler : m_Gun127RestEuler;
		m_ReloadPitchTargetX = TryComputeAimPitchTargetX(pitchPivot, rest, out float aimPitchX)
			? aimPitchX
			: m_SavedPitchBeforeReloadX;
		m_ReloadPitchReturning = true;
		m_ReloadPitchOverrideActive = true;
		m_ReloadPitchMoveRate = m_ReloadPitchReturnMaxRate;
	}

	public bool IsBarrelAlignedTo(Vector3 _worldPoint, float _maxErrorDegrees)
	{
		Transform origin = ResolveFireOrigin();
		if (origin == null)
			return false;

		Vector3 toTarget = _worldPoint - origin.position;
		if (toTarget.sqrMagnitude < 0.0001f)
			return true;

		float angle = Vector3.Angle(origin.forward, toTarget);
		return angle <= _maxErrorDegrees;
	}

#if UNITY_EDITOR
	[ContextMenu("Debug Aim Forward 50m")]
	private void DebugAimForward()
	{
		SetActive(true);
		Transform t = m_Hierarchy != null ? m_Hierarchy.Turret : transform;
		SetAimPoint(t.position + t.forward * 50f);
	}

	[ContextMenu("Debug Aim Right 50m")]
	private void DebugAimRight()
	{
		SetActive(true);
		Transform t = m_Hierarchy != null ? m_Hierarchy.Turret : transform;
		SetAimPoint(t.position + t.right * 50f);
	}
#endif
	#endregion

	#region Private Methods
	private void InitializeProfiles()
	{
		if (m_MechanicalProfile.TurretAxis.MaxSpeed <= 0f)
		{
			m_MechanicalProfile = new TurretDriveProfile
			{
				TurretAxis = new YawAxisProfile { MaxSpeed = 45f, Acceleration = 90f, Deceleration = 120f },
				BaseAxis = new YawAxisProfile { MaxSpeed = 100f, Acceleration = 220f, Deceleration = 300f },
				Backlash = 0.35f,
				AimTolerance = 0.45f
			};
		}

		if (m_ElectricProfile.TurretAxis.MaxSpeed <= 0f)
		{
			m_ElectricProfile = new TurretDriveProfile
			{
				TurretAxis = new YawAxisProfile { MaxSpeed = 75f, Acceleration = 260f, Deceleration = 300f },
				BaseAxis = new YawAxisProfile { MaxSpeed = 180f, Acceleration = 500f, Deceleration = 600f },
				Backlash = 0.05f,
				AimTolerance = 0.10f
			};
		}

		m_ActiveProfile = m_DriveType == TurretDriveType.Mechanical ? m_MechanicalProfile : m_ElectricProfile;
	}

	private void ResetDriveState()
	{
		m_TurretYawVelocity = 0f;
		m_BaseYawVelocity = 0f;
		m_TurretBacklashRemaining = 0f;
		m_BaseBacklashRemaining = 0f;
		m_TurretBacklashRefAngle = 0f;
		m_BaseBacklashRefAngle = 0f;
		m_TurretEngagedDir = 0;
		m_BaseEngagedDir = 0;
	}

	private void CaptureRestPosesIfNeeded(bool _force = false)
	{
		if (m_RestCaptured && !_force)
			return;
		if (m_Hierarchy == null || !m_Hierarchy.IsBound)
			return;

		if (m_Hierarchy.Turret != null)
			m_TurretRestEuler = m_Hierarchy.Turret.localEulerAngles;
		if (m_Hierarchy.GunBase127 != null)
			m_GunBase127RestEuler = m_Hierarchy.GunBase127.localEulerAngles;
		if (m_Hierarchy.Gun127 != null)
			m_Gun127RestEuler = m_Hierarchy.Gun127.localEulerAngles;
		if (m_Hierarchy.Mk19Base != null)
			m_Mk19BaseRestEuler = m_Hierarchy.Mk19Base.localEulerAngles;
		if (m_Hierarchy.Mk19 != null)
			m_Mk19RestEuler = m_Hierarchy.Mk19.localEulerAngles;
		m_RestCaptured = true;
	}

	private void TickAim(float _dt)
	{
		if (m_Hierarchy == null || m_Hierarchy.Turret == null || m_ActiveProfile == null)
			return;

		CaptureRestPosesIfNeeded();

		if (!m_Active || !m_HasAimPoint)
		{
			if (!m_Active)
				return;
			ReturnToRest(_dt);
			if (m_ReloadPitchOverrideActive)
				RotateWeaponPitch(_dt);
			return;
		}

		int prevDir = m_TurretEngagedDir;
		RotateTurretYaw(_dt);
		DetectTurretOscillation(prevDir, _dt);
		RotateWeaponBaseYaw(_dt);
		RotateWeaponPitch(_dt);
	}

	private void ReturnToRest(float _dt)
	{
		if (m_ActiveProfile == null)
			return;

		float currentTurretYaw = m_Hierarchy.Turret.localEulerAngles.y;
		float newTurretYaw = TickYawDrive(
			currentTurretYaw, m_TurretRestEuler.y,
			ref m_TurretYawVelocity,
			ref m_TurretBacklashRemaining, ref m_TurretBacklashRefAngle, ref m_TurretEngagedDir,
			m_ActiveProfile.TurretAxis, m_ActiveProfile, _dt);
		m_Hierarchy.Turret.localEulerAngles = new Vector3(m_TurretRestEuler.x, newTurretYaw, m_TurretRestEuler.z);

		Transform weaponBase = m_Hierarchy.GetActiveWeaponBase(m_ActiveVariant);
		if (weaponBase != null)
		{
			Vector3 baseRest = m_ActiveVariant == TurretWeaponVariant.Mk19
				? m_Mk19BaseRestEuler
				: m_GunBase127RestEuler;
			float newBaseYaw = TickYawDrive(
				weaponBase.localEulerAngles.y, baseRest.y,
				ref m_BaseYawVelocity,
				ref m_BaseBacklashRemaining, ref m_BaseBacklashRefAngle, ref m_BaseEngagedDir,
				m_ActiveProfile.BaseAxis, m_ActiveProfile, _dt);
			weaponBase.localEulerAngles = new Vector3(baseRest.x, newBaseYaw, baseRest.z);
		}

		Transform pitchPivot = m_Hierarchy.GetActiveWeaponPitch(m_ActiveVariant);
		if (pitchPivot != null && !m_ReloadPitchOverrideActive)
		{
			Vector3 pitchRest = m_ActiveVariant == TurretWeaponVariant.Mk19 ? m_Mk19RestEuler : m_Gun127RestEuler;
			RotateTowardsLocalEuler(pitchPivot, pitchRest, m_PitchRate * _dt);
		}
	}

	private void RotateTurretYaw(float _dt)
	{
		Transform turret = m_Hierarchy.Turret;
		Transform parent = turret.parent != null ? turret.parent : transform;
		Vector3 local = parent.InverseTransformPoint(m_AimPoint) - parent.InverseTransformPoint(turret.position);
		local.y = 0f;
		if (local.sqrMagnitude < 0.0001f)
			return;

		float desiredYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
		float newYaw = TickYawDrive(
			turret.localEulerAngles.y, desiredYaw,
			ref m_TurretYawVelocity,
			ref m_TurretBacklashRemaining, ref m_TurretBacklashRefAngle, ref m_TurretEngagedDir,
			m_ActiveProfile.TurretAxis, m_ActiveProfile, _dt);
		turret.localEulerAngles = new Vector3(m_TurretRestEuler.x, newYaw, m_TurretRestEuler.z);
	}

	private void RotateWeaponBaseYaw(float _dt)
	{
		Transform weaponBase = m_Hierarchy.GetActiveWeaponBase(m_ActiveVariant);
		if (weaponBase == null)
			return;

		Vector3 rest = m_ActiveVariant == TurretWeaponVariant.Mk19
			? m_Mk19BaseRestEuler
			: m_GunBase127RestEuler;

		Vector3 local = weaponBase.parent.InverseTransformPoint(m_AimPoint) -
		                weaponBase.parent.InverseTransformPoint(weaponBase.position);
		local.y = 0f;
		float desiredYaw = 0f;
		if (local.sqrMagnitude > 0.0001f)
			desiredYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

		float deltaFromRest = Mathf.DeltaAngle(rest.y, desiredYaw);
		deltaFromRest = Mathf.Clamp(deltaFromRest, -m_BaseYawLimitDegrees, m_BaseYawLimitDegrees);
		float targetYaw = rest.y + deltaFromRest;
		float newYaw = TickYawDrive(
			weaponBase.localEulerAngles.y, targetYaw,
			ref m_BaseYawVelocity,
			ref m_BaseBacklashRemaining, ref m_BaseBacklashRefAngle, ref m_BaseEngagedDir,
			m_ActiveProfile.BaseAxis, m_ActiveProfile, _dt);
		weaponBase.localEulerAngles = new Vector3(rest.x, newYaw, rest.z);
	}

	private void RotateWeaponPitch(float _dt)
	{
		Transform pitchPivot = m_Hierarchy.GetActiveWeaponPitch(m_ActiveVariant);
		if (pitchPivot == null)
			return;

		Vector3 rest = m_ActiveVariant == TurretWeaponVariant.Mk19
			? m_Mk19RestEuler
			: m_Gun127RestEuler;

		if (m_ReloadPitchOverrideActive)
		{
			Vector3 current = pitchPivot.localEulerAngles;
			float newX = Mathf.MoveTowardsAngle(current.x, m_ReloadPitchTargetX, m_ReloadPitchMoveRate * _dt);
			pitchPivot.localEulerAngles = new Vector3(newX, rest.y, rest.z);
			if (m_ReloadPitchReturning && Mathf.Abs(Mathf.DeltaAngle(newX, m_ReloadPitchTargetX)) <= 0.05f)
			{
				m_ReloadPitchOverrideActive = false;
				m_ReloadPitchReturning = false;
			}
			return;
		}

		Vector3 local = pitchPivot.parent.InverseTransformPoint(m_AimPoint) -
		                pitchPivot.parent.InverseTransformPoint(pitchPivot.position);
		float desiredPitch = 0f;
		if (local.sqrMagnitude > 0.0001f)
		{
			float horizontal = new Vector2(local.x, local.z).magnitude;
			desiredPitch = -Mathf.Atan2(local.y, horizontal) * Mathf.Rad2Deg;
		}

		desiredPitch = Mathf.Clamp(desiredPitch, -m_PitchUpLimitDegrees, m_PitchDownLimitDegrees);
		Vector3 goal = new Vector3(rest.x + desiredPitch, rest.y, rest.z);
		RotateTowardsLocalEuler(pitchPivot, goal, m_PitchRate * _dt);
	}

	private bool TryComputeAimPitchTargetX(Transform _pitchPivot, Vector3 _rest, out float _targetX)
	{
		_targetX = _rest.x;
		if (!m_HasAimPoint || _pitchPivot == null || _pitchPivot.parent == null)
			return false;

		Vector3 local = _pitchPivot.parent.InverseTransformPoint(m_AimPoint) -
		                _pitchPivot.parent.InverseTransformPoint(_pitchPivot.position);
		float desiredPitch = 0f;
		if (local.sqrMagnitude > 0.0001f)
		{
			float horizontal = new Vector2(local.x, local.z).magnitude;
			desiredPitch = -Mathf.Atan2(local.y, horizontal) * Mathf.Rad2Deg;
		}

		desiredPitch = Mathf.Clamp(desiredPitch, -m_PitchUpLimitDegrees, m_PitchDownLimitDegrees);
		_targetX = _rest.x + desiredPitch;
		return true;
	}

	/// <summary>
	/// Velocity-based привод одной оси Y с разгоном, торможением, люфтом и допуском точности.
	/// </summary>
	private static float TickYawDrive(
		float currentAngle,
		float targetAngle,
		ref float velocity,
		ref float backlashRemaining,
		ref float backlashRefAngle,
		ref int engagedDir,
		YawAxisProfile axis,
		TurretDriveProfile profile,
		float dt)
	{
		if (axis == null || profile == null)
			return currentAngle;

		float error = Mathf.DeltaAngle(currentAngle, targetAngle);
		int newDir = Mathf.Abs(error) <= profile.AimTolerance ? 0 : (error > 0 ? 1 : -1);

		if (newDir != 0 && engagedDir != 0 && newDir != engagedDir)
		{
			backlashRemaining = profile.Backlash;
			backlashRefAngle = currentAngle;
		}

		if (backlashRemaining > 0f && newDir != 0)
		{
			if (newDir == engagedDir)
			{
				backlashRemaining = 0f;
			}
			else
			{
				float mechTravel = Mathf.Abs(Mathf.DeltaAngle(backlashRefAngle, targetAngle));
				if (mechTravel >= backlashRemaining)
				{
					backlashRemaining = 0f;
					engagedDir = newDir;
				}
				else
				{
					float desiredVel = Mathf.Sign(error) * axis.MaxSpeed;
					float accel = Mathf.Abs(desiredVel) < Mathf.Abs(velocity) ? axis.Deceleration : axis.Acceleration;
					velocity = Mathf.MoveTowards(velocity, desiredVel, accel * dt);
					return currentAngle;
				}
			}
		}

		if (newDir != 0)
			engagedDir = newDir;

		if (Mathf.Abs(error) <= profile.AimTolerance)
		{
			velocity = 0f;
			return currentAngle;
		}

		float stopDist = (velocity * velocity) / (2f * axis.Deceleration);
		float desiredVelocity;
		if (Mathf.Abs(error) <= stopDist)
			desiredVelocity = Mathf.Sign(error) * Mathf.Sqrt(2f * axis.Deceleration * Mathf.Abs(error));
		else
			desiredVelocity = Mathf.Sign(error) * axis.MaxSpeed;

		float accelRate = Mathf.Abs(desiredVelocity) < Mathf.Abs(velocity) ? axis.Deceleration : axis.Acceleration;
		velocity = Mathf.MoveTowards(velocity, desiredVelocity, accelRate * dt);

		return currentAngle + velocity * dt;
	}

	private static void RotateTowardsLocalEuler(Transform _t, Vector3 _goalEuler, float _maxDegreesDelta)
	{
		if (_t == null)
			return;

		Quaternion goal = Quaternion.Euler(_goalEuler);
		_t.localRotation = Quaternion.RotateTowards(_t.localRotation, goal, _maxDegreesDelta);
	}

	private Transform ResolveFireOrigin()
	{
		Transform pitch = m_Hierarchy != null ? m_Hierarchy.GetActiveWeaponPitch(m_ActiveVariant) : null;
		if (pitch == null)
			return m_Hierarchy != null ? m_Hierarchy.Turret : null;

		EquippedWeapon equipped = pitch.GetComponentInChildren<EquippedWeapon>(true);
		if (equipped != null && equipped.FireOriginTransform != null)
			return equipped.FireOriginTransform;

		Transform muzzle = FindChildNamed(pitch, EquippedWeapon.MuzzleExitTransformName);
		return muzzle != null ? muzzle : pitch;
	}

	private static Transform FindChildNamed(Transform _root, string _name)
	{
		if (_root == null || string.IsNullOrEmpty(_name))
			return null;
		Transform[] all = _root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] != null && all[i].name == _name)
				return all[i];
		}

		return null;
	}

	private void DetectTurretOscillation(int _prevDir, float _dt)
	{
		if (!m_LogOscillation || m_OscillationReported)
			return;

		if (m_TurretEngagedDir != 0 && m_TurretEngagedDir != _prevDir)
			m_TurretDirChangeCount++;

		m_TurretDirChangeElapsed += _dt;
		if (m_TurretDirChangeElapsed > 3f)
		{
			m_TurretDirChangeCount = 0;
			m_TurretDirChangeElapsed = 0f;
		}

		if (m_TurretDirChangeCount >= 4)
		{
			Debug.LogWarning(
				$"[TurretAim] Oscillation detected: {m_TurretDirChangeCount} dir changes in {m_TurretDirChangeElapsed:F1}s. "
				+ $"Yaw velocity={m_TurretYawVelocity:F2} deg/s, backlash={m_TurretBacklashRemaining:F3}, "
				+ $"drive={m_DriveType}, tolerance={m_ActiveProfile?.AimTolerance:F2}",
				this);
			m_OscillationReported = true;
		}
	}
	#endregion
}
