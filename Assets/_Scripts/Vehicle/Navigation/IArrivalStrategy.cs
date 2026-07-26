using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ArrivalPlan
	{
		public IReadOnlyList<Maneuver> Maneuvers;
		public float Cost;
		public string DebugName;
		public bool Valid => Maneuvers != null && Maneuvers.Count > 0;

		public ArrivalPlan(List<Maneuver> _maneuvers, float _cost, string _name)
		{
			Maneuvers = _maneuvers ?? new List<Maneuver>();
			Cost = _cost;
			DebugName = _name;
		}

		public static ArrivalPlan Invalid(string _reason) => new ArrivalPlan(null, float.MaxValue, _reason);
	}

	public interface IArrivalStrategy
	{
		string Name { get; }
		ArrivalPlan Generate(ArrivalAnalysis _analysis, ArrivalPlanningSettings _settings,
			Vector3 _position, float _yaw, Vector3 _target, float? _targetHeading);
	}
}
