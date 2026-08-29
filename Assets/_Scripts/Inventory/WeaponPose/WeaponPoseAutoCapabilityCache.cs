using UnityEngine;

/// <summary>
/// Baked once on equip / attachment / skill change.
/// Runtime Auto and shot/aim paths only read these fields — no per-shot optic walks for HipFire/PointAim.
/// Transition table is indexed by (int)WeaponPoseState (0..9), not by logical combat order.
/// </summary>
public struct WeaponPoseAutoCapabilityCache
{
	/// <summary>Must match <see cref="WeaponPoseState"/> last value + 1 (LowReady…HipFireCrouchWalk).</summary>
	public const int PoseSlotCount = 10;

	public bool IsValid;
	public bool HasLaserDesignator;
	public bool HasImprovedLaser;

	public float HipFireMaxMeters;
	public float PointAimMaxMeters;
	public float HipFirePreferredMeters;
	public float PointAimPreferredMeters;

	public float HipFireSpreadMult;
	public float PointAimSpreadMult;
	public float AimingSpreadMult;
	public float PreAimSpreadMult;

	public float HipFireAimTimeMult;
	public float PointAimAimTimeMult;
	public float AimingAimTimeMult;
	public float PreAimAimTimeMult;

	public float LaserPointAimSpreadMult;
	public float LaserAimingAimTimeMult;

	/// <summary>Indexed [from * PoseSlotCount + to] seconds. Length PoseSlotCount².</summary>
	public float[] TransitionSeconds;

	public float GetSpreadMult(WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				return HipFireSpreadMult > 0f ? HipFireSpreadMult : 2.5f;
			case WeaponPoseState.PointAim:
				return PointAimSpreadMult > 0f ? PointAimSpreadMult : 1.5f;
			case WeaponPoseState.Aiming:
				return AimingSpreadMult > 0f ? AimingSpreadMult : 1f;
			case WeaponPoseState.PreAim:
				return PreAimSpreadMult > 0f ? PreAimSpreadMult : PreAimPoseUtility.SpreadMult;
			default:
				return 3f;
		}
	}

	public float GetAimTimeMult(WeaponPoseState _pose)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				return HipFireAimTimeMult > 0f ? HipFireAimTimeMult : 0.55f;
			case WeaponPoseState.PointAim:
				return PointAimAimTimeMult > 0f ? PointAimAimTimeMult : 0.85f;
			case WeaponPoseState.Aiming:
				return AimingAimTimeMult > 0f ? AimingAimTimeMult : 1f;
			case WeaponPoseState.PreAim:
				return PreAimAimTimeMult > 0f ? PreAimAimTimeMult : PreAimPoseUtility.AimTimeMult;
			default:
				return 1f;
		}
	}

	public float GetTransitionSeconds(WeaponPoseState _from, WeaponPoseState _to)
	{
		int needed = PoseSlotCount * PoseSlotCount;
		if (TransitionSeconds == null || TransitionSeconds.Length < needed)
			return DefaultTransitionSeconds(_from, _to);
		int from = (int)_from;
		int to = (int)_to;
		if (from < 0 || from >= PoseSlotCount || to < 0 || to >= PoseSlotCount)
			return DefaultTransitionSeconds(_from, _to);
		return Mathf.Max(0.01f, TransitionSeconds[from * PoseSlotCount + to]);
	}

	public WeaponPoseState ResolveAutoPose(float _distanceMeters, bool _hasTarget) =>
		ResolveAutoPose(new WeaponAutoPoseContext
		{
			HasTarget = _hasTarget,
			DistanceMeters = _distanceMeters,
			HasCombatAlert = false,
			CurrentPose = WeaponPoseState.LowReady,
		});

	public WeaponPoseState ResolveAutoPose(in WeaponAutoPoseContext _context) =>
		WeaponAutoPoseResolver.Resolve(this, in _context);

	public static float DefaultTransitionSeconds(WeaponPoseState _from, WeaponPoseState _to)
	{
		if (_from == _to)
			return 0.01f;
		if (_from.IsHipFireHold() && _to.IsHipFireHold())
			return 0.15f;
		if (_from.IsPeacefulCarryPose() && _to.IsPeacefulCarryPose())
			return 0.18f;
		if (_to.IsPeacefulCarryPose())
			return 0.28f;
		if (_from.IsPeacefulCarryPose() && _to == WeaponPoseState.LowReady)
			return 0.28f;
		if (_from == WeaponPoseState.HighReady && _to == WeaponPoseState.LowReady)
			return 0.3f;
		if (_to == WeaponPoseState.LowReady)
			return 0.20f;
		if (_from == WeaponPoseState.LowReady && _to == WeaponPoseState.HighReady)
			return 0.3f;
		if (_from == WeaponPoseState.HighReady && _to == WeaponPoseState.PreAim)
			return 0.28f;
		if (_from == WeaponPoseState.HighReady && _to == WeaponPoseState.Aiming)
			return 0.16f;
		if (_from == WeaponPoseState.PreAim && _to == WeaponPoseState.Aiming)
			return 0.14f;
		if (_from == WeaponPoseState.Aiming && _to == WeaponPoseState.PreAim)
			return 0.14f;
		if (_from == WeaponPoseState.LowReady && _to.IsHipFireHold())
			return 0.16f;
		if (_from == WeaponPoseState.LowReady && _to == WeaponPoseState.PointAim)
			return 0.20f;
		if (_from == WeaponPoseState.LowReady && _to == WeaponPoseState.Aiming)
			return 0.30f;
		if (_from == WeaponPoseState.LowReady && _to == WeaponPoseState.PreAim)
			return 0.20f;
		if (_from == WeaponPoseState.HighReady && _to.IsHipFireHold())
			return 0.15f;
		if (_from == WeaponPoseState.HighReady && _to == WeaponPoseState.PointAim)
			return 0.12f;
		if (_from == WeaponPoseState.PreAim && _to.IsHipFireHold())
			return 0.15f;
		if (_from == WeaponPoseState.PreAim && _to == WeaponPoseState.PointAim)
			return 0.12f;
		if (_from.IsHipFireHold() && _to == WeaponPoseState.PreAim)
			return 0.15f;
		if (_from.IsHipFireHold() && _to == WeaponPoseState.HighReady)
			return 0.15f;
		if (_from.IsHipFireHold() && _to == WeaponPoseState.PointAim)
			return 0.15f;
		if (_from.IsHipFireHold() && _to == WeaponPoseState.Aiming)
			return 0.25f;
		if (_from == WeaponPoseState.PointAim && _to == WeaponPoseState.Aiming)
			return 0.14f;
		if (_from == WeaponPoseState.Aiming && _to == WeaponPoseState.PointAim)
			return 0.14f;
		if (_from == WeaponPoseState.PointAim && _to.IsHipFireHold())
			return 0.12f;
		if (_from == WeaponPoseState.Aiming && _to.IsHipFireHold())
			return 0.18f;
		return 0.18f;
	}

	public static float[] BuildDefaultTransitionTable()
	{
		int n = PoseSlotCount;
		var table = new float[n * n];
		for (int from = 0; from < n; from++)
		{
			for (int to = 0; to < n; to++)
				table[from * n + to] = DefaultTransitionSeconds((WeaponPoseState)from, (WeaponPoseState)to);
		}

		return table;
	}
}
