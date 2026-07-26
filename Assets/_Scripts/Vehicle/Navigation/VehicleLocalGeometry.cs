using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Local free-space probes around the chassis for maneuver choice.
	/// Unity 6 note: rays must ignore the vehicle's own colliders (hull, ChassisGroundSupport,
	/// WheelColliders) because they otherwise report zero clearance and break decision making.
	/// </summary>
	public static class VehicleLocalGeometry
	{
		public struct Sample
		{
			public float LeftClearance;
			public float RightClearance;
			public float RearClearance;
			public float FrontClearance;
			/// <summary>-1 = prefer left turn, +1 = prefer right, 0 = either.</summary>
			public float PreferredTurnSign;
		}

		private const float c_ProbeHeight = 0.6f;
		private const float c_MaxProbe = 12f;

		public static Sample Probe(Transform _vehicle, float _vehicleWidth, LayerMask _mask)
		{
			Vector3 origin = _vehicle.position + Vector3.up * c_ProbeHeight;
			float halfWidth = Mathf.Max(0.5f, _vehicleWidth * 0.5f);

			// Ignore every collider that belongs to this vehicle so clearance is real world-space.
			Collider[] selfColliders = _vehicle.GetComponentsInChildren<Collider>(true);
			HashSet<Collider> selfSet = new HashSet<Collider>(selfColliders);

			// The kinematic UnitBlocker is a separate GameObject (not a child) but sits at the
			// same position; without ignoring it all side/rear clearances read as zero.
			if (_vehicle.TryGetComponent(out VehicleController vehicleCtrl) &&
			    vehicleCtrl.UnitBlocker != null &&
			    vehicleCtrl.UnitBlocker.BlockCollider != null)
			{
				selfSet.Add(vehicleCtrl.UnitBlocker.BlockCollider);
			}

			float left = RayClearance(origin, -_vehicle.right, c_MaxProbe, _mask, selfSet) - halfWidth;
			float right = RayClearance(origin, _vehicle.right, c_MaxProbe, _mask, selfSet) - halfWidth;
			float rear = RayClearance(origin, -_vehicle.forward, c_MaxProbe, _mask, selfSet);
			float front = RayClearance(origin, _vehicle.forward, c_MaxProbe, _mask, selfSet);

			left = Mathf.Max(0f, left);
			right = Mathf.Max(0f, right);
			rear = Mathf.Max(0f, rear);
			front = Mathf.Max(0f, front);

			float prefer = 0f;
			if (left > right + 0.75f)
				prefer = -1f;
			else if (right > left + 0.75f)
				prefer = 1f;

			return new Sample
			{
				LeftClearance = left,
				RightClearance = right,
				RearClearance = rear,
				FrontClearance = front,
				PreferredTurnSign = prefer
			};
		}

		private static float RayClearance(
			Vector3 _origin,
			Vector3 _dir,
			float _max,
			LayerMask _mask,
			HashSet<Collider> _ignoreSelf = null)
		{
			_dir.y = 0f;
			if (_dir.sqrMagnitude < 0.0001f)
				return _max;
			_dir.Normalize();

			RaycastHit[] hits = Physics.RaycastAll(
				_origin,
				_dir,
				_max,
				_mask,
				QueryTriggerInteraction.Ignore);

			if (hits == null || hits.Length == 0)
				return _max;

			System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

			for (int i = 0; i < hits.Length; i++)
			{
				Collider col = hits[i].collider;
				if (col == null)
					continue;
				if (_ignoreSelf != null && _ignoreSelf.Contains(col))
					continue;
				return hits[i].distance;
			}

			return _max;
		}
	}
}
