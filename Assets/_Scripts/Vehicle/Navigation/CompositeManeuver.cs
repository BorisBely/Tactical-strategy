using System.Collections.Generic;
using System.Linq;

namespace VehicleNavigation
{
	/// <summary>
	/// A maneuver composed of sub-maneuvers executed in sequence.
	/// Example: TurnAround = [Forward, Reverse, Forward] or Parking = [Reverse, Forward, Reverse].
	/// </summary>
	public sealed class CompositeManeuver : Maneuver
	{
		public override VehicleManeuverType Type => VehicleManeuverType.Forward;
		public IReadOnlyList<Maneuver> SubManeuvers => m_SubManeuvers;

		private readonly List<Maneuver> m_SubManeuvers;

		public CompositeManeuver(IEnumerable<Maneuver> _subManeuvers, string _label = "composite")
		{
			m_SubManeuvers = _subManeuvers?.ToList() ?? new List<Maneuver>();
			if (m_SubManeuvers.Count > 0)
			{
				var last = m_SubManeuvers[m_SubManeuvers.Count - 1];
				IsArrivalManeuver = last.IsArrivalManeuver;
				SpeedScale = last.SpeedScale;
				AllowReverse = m_SubManeuvers.Any(m => m.AllowReverse);
			}
		}

		/// <summary>
		/// Flatten composite into individual maneuvers for the planning pipeline.
		/// </summary>
		public List<Maneuver> Flatten()
		{
			var result = new List<Maneuver>();
			foreach (var m in m_SubManeuvers)
			{
				if (m is CompositeManeuver cm)
					result.AddRange(cm.Flatten());
				else
					result.Add(m);
			}
			return result;
		}
	}
}
