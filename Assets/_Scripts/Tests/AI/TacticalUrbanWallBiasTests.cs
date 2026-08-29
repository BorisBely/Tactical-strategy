using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.3 Urban Wall Bias. Preference, not hug-wall. Overlay does not Move. Not CQB.
	/// </summary>
	public sealed class TacticalUrbanWallBiasTests
	{
		#region Nested
		private sealed class ListSource : ICoverCandidateSource
		{
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(8);

			public void Generate(
				CoverRegionId _region,
				Bounds _bounds,
				int _geometryVersion,
				List<CoverCandidate> _destination)
			{
				for (int i = 0; i < Candidates.Count; i++)
					_destination.Add(Candidates[i]);
			}
		}

		private sealed class BlockedHopProbe : ITacticalRoutePathProbe
		{
			public Vector3 Blocked;

			public bool IsDestinationValid(Vector3 _destination)
			{
				return TacticalRouteViability.IsFinitePoint(_destination);
			}

			public bool IsReachable(
				Vector3 _origin,
				Vector3 _destination,
				IReadOnlyList<TacticalRouteWaypoint> _intermediates)
			{
				if (_intermediates == null)
					return true;
				for (int i = 0; i < _intermediates.Count; i++)
				{
					if (CoverSpatialMath.PlanarDistanceSqr(_intermediates[i].Position, Blocked) < 0.36f)
						return false;
				}

				return true;
			}
		}
		#endregion

		#region A No bias when safe
		[Test]
		public void A1_SafeOpen_SelectsDirect()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Normal, false);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Selected.Candidate.Kind);
			Assert.AreEqual(0, decision.Selected.Candidate.Intermediates.Count);
		}

		[Test]
		public void A2_SafeWithWall_DoesNotForceWall()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Normal, false);
			situation.WallAnchors = NorthWall();
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Selected.Candidate.Kind);
			Assert.AreEqual(0f, decision.Selected.Factors.WallBias, 0.0001f);
		}
		#endregion

		#region B Tactical wall preference
		[Test]
		public void B1_TacticalHighExposure_PrefersWallRoute()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, OpenVsWall(0.9f, 0.22f, 0f, 0.55f, 10f, 16f));
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
			Assert.Greater(decision.Selected.Candidate.WallProximity01, 0.5f);
		}
		#endregion

		#region C Weak difference
		[Test]
		public void C1_WeakDifference_DirectMayRemain()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteCandidate open = AuthoredDirect(1, 10f, 6.7f, 0.42f, 0.2f, 0.4f, 0.5f, 0.4f);
			TacticalRouteCandidate wall = AuthoredWaypoint(
				2, new Vector3(5f, 0f, 6f), 10.5f, 7f, 0.42f, 0.2f, 0.4f, 0.5f, 0.4f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { open, wall });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region D Strong exposure
		[Test]
		public void D1_StrongExposureGap_WallWins()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, OpenVsWall(0.88f, 0.2f, 0.05f, 0.5f, 10f, 14f));
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
			Assert.Greater(
				Find(decision, 1).Candidate.Exposure01,
				Find(decision, 2).Candidate.Exposure01);
		}
		#endregion

		#region E Wall corridor
		[Test]
		public void E1_PreferredCorridor_BeatsTooFarAndTooClose()
		{
			float far = TacticalUrbanWallMath.CorridorProximity01(8f);
			float useful = TacticalUrbanWallMath.CorridorProximity01(1.5f);
			float close = TacticalUrbanWallMath.CorridorProximity01(0.05f);
			Assert.Greater(useful, far);
			Assert.Greater(useful, close);
			Assert.Greater(useful, 0.85f);
			Assert.Less(close, 0.5f);
		}
		#endregion

		#region F Wall trap
		[Test]
		public void F1_BlockedWallRoute_NeverSelected()
		{
			var evaluator = new TacticalRouteEvaluator();
			Vector3 blocked = new Vector3(10f, 0f, 6f);
			evaluator.BindProbe(new BlockedHopProbe { Blocked = blocked });
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteCandidate wall = AuthoredWaypoint(
				2, blocked, 16f, 10.7f, 0.2f, 0.7f, 0.25f, 0.5f, 0.9f);
			TacticalRouteDecision decision = evaluator.Evaluate(
				in situation, new[] { AuthoredDirect(1, 10f, 6.7f, 0.7f, 0.1f, 0.6f, 0.5f, 0.1f), wall });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
			Assert.AreEqual(2, decision.CandidateCount);
			Assert.AreEqual(TacticalRouteRejectReason.Unreachable, Find(decision, 2).RejectReason);
		}
		#endregion

		#region G Mission
		[Test]
		public void G1_MissionCanOutweighWall()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.HasObjective = true;
			situation.Objective = new Vector3(20f, 0f, 0f);
			situation.WallAnchors = NorthWall();
			TacticalRouteCandidate direct = AuthoredDirect(1, 10f, 6.7f, 0.45f, 0.1f, 0.4f, 0.95f, 0.15f);
			TacticalRouteCandidate wall = AuthoredWaypoint(
				2, new Vector3(-8f, 0f, 6f), 22f, 14.7f, 0.22f, 0.4f, 0.25f, 0.12f, 0.9f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { direct, wall });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
			Assert.Greater(Find(decision, 1).Factors.MissionProgress, Find(decision, 2).Factors.MissionProgress);
		}
		#endregion

		#region H Cover interaction
		[Test]
		public void H1_SameWall_CoveredBeatsBare()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteCandidate covered = AuthoredWaypoint(
				1, new Vector3(6f, 0f, 6f), 16f, 10.7f, 0.3f, 0.85f, 0.3f, 0.5f, 0.85f);
			TacticalRouteCandidate bare = AuthoredWaypoint(
				2, new Vector3(14f, 0f, 6f), 16.2f, 10.8f, 0.32f, 0.05f, 0.32f, 0.5f, 0.85f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { covered, bare });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
			Assert.AreEqual(Find(decision, 1).Candidate.WallProximity01, Find(decision, 2).Candidate.WallProximity01, 0.001f);
			Assert.Greater(Find(decision, 1).Factors.Cover, Find(decision, 2).Factors.Cover);
		}
		#endregion

		#region I Side choice
		[Test]
		public void I1_LeftRight_UsesTacticalFactors()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.HostileDirection = Vector3.forward;
			situation.HasKnownThreat = true;
			situation.WallAnchors = BothWalls();
			TacticalRouteCandidate left = AuthoredWaypoint(
				1, new Vector3(10f, 0f, 6f), 16f, 10.7f, 0.82f, 0.2f, 0.7f, 0.5f, 0.8f);
			TacticalRouteCandidate right = AuthoredWaypoint(
				2, new Vector3(10f, 0f, -6f), 16.4f, 10.9f, 0.22f, 0.45f, 0.25f, 0.5f, 0.8f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { left, right });
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region Extra
		[Test]
		public void J1_SameInput_SameRoute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.WallAnchors = NorthWall();
			TacticalRouteCandidate[] pair = OpenVsWall(0.9f, 0.22f, 0f, 0.55f, 10f, 16f);
			int selected = evaluator.Evaluate(in situation, pair).Selected.Candidate.CandidateId;
			for (int i = 0; i < 20; i++)
			{
				TacticalRouteDecision again = evaluator.Evaluate(in situation, pair);
				Assert.AreEqual(selected, again.Selected.Candidate.CandidateId);
			}

			Assert.AreEqual(1, evaluator.EvaluationCount);
			Assert.Greater(evaluator.CacheHitCount, 0);
		}

		[Test]
		public void K1_Overlay_DoesNotMove()
		{
			var go = new GameObject("AI143_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
				situation.WallAnchors = NorthWall();
				controller.TacticalMovement.Update(in situation, OpenVsWall(0.9f, 0.22f, 0f, 0.55f, 10f, 16f));
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void L1_SharedCache_Reused()
		{
			var source = new ListSource();
			source.Candidates.Add(Cover(1, new Vector3(8f, 0f, 3.5f)));
			var cache = new SharedCoverSpatialCache(source);
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Tactical, true);
			situation.CoverCache = cache;
			situation.Destination = new Vector3(20f, 0f, 0f);
			var evaluator = new TacticalRouteEvaluator();
			evaluator.Evaluate(in situation, null);
			int generations = cache.GenerationCount;
			Assert.Greater(generations, 0);
			evaluator.Invalidate();
			evaluator.Evaluate(in situation, null);
			Assert.AreEqual(generations, cache.GenerationCount);
			Assert.Greater(cache.CacheHitCount, 0);
		}

		[Test]
		public void M1_Score_SeparatesWallAndExposure()
		{
			TacticalRouteCandidate candidate = AuthoredDirect(1, 10f, 6.7f, 0.8f, 0.1f, 0.7f, 0.5f, 0.9f);
			TacticalRouteScoreFactors withUrban = TacticalRouteScoreMath.EvaluateFactors(
				candidate, TacticalMovementMode.Tactical, true);
			TacticalRouteScoreFactors without = TacticalRouteScoreMath.EvaluateFactors(
				candidate, TacticalMovementMode.Tactical, false);
			Assert.AreEqual(withUrban.Total, withUrban.RebuiltTotal, 0.0001f);
			Assert.Greater(withUrban.WallBias, 0f);
			Assert.AreEqual(0f, without.WallBias);
			Assert.AreEqual(without.Exposure, withUrban.Exposure, 0.0001f);
		}
		#endregion

		#region Helpers
		private static TacticalRouteSituation Sit(TacticalMovementMode _mode, bool _threat)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(20f, 0f, 0f),
				HasDestination = true,
				Mode = _mode,
				HasKnownThreat = _threat,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
			};
		}

		private static TacticalWallAnchor[] NorthWall()
		{
			return new[]
			{
				new TacticalWallAnchor
				{
					Position = new Vector3(10f, 0f, 3.2f),
					Normal = Vector3.back,
					Length = 20f
				}
			};
		}

		private static TacticalWallAnchor[] BothWalls()
		{
			return new[]
			{
				new TacticalWallAnchor { Position = new Vector3(10f, 0f, 7f), Normal = Vector3.back, Length = 20f },
				new TacticalWallAnchor { Position = new Vector3(10f, 0f, -7f), Normal = Vector3.forward, Length = 20f }
			};
		}

		private static TacticalRouteCandidate[] OpenVsWall(
			float _openExposure,
			float _wallExposure,
			float _openCover,
			float _wallCover,
			float _openMeters,
			float _wallMeters)
		{
			return new[]
			{
				AuthoredDirect(1, _openMeters, _openMeters / 1.5f, _openExposure, _openCover, _openExposure * 0.85f, 0.5f, 0.08f),
				AuthoredWaypoint(
					2,
					new Vector3(_wallMeters * 0.5f, 0f, 6f),
					_wallMeters,
					_wallMeters / 1.5f,
					_wallExposure,
					_wallCover,
					_wallExposure * 0.85f,
					0.5f,
					0.88f)
			};
		}

		private static TacticalRouteCandidate AuthoredDirect(
			int _id,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission,
			float _wallProximity)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(_id, Vector3.zero, new Vector3(20f, 0f, 0f));
			ApplyAuthored(
				candidate, _distance, _time, _exposure, _cover, _danger, _mission, _wallProximity);
			return candidate;
		}

		private static TacticalRouteCandidate AuthoredWaypoint(
			int _id,
			Vector3 _hop,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission,
			float _wallProximity)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetWaypoint(_id, Vector3.zero, new Vector3(20f, 0f, 0f), _hop);
			ApplyAuthored(
				candidate, _distance, _time, _exposure, _cover, _danger, _mission, _wallProximity);
			return candidate;
		}

		private static void ApplyAuthored(
			TacticalRouteCandidate _candidate,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission,
			float _wallProximity)
		{
			_candidate.UseAuthoredMetrics = true;
			_candidate.DistanceMeters = _distance;
			_candidate.TravelTimeSeconds = _time;
			_candidate.Exposure01 = _exposure;
			_candidate.Cover01 = _cover;
			_candidate.Danger01 = _danger;
			_candidate.MissionProgress01 = _mission;
			_candidate.WallProximity01 = _wallProximity;
			_candidate.OpenExposure01 = 1f - _wallProximity;
		}

		private static TacticalRouteEvaluation Find(in TacticalRouteDecision _decision, int _id)
		{
			for (int i = 0; i < _decision.Evaluations.Count; i++)
			{
				if (_decision.Evaluations[i].Candidate != null &&
				    _decision.Evaluations[i].Candidate.CandidateId == _id)
					return _decision.Evaluations[i];
			}

			return default;
		}

		private static CoverCandidate Cover(int _id, Vector3 _position)
		{
			return new CoverCandidate
			{
				CandidateId = _id,
				Position = _position,
				Normal = Vector3.forward,
				CoverType = CoverType.Standing,
				StandingValid = true,
				CrouchValid = true,
				NavMeshValid = true,
				StandingProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				CrouchProfile = new CoverProtectionProfile
				{
					Head = 1f, Torso = 1f, Pelvis = 1f, Legs = 1f
				},
				GeometryVersion = 1
			};
		}
		#endregion
	}
}
