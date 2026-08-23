using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 17: one Publish, ally + distance² fan-out to registered <see cref="DetectionProcessor"/>.
/// No FindObjects, no per-frame scan, no raycast, no contact copy.
/// </summary>
public static class WorldAllyReportHub
{
	#region Private Fields
	private static readonly List<DetectionProcessor> s_Listeners = new List<DetectionProcessor>(64);
	#endregion

	#region Public Properties
	public static int ListenerCount => s_Listeners.Count;
	public static int LastPublishDeliveryCount { get; private set; }
	#endregion

	#region Public Methods
	public static void Register(DetectionProcessor _processor)
	{
		if (_processor == null || s_Listeners.Contains(_processor))
			return;
		s_Listeners.Add(_processor);
	}

	public static void Unregister(DetectionProcessor _processor)
	{
		if (_processor == null)
			return;
		s_Listeners.Remove(_processor);
	}

	public static void ResetForTests()
	{
		s_Listeners.Clear();
		LastPublishDeliveryCount = 0;
	}

	public static bool AreAllies(DetectionProcessor _a, DetectionProcessor _b)
	{
		if (_a == null || _b == null || _a == _b)
			return false;
		if (!_a.TryGetComponent(out UnitTeam teamA) || !_b.TryGetComponent(out UnitTeam teamB))
			return false;
		if (teamA.Team == UnitTeamId.Neutral || teamB.Team == UnitTeamId.Neutral)
			return false;
		return teamA.Team == teamB.Team;
	}

	public static void Publish(in WorldAllyReportEvent _evt)
	{
		LastPublishDeliveryCount = 0;
		if (_evt.Reporter == null || _evt.Subject == null)
			return;

		float range = _evt.RangeMeters > 0f ? _evt.RangeMeters : AllyReportEvidenceMath.DefaultRangeMeters;
		float rangeSq = range * range;
		float confidence = Mathf.Clamp01(_evt.Confidence);
		if (confidence <= 0f)
			return;

		Vector3 origin = _evt.Reporter.position;
		for (int i = s_Listeners.Count - 1; i >= 0; i--)
		{
			DetectionProcessor listener = s_Listeners[i];
			if (listener == null)
			{
				s_Listeners.RemoveAt(i);
				continue;
			}

			if (IsSelf(_evt.Reporter, listener.transform))
				continue;
			if (!AreAllies(ResolveReporterProcessor(_evt.Reporter), listener))
				continue;
			if (IsDead(listener))
				continue;

			Vector3 listenerPos = listener.transform.position;
			float dx = listenerPos.x - origin.x;
			float dy = listenerPos.y - origin.y;
			float dz = listenerPos.z - origin.z;
			if (!AllyReportEvidenceMath.IsInRange(dx * dx + dy * dy + dz * dz, rangeSq))
				continue;

			LastPublishDeliveryCount++;
			listener.ReceiveWorldAllyReport(in _evt, confidence);
		}
	}
	#endregion

	#region Private Methods
	private static bool IsSelf(Transform _reporter, Transform _listener)
	{
		if (_reporter == null || _listener == null)
			return true;
		if (_reporter == _listener)
			return true;
		return _reporter.IsChildOf(_listener) || _listener.IsChildOf(_reporter);
	}

	private static bool IsDead(DetectionProcessor _listener)
	{
		if (_listener == null)
			return true;
		return _listener.TryGetComponent(out UnitHealth health) && health.IsDead;
	}

	private static DetectionProcessor ResolveReporterProcessor(Transform _reporter)
	{
		if (_reporter == null)
			return null;
		if (_reporter.TryGetComponent(out DetectionProcessor processor))
			return processor;
		return _reporter.GetComponentInParent<DetectionProcessor>();
	}
	#endregion
}
