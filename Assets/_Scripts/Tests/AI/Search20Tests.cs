using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AI.Tests
{
	public sealed class Search20Tests
	{
		#region A Search Area
		[Test]
		public void A1_LastKnown_CreatesSearchArea()
		{
			Transform target = new GameObject("S10_A1").transform;
			try
			{
				Vector3 lastKnown = new Vector3(32f, 10f, 48f);
				AIPerceptionFrame frame = VisualFrame(RecentlyLostHostile(target, lastKnown, 0.8f));
				Assert.IsTrue(UnitAISearchDecision.TryBuildSearchArea(frame, 10f, out UnitAISearchArea area));
				Assert.AreEqual(UnitAISearchCue.VisualMemory, area.Source);
				Assert.AreEqual(lastKnown, area.Center);
				Assert.AreEqual(UnitAISearchDecision.DefaultAreaRadius, area.Radius);
				Assert.AreEqual(0.8f, area.Confidence, 0.0001f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(target.gameObject);
			}
		}

		[Test]
		public void A2_Sound_CreatesSearchArea()
		{
			Transform source = new GameObject("S10_A2").transform;
			try
			{
				Vector3 soundPos = new Vector3(36f, 10f, 45f);
				AIPerceptionFrame frame = SoundOnlyFrame(source, soundPos, 0.6f, true, SoundEventType.Gunshot);
				Assert.IsTrue(UnitAISearchDecision.TryBuildSearchArea(frame, 4f, out UnitAISearchArea area));
				Assert.AreEqual(UnitAISearchCue.Sound, area.Source);
				Assert.AreEqual(soundPos, area.Center);
				Assert.AreEqual(0.6f, area.Confidence, 0.0001f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(source.gameObject);
			}
		}

		[Test]
		public void A3_Report_CreatesSearchArea()
		{
			Transform reporter = new GameObject("S10_A3R").transform;
			Transform subject = new GameObject("S10_A3S").transform;
			try
			{
				Vector3 pos = new Vector3(8f, 0f, 3f);
				AIPerceptionFrame frame = ReportOnlyFrame(reporter, subject, pos, 0.7f, PerceivedIdentity.Hostile);
				Assert.IsTrue(UnitAISearchDecision.TryBuildSearchArea(frame, 1f, out UnitAISearchArea area));
				Assert.AreEqual(UnitAISearchCue.AllyReport, area.Source);
				Assert.AreEqual(pos, area.Center);
				Assert.AreEqual(0.7f, area.Confidence, 0.0001f);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(reporter.gameObject);
				UnityEngine.Object.DestroyImmediate(subject.gameObject);
			}
		}

		[Test]
		public void A4_ExpiredMemory_NoSearch()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("S10_A4").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.SetPerceptionFrame(VisualFrame(RecentlyLostHostile(target, Vector3.right * 10f, 0f)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void A5_StaleMemory_NoAutonomousSearch()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("S10_A5").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				controller.SetPerceptionFrame(VisualFrame(RecentlyLostHostile(target, Vector3.right * 10f, 0.1f)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
			}
			finally
			{
				Destroy(controller, target);
			}
		}
		#endregion

		#region B Candidate Generation
		[Test]
		public void B1_Area_ProducesCandidates()
		{
			List<UnitAISearchCandidate> candidates = PlanDefault();
			Assert.Greater(candidates.Count, 0);
		}

		[Test]
		public void B2_AllCandidatesLocal()
		{
			UnitAISearchArea area = Area(new Vector3(20f, 0f, 0f), 0.9f, 10f);
			List<UnitAISearchCandidate> candidates = Plan(area, Vector3.zero, 10f, null);
			Assert.Greater(candidates.Count, 0);
			for (int i = 0; i < candidates.Count; i++)
			{
				Assert.IsTrue(UnitSearchNavigationMath.IsInsideSearchArea(
					candidates[i].Position,
					area.Center,
					area.Radius));
			}
		}

		[Test]
		public void B3_UnreachableCandidatesFiltered()
		{
			UnitAISearchArea area = Area(new Vector3(20f, 0f, 0f), 0.9f, 10f);
			var reach = new PredicateReachability(_p => _p.x < 20.01f);
			List<UnitAISearchCandidate> candidates = Plan(area, Vector3.zero, 10f, reach);
			Assert.Greater(candidates.Count, 0);
			for (int i = 0; i < candidates.Count; i++)
				Assert.LessOrEqual(candidates[i].Position.x, 20.01f);
		}

		[Test]
		public void B4_DuplicateCandidatesRemoved()
		{
			List<UnitAISearchCandidate> candidates = PlanDefault();
			for (int i = 0; i < candidates.Count; i++)
			{
				for (int j = i + 1; j < candidates.Count; j++)
				{
					Assert.Greater(
						UnitSearchNavigationMath.PlanarDistance(candidates[i].Position, candidates[j].Position),
						UnitAISearchPlanner.DuplicateRadius);
				}
			}
		}

		[Test]
		public void B5_CandidateCountBounded()
		{
			List<UnitAISearchCandidate> candidates = PlanDefault();
			Assert.LessOrEqual(candidates.Count, UnitAISearchPlanner.MaxSearchCandidates);
		}
		#endregion

		#region C Candidate Ordering
		[Test]
		public void C1_FresherEvidenceWins()
		{
			UnitAISearchArea fresh = Area(new Vector3(20f, 0f, 0f), 0.8f, 10f);
			UnitAISearchArea stale = Area(new Vector3(20f, 0f, 0f), 0.8f, 0f);
			float freshScore = Plan(fresh, Vector3.zero, 10f, null)[0].Score;
			float staleScore = Plan(stale, Vector3.zero, 10f, null)[0].Score;
			Assert.Greater(freshScore, staleScore);
		}

		[Test]
		public void C2_HigherConfidenceWins()
		{
			UnitAISearchArea high = Area(new Vector3(20f, 0f, 0f), 0.95f, 10f);
			UnitAISearchArea low = Area(new Vector3(20f, 0f, 0f), 0.4f, 10f);
			Assert.Greater(
				Plan(high, Vector3.zero, 10f, null)[0].Score,
				Plan(low, Vector3.zero, 10f, null)[0].Score);
		}

		[Test]
		public void C3_UnreachableNeverWins()
		{
			UnitAISearchArea area = Area(new Vector3(20f, 0f, 0f), 0.9f, 10f);
			Vector3 blocked = area.Center;
			var reach = new PredicateReachability(
				_p => UnitSearchNavigationMath.PlanarDistance(_p, blocked) > 1f);
			List<UnitAISearchCandidate> candidates = Plan(area, Vector3.zero, 10f, reach);
			Assert.Greater(candidates.Count, 0);
			for (int i = 0; i < candidates.Count; i++)
				Assert.Greater(UnitSearchNavigationMath.PlanarDistance(candidates[i].Position, blocked), 1f);
		}

		[Test]
		public void C4_SaferEquivalentPointWins()
		{
			List<UnitAISearchCandidate> candidates = PlanDefault();
			Assert.Greater(candidates.Count, 1);
			Vector3 origin = Vector3.zero;
			for (int i = 0; i < candidates.Count - 1; i++)
			{
				float scoreA = candidates[i].Score;
				float scoreB = candidates[i + 1].Score;
				if (Mathf.Abs(scoreA - scoreB) > 0.0001f)
					continue;
				float da = UnitSearchNavigationMath.PlanarDistance(candidates[i].Position, origin);
				float db = UnitSearchNavigationMath.PlanarDistance(candidates[i + 1].Position, origin);
				Assert.LessOrEqual(da, db + 0.0001f);
			}

			int near = IndexNearestX(candidates, 11f);
			int far = IndexNearestX(candidates, 29f);
			if (near >= 0 && far >= 0)
				Assert.Less(near, far);
		}

		[Test]
		public void C5_OrderingDeterministicForSameInput()
		{
			List<UnitAISearchCandidate> a = PlanDefault();
			List<UnitAISearchCandidate> b = PlanDefault();
			Assert.AreEqual(a.Count, b.Count);
			for (int i = 0; i < a.Count; i++)
			{
				Assert.AreEqual(a[i].Position, b[i].Position);
				Assert.AreEqual(a[i].Score, b[i].Score, 0.0001f);
			}
		}
		#endregion

		#region D Search Execution
		[Test]
		public void D1_Search_CandidateA_StopInspect()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.AreEqual(1, recorder.MoveCount);
				Vector3 a = controller.CurrentContext.SearchPosition;
				controller.transform.position = a;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.IsTrue(controller.SearchAreaReached);
				Assert.IsFalse(recorder.HasMoveIntent);
				Assert.AreEqual(0, controller.SearchSession.Index);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void D2_A_NoTarget_GoesToB()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.Greater(controller.SearchSession.Candidates.Count, 1);
				controller.transform.position = controller.CurrentContext.SearchPosition;
				controller.Tick(0.05f);
				controller.Tick(UnitAISearchDecision.InspectDuration);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(1, controller.SearchSession.Index);
				Assert.AreEqual(2, recorder.MoveCount);
				Assert.AreEqual(
					controller.SearchSession.Candidates[1].Position,
					controller.CurrentContext.SearchPosition);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void D3_B_NoTarget_GoesToC()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				Assert.Greater(controller.SearchSession.Candidates.Count, 2);
				AdvanceToCandidate(controller, 1);
				controller.Tick(UnitAISearchDecision.InspectDuration);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(2, controller.SearchSession.Index);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void D4_TargetAppearsAtB_Found()
		{
			(UnitAIController controller, _, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				AdvanceToCandidate(controller, 1);
				controller.SetPerceptionFrame(VisualFrame(VisibleHostile(target)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreEqual(UnitAISearchCompletionReason.Found, controller.LastSearchCompletionReason);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void D5_AllCandidatesExhausted_ReturnState()
		{
			(UnitAIController controller, _, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(20f, 0f, 0f));
				int count = controller.SearchSession.Candidates.Count;
				for (int i = 0; i < count; i++)
				{
					Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
					AdvanceToCandidate(controller, i);
					controller.Tick(UnitAISearchDecision.InspectDuration);
				}

				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAISearchCompletionReason.Exhausted, controller.LastSearchCompletionReason);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void D6_NewCommand_SearchCancelled()
		{
			(UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(21f, 0f, 0f));
				Vector3 retreat = new Vector3(-18f, 0f, 0f);
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Retreat(UnitAIStateContext.ForRetreat(retreat))));
				Assert.AreEqual(UnitAIState.Retreat, controller.CurrentState);
				Assert.AreEqual(UnitAISearchCompletionReason.Cancelled, controller.LastSearchCompletionReason);
				Assert.AreEqual(retreat, recorder.LastDestination);
			}
			finally
			{
				Destroy(controller, target);
			}
		}
		#endregion

		#region E Integration
		[Test]
		public void E1_VisualLoss_SearchArea_Search()
		{
			UnitAIController controller = CreateController();
			Transform target = new GameObject("S10_E1").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Vector3 lastKnown = new Vector3(12f, 0f, 4f);
				controller.SetPerceptionFrame(VisualFrame(RecentlyLostHostile(target, lastKnown)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(UnitAISearchCue.VisualMemory, controller.CurrentContext.SearchCue);
				Assert.AreEqual(lastKnown, controller.CurrentSearchArea.Center);
				Assert.Greater(controller.SearchSession.Candidates.Count, 0);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void E2_Sound_SearchArea_Search()
		{
			UnitAIController controller = CreateController();
			Transform source = new GameObject("S10_E2").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Vector3 soundPos = new Vector3(9f, 0f, 2f);
				controller.SetPerceptionFrame(SoundOnlyFrame(source, soundPos, 0.82f, true, SoundEventType.Gunshot));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(UnitAISearchCue.Sound, controller.CurrentContext.SearchCue);
				Assert.AreEqual(soundPos, controller.CurrentSearchArea.Center);
				Assert.AreEqual(soundPos, controller.CurrentContext.SearchPosition);
			}
			finally
			{
				Destroy(controller, source);
			}
		}

		[Test]
		public void E3_Report_SearchArea_Search()
		{
			UnitAIController controller = CreateController();
			Transform reporter = new GameObject("S10_E3R").transform;
			Transform subject = new GameObject("S10_E3S").transform;
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
				Vector3 pos = new Vector3(6f, 0f, 1f);
				controller.SetPerceptionFrame(
					ReportOnlyFrame(reporter, subject, pos, 0.75f, PerceivedIdentity.Hostile));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreEqual(UnitAISearchCue.AllyReport, controller.CurrentContext.SearchCue);
				Assert.AreEqual(pos, controller.CurrentSearchArea.Center);
			}
			finally
			{
				if (controller != null)
					UnityEngine.Object.DestroyImmediate(controller.gameObject);
				UnityEngine.Object.DestroyImmediate(reporter.gameObject);
				UnityEngine.Object.DestroyImmediate(subject.gameObject);
			}
		}

		[Test]
		public void E4_NewVisualContact_StopEngage()
		{
			(UnitAIController controller, _, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(18f, 0f, 0f));
				controller.SetPerceptionFrame(VisualFrame(VisibleHostile(target)));
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Defense, controller.CurrentState);
				Assert.AreEqual(UnitAIAction.Engage, controller.CurrentAction);
				Assert.AreEqual(CombatIntent.Engage, controller.CurrentCombatIntent);
				Assert.AreEqual(UnitAISearchCompletionReason.Found, controller.LastSearchCompletionReason);
			}
			finally
			{
				Destroy(controller, target);
			}
		}

		[Test]
		public void E5_ImmediateThreat_KeepsSearch()
		{
			(UnitAIController controller, _, Transform target) = CreateWithRecorder();
			try
			{
				StartSearch(controller, target, new Vector3(19f, 0f, 0f));
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Search, controller.CurrentState);
				Assert.AreNotEqual(UnitAISearchCompletionReason.Threat, controller.LastSearchCompletionReason);
			}
			finally
			{
				Destroy(controller, target);
			}
		}
		#endregion

		#region F Overlay hysteresis
		[Test]
		public void F1_Attack_Gunshot_StartsSearch()
		{
			Transform source = new GameObject("S10_F1").transform;
			try
			{
				AIPerceptionFrame frame = SoundOnlyFrame(source, new Vector3(9f, 0f, 2f), 0.8f, true, SoundEventType.Gunshot);
				Assert.IsTrue(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Attack, in frame, 10f, 0f));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(source.gameObject);
			}
		}

		[Test]
		public void F2_Attack_Memory_DuringDwell_DoesNotStartSearch()
		{
			Transform target = new GameObject("S10_F2").transform;
			try
			{
				AIPerceptionFrame frame = VisualFrame(RecentlyLostHostile(target, Vector3.right * 10f));
				Assert.IsFalse(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Attack, in frame, 1f, 0.2f));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(target.gameObject);
			}
		}

		[Test]
		public void F3_Attack_Memory_AfterDwell_StartsSearch()
		{
			Transform target = new GameObject("S10_F3").transform;
			try
			{
				AIPerceptionFrame frame = VisualFrame(RecentlyLostHostile(target, Vector3.right * 10f));
				Assert.IsTrue(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Attack, in frame, 10f, 0f));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(target.gameObject);
			}
		}

		[Test]
		public void F4_Defense_Gunshot_StillStartsSearch()
		{
			Transform source = new GameObject("S10_F4").transform;
			try
			{
				AIPerceptionFrame frame = SoundOnlyFrame(source, new Vector3(9f, 0f, 2f), 0.8f, true, SoundEventType.Gunshot);
				Assert.IsTrue(UnitAISearchDecision.ShouldStartSearch(
					UnitAIState.Defense, in frame, 1f, 0f));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(source.gameObject);
			}
		}

		[Test]
		public void F5_ImmediateThreat_DoesNotStartSearchFromAttack()
		{
			UnitAIController controller = CreateController();
			try
			{
				Assert.IsTrue(controller.TryApplyCommand(UnitAICommand.Attack(
					UnitAIStateContext.ForAttack(Vector3.forward * 2f, Vector3.forward))));
				controller.ImmediateThreat = true;
				controller.Tick(0.05f);
				Assert.AreEqual(UnitAIState.Attack, controller.CurrentState);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(controller.gameObject);
			}
		}
		#endregion

		#region Private Methods
		private static List<UnitAISearchCandidate> PlanDefault()
		{
			return Plan(Area(new Vector3(20f, 0f, 0f), 0.9f, 10f), Vector3.zero, 10f, null);
		}

		private static UnitAISearchArea Area(Vector3 _center, float _confidence, float _timestamp)
		{
			return new UnitAISearchArea(
				_center,
				UnitAISearchDecision.DefaultAreaRadius,
				UnitAISearchCue.VisualMemory,
				_confidence,
				_timestamp);
		}

		private static List<UnitAISearchCandidate> Plan(
			UnitAISearchArea _area,
			Vector3 _origin,
			float _now,
			ISearchReachability _reachability)
		{
			var results = new List<UnitAISearchCandidate>(8);
			UnitAISearchPlanner.Build(in _area, _origin, _now, _reachability, results);
			return results;
		}

		private static int IndexNearestX(List<UnitAISearchCandidate> _candidates, float _x)
		{
			int best = -1;
			float bestDist = 999f;
			for (int i = 0; i < _candidates.Count; i++)
			{
				float dist = Mathf.Abs(_candidates[i].Position.x - _x);
				if (dist >= 2f || dist >= bestDist)
					continue;
				bestDist = dist;
				best = i;
			}

			return best;
		}

		private static void AdvanceToCandidate(UnitAIController _controller, int _index)
		{
			while (_controller.CurrentState == UnitAIState.Search &&
			       _controller.SearchSession != null &&
			       _controller.SearchSession.Index < _index)
			{
				_controller.transform.position = _controller.CurrentContext.SearchPosition;
				_controller.Tick(0.05f);
				_controller.Tick(UnitAISearchDecision.InspectDuration);
			}

			if (_controller.CurrentState == UnitAIState.Search)
			{
				_controller.transform.position = _controller.CurrentContext.SearchPosition;
				_controller.Tick(0.05f);
			}
		}

		private static (UnitAIController controller, UnitMoveCommandRecorder recorder, Transform target)
			CreateWithRecorder()
		{
			var go = new GameObject("S10_Exec");
			UnitMoveCommandRecorder recorder = go.AddComponent<UnitMoveCommandRecorder>();
			UnitAIController controller = go.AddComponent<UnitAIController>();
			Transform target = new GameObject("S10_ExecTarget").transform;
			return (controller, recorder, target);
		}

		private static void StartSearch(UnitAIController _controller, Transform _target, Vector3 _lastKnown)
		{
			Assert.IsTrue(_controller.TryApplyCommand(UnitAICommand.Defense(DefenseCtx(Vector3.zero))));
			_controller.SetPerceptionFrame(VisualFrame(RecentlyLostHostile(_target, _lastKnown)));
			_controller.Tick(0.05f);
			Assert.AreEqual(UnitAIState.Search, _controller.CurrentState);
		}

		private static UnitAIController CreateController()
		{
			return new GameObject("S10_Controller").AddComponent<UnitAIController>();
		}

		private static void Destroy(UnitAIController _controller, Transform _target)
		{
			if (_controller != null)
				UnityEngine.Object.DestroyImmediate(_controller.gameObject);
			if (_target != null)
				UnityEngine.Object.DestroyImmediate(_target.gameObject);
		}

		private static UnitAIStateContext DefenseCtx(Vector3 _anchor)
		{
			return UnitAIStateContext.ForDefense(_anchor, _anchor, 8f, Vector3.forward);
		}

		private static AIPerceptionFrame VisualFrame(params AIContactKnowledge[] _contacts)
		{
			return new AIPerceptionFrame(
				_contacts,
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None);
		}

		private static AIPerceptionFrame SoundOnlyFrame(
			Transform _source,
			Vector3 _position,
			float _confidence,
			bool _hostile,
			SoundEventType _type)
		{
			return new AIPerceptionFrame(
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None,
				new[]
				{
					new AISoundContact(_source, _position, _type, _confidence, 0f, 0.1f, _hostile)
				},
				Array.Empty<AIReportContact>());
		}

		private static AIPerceptionFrame ReportOnlyFrame(
			Transform _reporter,
			Transform _subject,
			Vector3 _position,
			float _confidence,
			PerceivedIdentity _identity)
		{
			return new AIPerceptionFrame(
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				Array.Empty<AIContactKnowledge>(),
				ThreatLevel.None,
				Array.Empty<AISoundContact>(),
				new[]
				{
					new AIReportContact(_reporter, _subject, _position, _identity, _confidence, 1f, 0.2f)
				});
		}

		private static AIContactKnowledge VisibleHostile(Transform _target)
		{
			return Knowledge(
				_target,
				ObservationState.Observed,
				1f,
				true,
				false,
				true,
				false,
				_target.position);
		}

		private static AIContactKnowledge RecentlyLostHostile(
			Transform _target,
			Vector3 _lastKnown,
			float _confidence = 0.95f)
		{
			return Knowledge(
				_target,
				ObservationState.RecentlyLost,
				_confidence,
				false,
				true,
				_confidence > 0.25f,
				_confidence <= 0.25f,
				_lastKnown);
		}

		private static AIContactKnowledge Knowledge(
			Transform _target,
			ObservationState _observation,
			float _lastSeenConfidence,
			bool _visibleNow,
			bool _recentlyLost,
			bool _useful,
			bool _stale,
			Vector3 _lastKnown)
		{
			return new AIContactKnowledge(
				_target,
				DetectionState.Detected,
				_observation,
				PerceivedIdentity.Hostile,
				1f,
				PerceivedRelationship.Hostile,
				ThreatLevel.High,
				_lastKnown,
				_lastKnown,
				12.5f,
				_lastSeenConfidence,
				_visibleNow,
				_recentlyLost,
				_observation == ObservationState.Lost,
				_useful,
				_stale,
				false,
				true,
				false,
				false,
				true,
				false,
				false,
				false,
				true);
		}

		private sealed class PredicateReachability : ISearchReachability
		{
			private readonly Func<Vector3, bool> m_Accept;

			public PredicateReachability(Func<Vector3, bool> _accept)
			{
				m_Accept = _accept;
			}

			public bool TryAccept(Vector3 _from, Vector3 _candidate, out Vector3 _sampled)
			{
				_sampled = _candidate;
				return m_Accept(_candidate);
			}
		}
		#endregion
	}
}
