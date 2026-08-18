using UnityEngine;

/// <summary>
/// Горизонтальный facing в raised combat: desired world yaw задаёт направление ствола,
/// корень компенсирует authored offset тела относительно ствола.
/// Barrel offset is used only for desired body yaw — never for weapon-local correction.
/// </summary>
public static class UnitHorizontalFacingUtility
{
	/// <summary>Порог NavSpeed шага (idle &lt; 0.05, шаг &gt; 0.055).</summary>
	public const float WalkNavSpeedThreshold = 0.055f;

	private static readonly int s_NavSpeed = Animator.StringToHash(UnitClickToMove.ParamNavSpeed);

	public static bool ShouldUseBarrelCentricFacing(UnitWeaponReadyHandsLayer _readyHands)
	{
		return _readyHands != null && _readyHands.WantsCombatTargetFacing();
	}

	public static bool IsHipFirePose(UnitWeaponReadyHandsLayer _readyHands)
	{
		return _readyHands != null && _readyHands.EffectivePoseState.IsHipFireHold();
	}

	public static bool IsWalkLocomotion(Animator _animator, bool _runOrSprint)
	{
		if (_runOrSprint)
			return false;
		return _animator != null && _animator.GetFloat(s_NavSpeed) >= WalkNavSpeedThreshold;
	}

	public static bool IsHipFireWalk(
		UnitWeaponReadyHandsLayer _readyHands,
		Animator _animator,
		bool _runOrSprint)
	{
		return IsHipFirePose(_readyHands) && IsWalkLocomotion(_animator, _runOrSprint);
	}

	/// <summary>
	/// HipFire / PointAim / Aiming while walking (not run/sprint).
	/// Root yaws so the bore tracks the target; spine does not also absorb that yaw.
	/// </summary>
	public static bool IsCombatShootWalk(
		UnitWeaponReadyHandsLayer _readyHands,
		Animator _animator,
		bool _runOrSprint)
	{
		if (_readyHands == null || !_readyHands.EffectivePoseState.CanShootFromPose())
			return false;
		return IsWalkLocomotion(_animator, _runOrSprint);
	}

	public static bool TryGetTargetWorldYaw(Transform _body, Vector3 _aimPointWorld, out float _yawDegrees)
	{
		_yawDegrees = 0f;
		if (_body == null)
			return false;

		Vector3 toTarget = _aimPointWorld - _body.position;
		toTarget.y = 0f;
		if (toTarget.sqrMagnitude < 1e-6f)
			return false;

		_yawDegrees = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
		return true;
	}

	public static bool TryGetBarrelForwardXZ(UnitEquipment _equipment, out Vector3 _forwardXZ)
	{
		_forwardXZ = default;
		if (_equipment == null)
			return false;

		EquippedWeapon weapon = _equipment.EquippedWeapon;
		if (weapon == null)
			return false;

		Transform barrel = weapon.BarrelTransform != null ? weapon.BarrelTransform : weapon.FireOriginTransform;
		if (barrel == null)
			return false;

		_forwardXZ = barrel.forward;
		_forwardXZ.y = 0f;
		if (_forwardXZ.sqrMagnitude < 1e-6f)
			return false;

		_forwardXZ.Normalize();
		return true;
	}

	public static bool TryGetBodyBarrelYawOffset(
		Transform _body,
		UnitEquipment _equipment,
		out float _offsetDegrees)
	{
		_offsetDegrees = 0f;
		if (_body == null)
			return false;

		Vector3 bodyFwd = _body.forward;
		bodyFwd.y = 0f;
		if (bodyFwd.sqrMagnitude < 1e-6f)
			return false;
		bodyFwd.Normalize();

		if (!TryGetBarrelForwardXZ(_equipment, out Vector3 barrelFwd))
			return false;

		_offsetDegrees = Vector3.SignedAngle(bodyFwd, barrelFwd, Vector3.up);
		return true;
	}

	public static float ConvertBarrelYawToBodyYaw(float _desiredBarrelYaw, float _bodyBarrelOffsetDegrees)
	{
		return _desiredBarrelYaw - _bodyBarrelOffsetDegrees;
	}

	public static float ResolveHorizontalFacingBodyYaw(
		Transform _body,
		UnitEquipment _equipment,
		UnitWeaponReadyHandsLayer _readyHands,
		float _desiredWorldYaw)
	{
		if (!ShouldUseBarrelCentricFacing(_readyHands))
			return _desiredWorldYaw;

		if (!TryGetBodyBarrelYawOffset(_body, _equipment, out float offset))
			return _desiredWorldYaw;

		return ConvertBarrelYawToBodyYaw(_desiredWorldYaw, offset);
	}

	public static bool IsBarrelYawReached(
		Transform _body,
		UnitEquipment _equipment,
		UnitWeaponReadyHandsLayer _readyHands,
		float _desiredBarrelYaw,
		float _thresholdDegrees)
	{
		if (!ShouldUseBarrelCentricFacing(_readyHands))
		{
			float bodyYaw = _body != null ? _body.eulerAngles.y : 0f;
			return Mathf.Abs(Mathf.DeltaAngle(bodyYaw, _desiredBarrelYaw)) <= _thresholdDegrees;
		}

		if (!TryGetBarrelForwardXZ(_equipment, out Vector3 barrelFwd))
		{
			float bodyYaw = _body != null ? _body.eulerAngles.y : 0f;
			return Mathf.Abs(Mathf.DeltaAngle(bodyYaw, _desiredBarrelYaw)) <= _thresholdDegrees;
		}

		float barrelYaw = Mathf.Atan2(barrelFwd.x, barrelFwd.z) * Mathf.Rad2Deg;
		return Mathf.Abs(Mathf.DeltaAngle(barrelYaw, _desiredBarrelYaw)) <= _thresholdDegrees;
	}

	public static Vector3 YawDegreesToForwardXZ(float _yawDegrees)
	{
		return Quaternion.Euler(0f, _yawDegrees, 0f) * Vector3.forward;
	}
}
