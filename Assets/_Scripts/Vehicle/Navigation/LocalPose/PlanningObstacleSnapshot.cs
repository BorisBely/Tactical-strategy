using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Dense obstacle fan built once per replan. Not used during continuous driving.
	/// </summary>
	public sealed class PlanningObstacleSnapshot
	{
		public struct RaySample
		{
			public float AngleDeg;
			public float Clearance;
			public Vector3 Direction;
		}

		public bool IsValid { get; private set; }
		public Vector3 Origin { get; private set; }
		public float YawDegrees { get; private set; }
		public float FrontClearance { get; private set; }
		public float RearClearance { get; private set; }
		public bool HasDropAhead { get; private set; }
		public bool HasDropBehind { get; private set; }
		public IReadOnlyList<RaySample> Rays => m_Rays;
		public HashSet<Collider> SelfColliders => m_Self;
		public LayerMask Mask { get; private set; }
		public float VehicleLength { get; private set; }
		public float VehicleWidth { get; private set; }
		public float SafetyMargin { get; private set; }
		public int RayCount => m_Rays.Count;
		public int PhysicsQueries { get; private set; }

		private readonly List<RaySample> m_Rays = new List<RaySample>(64);
		private HashSet<Collider> m_Self = new HashSet<Collider>();
		private static readonly RaycastHit[] s_RayHits = new RaycastHit[32];
		private static readonly Dictionary<EntityId, HashSet<Collider>> s_SelfColliderCache =
			new Dictionary<EntityId, HashSet<Collider>>(8);

		public static void ClearColliderCache(Transform _vehicle)
		{
			if (_vehicle != null)
				s_SelfColliderCache.Remove(_vehicle.root.GetEntityId());
		}

		private static void CopySelfColliders(Transform _vehicle, HashSet<Collider> _target)
		{
			_target.Clear();
			if (_vehicle == null)
				return;

			EntityId rootId = _vehicle.root.GetEntityId();
			if (!s_SelfColliderCache.TryGetValue(rootId, out HashSet<Collider> cached))
			{
				cached = new HashSet<Collider>(16);
				foreach (Collider col in _vehicle.GetComponentsInChildren<Collider>(true))
					cached.Add(col);
				if (_vehicle.TryGetComponent(out VehicleController vc) &&
				    vc.UnitBlocker != null &&
				    vc.UnitBlocker.IsSolidActive &&
				    vc.UnitBlocker.BlockCollider != null)
					cached.Add(vc.UnitBlocker.BlockCollider);
				s_SelfColliderCache[rootId] = cached;
			}

			foreach (Collider col in cached)
				_target.Add(col);
		}

		public static PlanningObstacleSnapshot Build(
			Transform _vehicle,
			VehicleKinematicsProfile _profile,
			LayerMask _mask,
			int _rayCount,
			float _maxDistance)
		{
			var snap = new PlanningObstacleSnapshot();
			snap.Capture(_vehicle, _profile, _mask, _rayCount, _maxDistance);
			return snap;
		}

		public void Capture(
			Transform _vehicle,
			VehicleKinematicsProfile _profile,
			LayerMask _mask,
			int _rayCount,
			float _maxDistance)
		{
			m_Rays.Clear();
			PhysicsQueries = 0;
			IsValid = false;

			if (_vehicle == null)
				return;

			Mask = _mask;
			VehicleLength = _profile != null ? _profile.Length : 4.8f;
			VehicleWidth = _profile != null ? _profile.Width : 2.4f;
			SafetyMargin = _profile != null ? _profile.SafetyMargin : 0.3f;
			Origin = _vehicle.position + Vector3.up * 0.6f;
			YawDegrees = _vehicle.eulerAngles.y;

			m_Self.Clear();
			CopySelfColliders(_vehicle, m_Self);

			int count = Mathf.Clamp(_rayCount, 8, 96);
			float maxDist = Mathf.Max(2f, _maxDistance);
			float halfWidth = VehicleWidth * 0.5f + SafetyMargin;

			FrontClearance = maxDist;
			RearClearance = maxDist;

			for (int i = 0; i < count; i++)
			{
				float angle = -180f + 360f * i / count;
				Vector3 dir = Quaternion.Euler(0f, angle, 0f) * _vehicle.forward;
				dir.y = 0f;
				if (dir.sqrMagnitude < 1e-6f)
					continue;
				dir.Normalize();

				float clearance = RayClearance(Origin, dir, maxDist, _mask, m_Self);
				PhysicsQueries++;

				float lateralFactor = Mathf.Abs(Vector3.Dot(dir, _vehicle.right));
				clearance = Mathf.Max(0f, clearance - halfWidth * lateralFactor);

				m_Rays.Add(new RaySample
				{
					AngleDeg = angle,
					Clearance = clearance,
					Direction = dir
				});

				float forwardDot = Vector3.Dot(dir, _vehicle.forward);
				if (forwardDot > 0.85f)
					FrontClearance = Mathf.Min(FrontClearance, clearance);
				if (forwardDot < -0.85f)
					RearClearance = Mathf.Min(RearClearance, clearance);
			}

			HasDropAhead = ProbeDrop(_vehicle, true, _mask);
			HasDropBehind = ProbeDrop(_vehicle, false, _mask);
			PhysicsQueries += 4;
			IsValid = true;
		}

		public float ClearanceNearAngle(float _localAngleDeg, float _halfWindowDeg = 15f)
		{
			float best = float.MaxValue;
			for (int i = 0; i < m_Rays.Count; i++)
			{
				float d = Mathf.Abs(Mathf.DeltaAngle(m_Rays[i].AngleDeg, _localAngleDeg));
				if (d <= _halfWindowDeg)
					best = Mathf.Min(best, m_Rays[i].Clearance);
			}
			return best < float.MaxValue ? best : 0f;
		}

		public bool SectorBlocked(float _localAngleDeg, float _minClearance, float _halfWindowDeg = 20f)
		{
			return ClearanceNearAngle(_localAngleDeg, _halfWindowDeg) < _minClearance;
		}

		private bool ProbeDrop(Transform _vehicle, bool _ahead, LayerMask _mask)
		{
			float halfWidth = VehicleWidth * 0.5f;
			Vector3 origin = _vehicle.position + Vector3.up * 1.1f;
			Vector3 fwd = _vehicle.forward;
			float sign = _ahead ? 1f : -1f;
			Vector3 o1 = origin + fwd * sign * (halfWidth + 0.5f);
			Vector3 o2 = origin + fwd * sign * (halfWidth + 1.5f);
			bool miss1 = !Physics.Raycast(o1, Vector3.down, 5f, _mask, QueryTriggerInteraction.Ignore);
			bool miss2 = !Physics.Raycast(o2, Vector3.down, 5f, _mask, QueryTriggerInteraction.Ignore);
			return miss1 && miss2;
		}

		private static float RayClearance(
			Vector3 _origin,
			Vector3 _dir,
			float _max,
			LayerMask _mask,
			HashSet<Collider> _ignore)
		{
			int hitCount = Physics.RaycastNonAlloc(
				_origin, _dir, s_RayHits, _max, _mask, QueryTriggerInteraction.Ignore);
			if (hitCount <= 0)
				return _max;

			float best = _max;
			for (int i = 0; i < hitCount; i++)
			{
				if (VehicleUnitBlocker.ShouldIgnoreForPlanning(s_RayHits[i].collider, _ignore))
					continue;
				best = Mathf.Min(best, s_RayHits[i].distance);
			}

			return best;
		}
	}
}
