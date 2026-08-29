using UnityEngine;

/// <summary>
/// #14.5 Replanning Gate. Event ≠ replan. Weights are prototype, not freeze.
/// </summary>
public static class TacticalReplanMath
{
	#region Constants
	public const float MinorExposureDelta = 0.08f;
	public const float MajorExposureDelta = 0.25f;
	public const float DefaultReplanningCost = 0.45f;
	public const float EmergencyCostScale = 0.25f;
	public const float DefaultCooldownSeconds = 0.75f;
	public const float ProgressCostWeight = 0.8f;
	#endregion

	#region Public Methods
	public static int EventRank(in TacticalReplanEvent _event)
	{
		switch (_event.Kind)
		{
			case TacticalReplanEventKind.MissionChanged:
			case TacticalReplanEventKind.DestinationInvalid:
			case TacticalReplanEventKind.RouteBlocked:
				return 100;
			case TacticalReplanEventKind.CoverInvalid:
				return 90;
			case TacticalReplanEventKind.ImmediateThreat:
				return 80;
			case TacticalReplanEventKind.GeometryChanged:
				return _event.OnRoute ? 70 : 5;
			case TacticalReplanEventKind.NewHostile:
				return 40;
			case TacticalReplanEventKind.EnemyMoved:
				return 30;
			case TacticalReplanEventKind.Sound:
				return 20;
			default:
				return 0;
		}
	}

	public static TacticalReplanEvent Coalesce(
		System.Collections.Generic.IReadOnlyList<TacticalReplanEvent> _events,
		out int _count)
	{
		_count = _events != null ? _events.Count : 0;
		if (_count <= 0)
			return default;

		TacticalReplanEvent best = _events[0];
		for (int i = 1; i < _count; i++)
		{
			TacticalReplanEvent next = _events[i];
			int nextRank = EventRank(in next);
			int bestRank = EventRank(in best);
			if (nextRank > bestRank ||
			    (nextRank == bestRank && next.Delta > best.Delta))
				best = Merge(in next, in best);
			else
				best = Merge(in best, in next);
		}

		return best;
	}

	public static TacticalReplanCheck EvaluateGate(
		in TacticalCommittedRoute _committed,
		in TacticalReplanEvent _event,
		int _coalescedCount,
		float _now,
		float _lastReplanTime,
		float _cooldownSeconds)
	{
		var check = new TacticalReplanCheck
		{
			EventKind = _event.Kind,
			Delta = _event.Delta,
			CoalescedCount = _coalescedCount
		};
		if (_event.Kind == TacticalReplanEventKind.None || _coalescedCount <= 0)
		{
			check.Reason = TacticalReplanReason.NoEvent;
			return check;
		}

		bool emergency = _event.Kind == TacticalReplanEventKind.ImmediateThreat;
		check.EmergencyBypass = emergency;
		check.ReplanningCost = ComputeReplanningCost(in _committed, emergency);

		bool cooling = _lastReplanTime >= 0f &&
		               (_now - _lastReplanTime) < Mathf.Max(0f, _cooldownSeconds);
		if (cooling && !emergency)
		{
			check.FromCooldown = true;
			check.Reason = TacticalReplanReason.Cooldown;
			return check;
		}

		if (IsMandatory(in _event))
		{
			check.ShouldReevaluate = true;
			check.Mandatory = true;
			check.Reason = MandatoryReason(in _event);
			check.ReplanningCost = 0f;
			return check;
		}

		if (_event.Kind == TacticalReplanEventKind.GeometryChanged && !_event.OnRoute)
		{
			check.Reason = TacticalReplanReason.GeometryOffRoute;
			return check;
		}

		if (_event.Kind == TacticalReplanEventKind.GeometryChanged && _event.OnRoute)
		{
			check.ShouldReevaluate = true;
			check.Reason = TacticalReplanReason.GeometryOnRoute;
			return check;
		}

		if (emergency)
		{
			check.ShouldReevaluate = true;
			check.Reason = TacticalReplanReason.ImmediateThreat;
			return check;
		}

		if (_event.Delta < MinorExposureDelta)
		{
			check.Reason = TacticalReplanReason.DeltaTooSmall;
			return check;
		}

		if (_event.Delta >= MajorExposureDelta)
		{
			check.ShouldReevaluate = true;
			check.Reason = TacticalReplanReason.ExposureWorsened;
			return check;
		}

		check.Reason = TacticalReplanReason.DeltaTooSmall;
		return check;
	}

	public static bool AdvantageBeatsCost(float _oldScore, float _newScore, float _cost)
	{
		return _newScore > _oldScore + Mathf.Max(0f, _cost);
	}

	public static float ComputeReplanningCost(in TacticalCommittedRoute _committed, bool _emergency)
	{
		float cost = DefaultReplanningCost + Mathf.Clamp01(_committed.Progress01) * ProgressCostWeight;
		if (_emergency)
			cost *= EmergencyCostScale;
		return cost;
	}

	public static float Progress01(Vector3 _origin, Vector3 _destination, Vector3 _current)
	{
		float totalSqr = CoverSpatialMath.PlanarDistanceSqr(_origin, _destination);
		if (totalSqr <= 0.0001f)
			return 1f;
		float total = Mathf.Sqrt(totalSqr);
		float remaining = Mathf.Sqrt(CoverSpatialMath.PlanarDistanceSqr(_current, _destination));
		return Mathf.Clamp01(1f - remaining / total);
	}

	public static bool IsMandatory(in TacticalReplanEvent _event)
	{
		return _event.Kind == TacticalReplanEventKind.RouteBlocked ||
		       _event.Kind == TacticalReplanEventKind.DestinationInvalid ||
		       _event.Kind == TacticalReplanEventKind.CoverInvalid ||
		       _event.Kind == TacticalReplanEventKind.MissionChanged;
	}
	#endregion

	#region Private Methods
	private static TacticalReplanReason MandatoryReason(in TacticalReplanEvent _event)
	{
		if (_event.Kind == TacticalReplanEventKind.MissionChanged)
			return TacticalReplanReason.MissionChanged;
		if (_event.Kind == TacticalReplanEventKind.CoverInvalid)
			return TacticalReplanReason.CoverInvalid;
		return TacticalReplanReason.RouteInvalid;
	}

	private static TacticalReplanEvent Merge(in TacticalReplanEvent _keep, in TacticalReplanEvent _other)
	{
		TacticalReplanEvent merged = _keep;
		if (_other.Delta > merged.Delta)
			merged.Delta = _other.Delta;
		merged.OnRoute |= _other.OnRoute;
		if (_other.GeometryVersion > merged.GeometryVersion)
			merged.GeometryVersion = _other.GeometryVersion;
		return merged;
	}
	#endregion
}
