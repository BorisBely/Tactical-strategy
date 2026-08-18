using UnityEngine;

/// <summary>Inputs for Auto pose selection (PLAN §68–72).</summary>
public struct WeaponAutoPoseContext
{
	public bool HasTarget;
	public float DistanceMeters;
	public bool HasCombatAlert;
	public WeaponPoseState CurrentPose;
}

/// <summary>
/// Distance + alert Auto pose. Rank is already baked into preferred distances on the cache.
/// </summary>
public static class WeaponAutoPoseResolver
{
	public const float EmergencyHipFireMeters = 3f;
	public const float HysteresisBand = 0.15f;

	public static WeaponPoseState Resolve(in WeaponPoseAutoCapabilityCache _cache, in WeaponAutoPoseContext _ctx)
	{
		if (!_ctx.HasTarget || _ctx.DistanceMeters < 0f)
			return _ctx.HasCombatAlert ? WeaponPoseState.HighReady : WeaponPoseState.LowReady;

		float distance = _ctx.DistanceMeters;
		float hipPref = _cache.HipFirePreferredMeters > 0.1f ? _cache.HipFirePreferredMeters : 6f;
		float pointPref = _cache.PointAimPreferredMeters > 0.1f ? _cache.PointAimPreferredMeters : 32f;

		if (_ctx.CurrentPose == WeaponPoseState.LowReady && distance <= EmergencyHipFireMeters)
			return WeaponPoseState.HipFire;

		WeaponPoseState desired;
		if (distance <= hipPref)
			desired = WeaponPoseState.HipFire;
		else if (distance <= pointPref)
			desired = WeaponPoseState.PointAim;
		else
			desired = WeaponPoseState.Aiming;

		return ApplyHysteresis(_ctx.CurrentPose, desired, distance, hipPref, pointPref);
	}

	private static WeaponPoseState ApplyHysteresis(
		WeaponPoseState _current,
		WeaponPoseState _desired,
		float _distance,
		float _hipPref,
		float _pointPref)
	{
		WeaponPoseState current = _current.IsHipFireHold() ? WeaponPoseState.HipFire : _current;
		if (current == _desired)
			return _desired;

		float hipStay = _hipPref * (1f + HysteresisBand);
		float pointStay = _pointPref * (1f + HysteresisBand);

		if (current == WeaponPoseState.HipFire && _desired == WeaponPoseState.PointAim && _distance <= hipStay)
			return WeaponPoseState.HipFire;
		if (current == WeaponPoseState.PointAim && _desired == WeaponPoseState.Aiming && _distance <= pointStay)
			return WeaponPoseState.PointAim;
		if (current == WeaponPoseState.PointAim && _desired == WeaponPoseState.HipFire && _distance >= _hipPref * (1f - HysteresisBand))
			return WeaponPoseState.PointAim;
		if (current == WeaponPoseState.Aiming && _desired == WeaponPoseState.PointAim && _distance >= _pointPref * (1f - HysteresisBand))
			return WeaponPoseState.Aiming;

		return _desired;
	}
}
