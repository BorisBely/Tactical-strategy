using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Footprint collision checks along motion primitives / trajectories.
	/// Used only during planning, not every FixedUpdate.
	/// </summary>
	public sealed class SweptVolumeChecker
	{
		private readonly Collider[] m_Hits = new Collider[16];
		public int PhysicsQueries { get; private set; }
		public int PrimitiveQueries { get; private set; }
		public int TrajectoryQueries { get; private set; }

		public void ResetCounters()
		{
			PhysicsQueries = 0;
			PrimitiveQueries = 0;
			TrajectoryQueries = 0;
		}

		public bool IsPrimitiveSafe(
			BicycleKinematics.Primitive _primitive,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			float _sampleStep = 0.4f)
		{
			if (_primitive.Samples == null || _primitive.Samples.Count == 0)
				return false;
			if (_snapshot == null || !_snapshot.IsValid)
				return true;

			LayerMask mask = ObstacleMask(_snapshot);
			HashSet<Collider> self = _snapshot?.SelfColliders;
			float length = _profile != null ? _profile.Length : 4.8f;
			float width = _profile != null ? _profile.Width : 2.4f;
			float margin = _profile != null ? _profile.SafetyMargin : 0.3f;
			Vector3 halfExtents = new Vector3(
				(width + margin * 2f) * 0.5f,
				0.45f,
				(length + margin * 2f) * 0.5f);

			float traveled = 0f;
			float nextSample = 0f;
			for (int i = 0; i < _primitive.Samples.Count; i++)
			{
				TrajectoryPoint p = _primitive.Samples[i];
				if (i > 0)
					traveled = p.ArcLength - _primitive.Samples[0].ArcLength;

				if (i > 0 && traveled + 1e-3f < nextSample && i < _primitive.Samples.Count - 1)
					continue;

				nextSample = traveled + Mathf.Max(0.2f, _sampleStep);
				if (OverlapsFootprint(p.Position, p.YawDegrees, halfExtents, mask, self, true))
					return false;
			}

			return true;
		}

		public bool IsTrajectorySafe(
			VehicleTrajectory _trajectory,
			VehicleKinematicsProfile _profile,
			PlanningObstacleSnapshot _snapshot,
			float _sampleStep = 0.45f)
		{
			if (_trajectory == null || !_trajectory.IsValid)
				return false;
			if (_snapshot == null || !_snapshot.IsValid)
				return true;

			LayerMask mask = ObstacleMask(_snapshot);
			HashSet<Collider> self = _snapshot?.SelfColliders;
			float length = _profile != null ? _profile.Length : 4.8f;
			float width = _profile != null ? _profile.Width : 2.4f;
			float margin = _profile != null ? _profile.SafetyMargin : 0.3f;
			Vector3 halfExtents = new Vector3(
				(width + margin * 2f) * 0.5f,
				0.45f,
				(length + margin * 2f) * 0.5f);

			float nextArc = 0f;
			for (int i = 0; i < _trajectory.PointCount; i++)
			{
				TrajectoryPoint p = _trajectory.Points[i];
				if (i > 0 && p.ArcLength + 1e-3f < nextArc && i < _trajectory.PointCount - 1)
					continue;
				nextArc = p.ArcLength + Mathf.Max(0.2f, _sampleStep);
				if (OverlapsFootprint(p.Position, p.YawDegrees, halfExtents, mask, self, false))
					return false;
			}

			return true;
		}

		private bool OverlapsFootprint(
			Vector3 _position,
			float _yawDegrees,
			Vector3 _halfExtents,
			LayerMask _mask,
			HashSet<Collider> _self,
			bool _isPrimitive)
		{
			// Lift the footprint above ground so terrain colliders are not treated as obstacles.
			Vector3 center = _position + Vector3.up * 1.0f;
			Quaternion rot = Quaternion.Euler(0f, _yawDegrees, 0f);
			int count = Physics.OverlapBoxNonAlloc(
				center, _halfExtents, m_Hits, rot, _mask, QueryTriggerInteraction.Ignore);
			PhysicsQueries++;
			if (_isPrimitive)
				PrimitiveQueries++;
			else
				TrajectoryQueries++;

			for (int i = 0; i < count; i++)
			{
				Collider c = m_Hits[i];
				if (VehicleUnitBlocker.ShouldIgnoreForPlanning(c, _self))
					continue;
				return true;
			}
			return false;
		}

		private static LayerMask ObstacleMask(PlanningObstacleSnapshot _snapshot)
		{
			int mask = _snapshot != null ? (int)_snapshot.Mask : ~0;
			int ground = LayerMask.NameToLayer("Ground");
			if (ground >= 0)
				mask &= ~(1 << ground);
			return mask;
		}
	}
}
