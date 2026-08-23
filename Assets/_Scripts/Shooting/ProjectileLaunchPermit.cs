using UnityEngine;

/// <summary>
/// Stage 12: почему снаряд не выпущен. Не результат hitscan SHOT.
/// </summary>
public enum ProjectileLaunchDeny
{
	None = 0,
	NoAimPoint = 1,
	NotG6Fire = 2,
	OutsideVision = 3,
	NoLOS = 4
}

/// <summary>
/// Допуск назначения projectile-цели. Не VisionSystem и не полёт.
/// LastKnown не является AimPoint. Lifetime сюда не входит.
/// </summary>
public static class ProjectileLaunchPermit
{
	#region Constants
	public const float HitscanAimProjectionSeconds = 0.5f;
	public const float RocketLeadMaxExtraSeconds = 2.5f;
	public const float RpgMuzzleSpeed = 115f;
	public const float DisposableMuzzleSpeed = 130f;
	public const float RocketLifetimeSeconds = 12f;
	public const float Mk19MuzzleSpeed = 240f;
	public const float Mk19LifetimeSeconds = 25f;
	#endregion

	#region Public Methods
	public static bool TryAuthorize(
		bool _hasEngageableAimPoint,
		Vector3 _origin,
		Vector3 _aimPoint,
		float _resolvedVisionRangeMeters,
		bool _hasEngagementDecision,
		bool _g6IsFire,
		bool _lineOfFireBlocked,
		out ProjectileLaunchDeny _reason)
	{
		_reason = ProjectileLaunchDeny.None;
		if (!_hasEngageableAimPoint || _aimPoint == Vector3.zero)
		{
			_reason = ProjectileLaunchDeny.NoAimPoint;
			return false;
		}

		if (_hasEngagementDecision && !_g6IsFire)
		{
			_reason = ProjectileLaunchDeny.NotG6Fire;
			return false;
		}

		float vision = Mathf.Max(0.5f, _resolvedVisionRangeMeters);
		float distance = Vector3.Distance(_origin, _aimPoint);
		if (distance > vision + 0.01f)
		{
			_reason = ProjectileLaunchDeny.OutsideVision;
			return false;
		}

		if (_lineOfFireBlocked)
		{
			_reason = ProjectileLaunchDeny.NoLOS;
			return false;
		}

		return true;
	}

	public static float TheoreticalPhysicalRangeMeters(float _muzzleSpeed, float _lifetimeSeconds)
	{
		return Mathf.Max(0f, _muzzleSpeed) * Mathf.Max(0f, _lifetimeSeconds);
	}

	/// <summary>
	/// Extra lead after hitscan's 0.5 s projection. Uses Observed velocity only.
	/// </summary>
	public static Vector3 ApplyRocketLead(
		Vector3 _aimPoint,
		Vector3 _targetVelocity,
		float _distanceMeters,
		float _muzzleSpeed)
	{
		if (_targetVelocity.sqrMagnitude < 0.0001f)
			return _aimPoint;

		float speed = Mathf.Max(1f, _muzzleSpeed);
		float timeOfFlight = Mathf.Max(0f, _distanceMeters) / speed;
		float extraSeconds = Mathf.Clamp(
			timeOfFlight - HitscanAimProjectionSeconds,
			0f,
			RocketLeadMaxExtraSeconds);
		if (extraSeconds <= 0.001f)
			return _aimPoint;

		return _aimPoint + _targetVelocity * extraSeconds;
	}

	public static string FormatResult(ProjectileLaunchDeny _reason)
	{
		return _reason == ProjectileLaunchDeny.None
			? "Launch"
			: "fireDenied=" + _reason;
	}
	#endregion
}
