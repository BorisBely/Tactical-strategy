using UnityEngine;

/// <summary>
/// Distance bucket for G8 instrumentation. Not a gameplay range gate.
/// </summary>
public enum VisionDistanceBucket
{
	Near20 = 0,
	Mid100 = 1,
	Far500 = 2,
	Beyond500 = 3
}

/// <summary>
/// Pure G8 LOD policy. Scales when work happens; does not change detection meaning.
/// </summary>
public static class VisionLodMath
{
	public const float DefaultIdleIntervalScale = 3f;
	public const float DefaultCheapIntervalScale = 1.75f;
	public const float DefaultRangeFovIntervalScale = 1f;
	public const float DefaultDetailIntervalScale = 0.75f;
	public const float DefaultDiscoverIntervalSeconds = 0.5f;
	public const float DefaultMembershipIntervalSeconds = 1.5f;
	public const float DefaultDetailQueueDelaySeconds = 0.35f;
	public const float DefaultCoarseFovPadDegrees = 8f;
	public const float DefaultCoarseRangePadMeters = 4f;
	public const float DefaultLosCacheTtlSeconds = 0.3f;
	public const float DefaultLosCacheMoveEpsilonMeters = 0.35f;
	public const int DefaultDetailSlotsPerFrame = 8;

	public static VisionScanTier ResolveObserverTier(in VisionLodObserverContext _context)
	{
		if (_context.ImmediateScan ||
		    _context.HasSelectedTarget ||
		    _context.HasRecentlyLostContact ||
		    _context.HasQueuedDetailDue)
			return VisionScanTier.Detail;

		if (_context.SecondsSinceLastDetailScan >= Mathf.Max(0.05f, _context.DiscoverIntervalSeconds))
			return VisionScanTier.RangeFov;

		if (_context.SecondsSinceLastMembershipScan >= Mathf.Max(0.05f, _context.MembershipIntervalSeconds))
			return VisionScanTier.Cheap;

		return VisionScanTier.Idle;
	}

	public static float IntervalScale(VisionScanTier _tier)
	{
		switch (_tier)
		{
			case VisionScanTier.Idle:
				return DefaultIdleIntervalScale;
			case VisionScanTier.Cheap:
				return DefaultCheapIntervalScale;
			case VisionScanTier.RangeFov:
				return DefaultRangeFovIntervalScale;
			default:
				return DefaultDetailIntervalScale;
		}
	}

	public static bool MaySpendLos(VisionScanTier _tier)
	{
		return _tier == VisionScanTier.Detail;
	}

	public static bool MayApplyVisionFrame(VisionScanTier _tier)
	{
		return _tier == VisionScanTier.Detail;
	}

	public static VisionDistanceBucket Bucket(float _distanceMeters)
	{
		float d = Mathf.Max(0f, _distanceMeters);
		if (d < 20f)
			return VisionDistanceBucket.Near20;
		if (d < 100f)
			return VisionDistanceBucket.Mid100;
		if (d < 500f)
			return VisionDistanceBucket.Far500;
		return VisionDistanceBucket.Beyond500;
	}

	public static bool CacheIsValid(
		float _now,
		float _storedTime,
		float _ttlSeconds,
		Vector3 _storedOrigin,
		Vector3 _currentOrigin,
		Vector3 _storedTarget,
		Vector3 _currentTarget,
		Vector3 _storedForwardXZ,
		Vector3 _currentForwardXZ,
		float _moveEpsilonMeters,
		float _forwardAngleEpsilonDegrees)
	{
		if (_now - _storedTime > Mathf.Max(0.02f, _ttlSeconds))
			return false;

		float eps = Mathf.Max(0.001f, _moveEpsilonMeters);
		if ((_storedOrigin - _currentOrigin).sqrMagnitude > eps * eps)
			return false;
		if ((_storedTarget - _currentTarget).sqrMagnitude > eps * eps)
			return false;

		Vector3 a = VisionGeometry.FlattenNormalized(_storedForwardXZ, Vector3.forward);
		Vector3 b = VisionGeometry.FlattenNormalized(_currentForwardXZ, Vector3.forward);
		return Vector3.Angle(a, b) <= Mathf.Max(0.1f, _forwardAngleEpsilonDegrees);
	}
}

public struct VisionLodObserverContext
{
	public bool ImmediateScan;
	public bool HasSelectedTarget;
	public bool HasRecentlyLostContact;
	public bool HasQueuedDetailDue;
	public float SecondsSinceLastDetailScan;
	public float SecondsSinceLastMembershipScan;
	public float DiscoverIntervalSeconds;
	public float MembershipIntervalSeconds;
}
