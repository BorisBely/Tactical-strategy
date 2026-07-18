using UnityEngine;

/// <summary>
/// Горизонтальный facing в high ready: desired world yaw задаёт направление ствола,
/// корень компенсирует offset ready-позы.
/// </summary>
public static class UnitHorizontalFacingUtility
{
	public static bool ShouldUseBarrelCentricFacing(UnitWeaponReadyHandsLayer _readyHands)
	{
		return _readyHands != null && _readyHands.IsWeaponEquippedAndReady();
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
