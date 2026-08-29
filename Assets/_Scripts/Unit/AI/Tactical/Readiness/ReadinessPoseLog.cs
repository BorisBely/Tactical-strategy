using System.Globalization;
using UnityEngine;

/// <summary>
/// #14B.2 pose-request log. Distinct from READINESS, G6, and SHOT.
/// </summary>
public static class ReadinessPoseLog
{
	#region Public Properties
	public static string Channel => UnitActionLog.ReadinessPose;
	#endregion

	#region Public Methods
	public static string Format(in ReadinessPoseRequest _request)
	{
		string line = "state=" + _request.State + " pose=" + _request.Pose;
		if (_request.FromLifeGate)
			line += " reason=LifeGate";
		if (_request.Duration > 0f && _request.FromPose != _request.Pose)
		{
			line += " transition=" + _request.FromPose + "->" + _request.Pose +
			        " duration=" + _request.Duration.ToString("0.###", CultureInfo.InvariantCulture);
		}

		return line;
	}

	public static void Emit(Component _actor, string _payload)
	{
		if (!UnitActionLog.Enabled || string.IsNullOrEmpty(_payload))
			return;

		UnitActionLog.Write(_actor, Channel, _payload);
		string prefix = _actor != null ? "actor=" + UnitActionLog.Slot(_actor) + " " : string.Empty;
		UnitActionLog.Timeline(Channel, prefix + _payload);
	}
	#endregion
}
