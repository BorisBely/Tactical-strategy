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

			public float FrontDiagonalLeftClearance;
			public float FrontDiagonalRightClearance;
			public float RearDiagonalLeftClearance;
			public float RearDiagonalRightClearance;

			public bool HasDropAhead;
			public bool HasDropBehind;
			public bool HasNarrowPassage;

			/// <summary>-1 = prefer left turn, +1 = prefer right, 0 = either.</summary>
			public float PreferredTurnSign;
		}

		private const float c_ProbeHeight = 0.6f;
		private const float c_MaxProbe = 12f;

		public static Sample Probe(Transform _vehicle, float _vehicleWidth, LayerMask _mask)
		{
			HashSet<Collider> selfSet = BuildSelfSet(_vehicle);
			return ProbeInternal(_vehicle, _vehicleWidth, _mask, selfSet, _dense: true);
		}

		/// <summary>
		/// Cheap front/rear emergency probes for runtime driving (not route selection).
		/// </summary>
		public static Sample ProbeLightweight(Transform _vehicle, float _vehicleWidth, LayerMask _mask)
		{
			HashSet<Collider> selfSet = BuildSelfSet(_vehicle);
			return ProbeInternal(_vehicle, _vehicleWidth, _mask, selfSet, _dense: false);
		}

		private static Sample ProbeInternal(
			Transform _vehicle,
			float _vehicleWidth,
			LayerMask _mask,
			HashSet<Collider> selfSet,
			bool _dense)
		{
			Vector3 origin = _vehicle.position + Vector3.up * c_ProbeHeight;
			float halfWidth = Mathf.Max(0.5f, _vehicleWidth * 0.5f);

			float rear = RayClearance(origin, -_vehicle.forward, c_MaxProbe, _mask, selfSet);
			float front = RayClearance(origin, _vehicle.forward, c_MaxProbe, _mask, selfSet);
			rear = Mathf.Max(0f, rear);
			front = Mathf.Max(0f, front);

			float left = c_MaxProbe;
			float right = c_MaxProbe;
			float fal = c_MaxProbe;
			float far = c_MaxProbe;
			float ral = c_MaxProbe;
			float rar = c_MaxProbe;

			if (_dense)
			{
				left = Mathf.Max(0f, RayClearance(origin, -_vehicle.right, c_MaxProbe, _mask, selfSet) - halfWidth);
				right = Mathf.Max(0f, RayClearance(origin, _vehicle.right, c_MaxProbe, _mask, selfSet) - halfWidth);

				Vector3 diagFrontLeft  = Quaternion.Euler(0f, -30f, 0f) * _vehicle.forward;
				Vector3 diagFrontRight = Quaternion.Euler(0f,  30f, 0f) * _vehicle.forward;
				Vector3 diagRearLeft   = Quaternion.Euler(0f, -150f, 0f) * _vehicle.forward;
				Vector3 diagRearRight  = Quaternion.Euler(0f,  150f, 0f) * _vehicle.forward;

				fal = Mathf.Max(0f, RayClearance(origin, diagFrontLeft,  c_MaxProbe, _mask, selfSet) - halfWidth * 1.15f);
				far = Mathf.Max(0f, RayClearance(origin, diagFrontRight, c_MaxProbe, _mask, selfSet) - halfWidth * 1.15f);
				ral = Mathf.Max(0f, RayClearance(origin, diagRearLeft,   c_MaxProbe, _mask, selfSet) - halfWidth * 1.15f);
				rar = Mathf.Max(0f, RayClearance(origin, diagRearRight,  c_MaxProbe, _mask, selfSet) - halfWidth * 1.15f);
			}
			else
			{
				// One diagonal each side for emergency awareness only.
				Vector3 diagFrontLeft  = Quaternion.Euler(0f, -25f, 0f) * _vehicle.forward;
				Vector3 diagFrontRight = Quaternion.Euler(0f,  25f, 0f) * _vehicle.forward;
				fal = Mathf.Max(0f, RayClearance(origin, diagFrontLeft,  6f, _mask, selfSet) - halfWidth);
				far = Mathf.Max(0f, RayClearance(origin, diagFrontRight, 6f, _mask, selfSet) - halfWidth);
				left = fal;
				right = far;
			}

			Vector3 dropForwardOrigin = origin + _vehicle.forward * (halfWidth + 0.5f);
			dropForwardOrigin.y += 0.5f;
			bool dropAhead1 = !Physics.Raycast(dropForwardOrigin, Vector3.down, out _, 5f, _mask, QueryTriggerInteraction.Ignore);
			Vector3 dropForwardOrigin2 = origin + _vehicle.forward * (halfWidth + 1.5f);
			dropForwardOrigin2.y += 0.5f;
			bool dropAhead2 = !Physics.Raycast(dropForwardOrigin2, Vector3.down, out _, 5f, _mask, QueryTriggerInteraction.Ignore);
			bool dropAhead = dropAhead1 && dropAhead2;

			bool dropBehind = false;
			if (_dense)
			{
				Vector3 dropBackOrigin = origin - _vehicle.forward * (halfWidth + 0.5f);
				dropBackOrigin.y += 0.5f;
				bool dropBehind1 = !Physics.Raycast(dropBackOrigin, Vector3.down, out _, 5f, _mask, QueryTriggerInteraction.Ignore);
				Vector3 dropBackOrigin2 = origin - _vehicle.forward * (halfWidth + 1.5f);
				dropBackOrigin2.y += 0.5f;
				bool dropBehind2 = !Physics.Raycast(dropBackOrigin2, Vector3.down, out _, 5f, _mask, QueryTriggerInteraction.Ignore);
				dropBehind = dropBehind1 && dropBehind2;
			}

			bool narrowPassage = left < 2f && right < 2f;

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
				FrontDiagonalLeftClearance = fal,
				FrontDiagonalRightClearance = far,
				RearDiagonalLeftClearance = ral,
				RearDiagonalRightClearance = rar,
				HasDropAhead = dropAhead,
				HasDropBehind = dropBehind,
				HasNarrowPassage = narrowPassage,
				PreferredTurnSign = prefer
			};
		}

		private static HashSet<Collider> BuildSelfSet(Transform _vehicle)
		{
			Collider[] selfColliders = _vehicle.GetComponentsInChildren<Collider>(true);
			HashSet<Collider> selfSet = new HashSet<Collider>(selfColliders);

			if (_vehicle.TryGetComponent(out VehicleController vehicleCtrl) &&
			    vehicleCtrl.UnitBlocker != null &&
			    vehicleCtrl.UnitBlocker.IsSolidActive &&
			    vehicleCtrl.UnitBlocker.BlockCollider != null)
			{
				selfSet.Add(vehicleCtrl.UnitBlocker.BlockCollider);
			}
			return selfSet;
		}

		public static bool CanFitTurnRadius(float _radius, Sample _geometry)
		{
			float needed = _radius * 0.7f;
			return _geometry.FrontDiagonalLeftClearance >= needed * 0.8f
			    || _geometry.FrontDiagonalRightClearance >= needed * 0.8f;
		}

		public static bool HasSafeBackingSpace(Sample _geometry, float _minDistance)
		{
			if (_geometry.HasDropBehind)
				return false;
			return _geometry.RearClearance >= _minDistance
			    && _geometry.RearDiagonalLeftClearance >= _minDistance * 0.6f
			    && _geometry.RearDiagonalRightClearance >= _minDistance * 0.6f;
		}

		public static bool HasSafeForwardSpace(Sample _geometry, float _minDistance)
		{
			if (_geometry.HasDropAhead)
				return false;
			return _geometry.FrontClearance >= _minDistance
			    && _geometry.FrontDiagonalLeftClearance >= _minDistance * 0.6f
			    && _geometry.FrontDiagonalRightClearance >= _minDistance * 0.6f;
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
