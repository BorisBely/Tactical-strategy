using System.Collections.Generic;
using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Close-pose Reeds–Shepp connector (CSC / CCC / CCCC families) for heading goals inside turning radius.
	/// Ported from standard RS path taxonomy (Atsushi Sakai / Reeds &amp; Shepp).
	/// </summary>
	internal static class ReedsSheppClosePoseSolver
	{
		public struct BuildStats
		{
			public int FormulasGenerated;
			public int IntegrationRejected;
			public int EndpointRejected;
			public int SanitationRejected;
			public int ValidCandidates;

			public string ToSummary()
			{
				return $"rs=f{FormulasGenerated}/i{IntegrationRejected}/e{EndpointRejected}/s{SanitationRejected}/ok{ValidCandidates}";
			}
		}

		private struct RsCandidate
		{
			public float[] LengthsRad;
			public char[] Types;
			public float TotalRad;
			public string Reason;
		}

		public static VehicleTrajectory Build(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase)
		{
			return Build(_from, _fromYaw, _goal, _radius, _wheelBase, out _);
		}

		public static VehicleTrajectory Build(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			out BuildStats _stats)
		{
			_stats = default;
			if (!_goal.HasHeading)
				return null;

			float r = Mathf.Max(1f, _radius);
			ToRsLocalFrame(
				_from, _fromYaw, _goal.Position, _goal.YawDegrees, r,
				out float x, out float y, out float phi);

			var candidates = new List<RsCandidate>(32);
			CollectPaths(x, y, phi, candidates);
			_stats.FormulasGenerated = candidates.Count;
			if (candidates.Count == 0)
				return null;

			candidates.Sort((a, b) => a.TotalRad.CompareTo(b.TotalRad));

			VehicleTrajectory best = null;
			float bestLen = float.MaxValue;
			float dist = BicycleKinematics.FlatDistance(_from, _goal.Position);
			const float lengthTieEps = 0.05f;

			for (int i = 0; i < candidates.Count; i++)
			{
				RsCandidate c = candidates[i];
				VehicleTrajectory traj = IntegrateCandidate(
					_from, _fromYaw, _goal, r, _wheelBase, c, out bool endpointRejected);
				if (traj == null || !traj.IsValid)
				{
					if (endpointRejected)
						_stats.EndpointRejected++;
					else
						_stats.IntegrationRejected++;
					continue;
				}

				if (!ReedsSheppPathBuilder.ValidateTrajectoryEnd(traj, _goal))
				{
					_stats.EndpointRejected++;
					continue;
				}

				if (!ReedsSheppPathBuilder.IsSanitary(traj, dist, r))
				{
					_stats.SanitationRejected++;
					continue;
				}

				if (!TrajectoryKinematicsValidator.Validate(traj, r, out _))
				{
					_stats.IntegrationRejected++;
					continue;
				}

				_stats.ValidCandidates++;
				if (IsBetterCandidate(traj, best, bestLen, lengthTieEps))
				{
					bestLen = traj.TotalLength;
					best = traj;
				}
			}

			return best;
		}

		/// <summary>
		/// Standard RS frame: x forward, y left. Unity yaw is right-positive, so invert phi.
		/// </summary>
		private static void ToRsLocalFrame(
			Vector3 _from,
			float _fromYaw,
			Vector3 _goalPos,
			float _goalYaw,
			float _radius,
			out float _x,
			out float _y,
			out float _phi)
		{
			ReedsSheppPathBuilder.ToLocalFrame(
				_from, _fromYaw, _goalPos, _goalYaw, _radius,
				out _x, out _y, out _phi);
			_phi = -_phi;
		}

		private static bool IsBetterCandidate(
			VehicleTrajectory _candidate,
			VehicleTrajectory _best,
			float _bestLen,
			float _tieEps)
		{
			if (_best == null)
				return true;

			float candidateLen = _candidate.TotalLength;
			if (candidateLen + _tieEps < _bestLen)
				return true;
			if (_bestLen + _tieEps < candidateLen)
				return false;

			TrajectoryGear candidateFirst = _candidate.Points[0].Gear;
			TrajectoryGear bestFirst = _best.Points[0].Gear;
			if (candidateFirst == TrajectoryGear.Reverse && bestFirst != TrajectoryGear.Reverse)
				return true;
			if (bestFirst == TrajectoryGear.Reverse && candidateFirst != TrajectoryGear.Reverse)
				return false;

			return candidateLen < _bestLen;
		}

		private static void CollectPaths(float _x, float _y, float _phi, List<RsCandidate> _out)
		{
			TryAddPath(_out, LeftStraightLeft(_x, _y, _phi), "rs-lsl");
			TryAddPath(_out, LeftStraightRight(_x, _y, _phi), "rs-lsr");
			TryAddPath(_out, LeftXRightXLeft(_x, _y, _phi), "rs-lrl");
			TryAddPath(_out, LeftXRightLeft(_x, _y, _phi), "rs-lrl-");
			TryAddPath(_out, LeftRightXLeft(_x, _y, _phi), "rs-lrl2");
			TryAddPath(_out, LeftRightXLeftRight(_x, _y, _phi), "rs-lrlr");
			TryAddPath(_out, LeftXRightLeftXRight(_x, _y, _phi), "rs-lrlr2");

			TryAddPath(_out, TimeFlip(LeftStraightLeft(-_x, _y, -_phi)), "rs-lsl-tf");
			TryAddPath(_out, TimeFlip(LeftStraightRight(-_x, _y, -_phi)), "rs-lsr-tf");
			TryAddPath(_out, TimeFlip(LeftXRightXLeft(-_x, _y, -_phi)), "rs-lrl-tf");
			TryAddPath(_out, TimeFlip(LeftXRightLeft(-_x, _y, -_phi)), "rs-lrl--tf");
			TryAddPath(_out, TimeFlip(LeftRightXLeft(-_x, _y, -_phi)), "rs-lrl2-tf");
			TryAddPath(_out, TimeFlip(LeftRightXLeftRight(-_x, _y, -_phi)), "rs-lrlr-tf");
			TryAddPath(_out, TimeFlip(LeftXRightLeftXRight(-_x, _y, -_phi)), "rs-lrlr2-tf");

			TryAddPath(_out, ReflectTypes(LeftStraightLeft(_x, -_y, -_phi)), "rs-lsl-ref");
			TryAddPath(_out, ReflectTypes(LeftStraightRight(_x, -_y, -_phi)), "rs-lsr-ref");
			TryAddPath(_out, ReflectTypes(LeftXRightXLeft(_x, -_y, -_phi)), "rs-lrl-ref");
			TryAddPath(_out, ReflectTypes(LeftXRightLeft(_x, -_y, -_phi)), "rs-lrl--ref");
			TryAddPath(_out, ReflectTypes(LeftRightXLeft(_x, -_y, -_phi)), "rs-lrl2-ref");
			TryAddPath(_out, ReflectTypes(LeftRightXLeftRight(_x, -_y, -_phi)), "rs-lrlr-ref");
			TryAddPath(_out, ReflectTypes(LeftXRightLeftXRight(_x, -_y, -_phi)), "rs-lrlr2-ref");

			TryAddPath(_out, TimeFlipReflect(LeftStraightLeft(-_x, -_y, _phi)), "rs-lsl-tr");
			TryAddPath(_out, TimeFlipReflect(LeftStraightRight(-_x, -_y, _phi)), "rs-lsr-tr");
			TryAddPath(_out, TimeFlipReflect(LeftXRightXLeft(-_x, -_y, _phi)), "rs-lrl-tr");
			TryAddPath(_out, TimeFlipReflect(LeftXRightLeft(-_x, -_y, _phi)), "rs-lrl--tr");
			TryAddPath(_out, TimeFlipReflect(LeftRightXLeft(-_x, -_y, _phi)), "rs-lrl2-tr");
			TryAddPath(_out, TimeFlipReflect(LeftRightXLeftRight(-_x, -_y, _phi)), "rs-lrlr-tr");
			TryAddPath(_out, TimeFlipReflect(LeftXRightLeftXRight(-_x, -_y, _phi)), "rs-lrlr2-tr");
		}

		private static RsCandidate? TimeFlip(RsCandidate? _candidate)
		{
			if (!_candidate.HasValue)
				return null;
			RsCandidate c = _candidate.Value;
			var lengths = new float[c.LengthsRad.Length];
			for (int i = 0; i < lengths.Length; i++)
				lengths[i] = -c.LengthsRad[i];
			return new RsCandidate { LengthsRad = lengths, Types = c.Types };
		}

		private static RsCandidate? TimeFlipReflect(RsCandidate? _candidate)
		{
			return TimeFlip(ReflectTypes(_candidate));
		}

		private static void TryAddPath(List<RsCandidate> _out, RsCandidate? _candidate, string _reason)
		{
			if (!_candidate.HasValue)
				return;

			RsCandidate c = _candidate.Value;
			if (c.LengthsRad == null || c.LengthsRad.Length == 0)
				return;

			float total = 0f;
			for (int i = 0; i < c.LengthsRad.Length; i++)
			{
				float len = c.LengthsRad[i];
				if (!IsFinite(len))
					return;
				total += Mathf.Abs(len);
			}

			if (total < 1e-3f || !IsFinite(total))
				return;

			c.TotalRad = total;
			c.Reason = _reason;
			_out.Add(c);
		}

		private static RsCandidate? ReflectTypes(RsCandidate? _candidate)
		{
			if (!_candidate.HasValue)
				return null;

			RsCandidate c = _candidate.Value;
			var types = new char[c.Types.Length];
			for (int i = 0; i < types.Length; i++)
			{
				char t = c.Types[i];
				types[i] = t == 'L' ? 'R' : (t == 'R' ? 'L' : 'S');
			}

			return new RsCandidate
			{
				LengthsRad = c.LengthsRad,
				Types = types,
				TotalRad = c.TotalRad,
				Reason = c.Reason
			};
		}

		private static VehicleTrajectory IntegrateCandidate(
			Vector3 _from,
			float _fromYaw,
			GoalPose _goal,
			float _radius,
			float _wheelBase,
			RsCandidate _candidate,
			out bool _endpointRejected)
		{
			_endpointRejected = false;
			var pts = new List<TrajectoryPoint>();
			Vector3 pos = _from;
			float yaw = _fromYaw;
			float arc = 0f;
			TrajectoryGear prevGear = TrajectoryGear.Forward;
			bool hasPrev = false;
			float cost = 0f;
			const float reversePenalty = 1.35f;
			const float gearSwitchPenalty = 2.5f;

			for (int i = 0; i < _candidate.LengthsRad.Length; i++)
			{
				float lenRad = _candidate.LengthsRad[i];
				if (Mathf.Abs(lenRad) < 1e-4f)
					continue;

				char ctype = _candidate.Types[i];
				float lenM = Mathf.Abs(lenRad) * _radius;
				TrajectoryGear gear = lenRad >= 0f ? TrajectoryGear.Forward : TrajectoryGear.Reverse;
				float curv = 0f;
				if (ctype == 'L')
					curv = -1f / _radius;
				else if (ctype == 'R')
					curv = 1f / _radius;

				if (hasPrev && gear != prevGear && pts.Count > 0)
					ReedsSheppPathBuilder.MarkCuspPoint(pts, pts.Count - 1);

				int sampleCount = ctype == 'S'
					? Mathf.Max(2, Mathf.CeilToInt(lenM / 0.12f))
					: Mathf.Max(4, Mathf.CeilToInt(lenM / 0.08f));
				var prim = BicycleKinematics.Integrate(
					pos, yaw, curv, gear, lenM, _wheelBase, arc, sampleCount);
				if (prim.Samples == null || prim.Samples.Count == 0)
					return null;

				ReedsSheppPathBuilder.AppendTrajectorySegment(pts, prim.Samples, pts.Count > 0);
				arc = prim.Samples[prim.Samples.Count - 1].ArcLength;
				pos = prim.EndPosition;
				yaw = prim.EndYawDegrees;

				float segCost = lenM;
				if (gear == TrajectoryGear.Reverse)
					segCost *= reversePenalty;
				if (hasPrev && gear != prevGear)
					segCost += gearSwitchPenalty;
				cost += segCost;

				prevGear = gear;
				hasPrev = true;
			}

			if (pts.Count < 2)
				return null;

			ReedsSheppPathBuilder.TrySnapTrajectoryEnd(pts, _goal);
			if (!ReedsSheppPathBuilder.ValidateIntegratedPoints(pts, _goal))
			{
				_endpointRejected = true;
				return null;
			}

			var traj = new VehicleTrajectory();
			traj.Build(pts, cost, 0, _candidate.Reason);
			return traj.IsValid ? traj : null;
		}

		private static RsCandidate? MakeCandidate(float[] _lengths, char[] _types)
		{
			if (_lengths == null || _types == null || _lengths.Length != _types.Length)
				return null;
			for (int i = 0; i < _lengths.Length; i++)
			{
				if (!IsFinite(_lengths[i]))
					return null;
			}
			return new RsCandidate { LengthsRad = _lengths, Types = _types };
		}

		private static void Polar(float _x, float _y, out float _r, out float _theta)
		{
			_r = Mathf.Sqrt(_x * _x + _y * _y);
			_theta = Mathf.Atan2(_y, _x);
		}

		private static float Mod2Pi(float _a)
		{
			float v = _a % (2f * Mathf.PI);
			if (v < -Mathf.PI) v += 2f * Mathf.PI;
			else if (v > Mathf.PI) v -= 2f * Mathf.PI;
			return v;
		}

		private static float SafeAcos(float _v)
		{
			return Mathf.Acos(Mathf.Clamp(_v, -1f, 1f));
		}

		private static float SafeAsin(float _v)
		{
			return Mathf.Asin(Mathf.Clamp(_v, -1f, 1f));
		}

		private static bool IsFinite(float _v)
		{
			return !float.IsNaN(_v) && !float.IsInfinity(_v);
		}

		private static RsCandidate? LeftStraightLeft(float _x, float _y, float _phi)
		{
			Polar(_x - Mathf.Sin(_phi), _y - 1f + Mathf.Cos(_phi), out float u, out float t);
			if (t < 0f || t > Mathf.PI)
				return null;
			float v = Mod2Pi(_phi - t);
			if (v < 0f || v > Mathf.PI)
				return null;
			return MakeCandidate(new[] { t, u, v }, new[] { 'L', 'S', 'L' });
		}

		private static RsCandidate? LeftStraightRight(float _x, float _y, float _phi)
		{
			Polar(_x + Mathf.Sin(_phi), _y - 1f - Mathf.Cos(_phi), out float u1, out float t1);
			float u1Sq = u1 * u1;
			if (u1Sq < 4f)
				return null;
			float u = Mathf.Sqrt(u1Sq - 4f);
			float theta = Mathf.Atan2(2f, u);
			float t = Mod2Pi(t1 + theta);
			float v = Mod2Pi(t - _phi);
			if (t < 0f || v < 0f)
				return null;
			return MakeCandidate(new[] { t, u, v }, new[] { 'L', 'S', 'R' });
		}

		private static RsCandidate? LeftXRightXLeft(float _x, float _y, float _phi)
		{
			float zeta = _x - Mathf.Sin(_phi);
			float eeta = _y - 1f + Mathf.Cos(_phi);
			Polar(zeta, eeta, out float u1, out float theta);
			if (u1 > 4f)
				return null;
			float a = SafeAcos(0.25f * u1);
			float t = Mod2Pi(a + theta + Mathf.PI * 0.5f);
			float u = Mod2Pi(Mathf.PI - 2f * a);
			float v = Mod2Pi(_phi - t - u);
			return MakeCandidate(new[] { t, -u, v }, new[] { 'L', 'R', 'L' });
		}

		private static RsCandidate? LeftXRightLeft(float _x, float _y, float _phi)
		{
			float zeta = _x - Mathf.Sin(_phi);
			float eeta = _y - 1f + Mathf.Cos(_phi);
			Polar(zeta, eeta, out float u1, out float theta);
			if (u1 > 4f)
				return null;
			float a = SafeAcos(0.25f * u1);
			float t = Mod2Pi(a + theta + Mathf.PI * 0.5f);
			float u = Mod2Pi(Mathf.PI - 2f * a);
			float v = Mod2Pi(-_phi + t + u);
			return MakeCandidate(new[] { t, -u, -v }, new[] { 'L', 'R', 'L' });
		}

		private static RsCandidate? LeftRightXLeft(float _x, float _y, float _phi)
		{
			float zeta = _x - Mathf.Sin(_phi);
			float eeta = _y - 1f + Mathf.Cos(_phi);
			Polar(zeta, eeta, out float u1, out float theta);
			if (u1 > 4f)
				return null;
			float u = SafeAcos(1f - u1 * u1 * 0.125f);
			float a = Mathf.Abs(u1) > 1e-4f
				? SafeAsin(2f * Mathf.Sin(u) / u1)
				: 0f;
			float t = Mod2Pi(-a + theta + Mathf.PI * 0.5f);
			float v = Mod2Pi(t - u - _phi);
			return MakeCandidate(new[] { t, u, -v }, new[] { 'L', 'R', 'L' });
		}

		private static RsCandidate? LeftRightXLeftRight(float _x, float _y, float _phi)
		{
			float zeta = _x + Mathf.Sin(_phi);
			float eeta = _y - 1f - Mathf.Cos(_phi);
			Polar(zeta, eeta, out float u1, out float theta);
			if (u1 > 2f)
				return null;
			float a = SafeAcos((u1 + 2f) * 0.25f);
			float t = Mod2Pi(theta + a + Mathf.PI * 0.5f);
			float u = Mod2Pi(a);
			float v = Mod2Pi(_phi - t + 2f * u);
			if (t < 0f || u < 0f || v < 0f)
				return null;
			return MakeCandidate(new[] { t, u, -u, -v }, new[] { 'L', 'R', 'L', 'R' });
		}

		private static RsCandidate? LeftXRightLeftXRight(float _x, float _y, float _phi)
		{
			float zeta = _x + Mathf.Sin(_phi);
			float eeta = _y - 1f - Mathf.Cos(_phi);
			Polar(zeta, eeta, out float u1, out float theta);
			float u2 = (20f - u1 * u1) / 16f;
			if (u2 < 0f || u2 > 1f)
				return null;
			float u = SafeAcos(u2);
			float a = Mathf.Abs(u1) > 1e-4f
				? SafeAsin(2f * Mathf.Sin(u) / u1)
				: 0f;
			float t = Mod2Pi(theta + a + Mathf.PI * 0.5f);
			float v = Mod2Pi(t - _phi);
			if (t < 0f || v < 0f)
				return null;
			return MakeCandidate(new[] { t, -u, -u, v }, new[] { 'L', 'R', 'L', 'R' });
		}
	}
}
