namespace VehicleNavigation
{
	/// <summary>
	/// Maneuver wrapper that delegates to the new ReverseDriver system.
	/// Stores a ReversePath; execution is handled by ReverseDriver, not PursuitController.
	/// </summary>
	public sealed class ReverseIntentManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Reverse;
		public ReversePath Path { get; }

		public ReverseIntentManeuver(ReversePath _path)
		{
			Path = _path;
			AllowReverse = true;
			SpeedScale = 0.45f;
			LookAheadOverride = 2.5f;

			if (_path != null && _path.IsValid)
			{
				var wps = new UnityEngine.Vector3[_path.Points.Count];
				for (int i = 0; i < _path.Points.Count; i++)
					wps[i] = _path.Points[i].Position;
				SetWaypoints(wps);
			}
		}
	}
}
