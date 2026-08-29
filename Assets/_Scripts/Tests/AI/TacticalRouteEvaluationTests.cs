using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	/// <summary>
	/// #14.1 Tactical Route Evaluation. Viability first. Overlay does not Move. Not cover-to-cover.
	/// </summary>
	public sealed class TacticalRouteEvaluationTests
	{
		#region Nested
		private sealed class UnreachableProbe : ITacticalRoutePathProbe
		{
			public bool IsDestinationValid(Vector3 _destination)
			{
				return TacticalRouteViability.IsFinitePoint(_destination);
			}

			public bool IsReachable(
				Vector3 _origin,
				Vector3 _destination,
				IReadOnlyList<TacticalRouteWaypoint> _intermediates)
			{
				return false;
			}
		}

		private sealed class InvalidDestinationProbe : ITacticalRoutePathProbe
		{
			public bool IsDestinationValid(Vector3 _destination)
			{
				return false;
			}

			public bool IsReachable(
				Vector3 _origin,
				Vector3 _destination,
				IReadOnlyList<TacticalRouteWaypoint> _intermediates)
			{
				return true;
			}
		}
		#endregion

		#region A Viability
		[Test]
		public void A1_ValidRoute_Accepted()
		{
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Normal);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { AuthoredDirect(1, 10f, 6.7f, 0.3f, 0.4f, 0.2f, 0.5f) });
			Assert.IsTrue(decision.HasSelection);
			Assert.IsTrue(decision.Selected.Viable);
			Assert.AreEqual(TacticalRouteRejectReason.None, decision.Selected.RejectReason);
			Assert.AreEqual(1, decision.ViableCount);
		}

		[Test]
		public void A2_Unreachable_Rejected()
		{
			var evaluator = new TacticalRouteEvaluator();
			evaluator.BindProbe(new UnreachableProbe());
			TacticalRouteSituation situation = Sit(TacticalMovementMode.Normal);
			TacticalRouteDecision decision = evaluator.Evaluate(
				in situation, new[] { AuthoredDirect(1, 10f, 6.7f, 0.1f, 0.9f, 0.1f, 0.9f) });
			Assert.IsFalse(decision.HasSelection);
			Assert.AreEqual(0, decision.ViableCount);
			Assert.AreEqual(TacticalRouteRejectReason.Unreachable, decision.Evaluations[0].RejectReason);
			Assert.AreEqual(0f, decision.Evaluations[0].Score);
		}

		[Test]
		public void A3_InvalidDestination_Rejected()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteSituation nan = Sit(TacticalMovementMode.Normal);
			nan.Destination = new Vector3(float.NaN, 0f, 0f);
			TacticalRouteDecision missing = evaluator.Evaluate(in nan, null);
			Assert.IsFalse(missing.HasSelection);

			evaluator.BindProbe(new InvalidDestinationProbe());
			TacticalRouteDecision invalid = evaluator.Evaluate(
				Sit(TacticalMovementMode.Normal),
				new[] { AuthoredDirect(1, 8f, 5f, 0.2f, 0.2f, 0.2f, 0.5f) });
			Assert.IsFalse(invalid.HasSelection);
			Assert.AreEqual(TacticalRouteRejectReason.InvalidDestination, invalid.Evaluations[0].RejectReason);
		}
		#endregion

		#region B Distance
		[Test]
		public void B1_SameFactors_ShorterWins()
		{
			TacticalRouteCandidate shorter = AuthoredDirect(1, 10f, 6.7f, 0.3f, 0.4f, 0.2f, 0.5f);
			TacticalRouteCandidate longer = AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 8f),
				20f, 13.3f, 0.3f, 0.4f, 0.2f, 0.5f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Normal), new[] { longer, shorter });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region C Tactical safety
		[Test]
		public void C1_Tactical_CoveredBeatsShortOpen()
		{
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Tactical), OpenVsCovered());
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region D Normal mode
		[Test]
		public void D1_Normal_ShorterFasterWins()
		{
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Normal), OpenVsCovered());
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Selected.Candidate.Kind);
		}
		#endregion

		#region E Mission
		[Test]
		public void E1_MissionProgress_BeatsSaferBackwards()
		{
			Vector3 origin = Vector3.zero;
			Vector3 dest = new Vector3(16f, 0f, 0f);
			TacticalRouteCandidate backwards = AuthoredWaypoint(
				1, origin, dest, new Vector3(-8f, 0f, 0f), 18f, 12f, 0.2f, 0.7f, 0.2f, 0f);
			TacticalRouteCandidate forward = AuthoredDirect(2, 14f, 9.3f, 0.4f, 0.4f, 0.35f, 1f);
			var situation = Sit(TacticalMovementMode.Tactical);
			situation.HasObjective = true;
			situation.Objective = dest;
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				in situation, new[] { backwards, forward });
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region F Cover
		[Test]
		public void F1_Tactical_PrefersRouteNearCover()
		{
			TacticalRouteCandidate open = AuthoredDirect(1, 12f, 8f, 0.85f, 0f, 0.7f, 0.5f);
			TacticalRouteCandidate nearCover = AuthoredWaypoint(
				2, Vector3.zero, new Vector3(12f, 0f, 0f), new Vector3(6f, 0f, 4f),
				12f, 8f, 0.25f, 0.8f, 0.25f, 0.5f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Tactical), new[] { open, nearCover });
			Assert.AreEqual(2, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region G Candidate cap
		[Test]
		public void G1_TwentyGenerated_CappedToMax()
		{
			var evaluator = new TacticalRouteEvaluator { MaxRouteCandidates = 4 };
			var authored = new List<TacticalRouteCandidate>(20);
			for (int i = 0; i < 20; i++)
			{
				authored.Add(AuthoredWaypoint(
					i + 1,
					Vector3.zero,
					new Vector3(20f, 0f, 0f),
					new Vector3(10f, 0f, i * 10f),
					20f + i,
					13f,
					0.4f,
					0.2f,
					0.3f,
					0.5f));
			}

			TacticalRouteDecision decision = evaluator.Evaluate(
				Sit(TacticalMovementMode.Normal), authored);
			Assert.LessOrEqual(decision.CandidateCount, 4);
			Assert.LessOrEqual(decision.Evaluations.Count, 4);
		}
		#endregion

		#region H Diversity
		[Test]
		public void H1_NearIdentical_Deduplicated()
		{
			Vector3 dest = new Vector3(16f, 0f, 0f);
			TacticalRouteCandidate a = AuthoredWaypoint(
				1, Vector3.zero, dest, new Vector3(8f, 0f, 2f), 17f, 11f, 0.4f, 0.2f, 0.3f, 0.5f);
			TacticalRouteCandidate b = AuthoredWaypoint(
				2, Vector3.zero, dest, new Vector3(8.1f, 0f, 2.05f), 17.1f, 11.1f, 0.4f, 0.2f, 0.3f, 0.5f);
			TacticalRouteCandidate c = AuthoredWaypoint(
				3, Vector3.zero, dest, new Vector3(8.2f, 0f, 2.1f), 17.2f, 11.2f, 0.4f, 0.2f, 0.3f, 0.5f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Normal), new[] { a, b, c });
			Assert.AreEqual(1, decision.CandidateCount);
		}
		#endregion

		#region I Determinism
		[Test]
		public void I1_SameInput_SameSelectedRoute()
		{
			var evaluator = new TacticalRouteEvaluator();
			TacticalRouteCandidate[] authored = OpenVsCovered();
			int selected = -1;
			for (int i = 0; i < 100; i++)
			{
				TacticalRouteDecision decision = evaluator.Evaluate(
					Sit(TacticalMovementMode.Tactical), authored);
				Assert.IsTrue(decision.HasSelection);
				int id = decision.Selected.Candidate.CandidateId;
				if (i == 0)
					selected = id;
				else
					Assert.AreEqual(selected, id);
			}

			Assert.AreEqual(2, selected);
			Assert.AreEqual(1, evaluator.EvaluationCount);
			Assert.GreaterOrEqual(evaluator.CacheHitCount, 99);
		}

		[Test]
		public void I2_Tie_BreaksByDistanceThenOrder()
		{
			TacticalRouteCandidate a = AuthoredWaypoint(
				3, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				12f, 8f, 0.3f, 0.4f, 0.2f, 0.5f);
			TacticalRouteCandidate b = AuthoredWaypoint(
				1, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, -6f),
				12f, 8f, 0.3f, 0.4f, 0.2f, 0.5f);
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Normal), new[] { a, b });
			Assert.AreEqual(1, decision.Selected.Candidate.CandidateId);
		}
		#endregion

		#region Extra closed-criteria
		[Test]
		public void Direct_IsBaselineCandidate()
		{
			var generated = new List<TacticalRouteCandidate>(4);
			TacticalRouteGenerator.Generate(
				Sit(TacticalMovementMode.Normal),
				generated,
				TacticalRouteGenerator.DefaultMaxRouteCandidates,
				TacticalRouteGenerator.DefaultDiversityMeters,
				TacticalRouteGenerator.DefaultOffsetMeters);
			Assert.GreaterOrEqual(generated.Count, 2);
			Assert.AreEqual(1, generated[0].CandidateId);
			Assert.AreEqual(TacticalRouteKind.Direct, generated[0].Kind);
			Assert.LessOrEqual(generated.Count, TacticalRouteGenerator.DefaultMaxRouteCandidates);
		}

		[Test]
		public void Overlay_NormalGenerated_SelectsDirect()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalMovementDecision decision = overlay.Update(
				TacticalRouteMath.Goal(Vector3.zero, new Vector3(12f, 0f, 3f), TacticalMovementMode.Normal));
			Assert.IsTrue(decision.HasRoute);
			Assert.AreEqual(TacticalRouteKind.Direct, decision.Kind);
			Assert.AreEqual(new Vector3(12f, 0f, 3f), decision.Destination);
			Assert.AreEqual(decision.Destination, decision.CurrentHop);
			Assert.AreEqual(1, decision.SelectedCandidateId);
		}

		[Test]
		public void Overlay_DoesNotMove()
		{
			var go = new GameObject("AI141_NoMove");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				controller.TacticalMovement.Update(
					Sit(TacticalMovementMode.Tactical), OpenVsCovered());
				Assert.AreEqual(0, recorder.MoveCount);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.IsFalse(controller.TacticalNavigationIssued);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void CoverDestination_IsGoal_NotCoverChain()
		{
			Vector3 c01 = Vector3.zero;
			Vector3 c07 = new Vector3(24f, 0f, 4f);
			var situation = Sit(TacticalMovementMode.Tactical);
			situation.Origin = c01;
			situation.Destination = c07;
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(in situation, null);
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(c07, decision.Selected.Candidate.Destination);
			Assert.LessOrEqual(decision.Selected.Candidate.Intermediates.Count, 1);
		}

		[Test]
		public void Emergency_ModeExists()
		{
			TacticalRouteDecision decision = new TacticalRouteEvaluator().Evaluate(
				Sit(TacticalMovementMode.Emergency), OpenVsCovered());
			Assert.IsTrue(decision.HasSelection);
			Assert.AreEqual(TacticalMovementMode.Emergency, Sit(TacticalMovementMode.Emergency).Mode);
		}

		[Test]
		public void Score_IsExplainable()
		{
			TacticalRouteCandidate candidate = AuthoredDirect(1, 10f, 6.7f, 0.9f, 0f, 0.8f, 0.5f);
			TacticalRouteScoreFactors factors = TacticalRouteScoreMath.EvaluateFactors(
				candidate, TacticalMovementMode.Tactical);
			Assert.AreEqual(factors.Total, factors.RebuiltTotal, 0.0001f);
		}

		[Test]
		public void Executor_WalksSelectedHop_DestinationIntact()
		{
			var overlay = new TacticalMovementOverlay();
			TacticalMovementDecision decision = overlay.Update(
				Sit(TacticalMovementMode.Tactical), OpenVsCovered());
			Assert.AreEqual(2, decision.SelectedCandidateId);
			Assert.AreEqual(new Vector3(10f, 0f, 0f), decision.Destination);
			var go = new GameObject("AI141_Exec");
			try
			{
				UnitAIController controller = go.AddComponent<UnitAIController>();
				UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
				controller.EnsureStarted();
				var nav = new TacticalNavigationExecutor();
				nav.Begin();
				nav.Tick(
					controller,
					true,
					decision.CurrentHop,
					TacticalNavigationMath.DefaultPointArrivalRadius,
					UnitNavigationReason.Attack);
				Assert.AreEqual(1, recorder.MoveCount);
				Assert.AreEqual(decision.CurrentHop, recorder.LastDestination);
				Assert.AreEqual(new Vector3(10f, 0f, 0f), overlay.Last.Destination);
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region Helpers
		private static TacticalRouteSituation Sit(TacticalMovementMode _mode)
		{
			return new TacticalRouteSituation
			{
				Origin = Vector3.zero,
				Destination = new Vector3(10f, 0f, 0f),
				HasDestination = true,
				Mode = _mode,
				WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
			};
		}

		private static TacticalRouteCandidate[] OpenVsCovered()
		{
			return new[]
			{
				AuthoredDirect(1, 10f, 6.7f, 0.9f, 0f, 0.8f, 0.5f),
				AuthoredWaypoint(
					2,
					Vector3.zero,
					new Vector3(10f, 0f, 0f),
					new Vector3(5f, 0f, 6f),
					16f,
					10.7f,
					0.2f,
					0.85f,
					0.25f,
					0.5f)
			};
		}

		private static TacticalRouteCandidate AuthoredDirect(
			int _id,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetDirect(_id, Vector3.zero, new Vector3(10f, 0f, 0f));
			ApplyAuthored(candidate, _distance, _time, _exposure, _cover, _danger, _mission);
			return candidate;
		}

		private static TacticalRouteCandidate AuthoredWaypoint(
			int _id,
			Vector3 _origin,
			Vector3 _destination,
			Vector3 _hop,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission)
		{
			var candidate = new TacticalRouteCandidate();
			candidate.SetWaypoint(_id, _origin, _destination, _hop);
			ApplyAuthored(candidate, _distance, _time, _exposure, _cover, _danger, _mission);
			return candidate;
		}

		private static void ApplyAuthored(
			TacticalRouteCandidate _candidate,
			float _distance,
			float _time,
			float _exposure,
			float _cover,
			float _danger,
			float _mission)
		{
			_candidate.UseAuthoredMetrics = true;
			_candidate.DistanceMeters = _distance;
			_candidate.TravelTimeSeconds = _time;
			_candidate.Exposure01 = _exposure;
			_candidate.Cover01 = _cover;
			_candidate.Danger01 = _danger;
			_candidate.MissionProgress01 = _mission;
		}
		#endregion
	}
}
