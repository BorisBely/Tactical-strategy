using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// File-only infantry action log. Never writes to the Unity console.
/// One file per unit plus a session timeline. See <see cref="UnitActionLogSession"/>.
/// </summary>
public static class UnitActionLog
{
	#region Constants
	public const string Spawn = "SPAWN";
	public const string Scan = "SCAN";
	public const string Vision = "VISION";
	public const string Sound = "SOUND";
	public const string Shared = "SHARED";
	public const string Select = "SELECT";
	public const string G6 = "G6";
	public const string Disc = "DISC";
	public const string Gate = "GATE";
	public const string Shot = "SHOT";
	public const string Projectile = "PROJECTILE";
	public const string Move = "MOVE";
	public const string Ai = "AI";
	public const string Cmd = "CMD";
	public const string GameCmd = "GAMECMD";
	public const string Input = "INPUT";
	public const string Snap = "SNAP";
	public const string Death = "DEATH";
	public const string Threat = "THREAT";
	#endregion

	#region Public Properties
	public static bool Enabled => UnitActionLogSession.IsEnabled;
	#endregion

	#region Public Methods
	public static void Write(Component _actor, string _channel, string _payload)
	{
		if (!Enabled)
			return;
		UnitActionLogSession.WriteActor(_actor, _channel, _payload);
	}

	public static void Timeline(string _channel, string _payload)
	{
		if (!Enabled)
			return;
		UnitActionLogSession.WriteTimeline(_channel, _payload);
	}

	public static string Slot(Component _component)
	{
		return UnitActionLogIdentity.Slot(_component);
	}

	public static string Slot(Transform _transform)
	{
		return UnitActionLogIdentity.Slot(_transform);
	}

	public static string Vec(Vector3 _value)
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"({0:0.0}, {1:0.0}, {2:0.0})",
			_value.x,
			_value.y,
			_value.z);
	}

	public static string TimeNow()
	{
		return Time.time.ToString("0.000", CultureInfo.InvariantCulture);
	}

	public static string F1(float _value)
	{
		return _value.ToString("0.0", CultureInfo.InvariantCulture);
	}

	public static string F2(float _value)
	{
		return _value.ToString("0.00", CultureInfo.InvariantCulture);
	}

	public static string F3(float _value)
	{
		return _value.ToString("0.000", CultureInfo.InvariantCulture);
	}

	public static string ContactLine(PerceivedContact _contact)
	{
		if (_contact == null || _contact.Target == null)
			return "tgt=?";

		DetectionEvaluation e = _contact.CurrentEvaluation;
		bool hasAim = _contact.LastObservation.HasAimPoint && _contact.ObservationState == ObservationState.Observed;
		StringBuilder builder = SharedScratch.Clear();
		builder.Append("tgt=").Append(Slot(_contact.Target));
		builder.Append(" obs=").Append(_contact.ObservationState);
		builder.Append(" det=").Append(_contact.State);
		builder.Append(" Q=").Append(F3(e.VisibilityQuality));
		builder.Append(" D=").Append(F2(e.DistanceFactor));
		builder.Append(" F=").Append(F2(e.FovFactor));
		builder.Append(" E=").Append(F2(e.ExposureFactor));
		builder.Append(" M=").Append(F2(e.MovementFactor));
		builder.Append(" id=").Append(_contact.Identity);
		builder.Append(" idC=").Append(F2(_contact.IdentityConfidence));
		builder.Append(" rel=").Append(_contact.Relationship);
		builder.Append(" threat=").Append(_contact.Threat);
		builder.Append(" lastKnown=").Append(Vec(_contact.LastKnownPosition));
		builder.Append(" aim=").Append(hasAim ? "1" : "0");
		if (hasAim)
			builder.Append(" aimPt=").Append(Vec(_contact.LastObservation.AimPoint));
		builder.Append(" p=").Append(F2(_contact.DetectionProgress));
		float angle = Mathf.Abs(_contact.LastObservation.FovOffsetDegrees);
		float attMul = AttentionMath.EvaluateMultiplier(angle);
		builder.Append(" angle=").Append(angle.ToString("0.0", CultureInfo.InvariantCulture));
		builder.Append(" att=").Append(AttentionMath.EvaluateBand(angle));
		builder.Append(" attMul=").Append(attMul.ToString("0.0", CultureInfo.InvariantCulture));
		builder.Append(" memC=").Append(F2(_contact.LastSeenConfidence));
		if (_contact.SoundConfidence > 0f)
			builder.Append(" sound=").Append(F2(_contact.SoundConfidence));
		if (_contact.SharedConfidence > 0f)
			builder.Append(" shared=").Append(F2(_contact.SharedConfidence));
		return builder.ToString();
	}

	public static string CompactContact(PerceivedContact _contact)
	{
		if (_contact == null || _contact.Target == null)
			return "?";
		return Slot(_contact.Target) + ":" + _contact.ObservationState + "/" + _contact.State + "/" +
		       _contact.Identity + "/Q" + F2(_contact.VisibilityQuality);
	}

	public static string AgentPath(NavMeshAgent _agent)
	{
		if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
			return "none";
		if (_agent.pathPending)
			return "pending";
		return _agent.pathStatus.ToString();
	}

	public static string AgentRemaining(NavMeshAgent _agent)
	{
		if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
			return "-";
		if (!_agent.hasPath && !_agent.pathPending)
			return "-";
		float remaining = _agent.remainingDistance;
		return float.IsPositiveInfinity(remaining) ? "inf" : F1(remaining);
	}
	#endregion

	#region Nested
	private static class SharedScratch
	{
		[System.ThreadStatic]
		private static StringBuilder s_Builder;

		public static StringBuilder Clear()
		{
			if (s_Builder == null)
				s_Builder = new StringBuilder(256);
			else
				s_Builder.Length = 0;
			return s_Builder;
		}
	}
	#endregion
}
