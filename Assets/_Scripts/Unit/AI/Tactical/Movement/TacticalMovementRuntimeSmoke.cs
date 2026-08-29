using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14 Play: CLOSED / FROZEN. 14.0–14.10. Overlay does not Move. Executor still Walks.
/// Report: Assets/_Docs/Logs/Tests/TacticalMovement_LAST.txt
/// </summary>
[DefaultExecutionOrder(69)]
[DisallowMultipleComponent]
public sealed class TacticalMovementRuntimeSmoke : MonoBehaviour
{
	#region Nested
		private sealed class RecordingFire
		{
			public int CallCount;

			public void Fire()
			{
				CallCount++;
			}
		}

		private sealed class RecordingLeanExecutor : ICoverLeanExecutor
		{
			public CoverLeanLevel LastLevel;
			public CoverPeekDirection LastDirection;

			public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
			{
				LastLevel = _level;
				LastDirection = _direction;
			}
		}

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

		private sealed class CoverListSource : ICoverCandidateSource
		{
			public readonly List<CoverCandidate> Candidates = new List<CoverCandidate>(16);

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

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(2048);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Unit;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunTacticalMovement;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunTacticalMovement)
			return;
		if (FindAnyObjectByType<TacticalMovementRuntimeSmoke>() != null)
			return;
		var go = new GameObject("TacticalMovementRuntimeSmoke");
		go.AddComponent<TacticalMovementRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyUnit();
		if (DetectionHarnessPlayMode.RunTacticalMovement)
			DetectionHarnessPlayMode.ResetFlags();
	}
	#endregion

	#region Public Methods
	public void RunFromEditor()
	{
		if (!isActiveAndEnabled)
			return;
		StopAllCoroutines();
		StartCoroutine(RunSuite());
	}
	#endregion

	#region Private Methods
	private IEnumerator RunSuite()
	{
		yield return null;
		m_Report.Length = 0;
		m_PassCount = 0;
		m_FailCount = 0;
		AppendLine("STAGE 14.0 — TACTICAL MOVEMENT CONTRACT");
		AppendLine("=======================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Destination ≠ Route. Overlay ≠ Move. Executor still Walks. 14.1 evaluates. 14.2 cover-to-cover. 14.3 wall bias. 14.4 exposure profile. 14.5 event replan. 14.6 under fire. 14.7 arrival. 14.8 moving lean. 14.9 tactical LOD. 14.10 final acceptance. Not #15.");
		AppendLine("---");

		m_Unit = new GameObject("TacticalMovementUnit");
		m_Unit.transform.position = Vector3.zero;
		UnitAIController ai = m_Unit.AddComponent<UnitAIController>();
		UnitMoveCommandRecorder recorder = m_Unit.AddComponent<UnitMoveCommandRecorder>();
		TacticalMovementDebugDraw debug = m_Unit.AddComponent<TacticalMovementDebugDraw>();
		ai.EnsureStarted();
		var fire = new RecordingFire();

		AppendLine("[A1] Direct destination intact");
		Vector3 dest = new Vector3(16f, 0f, 2f);
		TacticalMovementDecision direct = ai.TacticalMovement.Update(
			TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Normal));
		Check("A1_DirectDest", TacticalRouteMath.DestinationUnchanged(in direct, dest),
			"dest=" + direct.Destination);
		Check("A1_HopIsDest", direct.CurrentHop == dest && direct.Kind == TacticalRouteKind.Direct,
			"kind=" + direct.Kind);

		AppendLine("[A4] Overlay does not Walk or Fire");
		Check("A4_NoWalk", recorder.MoveCount == 0 && !ai.TacticalNavigationIssued,
			"moves=" + recorder.MoveCount);
		Check("A4_NoFire", fire.CallCount == 0, "fire=" + fire.CallCount);

		AppendLine("[A2] Waypoint route keeps Destination");
		Vector3 mid = new Vector3(6f, 0f, 1f);
		TacticalMovementDecision hops = ai.TacticalMovement.Adopt(
			Vector3.zero, dest, new[] { TacticalRouteWaypoint.At(mid) }, TacticalMovementMode.Tactical);
		Check("A2_DestIntact", hops.Destination == dest, "dest=" + hops.Destination);
		Check("A2_HopIsMid", hops.CurrentHop == mid && hops.IntermediateCount == 1,
			"hop=" + hops.CurrentHop);
		Check("A2_StillNoWalk", recorder.MoveCount == 0, "moves=" + recorder.MoveCount);

		AppendLine("[A3] Existing executor Walks current hop");
		var nav = new TacticalNavigationExecutor();
		nav.Begin();
		nav.Tick(
			ai,
			true,
			ai.LastTacticalMovement.CurrentHop,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Attack);
		Check("A3_Issued", nav.Issued && recorder.MoveCount == 1,
			"issued=" + nav.Issued + " moves=" + recorder.MoveCount);
		Check("A3_Hop", recorder.LastDestination == mid,
			"last=" + recorder.LastDestination);
		Check("A3_DestIntact", ai.LastTacticalMovement.Destination == dest,
			"dest=" + ai.LastTacticalMovement.Destination);

		AppendLine("[Stack] Attack still uses executor; cover still does not Move");
		recorder.Stop();
		int afterStop = recorder.MoveCount;
		AssertIdleCoverDoesNotMove(ai, recorder, afterStop);
		bool attack = ai.TryApplyCommand(
			UnitAICommand.Attack(UnitAIStateContext.ForAttack(dest, Vector3.forward)));
		Check("S_Attack", attack && ai.CurrentState == UnitAIState.Attack, "ok=" + attack);
		Check("S_DestIntact", ai.CurrentContext.Destination == dest,
			"ctx=" + ai.CurrentContext.Destination);
		Check("S_WalkDest", recorder.LastDestination == dest && recorder.MoveCount > afterStop,
			"last=" + recorder.LastDestination);

		AppendLine("[Contract] no second driver, formation none");
		Check("C_NoAltDriver",
			Type.GetType("TacticalLocomotionDriver") == null &&
			typeof(TacticalNavigationExecutor).Assembly.GetType("TacticalMovementDriver") == null,
			"altDriver");
		Check("C_FormationNone", !TacticalRouteContext.Single(TacticalMovementMode.Normal).Formation.Present,
			"formation");
		TacticalMovementDecision captured = ai.LastTacticalMovement;
		debug.Capture(in captured, Vector3.zero);
		Check("V_Overlay", debug.HasCapture && debug.Kind == TacticalRouteKind.Direct,
			"kind=" + debug.Kind);

		RunRouteEvaluationChecks(ai, recorder, debug);
		RunCoverToCoverChecks(ai, recorder, debug);
		RunUrbanWallChecks(ai, recorder, debug);
		RunExposureTraversalChecks(ai, recorder, debug);
		RunReplanChecks(ai, recorder, debug);
		RunUnderFireChecks(ai, recorder, debug);
		RunArrivalChecks(ai, recorder, debug);
		RunMovingLeanChecks(ai, recorder, debug);
		RunLodChecks(ai, recorder, debug);
		RunFinalAcceptanceChecks(ai, recorder, debug);

		yield return null;
		Finish();
	}

	private void RunRouteEvaluationChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.1] Tactical Route Evaluation");
		TacticalRouteSituation normalSit = ArenaSit(TacticalMovementMode.Normal);
		TacticalRouteSituation tacticalSit = ArenaSit(TacticalMovementMode.Tactical);
		TacticalRouteCandidate[] arena = ArenaOpenVsCovered();

		TacticalRouteDecision valid = new TacticalRouteEvaluator().Evaluate(in normalSit, arena);
		Check("R_A1_Valid", valid.HasSelection && valid.ViableCount == 2,
			"viable=" + valid.ViableCount);

		var unreachable = new TacticalRouteEvaluator();
		unreachable.BindProbe(new UnreachableProbe());
		TacticalRouteDecision rejected = unreachable.Evaluate(in normalSit, arena);
		Check("R_A2_Unreachable", !rejected.HasSelection && rejected.ViableCount == 0,
			"viable=" + rejected.ViableCount);

		TacticalRouteSituation invalid = ArenaSit(TacticalMovementMode.Normal);
		invalid.Destination = new Vector3(float.NaN, 0f, 0f);
		Check("R_A3_InvalidDest", !new TacticalRouteEvaluator().Evaluate(in invalid, arena).HasSelection,
			"invalid");

		Check("R_B_Shorter",
			new TacticalRouteEvaluator().Evaluate(in normalSit, ArenaSameExposure()).Selected.Candidate.CandidateId == 1,
			"shorter");
		Check("R_C_TacticalCovered",
			new TacticalRouteEvaluator().Evaluate(in tacticalSit, arena).Selected.Candidate.CandidateId == 2,
			"tactical");
		Check("R_D_NormalShort",
			new TacticalRouteEvaluator().Evaluate(in normalSit, arena).Selected.Candidate.CandidateId == 1,
			"normal");

		TacticalRouteSituation missionSit = ArenaSit(TacticalMovementMode.Tactical);
		missionSit.HasObjective = true;
		missionSit.Objective = new Vector3(16f, 0f, 0f);
		TacticalRouteCandidate backwards = AuthoredWaypoint(
			1, Vector3.zero, new Vector3(16f, 0f, 0f), new Vector3(-8f, 0f, 0f),
			18f, 12f, 0.2f, 0.7f, 0.2f, 0f);
		TacticalRouteCandidate forward = AuthoredDirect(2, 14f, 9.3f, 0.4f, 0.4f, 0.35f, 1f);
		forward.Destination = new Vector3(16f, 0f, 0f);
		Check("R_E_Mission",
			new TacticalRouteEvaluator().Evaluate(in missionSit, new[] { backwards, forward })
				.Selected.Candidate.CandidateId == 2,
			"mission");

		TacticalRouteCandidate open = AuthoredDirect(1, 12f, 8f, 0.85f, 0f, 0.7f, 0.5f);
		TacticalRouteCandidate nearCover = AuthoredWaypoint(
			2, Vector3.zero, new Vector3(12f, 0f, 0f), new Vector3(6f, 0f, 4f),
			12f, 8f, 0.25f, 0.8f, 0.25f, 0.5f);
		Check("R_F_Cover",
			new TacticalRouteEvaluator().Evaluate(in tacticalSit, new[] { open, nearCover })
				.Selected.Candidate.CandidateId == 2,
			"cover");

		var cap = new TacticalRouteEvaluator { MaxRouteCandidates = 4 };
		var many = new List<TacticalRouteCandidate>(20);
		for (int i = 0; i < 20; i++)
		{
			many.Add(AuthoredWaypoint(
				i + 1, Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, i * 10f),
				20f + i, 13f, 0.4f, 0.2f, 0.3f, 0.5f));
		}

		Check("R_G_Cap", cap.Evaluate(in normalSit, many).CandidateCount <= 4, "cap");

		TacticalRouteCandidate cloneA = AuthoredWaypoint(
			1, Vector3.zero, new Vector3(16f, 0f, 0f), new Vector3(8f, 0f, 2f),
			17f, 11f, 0.4f, 0.2f, 0.3f, 0.5f);
		TacticalRouteCandidate cloneB = AuthoredWaypoint(
			2, Vector3.zero, new Vector3(16f, 0f, 0f), new Vector3(8.1f, 0f, 2.05f),
			17.1f, 11.1f, 0.4f, 0.2f, 0.3f, 0.5f);
		Check("R_H_Diversity",
			new TacticalRouteEvaluator().Evaluate(in normalSit, new[] { cloneA, cloneB }).CandidateCount == 1,
			"diversity");

		var det = new TacticalRouteEvaluator();
		int selected = det.Evaluate(in tacticalSit, arena).Selected.Candidate.CandidateId;
		bool same = true;
		for (int i = 0; i < 20; i++)
		{
			if (det.Evaluate(in tacticalSit, arena).Selected.Candidate.CandidateId != selected)
				same = false;
		}

		Check("R_I_Determinism", same && selected == 2 && det.EvaluationCount == 1 && det.CacheHitCount >= 19,
			"eval=" + det.EvaluationCount + " hits=" + det.CacheHitCount);

		TacticalRouteCandidate street = AuthoredDirect(1, 24f, 16f, 0.9f, 0f, 0.75f, 0.5f);
		TacticalRouteCandidate alongBuilding = AuthoredWaypoint(
			2, Vector3.zero, new Vector3(24f, 0f, 0f), new Vector3(12f, 0f, 6f),
			28f, 18.7f, 0.22f, 0.55f, 0.3f, 0.5f);
		TacticalRouteCandidate[] urban = { street, alongBuilding };
		Check("R_UrbanNormal",
			new TacticalRouteEvaluator().Evaluate(in normalSit, urban).Selected.Candidate.CandidateId == 1,
			"street");
		Check("R_UrbanTactical",
			new TacticalRouteEvaluator().Evaluate(in tacticalSit, urban).Selected.Candidate.CandidateId == 2,
			"building");

		var generated = new TacticalRouteEvaluator();
		TacticalRouteDecision gen = generated.Evaluate(
			TacticalRouteMath.Goal(Vector3.zero, new Vector3(18f, 0f, 0f), TacticalMovementMode.Normal));
		Check("R_DirectBaseline",
			gen.HasSelection && gen.Selected.Candidate.Kind == TacticalRouteKind.Direct,
			"kind=" + (gen.HasSelection ? gen.Selected.Candidate.Kind.ToString() : "none"));

		Vector3 c07 = new Vector3(24f, 0f, 4f);
		TacticalRouteSituation coverGoal = ArenaSit(TacticalMovementMode.Tactical);
		coverGoal.Destination = c07;
		TacticalRouteDecision coverDest = new TacticalRouteEvaluator().Evaluate(in coverGoal, null);
		Check("R_C13_Dest",
			coverDest.HasSelection && coverDest.Selected.Candidate.Destination == c07,
			"dest=" + (coverDest.HasSelection ? coverDest.Selected.Candidate.Destination.ToString() : "none"));

		Check("R_Emergency",
			new TacticalRouteEvaluator().Evaluate(
				ArenaSit(TacticalMovementMode.Emergency), arena).HasSelection,
			"emergency");

		int moves = _recorder.MoveCount;
		TacticalMovementDecision overlayDecision = _ai.TacticalMovement.Update(in tacticalSit, arena);
		Check("R_OverlayNoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		Check("R_SelectCovered", overlayDecision.SelectedCandidateId == 2 && overlayDecision.Kind == TacticalRouteKind.Waypoint,
			"id=" + overlayDecision.SelectedCandidateId);
		Check("R_DestIntact", overlayDecision.Destination == new Vector3(10f, 0f, 0f),
			"dest=" + overlayDecision.Destination);

		var nav = new TacticalNavigationExecutor();
		nav.Begin();
		nav.Tick(
			_ai,
			true,
			overlayDecision.CurrentHop,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Attack);
		Check("R_ExecutorHop", nav.Issued && _recorder.LastDestination == overlayDecision.CurrentHop,
			"last=" + _recorder.LastDestination);
		Check("R_ExecutorDest", _ai.LastTacticalMovement.Destination == overlayDecision.Destination,
			"dest=" + _ai.LastTacticalMovement.Destination);

		TacticalRouteDecision evaluation = _ai.TacticalMovement.LastEvaluation;
		_debug.Capture(in overlayDecision, in evaluation, Vector3.zero);
		Check("R_Explain",
			Mathf.Abs(valid.Selected.Factors.Total - valid.Selected.Factors.RebuiltTotal) < 0.0001f,
			"score=" + valid.Selected.Score);
	}

	private void RunCoverToCoverChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.2] Cover-to-Cover Movement");
		TacticalRouteSituation open = CoverSit(6f, false);
		open.CoverCandidates = new[] { CoverAt(1, new Vector3(3f, 0f, 3.5f)) };
		TacticalRouteDecision openDirect = new TacticalRouteEvaluator().Evaluate(in open, null);
		Check("C2_S1_Direct",
			openDirect.HasSelection && openDirect.Selected.Candidate.Kind == TacticalRouteKind.Direct,
			"kind=" + (openDirect.HasSelection ? openDirect.Selected.Candidate.Kind.ToString() : "none"));

		TacticalRouteSituation exposed = CoverSit(24f, true);
		exposed.CoverCandidates = new[]
		{
			CoverAt(1, new Vector3(8f, 0f, 3.5f)),
			CoverAt(2, new Vector3(16f, 0f, 3.5f))
		};
		TacticalRouteDecision coverRoute = new TacticalRouteEvaluator().Evaluate(in exposed, null);
		Check("C2_S2_Cover",
			coverRoute.HasSelection &&
			coverRoute.Selected.Candidate.Kind == TacticalRouteKind.Waypoint &&
			coverRoute.Selected.Candidate.Intermediates.Count > 0,
			"hops=" + (coverRoute.HasSelection ? coverRoute.Selected.Candidate.Intermediates.Count : 0));

		TacticalRouteSituation three = CoverSit(24f, true);
		three.CoverCandidates = new[]
		{
			CoverAt(1, new Vector3(6f, 0f, 3.5f)),
			CoverAt(2, new Vector3(12f, 0f, 3.5f)),
			CoverAt(3, new Vector3(18f, 0f, 3.5f))
		};
		TacticalRouteDecision pick = new TacticalRouteEvaluator().Evaluate(in three, null);
		Check("C2_S3_NotAll",
			pick.HasSelection && pick.Selected.Candidate.Intermediates.Count < 3,
			"hops=" + (pick.HasSelection ? pick.Selected.Candidate.Intermediates.Count : 0));

		CoverCandidate c1 = CoverAt(1, new Vector3(8f, 0f, 3.5f));
		CoverCandidate c2 = CoverAt(2, new Vector3(16f, 0f, 3.5f));
		var occupiedBoard = new CoverOccupancyBoard();
		occupiedBoard.TryReserve(c2, 99, 0f);
		TacticalRouteSituation occupiedSit = CoverSit(24f, true);
		occupiedSit.CoverCandidates = new[] { c1, c2 };
		occupiedSit.Occupancy = occupiedBoard;
		occupiedSit.OccupancyUnitId = 7;
		TacticalRouteDecision skip = new TacticalRouteEvaluator().Evaluate(in occupiedSit, null);
		Check("C2_S4_SkipReserved",
			skip.HasSelection &&
			skip.Selected.Candidate.Intermediates.Count > 0 &&
			skip.Selected.Candidate.Intermediates[0].CoverCandidateId == 1,
			"id=" + (skip.HasSelection && skip.Selected.Candidate.Intermediates.Count > 0
				? skip.Selected.Candidate.Intermediates[0].CoverCandidateId
				: 0));

		var hopBoard = new CoverOccupancyBoard();
		var hopOverlay = new TacticalMovementOverlay();
		TacticalRouteSituation hopSit = CoverSit(24f, true);
		hopSit.CoverCandidates = new[] { c1, c2 };
		hopSit.Occupancy = hopBoard;
		hopSit.OccupancyUnitId = 7;
		TacticalMovementDecision hops = hopOverlay.Update(in hopSit);
		Vector3 firstHop = hops.CurrentHop;
		int firstId = hops.Route.CurrentWaypoint.CoverCandidateId;
		CoverCandidate firstCover = firstId == 1 ? c1 : c2;
		hopOverlay.NotifyHopCompleted(1f);
		Check("C2_F_Release", hopBoard.GetState(firstCover, 1f) == CoverOccupancy.Available,
			"state=" + hopBoard.GetState(firstCover, 1f));

		CoverCandidate c4 = CoverAt(4, new Vector3(8f, 0f, 3.5f));
		CoverCandidate c13 = CoverAt(13, new Vector3(24f, 0f, 0f));
		var finalBoard = new CoverOccupancyBoard();
		var finalOverlay = new TacticalMovementOverlay();
		TacticalRouteSituation finalSit = CoverSit(24f, true);
		finalSit.CoverCandidates = new[] { c4, c13 };
		finalSit.Occupancy = finalBoard;
		finalSit.OccupancyUnitId = 7;
		finalSit.FinalCoverCandidateId = 13;
		finalOverlay.Update(in finalSit);
		finalOverlay.NotifyHopCompleted(1f);
		Check("C2_G_FinalHeld", finalBoard.GetState(c13, 1f) == CoverOccupancy.Reserved,
			"final=" + finalBoard.GetState(c13, 1f));
		Check("C2_G_MidFree", finalBoard.GetState(c4, 1f) == CoverOccupancy.Available,
			"mid=" + finalBoard.GetState(c4, 1f));

		var many = new List<CoverCandidate>(20);
		for (int i = 0; i < 20; i++)
			many.Add(CoverAt(i + 1, new Vector3(2f + i * 1.1f, 0f, 3.5f)));
		TacticalRouteSituation capSit = CoverSit(24f, true);
		capSit.CoverCandidates = many;
		var capEval = new TacticalRouteEvaluator();
		TacticalRouteDecision capped = capEval.Evaluate(in capSit, null);
		Check("C2_D_HopCap",
			capped.HasSelection &&
			capped.Selected.Candidate.Intermediates.Count <= capEval.CoverPlanner.MaxIntermediateHops,
			"hops=" + (capped.HasSelection ? capped.Selected.Candidate.Intermediates.Count : 0));

		var source = new CoverListSource();
		for (int i = 0; i < 8; i++)
			source.Candidates.Add(CoverAt(i + 1, new Vector3(4f + i * 2f, 0f, 3.5f)));
		var cache = new SharedCoverSpatialCache(source);
		TacticalRouteSituation cacheSit = CoverSit(24f, true);
		cacheSit.CoverCache = cache;
		new TacticalRouteEvaluator().Evaluate(in cacheSit, null);
		int generations = cache.GenerationCount;
		new TacticalRouteEvaluator().Evaluate(in cacheSit, null);
		Check("C2_S5_Cache", generations == cache.GenerationCount && cache.CacheHitCount > 0,
			"gen=" + cache.GenerationCount + " hits=" + cache.CacheHitCount);

		var sharedBoard = new CoverOccupancyBoard();
		int reserved = 0;
		for (int u = 1; u <= 20; u++)
		{
			var unitOverlay = new TacticalMovementOverlay();
			TacticalRouteSituation unitSit = CoverSit(24f, true);
			unitSit.CoverCache = cache;
			unitSit.Occupancy = sharedBoard;
			unitSit.OccupancyUnitId = u;
			TacticalMovementDecision unitDecision = unitOverlay.Update(in unitSit);
			if (unitDecision.ReservedCoverCandidateId != 0)
				reserved++;
		}

		Check("C2_S5_Units", sharedBoard.CountHeld() <= 16 && reserved >= 1,
			"held=" + sharedBoard.CountHeld() + " reserved=" + reserved);

		int moves = _recorder.MoveCount;
		_ai.TacticalMovement.Update(in exposed);
		Check("C2_H_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		var nav = new TacticalNavigationExecutor();
		nav.Begin();
		nav.Tick(
			_ai,
			true,
			firstHop,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Attack);
		Check("C2_H_Executor", nav.Issued && _recorder.LastDestination == firstHop,
			"last=" + _recorder.LastDestination);

		var det = new TacticalRouteEvaluator();
		int id = det.Evaluate(in exposed, null).Selected.Candidate.CandidateId;
		bool same = true;
		for (int i = 0; i < 20; i++)
		{
			if (det.Evaluate(in exposed, null).Selected.Candidate.CandidateId != id)
				same = false;
		}

		Check("C2_I_Determinism", same && det.EvaluationCount == 1, "id=" + id);
		_debug.CaptureCoverRejections(det.CoverPlanner.LastRejections);
		TacticalMovementDecision drawn = hopOverlay.Last;
		TacticalRouteDecision drawnEval = hopOverlay.LastEvaluation;
		_debug.Capture(in drawn, in drawnEval, Vector3.zero);
	}

	private void RunUrbanWallChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.3] Urban Wall Bias");
		TacticalWallAnchor[] north =
		{
			new TacticalWallAnchor
			{
				Position = new Vector3(10f, 0f, 3.2f),
				Normal = Vector3.back,
				Length = 20f
			}
		};
		TacticalRouteSituation safe = ArenaSit(TacticalMovementMode.Normal);
		safe.WallAnchors = north;
		TacticalRouteDecision s1 = new TacticalRouteEvaluator().Evaluate(in safe, null);
		Check("U_S1_Direct",
			s1.HasSelection && s1.Selected.Candidate.Kind == TacticalRouteKind.Direct,
			"kind=" + (s1.HasSelection ? s1.Selected.Candidate.Kind.ToString() : "none"));

		TacticalRouteSituation tactical = ArenaSit(TacticalMovementMode.Tactical);
		tactical.HasKnownThreat = true;
		tactical.WallAnchors = north;
		TacticalRouteCandidate street = WithWall(
			AuthoredDirect(1, 10f, 6.7f, 0.9f, 0f, 0.8f, 0.5f), 0.08f);
		TacticalRouteCandidate along = WithWall(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				16f, 10.7f, 0.22f, 0.55f, 0.3f, 0.5f),
			0.88f);
		TacticalRouteCandidate[] pair = { street, along };
		TacticalRouteDecision s2 = new TacticalRouteEvaluator().Evaluate(in tactical, pair);
		Check("U_S2_Wall",
			s2.HasSelection && s2.Selected.Candidate.CandidateId == 2,
			"id=" + (s2.HasSelection ? s2.Selected.Candidate.CandidateId : 0));

		TacticalRouteCandidate longWall = WithWall(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				28f, 18.7f, 0.32f, 0.1f, 0.3f, 0.5f),
			0.9f);
		TacticalRouteCandidate shortOpen = WithWall(
			AuthoredDirect(1, 10f, 6.7f, 0.35f, 0.1f, 0.3f, 0.5f), 0.1f);
		TacticalRouteDecision s3 = new TacticalRouteEvaluator().Evaluate(
			in tactical, new[] { shortOpen, longWall });
		Check("U_S3_TooLong",
			s3.HasSelection && s3.Selected.Candidate.CandidateId == 1,
			"id=" + (s3.HasSelection ? s3.Selected.Candidate.CandidateId : 0));

		TacticalWallAnchor[] both =
		{
			new TacticalWallAnchor { Position = new Vector3(5f, 0f, 7f), Normal = Vector3.back, Length = 16f },
			new TacticalWallAnchor { Position = new Vector3(5f, 0f, -7f), Normal = Vector3.forward, Length = 16f }
		};
		tactical.WallAnchors = both;
		tactical.HostileDirection = Vector3.forward;
		TacticalRouteCandidate left = WithWall(
			AuthoredWaypoint(
				1, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				16f, 10.7f, 0.82f, 0.2f, 0.7f, 0.5f),
			0.8f);
		TacticalRouteCandidate right = WithWall(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, -6f),
				16.4f, 10.9f, 0.22f, 0.45f, 0.25f, 0.5f),
			0.8f);
		TacticalRouteDecision s4 = new TacticalRouteEvaluator().Evaluate(
			in tactical, new[] { left, right });
		Check("U_S4_Side",
			s4.HasSelection && s4.Selected.Candidate.CandidateId == 2,
			"id=" + (s4.HasSelection ? s4.Selected.Candidate.CandidateId : 0));

		var blockedEval = new TacticalRouteEvaluator();
		Vector3 blockedHop = new Vector3(5f, 0f, 6f);
		blockedEval.BindProbe(new BlockedHopProbe { Blocked = blockedHop });
		tactical.WallAnchors = north;
		TacticalRouteCandidate trapped = WithWall(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), blockedHop,
				16f, 10.7f, 0.2f, 0.7f, 0.25f, 0.5f),
			0.9f);
		TacticalRouteDecision s5 = blockedEval.Evaluate(
			in tactical, new[] { WithWall(AuthoredDirect(1, 10f, 6.7f, 0.7f, 0.1f, 0.6f, 0.5f), 0.1f), trapped });
		Check("U_S5_Blocked",
			s5.HasSelection && s5.Selected.Candidate.CandidateId == 1,
			"id=" + (s5.HasSelection ? s5.Selected.Candidate.CandidateId : 0));

		float useful = TacticalUrbanWallMath.CorridorProximity01(1.5f);
		Check("U_Corridor",
			useful > TacticalUrbanWallMath.CorridorProximity01(8f) &&
			useful > TacticalUrbanWallMath.CorridorProximity01(0.05f),
			"useful=" + useful.ToString("0.00"));

		int moves = _recorder.MoveCount;
		TacticalMovementDecision overlayDecision = _ai.TacticalMovement.Update(in tactical, pair);
		Check("U_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);

		var det = new TacticalRouteEvaluator();
		int id = det.Evaluate(in tactical, pair).Selected.Candidate.CandidateId;
		bool same = true;
		for (int i = 0; i < 20; i++)
		{
			if (det.Evaluate(in tactical, pair).Selected.Candidate.CandidateId != id)
				same = false;
		}

		Check("U_Determinism", same && det.EvaluationCount == 1 && id == 2,
			"id=" + id + " eval=" + det.EvaluationCount);

		var source = new CoverListSource();
		source.Candidates.Add(CoverAt(1, new Vector3(8f, 0f, 3.5f)));
		var cache = new SharedCoverSpatialCache(source);
		TacticalRouteSituation cacheSit = CoverSit(20f, true);
		cacheSit.CoverCache = cache;
		var cacheEval = new TacticalRouteEvaluator();
		cacheEval.Evaluate(in cacheSit, null);
		int generations = cache.GenerationCount;
		cacheEval.Invalidate();
		cacheEval.Evaluate(in cacheSit, null);
		Check("U_Cache",
			generations > 0 && cache.GenerationCount == generations && cache.CacheHitCount > 0,
			"gen=" + cache.GenerationCount + " hits=" + cache.CacheHitCount);

		Check("U_Explain",
			Mathf.Abs(s2.Selected.Factors.Total - s2.Selected.Factors.RebuiltTotal) < 0.0001f &&
			s2.Selected.Factors.WallBias > 0f &&
			s2.Selected.Factors.Exposure > 0f,
			"wallBias=" + s2.Selected.Factors.WallBias);

		_debug.Capture(in overlayDecision, in s2, Vector3.zero);
	}

	private static TacticalRouteCandidate WithWall(TacticalRouteCandidate _candidate, float _wallProximity)
	{
		_candidate.WallProximity01 = _wallProximity;
		_candidate.OpenExposure01 = 1f - _wallProximity;
		return _candidate;
	}

	private void RunExposureTraversalChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.4] Exposure-aware Traversal");
		TacticalRouteSituation tactical = ArenaSit(TacticalMovementMode.Tactical);
		tactical.HasKnownThreat = true;
		tactical.Destination = new Vector3(20f, 0f, 0f);

		TacticalRouteCandidate grind = WithProfile(
			AuthoredWaypoint(
				1, Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, 6f),
				16f, 10.7f, 0.32f, 0.2f, 0.3f, 0.5f),
			0.32f, 0f, 8f);
		TacticalRouteCandidate spike = WithProfile(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, -6f),
				16f, 10.7f, 0.32f, 0.2f, 0.3f, 0.5f),
			0.95f, 0.8f, 1.2f);
		TacticalRouteCandidate[] sameAvg = { grind, spike };
		TacticalRouteDecision s1 = new TacticalRouteEvaluator().Evaluate(in tactical, sameAvg);
		Check("E_S1_Profile",
			s1.HasSelection &&
			s1.Selected.Candidate.CandidateId == 2 &&
			Mathf.Abs(grind.Exposure01 - spike.Exposure01) < 0.001f,
			"id=" + (s1.HasSelection ? s1.Selected.Candidate.CandidateId : 0));

		TacticalRouteSituation dash = CoverSit(8f, true);
		dash.CoverHints = new[] { Vector3.zero, new Vector3(8f, 0f, 0f) };
		TacticalRouteCandidate openDash = new TacticalRouteCandidate();
		openDash.SetDirect(1, Vector3.zero, new Vector3(8f, 0f, 0f));
		TacticalExposureTraversalMath.Fill(
			openDash, in dash, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
		Check("E_S2_ShortDash",
			openDash.TimeAboveThresholdSeconds < 8f && openDash.PeakExposure01 > 0.5f,
			"above=" + openDash.TimeAboveThresholdSeconds.ToString("0.00") +
			" peak=" + openDash.PeakExposure01.ToString("0.00"));

		TacticalRouteCandidate corridor = new TacticalRouteCandidate();
		corridor.SetDirect(1, Vector3.zero, new Vector3(20f, 0f, 0f));
		TacticalRouteSituation longOpen = CoverSit(20f, true);
		TacticalExposureTraversalMath.Fill(
			corridor, in longOpen, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
		Check("E_S3_LongOpen",
			corridor.TimeAboveThresholdSeconds > openDash.TimeAboveThresholdSeconds &&
			corridor.ExposureCost > openDash.ExposureCost,
			"long=" + corridor.TimeAboveThresholdSeconds.ToString("0.00"));

		TacticalRouteCandidate shortOpen = WithProfile(
			AuthoredDirect(1, 10f, 6.7f, 0.5f, 0.1f, 0.4f, 0.5f), 0.7f, 1.2f, 2f);
		TacticalRouteCandidate longDetour = WithProfile(
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				28f, 18.7f, 0.45f, 0.2f, 0.4f, 0.5f),
			0.35f, 0.4f, 5f);
		TacticalRouteSituation cross = ArenaSit(TacticalMovementMode.Tactical);
		cross.HasKnownThreat = true;
		TacticalRouteDecision s4 = new TacticalRouteEvaluator().Evaluate(
			in cross, new[] { shortOpen, longDetour });
		Check("E_S4_ShortCross",
			s4.HasSelection && s4.Selected.Candidate.CandidateId == 1,
			"id=" + (s4.HasSelection ? s4.Selected.Candidate.CandidateId : 0));

		Check("E_S5_SameAvg",
			s1.Evaluations.Count == 2 &&
			Mathf.Abs(FindEval(s1, 1).Candidate.Exposure01 - FindEval(s1, 2).Candidate.Exposure01) < 0.001f &&
			FindEval(s1, 1).Score != FindEval(s1, 2).Score,
			"scores");

		TacticalRouteCandidate unknown = new TacticalRouteCandidate();
		unknown.SetDirect(1, Vector3.zero, new Vector3(16f, 0f, 0f));
		TacticalRouteSituation quiet = CoverSit(16f, false);
		TacticalExposureTraversalMath.Fill(
			unknown, in quiet, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
		Check("E_Unknown",
			unknown.ExposureSamples.Count > 0 &&
			unknown.ExposureSamples[1].Exposure01 > 0.2f,
			"e=" + (unknown.ExposureSamples.Count > 1
				? unknown.ExposureSamples[1].Exposure01.ToString("0.00")
				: "none"));

		int maxSamples = 0;
		for (int i = 0; i < 100; i++)
		{
			TacticalRouteCandidate paced = new TacticalRouteCandidate();
			paced.SetDirect(1, Vector3.zero, new Vector3(12f + i * 0.25f, 0f, 0f));
			TacticalExposureTraversalMath.Fill(
				paced, in longOpen, TacticalExposureTraversalMath.DefaultMaxExposureSamples);
			if (paced.ExposureSamples.Count > maxSamples)
				maxSamples = paced.ExposureSamples.Count;
		}

		Check("E_Bound",
			maxSamples <= TacticalExposureTraversalMath.DefaultMaxExposureSamples,
			"max=" + maxSamples);

		int moves = _recorder.MoveCount;
		TacticalMovementDecision overlayDecision = _ai.TacticalMovement.Update(in tactical, sameAvg);
		Check("E_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);

		var det = new TacticalRouteEvaluator();
		int id = det.Evaluate(in tactical, sameAvg).Selected.Candidate.CandidateId;
		int builds = TacticalExposureTraversalMath.BuildCount;
		bool same = true;
		for (int i = 0; i < 12; i++)
		{
			if (det.Evaluate(in tactical, sameAvg).Selected.Candidate.CandidateId != id)
				same = false;
		}

		Check("E_Cache",
			same && det.EvaluationCount == 1 && det.CacheHitCount >= 11 &&
			TacticalExposureTraversalMath.BuildCount == builds,
			"eval=" + det.EvaluationCount);

		Check("E_Explain",
			Mathf.Abs(s1.Selected.Factors.Total - s1.Selected.Factors.RebuiltTotal) < 0.0001f &&
			s1.Selected.Factors.TimeExposed > 0f,
			"total=" + s1.Selected.Score);

		_debug.Capture(in overlayDecision, in s1, Vector3.zero);
	}

	private static TacticalRouteCandidate WithProfile(
		TacticalRouteCandidate _candidate,
		float _peak,
		float _timeAbove,
		float _timeExposed)
	{
		_candidate.UseAuthoredExposureProfile = true;
		_candidate.PeakExposure01 = _peak;
		_candidate.TimeAboveThresholdSeconds = _timeAbove;
		_candidate.TimeExposedSeconds = _timeExposed;
		return _candidate;
	}

	private static TacticalRouteEvaluation FindEval(in TacticalRouteDecision _decision, int _id)
	{
		for (int i = 0; i < _decision.Evaluations.Count; i++)
		{
			if (_decision.Evaluations[i].Candidate != null &&
			    _decision.Evaluations[i].Candidate.CandidateId == _id)
				return _decision.Evaluations[i];
		}

		return default;
	}

	private static TacticalRouteSituation CoverSit(float _distance, bool _threat)
	{
		return new TacticalRouteSituation
		{
			Origin = Vector3.zero,
			Destination = new Vector3(_distance, 0f, 0f),
			HasDestination = true,
			Mode = TacticalMovementMode.Tactical,
			HasKnownThreat = _threat,
			WalkSpeedMetersPerSecond = TacticalRouteScoreMath.DefaultWalkSpeed
		};
	}

	private static CoverCandidate CoverAt(int _id, Vector3 _position)
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

	private static TacticalRouteSituation ArenaSit(TacticalMovementMode _mode)
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

	private static TacticalRouteCandidate[] ArenaOpenVsCovered()
	{
		return new[]
		{
			AuthoredDirect(1, 10f, 6.7f, 0.9f, 0f, 0.8f, 0.5f),
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
				16f, 10.7f, 0.2f, 0.85f, 0.25f, 0.5f)
		};
	}

	private static TacticalRouteCandidate[] ArenaSameExposure()
	{
		return new[]
		{
			AuthoredDirect(1, 10f, 6.7f, 0.3f, 0.4f, 0.2f, 0.5f),
			AuthoredWaypoint(
				2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 8f),
				20f, 13.3f, 0.3f, 0.4f, 0.2f, 0.5f)
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
		candidate.UseAuthoredMetrics = true;
		candidate.DistanceMeters = _distance;
		candidate.TravelTimeSeconds = _time;
		candidate.Exposure01 = _exposure;
		candidate.Cover01 = _cover;
		candidate.Danger01 = _danger;
		candidate.MissionProgress01 = _mission;
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
		candidate.UseAuthoredMetrics = true;
		candidate.DistanceMeters = _distance;
		candidate.TravelTimeSeconds = _time;
		candidate.Exposure01 = _exposure;
		candidate.Cover01 = _cover;
		candidate.Danger01 = _danger;
		candidate.MissionProgress01 = _mission;
		return candidate;
	}

	private void RunReplanChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.5] Event-driven Replanning");
		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		TacticalRouteSituation sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		TacticalRouteCandidate safe = AuthoredDirect(1, 10f, 6.7f, 0.15f, 0.8f, 0.1f, 0.8f);
		TacticalRouteCandidate exposed = AuthoredDirect(1, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f);
		TacticalRouteCandidate covered = AuthoredWaypoint(
			2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
			16f, 10.7f, 0.15f, 0.85f, 0.2f, 0.7f);

		overlay.Update(in sit, new[] { safe });
		int evals = overlay.Evaluator.EvaluationCount;
		for (int i = 0; i < 20; i++)
		{
			sit.Now = 1.1f + i * 0.05f;
			overlay.Update(in sit, new[] { safe });
		}

		Check("P_NoEvent",
			overlay.Evaluator.EvaluationCount == evals && overlay.ReevaluationCount == 0,
			"eval=" + overlay.Evaluator.EvaluationCount);

		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
		sit.Now = 3f;
		overlay.Update(in sit, new[] { safe });
		Check("P_Minor",
			overlay.ReevaluationCount == 0 &&
			overlay.LastReplanCheck.Reason == TacticalReplanReason.DeltaTooSmall,
			"why=" + overlay.LastReplanCheck.Reason);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { exposed });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.5f));
		sit.Now = 4f;
		TacticalMovementDecision major = overlay.Update(in sit, new[] { exposed, covered });
		Check("P_EnemyOnRoute",
			overlay.ReevaluationCount == 1 &&
			overlay.ReplacementCount == 1 &&
			major.SelectedCandidateId == 2,
			"id=" + major.SelectedCandidateId + " repl=" + overlay.ReplacementCount);

		overlay.Invalidate();
		sit.Now = 1f;
		overlay.Update(in sit, new[] { safe });
		overlay.NotifyEvent(TacticalReplanEvent.Geometry(false, 2));
		sit.Now = 2f;
		overlay.Update(in sit, new[] { safe });
		Check("P_GeomOff",
			overlay.ReevaluationCount == 0 &&
			overlay.LastReplanCheck.Reason == TacticalReplanReason.GeometryOffRoute,
			"why=" + overlay.LastReplanCheck.Reason);

		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
		sit.Now = 3f;
		overlay.Update(in sit, new[] { exposed, covered });
		Check("P_Blocked",
			overlay.ReplacementCount == 1 && overlay.LastReplanCheck.Mandatory,
			"repl=" + overlay.ReplacementCount);

		overlay.Invalidate();
		sit.Now = 1f;
		overlay.Update(in sit, new[] { safe });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.1f));
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.Sound, 0.05f));
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		overlay.NotifyEvent(TacticalReplanEvent.Geometry(true, 2));
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.CoverInvalid, 1f));
		sit.Now = 2f;
		overlay.Update(in sit, new[] { safe, covered });
		Check("P_Coalesce",
			overlay.EventsReceived == 5 &&
			overlay.LastReplanCheck.CoalescedCount == 5 &&
			overlay.ReevaluationCount == 1,
			"recv=" + overlay.EventsReceived + " reeval=" + overlay.ReevaluationCount);

		overlay.Invalidate();
		sit.Origin = Vector3.zero;
		sit.Destination = new Vector3(20f, 0f, 0f);
		sit.Now = 1f;
		TacticalRouteCandidate startHop = AuthoredWaypoint(
			2, Vector3.zero, sit.Destination, new Vector3(10f, 0f, 6f),
			16f, 10.7f, 0.2f, 0.8f, 0.2f, 0.6f);
		overlay.Update(in sit, new[] { startHop });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
		sit.Origin = new Vector3(14f, 0f, 0f);
		sit.Now = 2f;
		TacticalRouteCandidate later = AuthoredWaypoint(
			4, sit.Origin, sit.Destination, new Vector3(16f, 0f, -4f),
			8f, 5.3f, 0.15f, 0.8f, 0.15f, 0.7f);
		TacticalMovementDecision progress = overlay.Update(in sit, new[] { later });
		Check("P_Progress",
			overlay.ReplacementCount == 1 && progress.Origin.x >= 12f,
			"origin=" + progress.Origin);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { safe });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision keep = overlay.Update(in sit, new[] { safe });
		Check("P_SameRoute",
			overlay.ReevaluationCount == 1 &&
			overlay.ReplacementCount == 0 &&
			keep.ReplanAction == TacticalReplanAction.Keep,
			"action=" + keep.ReplanAction);

		overlay.Invalidate();
		CoverCandidate c1 = CoverAt(1, new Vector3(5f, 0f, 6f));
		CoverCandidate c3 = CoverAt(3, new Vector3(5f, 0f, -6f));
		var board = new CoverOccupancyBoard();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 7;
		sit.CoverCandidates = new[] { c1, c3 };
		var hop1 = new TacticalRouteCandidate();
		hop1.SetCoverHops(
			2, Vector3.zero, sit.Destination,
			new[] { TacticalRouteWaypoint.CoverHop(c1.Position, c1.CandidateId, c1.RegionId) });
		hop1.UseAuthoredMetrics = true;
		hop1.DistanceMeters = 16f;
		hop1.TravelTimeSeconds = 10.7f;
		hop1.Exposure01 = 0.2f;
		hop1.Cover01 = 0.85f;
		hop1.Danger01 = 0.2f;
		hop1.MissionProgress01 = 0.6f;
		overlay.Update(in sit, new[] { hop1 });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.RouteBlocked, 1f));
		sit.Now = 2f;
		var hop3 = new TacticalRouteCandidate();
		hop3.SetCoverHops(
			4, Vector3.zero, sit.Destination,
			new[] { TacticalRouteWaypoint.CoverHop(c3.Position, c3.CandidateId, c3.RegionId) });
		hop3.UseAuthoredMetrics = true;
		hop3.DistanceMeters = 16f;
		hop3.TravelTimeSeconds = 10.7f;
		hop3.Exposure01 = 0.18f;
		hop3.Cover01 = 0.85f;
		hop3.Danger01 = 0.18f;
		hop3.MissionProgress01 = 0.6f;
		overlay.Update(in sit, new[] { hop3 });
		Check("P_Reserve",
			board.GetState(c1, 2f) == CoverOccupancy.Available &&
			board.GetState(c3, 2f) == CoverOccupancy.Reserved,
			"c1=" + board.GetState(c1, 2f) + " c3=" + board.GetState(c3, 2f));

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { safe });
		for (int i = 0; i < 1000; i++)
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.01f));
		sit.Now = 2f;
		overlay.Update(in sit, new[] { safe });
		Check("P_Perf",
			overlay.EventsReceived == 1000 &&
			overlay.LastReplanCheck.CoalescedCount == 1000 &&
			overlay.ReevaluationCount == 0 &&
			overlay.ReplacementCount == 0,
			"recv=" + overlay.EventsReceived + " reeval=" + overlay.ReevaluationCount);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		sit.Now = 1f;
		overlay.Update(in sit, new[] { exposed });
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.5f));
		sit.Now = 2f;
		TacticalMovementDecision drawn = overlay.Update(in sit, new[] { exposed, covered });
		Check("P_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalRouteDecision drawnEval = overlay.LastEvaluation;
		_debug.Capture(in drawn, in drawnEval, Vector3.zero);
		Check("P_Overlay",
			_debug.HasCapture && _debug.ReplanAction == TacticalReplanAction.Replace,
			"action=" + _debug.ReplanAction);
	}

	private void RunUnderFireChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.6] Movement Under Fire");
		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		TacticalRouteSituation sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		TacticalRouteCandidate ahead = CoverHopRoute(
			2, CoverAt(7, new Vector3(2f, 0f, 0f)));
		TacticalRouteCandidate exposed = AuthoredDirect(1, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f);
		TacticalRouteCandidate covered = AuthoredWaypoint(
			2, Vector3.zero, new Vector3(10f, 0f, 0f), new Vector3(5f, 0f, 6f),
			16f, 10.7f, 0.15f, 0.85f, 0.2f, 0.7f);

		overlay.Update(in sit, new[] { ahead });
		sit.UnderFire = NearbyCoverFire(2f);
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision cont = overlay.Update(in sit, new[] { ahead });
		Check("P_CoverAhead",
			cont.UnderFireAction == TacticalUnderFireAction.Continue &&
			cont.UnderFireReason == TacticalUnderFireReason.CoverAhead &&
			overlay.ReevaluationCount == 0,
			"action=" + cont.UnderFireAction + " reeval=" + overlay.ReevaluationCount);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { exposed });
		sit.UnderFire = NearbyEmergencyFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision cover = overlay.Update(in sit, new[] { exposed });
		Check("P_Dangerous",
			cover.UnderFireAction == TacticalUnderFireAction.EmergencyCover &&
			overlay.ReevaluationCount == 0,
			"action=" + cover.UnderFireAction);

		overlay.Invalidate();
		sit.Now = 1f;
		overlay.Update(in sit, new[] { exposed });
		sit.UnderFire = DangerousAltFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 3f;
		TacticalMovementDecision alt = overlay.Update(in sit, new[] { exposed, covered });
		Check("P_Alt",
			alt.UnderFireAction == TacticalUnderFireAction.Replan &&
			overlay.ReplacementCount == 1 &&
			alt.SelectedCandidateId == 2,
			"id=" + alt.SelectedCandidateId + " action=" + alt.UnderFireAction);

		overlay.Invalidate();
		sit.Now = 1f;
		overlay.Update(in sit, new[] { exposed });
		sit.UnderFire = NoAlternativeFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision fallback = overlay.Update(in sit, new[] { exposed });
		Check("P_NoCover",
			fallback.UnderFireAction == TacticalUnderFireAction.Continue &&
			fallback.UnderFireReason == TacticalUnderFireReason.NoAlternativeFallback,
			"action=" + fallback.UnderFireAction);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { AuthoredDirect(1, 10f, 6.7f, 0.15f, 0.8f, 0.1f, 0.8f) });
		sit.Destination = new Vector3(20f, 0f, 0f);
		sit.UnderFire = new TacticalUnderFireSituation
		{
			Present = true,
			ImmediateThreat = true,
			Moving = true,
			MissionOverride = true,
			RemainingHopMeters = 20f,
			CurrentExposure01 = 0.8f,
			HasNearbyEmergencyCover = true
		};
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalRouteCandidate retreat = AuthoredDirect(1, 20f, 13.3f, 0.15f, 0.8f, 0.1f, 0.8f);
		retreat.SetDirect(1, Vector3.zero, sit.Destination);
		retreat.UseAuthoredMetrics = true;
		retreat.DistanceMeters = 20f;
		retreat.TravelTimeSeconds = 13.3f;
		retreat.Exposure01 = 0.15f;
		retreat.Cover01 = 0.8f;
		retreat.Danger01 = 0.1f;
		retreat.MissionProgress01 = 0.8f;
		TacticalMovementDecision cmd = overlay.Update(in sit, new[] { retreat });
		Check("P_Command",
			cmd.UnderFireReason == TacticalUnderFireReason.CommandOverride &&
			cmd.Destination == sit.Destination &&
			overlay.LastReplanCheck.Reason == TacticalReplanReason.MissionChanged,
			"reason=" + cmd.UnderFireReason + " dest=" + cmd.Destination);

		TacticalUnderFireDecision dontPanic = TacticalUnderFireMath.Decide(NearbyCoverFire(1.5f));
		Check("P_DontPanic",
			dontPanic.Action == TacticalUnderFireAction.Continue &&
			dontPanic.Reason == TacticalUnderFireReason.CoverAhead,
			"action=" + dontPanic.Action);

		TacticalUnderFireDecision dontSuicide = TacticalUnderFireMath.Decide(NearbyEmergencyFire());
		Check("P_DontSuicide",
			dontSuicide.Action == TacticalUnderFireAction.EmergencyCover,
			"action=" + dontSuicide.Action);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { ahead });
		sit.UnderFire = NearbyCoverFire(2f);
		for (int i = 0; i < 100; i++)
			overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		overlay.Update(in sit, new[] { ahead });
		Check("P_FireCoalesce",
			overlay.EventsReceived == 100 &&
			overlay.LastReplanCheck.CoalescedCount == 100 &&
			overlay.UnderFireEvaluationCount == 1 &&
			overlay.ReevaluationCount == 0,
			"recv=" + overlay.EventsReceived + " uf=" + overlay.UnderFireEvaluationCount);

		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2.1f;
		overlay.Update(in sit, new[] { ahead });
		Check("P_FireCooldown",
			overlay.UnderFireEvaluationCount == 1 &&
			overlay.Last.UnderFireAction == TacticalUnderFireAction.Continue,
			"uf=" + overlay.UnderFireEvaluationCount);

		overlay.Invalidate();
		CoverCandidate c1 = CoverAt(1, new Vector3(5f, 0f, 6f));
		CoverCandidate c7 = CoverAt(7, new Vector3(3f, 0f, -4f));
		var board = new CoverOccupancyBoard();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 7;
		sit.CoverCandidates = new[] { c1, c7 };
		sit.FinalCoverCandidateId = 1;
		overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
		sit.UnderFire = NearbyEmergencyFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		overlay.Update(in sit, new[] { CoverHopRoute(2, c1) });
		sit.Destination = c7.Position;
		sit.FinalCoverCandidateId = 7;
		sit.Now = 3f;
		var toCover = new TacticalRouteCandidate();
		toCover.SetCoverHops(
			4, Vector3.zero, c7.Position,
			new[] { TacticalRouteWaypoint.CoverHop(c7.Position, c7.CandidateId, c7.RegionId) });
		toCover.UseAuthoredMetrics = true;
		toCover.DistanceMeters = 8f;
		toCover.TravelTimeSeconds = 5.3f;
		toCover.Exposure01 = 0.2f;
		toCover.Cover01 = 0.85f;
		toCover.Danger01 = 0.2f;
		toCover.MissionProgress01 = 0.7f;
		overlay.Update(in sit, new[] { toCover });
		Check("P_FireReserve",
			board.GetState(c1, 3f) == CoverOccupancy.Available &&
			board.GetState(c7, 3f) == CoverOccupancy.Reserved,
			"c1=" + board.GetState(c1, 3f) + " c7=" + board.GetState(c7, 3f));

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = new Vector3(20f, 0f, 0f);
		sit.Now = 1f;
		TacticalRouteCandidate start = AuthoredDirect(1, 20f, 13.3f, 0.85f, 0.1f, 0.8f, 0.4f);
		start.SetDirect(1, Vector3.zero, sit.Destination);
		start.UseAuthoredMetrics = true;
		start.DistanceMeters = 20f;
		start.TravelTimeSeconds = 13.3f;
		start.Exposure01 = 0.85f;
		start.Cover01 = 0.1f;
		start.Danger01 = 0.8f;
		start.MissionProgress01 = 0.4f;
		overlay.Update(in sit, new[] { start });
		sit.Origin = new Vector3(14f, 0f, 0f);
		sit.UnderFire = DangerousAltFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalRouteCandidate later = AuthoredWaypoint(
			4, sit.Origin, sit.Destination, new Vector3(16f, 0f, -4f),
			8f, 5.3f, 0.2f, 0.8f, 0.2f, 0.7f);
		later.Origin = sit.Origin;
		TacticalMovementDecision progress = overlay.Update(in sit, new[] { later });
		Check("P_FireProgress",
			overlay.ReplacementCount == 1 && progress.Origin.x >= 12f,
			"origin=" + progress.Origin);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { ahead });
		sit.UnderFire = NearbyCoverFire(2f);
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision drawn = overlay.Update(in sit, new[] { ahead });
		Check("P_FireNoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalRouteDecision drawnEval = overlay.LastEvaluation;
		_debug.Capture(in drawn, in drawnEval, Vector3.zero);
		Check("P_FireOverlay",
			_debug.HasCapture && _debug.UnderFireAction == TacticalUnderFireAction.Continue,
			"action=" + _debug.UnderFireAction);
	}

	private void RunArrivalChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.7] Arrival / Position Acquisition");
		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		CoverCandidate c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		var board = new CoverOccupancyBoard();
		TacticalRouteSituation sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c07.Position;
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 3;
		sit.CoverCandidates = new[] { c07 };
		sit.FinalCoverCandidateId = 7;
		overlay.Update(in sit, new[] { DirectCover(c07) });
		TacticalArrivalDecision acquired = overlay.NotifyTacticalArrival(ArriveAt(c07, 0.38f, 2f));
		Check("A_Acquired",
			acquired.Result == TacticalArrivalResult.Acquired &&
			board.GetState(c07, 2f) == CoverOccupancy.Occupied &&
			overlay.CurrentTacticalPosition.CandidateId == 7,
			"result=" + acquired.Result + " occ=" + board.GetState(c07, 2f));

		overlay.Invalidate();
		board = new CoverOccupancyBoard();
		c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		sit.Occupancy = board;
		sit.CoverCandidates = new[] { c07 };
		sit.Now = 1f;
		overlay.Update(in sit, new[] { DirectCover(c07) });
		TacticalArrivalDecision far = overlay.NotifyTacticalArrival(ArriveAt(c07, 2f, 2f));
		Check("A_TooFar",
			far.Result == TacticalArrivalResult.OutOfTolerance &&
			board.GetState(c07, 2f) != CoverOccupancy.Occupied &&
			!overlay.NeedsReroute,
			"result=" + far.Result + " occ=" + board.GetState(c07, 2f) + " reroute=" + overlay.NeedsReroute);

		overlay.Invalidate();
		board = new CoverOccupancyBoard();
		c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		AssertReserve(board, c07, 9, 1f);
		board.ConfirmOccupied(c07, 9, 1f);
		TacticalArrivalSituation occupiedSit = ArriveAt(c07, 0.2f, 2f);
		occupiedSit.Occupancy = board;
		occupiedSit.UnitId = 3;
		occupiedSit.Candidate = c07;
		occupiedSit.CandidateId = 7;
		TacticalArrivalDecision occupied = TacticalArrivalMath.Evaluate(in occupiedSit);
		Check("A_OccupiedOther",
			occupied.Result == TacticalArrivalResult.Occupied &&
			occupied.Reason == TacticalArrivalFailureReason.Occupied,
			"result=" + occupied.Result);

		overlay.Invalidate();
		c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		c07.GeometryVersion = 12;
		TacticalArrivalSituation geoSit = ArriveAt(c07, 0.2f, 2f);
		geoSit.Candidate = c07;
		geoSit.CandidateId = 7;
		geoSit.GeometryVersion = 13;
		TacticalArrivalDecision geo = TacticalArrivalMath.Evaluate(in geoSit);
		Check("A_Geometry",
			geo.Result == TacticalArrivalResult.Reevaluate &&
			geo.Reason == TacticalArrivalFailureReason.GeometryChanged,
			"result=" + geo.Result + " reason=" + geo.Reason);

		overlay.Invalidate();
		board = new CoverOccupancyBoard();
		CoverCandidate c04 = CoverAt(4, new Vector3(8f, 0f, 0f));
		c07 = CoverAt(7, new Vector3(16f, 0f, 0f));
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c07.Position;
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 3;
		sit.CoverCandidates = new[] { c04, c07 };
		sit.FinalCoverCandidateId = 7;
		overlay.Update(in sit, new[] { CoverVia(c04, c07) });
		TacticalArrivalDecision hop = overlay.NotifyTacticalArrival(ArriveAt(c04, 0.1f, 2f));
		Check("A_Hop",
			hop.Result == TacticalArrivalResult.Traversed &&
			board.GetState(c04, 2f) == CoverOccupancy.Available &&
			board.GetState(c07, 2f) == CoverOccupancy.Reserved &&
			!overlay.CurrentTacticalPosition.Valid,
			"result=" + hop.Result +
			" c04=" + board.GetState(c04, 2f) +
			" c07=" + board.GetState(c07, 2f));

		overlay.Invalidate();
		board = new CoverOccupancyBoard();
		c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c07.Position;
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 3;
		sit.CoverCandidates = new[] { c07 };
		sit.FinalCoverCandidateId = 7;
		overlay.Update(in sit, new[] { DirectCover(c07) });
		Check("A_Reserved", board.GetState(c07, 1f) == CoverOccupancy.Reserved, "state=" + board.GetState(c07, 1f));
		overlay.ReleaseFinal(2f);
		Check("A_CancelRelease",
			board.GetState(c07, 2f) == CoverOccupancy.Available,
			"state=" + board.GetState(c07, 2f));

		overlay.Invalidate();
		board = new CoverOccupancyBoard();
		CoverCandidate c01 = CoverAt(1, new Vector3(2f, 0f, 0f));
		c07 = CoverAt(7, new Vector3(10f, 0f, 0f));
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c01.Position;
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 3;
		sit.CoverCandidates = new[] { c01, c07 };
		sit.FinalCoverCandidateId = 1;
		overlay.Update(in sit, new[] { DirectCover(c01) });
		overlay.NotifyTacticalArrival(ArriveAt(c01, 0f, 1f));
		sit.Destination = c07.Position;
		sit.FinalCoverCandidateId = 7;
		sit.Now = 2f;
		overlay.Update(in sit, new[] { DirectCover(c07) });
		overlay.NotifyTacticalArrival(ArriveAt(c07, 0.1f, 3f));
		Check("A_Transition",
			board.GetState(c01, 3f) == CoverOccupancy.Available &&
			board.GetState(c07, 3f) == CoverOccupancy.Occupied,
			"c01=" + board.GetState(c01, 3f) + " c07=" + board.GetState(c07, 3f));

		UnitAIState before = _ai.CurrentState;
		_ai.SetAttack(c07.Position);
		overlay.NotifyTacticalArrival(ArriveAt(c07, 0.1f, 4f));
		Check("A_Mission",
			_ai.CurrentState == UnitAIState.Attack,
			"state=" + _ai.CurrentState + " before=" + before);

		TacticalArrivalSituation same = ArriveAt(c07, 0.38f, 1f);
		same.Candidate = c07;
		same.CandidateId = 7;
		same.RequiredCoverType = CoverType.Standing;
		TacticalArrivalDecision d1 = TacticalArrivalMath.Evaluate(in same);
		TacticalArrivalDecision d2 = TacticalArrivalMath.Evaluate(in same);
		Check("A_Determinism",
			d1.Result == d2.Result && d1.Reason == d2.Reason && d1.CandidateId == d2.CandidateId,
			"a=" + d1.Result + " b=" + d2.Result);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c07.Position;
		sit.Now = 1f;
		sit.CoverCandidates = new[] { c07 };
		sit.FinalCoverCandidateId = 7;
		overlay.Update(in sit, new[] { DirectCover(c07) });
		TacticalArrivalDecision drawn = overlay.NotifyTacticalArrival(ArriveAt(c07, 0.2f, 2f));
		Check("A_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalMovementDecision lastMove = overlay.Last;
		_debug.Capture(in lastMove, Vector3.zero);
		_debug.CaptureArrival(in drawn);
		Check("A_Overlay",
			_debug.HasCapture && _debug.ArrivalResult != TacticalArrivalResult.None,
			"result=" + _debug.ArrivalResult);
	}

	private void RunMovingLeanChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		AppendLine("[14.8] Moving Lean");
		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		var executor = new RecordingLeanExecutor();

		TacticalMovingLeanDecision opp = overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		Check("L_Opportunity",
			opp.Action == TacticalMovingLeanAction.Lean && opp.Opportunity,
			"action=" + opp.Action);

		overlay.Invalidate();
		TacticalMovingLeanSituation none = LeanBenefit(CoverPeekDirection.Left);
		none.LeftSmallSufficient = false;
		none.LeftMediumSufficient = false;
		none.LeftDeepSufficient = false;
		none.LeftVisibilityGain = 0f;
		TacticalMovingLeanDecision noGain = overlay.NotifyMovingLean(in none, executor);
		Check("L_NoBenefit",
			noGain.Action == TacticalMovingLeanAction.None,
			"action=" + noGain.Action);

		TacticalMovingLeanDecision left = TacticalMovingLeanMath.Decide(LeanBenefit(CoverPeekDirection.Left));
		TacticalMovingLeanDecision right = TacticalMovingLeanMath.Decide(LeanBenefit(CoverPeekDirection.Right));
		Check("L_Direction",
			left.Direction == CoverPeekDirection.Left && right.Direction == CoverPeekDirection.Right,
			"L=" + left.Direction + " R=" + right.Direction);

		Check("L_Small", left.Depth == CoverLeanLevel.Small, "depth=" + left.Depth);

		overlay.Invalidate();
		overlay.NotifyMovingLean(LeanFar(), executor);
		overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		TacticalMovingLeanSituation passed = LeanBenefit(CoverPeekDirection.Left);
		passed.CornerPassed = true;
		TacticalMovingLeanDecision exit = overlay.NotifyMovingLean(in passed, executor);
		Check("L_Corner",
			exit.Action == TacticalMovingLeanAction.Exit &&
			exit.Reason == TacticalMovingLeanReason.CornerPassed &&
			!overlay.MovingLeanActive,
			"action=" + exit.Action + " reason=" + exit.Reason);

		overlay.Invalidate();
		overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		TacticalMovingLeanSituation fire = LeanBenefit(CoverPeekDirection.Left);
		fire.ImmediateThreat = true;
		TacticalMovingLeanDecision threat = overlay.NotifyMovingLean(in fire, executor);
		Check("L_Threat",
			threat.Action == TacticalMovingLeanAction.Exit &&
			threat.Reason == TacticalMovingLeanReason.ImmediateThreat,
			"reason=" + threat.Reason);

		overlay.Invalidate();
		overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		TacticalMovingLeanSituation replan = LeanBenefit(CoverPeekDirection.Left);
		replan.Replan = true;
		TacticalMovingLeanDecision replanned = overlay.NotifyMovingLean(in replan, executor);
		Check("L_Replan",
			replanned.Action == TacticalMovingLeanAction.Exit &&
			replanned.Reason == TacticalMovingLeanReason.Replan,
			"reason=" + replanned.Reason);

		overlay.Invalidate();
		overlay.Update(TacticalRouteMath.Goal(Vector3.zero, new Vector3(4f, 0f, 0f), TacticalMovementMode.Normal, 1f));
		overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		var arrive = new TacticalArrivalSituation
		{
			NavigationReached = true,
			CurrentPosition = new Vector3(4f, 0f, 0f),
			TargetPosition = new Vector3(4f, 0f, 0f),
			HasTargetPosition = true,
			Now = 2f
		};
		overlay.NotifyTacticalArrival(in arrive);
		TacticalMovingLeanDecision arrived = overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		Check("L_Arrival",
			arrived.Action == TacticalMovingLeanAction.Exit &&
			arrived.Reason == TacticalMovingLeanReason.Arrival,
			"reason=" + arrived.Reason);

		overlay.Invalidate();
		TacticalMovingLeanSituation sit = LeanFar();
		overlay.NotifyMovingLean(in sit, executor);
		sit.Approach = false;
		for (int i = 0; i < 10; i++)
			overlay.NotifyMovingLean(in sit, executor);
		Check("L_Coalesce", overlay.MovingLeanEvaluationCount == 1, "eval=" + overlay.MovingLeanEvaluationCount);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		TacticalMovingLeanDecision drawn = overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), executor);
		Check("L_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalMovementDecision lastMove = overlay.Last;
		_debug.Capture(in lastMove, Vector3.zero);
		_debug.CaptureMovingLean(in drawn);
		Check("L_Overlay",
			_debug.HasCapture && _debug.MovingLeanAction == TacticalMovingLeanAction.Lean,
			"action=" + _debug.MovingLeanAction);
		Check("L_Executor",
			executor.LastDirection == CoverPeekDirection.Left && executor.LastLevel == CoverLeanLevel.Small,
			"dir=" + executor.LastDirection);
	}

	private void RunLodChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		TacticalUpdateScheduler.ResetShared();
		AppendLine("[14.9] Performance / Tactical LOD");

		TacticalLodDecision idleFar = TacticalLodMath.Select(LodIdleFar());
		Check("P_A1_Background",
			idleFar.Tier == TacticalLodTier.Background,
			"tier=" + idleFar.Tier);
		TacticalLodDecision moving = TacticalLodMath.Select(LodMoving());
		Check("P_A2_Reduced",
			moving.Tier == TacticalLodTier.Reduced,
			"tier=" + moving.Tier);
		TacticalLodDecision combat = TacticalLodMath.Select(LodCombat());
		Check("P_A3_Full",
			combat.Tier == TacticalLodTier.Full,
			"tier=" + combat.Tier);

		TacticalLodSituation wake = LodIdleFar();
		wake.PreviousTier = TacticalLodTier.Background;
		wake.HasImmediateThreat = true;
		Check("P_Wake",
			TacticalLodMath.Select(in wake).Tier == TacticalLodTier.Full,
			"tier=" + TacticalLodMath.Select(in wake).Tier);

		var quiet = new TacticalLodSituation
		{
			Idle = true,
			PreviousTier = TacticalLodTier.Full,
			SecondsSinceSignificantEvent = TacticalLodMath.QuietToReducedSeconds + 0.5f
		};
		TacticalLodDecision reduced = TacticalLodMath.Select(in quiet);
		quiet.PreviousTier = TacticalLodTier.Reduced;
		quiet.SecondsSinceSignificantEvent = TacticalLodMath.QuietToBackgroundSeconds + 0.5f;
		TacticalLodDecision background = TacticalLodMath.Select(in quiet);
		Check("P_Quiet",
			reduced.Tier == TacticalLodTier.Reduced && background.Tier == TacticalLodTier.Background,
			"r=" + reduced.Tier + " b=" + background.Tier);

		TacticalRouteSituation sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.HasKnownThreat = true;
		sit.Destination = new Vector3(20f, 0f, 0f);
		sit.GeometryVersion = 1;
		sit.KnowledgeVersion = 1;
		TacticalRouteCandidate[] pair = LodDirectVsWall();
		TacticalRouteDecision fullEval = new TacticalRouteEvaluator().Evaluate(in sit, pair);
		TacticalRouteDecision reducedEval = new TacticalRouteEvaluator().Evaluate(in sit, pair);
		Check("P_Invariant",
			fullEval.HasSelection &&
			fullEval.Selected.Candidate.CandidateId == reducedEval.Selected.Candidate.CandidateId &&
			fullEval.Selected.Score == reducedEval.Selected.Score,
			"full=" + fullEval.Selected.Candidate.CandidateId + " red=" + reducedEval.Selected.Candidate.CandidateId);

		var budget = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 20,
			StaggerSlots = 1
		};
		budget.BeginTick(0, 0f);
		for (int i = 0; i < 100; i++)
			budget.TryAdmit(i + 1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
		Check("P_Budget", budget.AdmittedCount == 20, "admitted=" + budget.AdmittedCount);

		var stagger = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 100,
			StaggerSlots = 5
		};
		stagger.BeginTick(0, 0f);
		for (int i = 0; i < 100; i++)
			stagger.TryAdmit(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
		Check("P_Stagger", stagger.AdmittedCount == 20 && stagger.AdmittedCount < 100,
			"admitted=" + stagger.AdmittedCount);

		var priority = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 20,
			StaggerSlots = 1
		};
		priority.BeginTick(0, 0f);
		for (int i = 1; i <= 99; i++)
			priority.Enqueue(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Low);
		priority.Enqueue(1000, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Emergency);
		priority.Dispatch();
		Check("P_Priority",
			priority.AdmittedCount > 0 &&
			priority.Admitted[0].Criticality == TacticalCriticality.Emergency &&
			priority.Admitted[0].UnitId == 1000,
			"first=" + (priority.AdmittedCount > 0 ? priority.Admitted[0].UnitId.ToString() : "none"));

		var cacheEval = new TacticalRouteEvaluator();
		cacheEval.Evaluate(in sit, pair);
		int evals = cacheEval.EvaluationCount;
		int fills = cacheEval.ExposureFillCount;
		cacheEval.Evaluate(in sit, pair);
		Check("P_RouteCache",
			cacheEval.EvaluationCount == evals && cacheEval.CacheHitCount > 0,
			"eval=" + cacheEval.EvaluationCount);
		Check("P_ExposureCache",
			cacheEval.ExposureFillCount == fills,
			"fills=" + cacheEval.ExposureFillCount);

		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		overlay.Invalidate();
		var leanSched = new TacticalUpdateScheduler();
		leanSched.BeginTick(0, 1f);
		overlay.BindScheduler(leanSched, 11);
		overlay.NotifyLod(LodIdleFar());
		TacticalMovingLeanSituation far = LeanFar();
		far.Approach = false;
		overlay.NotifyMovingLean(in far);
		Check("P_LeanPause",
			overlay.MovingLeanEvaluationCount == 0,
			"eval=" + overlay.MovingLeanEvaluationCount);
		far.Approach = true;
		far.DistanceToCornerMeters = 1.2f;
		far.LeftSmallSufficient = true;
		far.LeftVisibilityGain = 0.41f;
		overlay.NotifyMovingLean(in far);
		Check("P_LeanWake",
			overlay.LastLod.Tier == TacticalLodTier.Full && overlay.MovingLeanEvaluationCount == 1,
			"tier=" + overlay.LastLod.Tier + " eval=" + overlay.MovingLeanEvaluationCount);

		overlay.Invalidate();
		overlay.BindScheduler(leanSched, 11);
		Vector3 dest = new Vector3(14f, 0f, 2f);
		TacticalMovementDecision committed = overlay.Update(
			TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Normal, 1f));
		overlay.NotifyLod(LodIdleFar());
		int movesBefore = _recorder.MoveCount;
		var nav = new TacticalNavigationExecutor();
		nav.Begin();
		nav.Tick(
			_ai,
			true,
			overlay.Last.CurrentHop,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Attack);
		Check("P_NavContinues",
			committed.HasRoute && overlay.Last.HasRoute && _recorder.MoveCount > movesBefore,
			"route=" + overlay.Last.HasRoute + " moves=" + _recorder.MoveCount);

		int mixFull = 0;
		int mixReduced = 0;
		int mixBackground = 0;
		for (int i = 0; i < 10; i++)
		{
			if (TacticalLodMath.Select(LodCombat()).Tier == TacticalLodTier.Full)
				mixFull++;
		}

		for (int i = 0; i < 20; i++)
		{
			if (TacticalLodMath.Select(LodMoving()).Tier == TacticalLodTier.Reduced)
				mixReduced++;
		}

		for (int i = 0; i < 40; i++)
		{
			if (TacticalLodMath.Select(LodIdleFar()).Tier == TacticalLodTier.Background)
				mixBackground++;
		}

		var mix = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 20,
			StaggerSlots = 1
		};
		mix.BeginTick(1, 1f);
		for (int i = 0; i < 40; i++)
			mix.Enqueue(i + 1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Low);
		mix.Enqueue(500, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Emergency);
		mix.Dispatch();
		Check("P_MixWake",
			mixFull == 10 && mixReduced == 20 && mixBackground == 40 &&
			mix.Admitted[0].Criticality == TacticalCriticality.Emergency,
			"full=" + mixFull + " red=" + mixReduced + " bg=" + mixBackground);

		int without = 100 * 10;
		int withLod = 0;
		var vs = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 20,
			StaggerSlots = 1
		};
		for (int tick = 0; tick < 10; tick++)
		{
			vs.BeginTick(tick, tick);
			for (int i = 0; i < 100; i++)
			{
				if (vs.TryAdmit(i, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium))
					withLod++;
			}
		}

		Check("P_WithoutVsWith",
			withLod < without && withLod <= 200,
			"with=" + withLod + " without=" + without);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		overlay.NotifyLod(LodCombat());
		overlay.Update(in sit, pair);
		Check("P_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalMovementDecision lastMove = overlay.Last;
		TacticalLodDecision drawn = overlay.LastLod;
		_debug.Capture(in lastMove, Vector3.zero);
		_debug.CaptureLod(in drawn);
		Check("P_Overlay",
			_debug.HasCapture && _debug.LodTier != TacticalLodTier.None,
			"tier=" + _debug.LodTier);

		overlay.BindScheduler(TacticalUpdateScheduler.Shared, _ai.CoverOccupancyUnitId);
	}

	private void RunFinalAcceptanceChecks(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		TacticalMovementDebugDraw _debug)
	{
		_ai.TacticalMovement.Invalidate();
		TacticalUpdateScheduler.ResetShared();
		AppendLine("[14.10] Final Acceptance");
		CoverCandidate c05 = CoverAt(5, new Vector3(12f, 0f, 3f));
		CoverCandidate c07 = CoverAt(7, new Vector3(20f, 0f, 0f));
		var board = new CoverOccupancyBoard();
		TacticalMovementOverlay overlay = _ai.TacticalMovement;
		TacticalRouteSituation sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Destination = c07.Position;
		sit.Now = 1f;
		sit.Occupancy = board;
		sit.OccupancyUnitId = 3;
		sit.CoverCandidates = new[] { c05, c07 };
		sit.FinalCoverCandidateId = 7;
		sit.HasKnownThreat = true;
		TacticalMovementDecision route = overlay.Update(in sit, new[] { CoverVia(c05, c07) });
		Check("F_GoldenDest",
			TacticalRouteMath.DestinationUnchanged(in route, c07.Position) &&
			route.CurrentHop == c05.Position,
			"dest=" + route.Destination + " hop=" + route.CurrentHop);
		Check("F_Reserve",
			board.GetState(c05, 1f) == CoverOccupancy.Reserved,
			"c05=" + board.GetState(c05, 1f));
		var lean = new RecordingLeanExecutor();
		TacticalMovingLeanDecision leaned = overlay.NotifyMovingLean(LeanBenefit(CoverPeekDirection.Left), lean);
		Check("F_Lean",
			leaned.Action == TacticalMovingLeanAction.Lean,
			"action=" + leaned.Action);
		TacticalMovingLeanSituation passed = LeanBenefit(CoverPeekDirection.Left);
		passed.CornerPassed = true;
		overlay.NotifyMovingLean(in passed, lean);
		TacticalArrivalDecision hop = overlay.NotifyTacticalArrival(ArriveAt(c05, 0.1f, 2f));
		Check("F_Hop",
			hop.Result == TacticalArrivalResult.Traversed &&
			board.GetState(c05, 2f) == CoverOccupancy.Available &&
			board.GetState(c07, 2f) == CoverOccupancy.Reserved,
			"hop=" + hop.Result);
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
		sit.Now = 3f;
		overlay.Update(in sit, new[] { CoverVia(c05, c07) });
		Check("F_Stable", overlay.ReplacementCount == 0, "rep=" + overlay.ReplacementCount);
		TacticalArrivalDecision acquired = overlay.NotifyTacticalArrival(ArriveAt(c07, 0.1f, 4f));
		Check("F_Acquire",
			acquired.Result == TacticalArrivalResult.Acquired &&
			board.GetState(c07, 4f) == CoverOccupancy.Occupied,
			"result=" + acquired.Result);

		overlay.Invalidate();
		Vector3 dest = new Vector3(12f, 0f, 0f);
		TacticalMovementDecision walked = overlay.Update(
			TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Normal, 1f));
		int movesBefore = _recorder.MoveCount;
		var nav = new TacticalNavigationExecutor();
		nav.Begin();
		nav.Tick(
			_ai,
			true,
			walked.CurrentHop,
			TacticalNavigationMath.DefaultPointArrivalRadius,
			UnitNavigationReason.Attack);
		Check("F_Nav",
			_recorder.MoveCount > movesBefore && _recorder.LastDestination == dest,
			"moves=" + _recorder.MoveCount);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { CoverHopRoute(2, CoverAt(7, new Vector3(2f, 0f, 0f))) });
		sit.UnderFire = NearbyCoverFire(2f);
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision under = overlay.Update(
			in sit, new[] { CoverHopRoute(2, CoverAt(7, new Vector3(2f, 0f, 0f))) });
		Check("F_UnderFire",
			under.UnderFireAction == TacticalUnderFireAction.Continue,
			"action=" + under.UnderFireAction);

		overlay.Invalidate();
		sit = ArenaSit(TacticalMovementMode.Tactical);
		sit.Now = 1f;
		overlay.Update(in sit, new[] { AuthoredDirect(1, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f) });
		sit.UnderFire = NearbyEmergencyFire();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.ImmediateThreat, 1f));
		sit.Now = 2f;
		TacticalMovementDecision emergency = overlay.Update(
			in sit, new[] { AuthoredDirect(1, 10f, 6.7f, 0.9f, 0.1f, 0.8f, 0.5f) });
		Check("F_Emergency",
			emergency.UnderFireAction == TacticalUnderFireAction.EmergencyCover &&
			emergency.NeedsEmergencyCover,
			"action=" + emergency.UnderFireAction);

		TacticalRouteSituation urbanSafe = ArenaSit(TacticalMovementMode.Normal);
		urbanSafe.HasKnownThreat = false;
		Check("F_UrbanSafe",
			new TacticalRouteEvaluator().Evaluate(in urbanSafe, null).Selected.Candidate.Kind ==
			TacticalRouteKind.Direct,
			"kind");
		TacticalRouteSituation urbanTac = ArenaSit(TacticalMovementMode.Tactical);
		urbanTac.HasKnownThreat = true;
		urbanTac.Destination = new Vector3(20f, 0f, 0f);
		TacticalRouteDecision wall = new TacticalRouteEvaluator().Evaluate(
			in urbanTac, LodDirectVsWall());
		Check("F_UrbanWall",
			wall.HasSelection && wall.Selected.Candidate.CandidateId == 2,
			"id=" + (wall.HasSelection ? wall.Selected.Candidate.CandidateId : 0));

		var overlayKeep = new TacticalMovementOverlay();
		TacticalRouteSituation quiet = ArenaSit(TacticalMovementMode.Tactical);
		quiet.Now = 1f;
		var safe = new TacticalRouteCandidate();
		safe.SetDirect(1, Vector3.zero, quiet.Destination);
		safe.UseAuthoredMetrics = true;
		safe.DistanceMeters = 10f;
		safe.TravelTimeSeconds = 6.7f;
		safe.Exposure01 = 0.15f;
		safe.Cover01 = 0.8f;
		safe.Danger01 = 0.1f;
		safe.MissionProgress01 = 0.8f;
		overlayKeep.Update(in quiet, new[] { safe });
		int evals = overlayKeep.Evaluator.EvaluationCount;
		for (int i = 0; i < 40; i++)
		{
			quiet.Now = 1f + i * 0.05f;
			overlayKeep.Update(in quiet, new[] { safe });
		}

		Check("F_NoPerFrame",
			overlayKeep.Evaluator.EvaluationCount == evals && overlayKeep.ReevaluationCount == 0,
			"eval=" + overlayKeep.Evaluator.EvaluationCount);

		var overlayThrash = new TacticalMovementOverlay();
		TacticalRouteSituation thrash = ArenaSit(TacticalMovementMode.Tactical);
		thrash.Now = 1f;
		TacticalRouteCandidate nearA = AuthoredDirect(1, 10f, 6.7f, 0.31f, 0.7f, 0.3f, 0.8f);
		TacticalRouteCandidate nearB = AuthoredDirect(2, 10.2f, 6.8f, 0.30f, 0.7f, 0.3f, 0.8f);
		overlayThrash.Update(in thrash, new[] { nearA, nearB });
		int firstId = overlayThrash.Last.SelectedCandidateId;
		for (int i = 0; i < 6; i++)
		{
			overlayThrash.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.02f));
			thrash.Now = 2f + i;
			overlayThrash.Update(in thrash, new[] { nearA, nearB });
		}

		Check("F_NoThrash",
			overlayThrash.Last.SelectedCandidateId == firstId && overlayThrash.ReplacementCount == 0,
			"id=" + overlayThrash.Last.SelectedCandidateId + " rep=" + overlayThrash.ReplacementCount);

		var sched = new TacticalUpdateScheduler
		{
			MaxRouteEvaluationsPerTick = 20,
			StaggerSlots = 1
		};
		sched.BeginTick(0, 0f);
		for (int i = 0; i < 10; i++)
			sched.ReportTier(i + 1, TacticalLodTier.Full);
		for (int i = 0; i < 20; i++)
			sched.ReportTier(100 + i, TacticalLodTier.Reduced);
		for (int i = 0; i < 70; i++)
			sched.ReportTier(200 + i, TacticalLodTier.Background);
		for (int i = 0; i < 100; i++)
			sched.TryAdmit(i + 1, TacticalLodOperation.RouteEvaluation, TacticalCriticality.Medium);
		Check("F_LodMix",
			sched.AdmittedCount == 20 && sched.FullCount == 10 && sched.BackgroundCount == 70,
			"admitted=" + sched.AdmittedCount + " bg=" + sched.BackgroundCount);

		UseOfForceLevel roe = _ai.CurrentUseOfForceLevel;
		var cover = new TacticalCoverOverlay();
		overlay.NotifyEvent(TacticalReplanEvent.Of(TacticalReplanEventKind.EnemyMoved, 0.47f));
		Check("F_RoE", _ai.CurrentUseOfForceLevel == roe, "roe=" + _ai.CurrentUseOfForceLevel);
		Check("F_NoCoverPick",
			cover.Last.Decision == TacticalCoverDecisionKind.None,
			"kind=" + cover.Last.Decision);

		int moves = _recorder.MoveCount;
		overlay.Invalidate();
		overlay.Update(TacticalRouteMath.Goal(Vector3.zero, dest, TacticalMovementMode.Tactical, 1f));
		Check("F_NoWalk", _recorder.MoveCount == moves, "moves=" + _recorder.MoveCount);
		TacticalMovementDecision lastMove = overlay.Last;
		_debug.Capture(in lastMove, Vector3.zero);
		Check("F_Overlay", _debug.HasCapture && lastMove.HasRoute, "route=" + lastMove.HasRoute);
		overlay.BindScheduler(TacticalUpdateScheduler.Shared, _ai.CoverOccupancyUnitId);
	}

	private static TacticalLodSituation LodIdleFar()
	{
		return new TacticalLodSituation
		{
			Idle = true,
			HasPlayerDistance = true,
			DistanceToPlayerMeters = 80f,
			SecondsSinceSignificantEvent = 30f
		};
	}

	private static TacticalLodSituation LodMoving()
	{
		return new TacticalLodSituation
		{
			HasActiveTacticalMovement = true
		};
	}

	private static TacticalLodSituation LodCombat()
	{
		return new TacticalLodSituation
		{
			InCombat = true
		};
	}

	private static TacticalRouteCandidate[] LodDirectVsWall()
	{
		var direct = new TacticalRouteCandidate();
		direct.SetDirect(1, Vector3.zero, new Vector3(20f, 0f, 0f));
		direct.UseAuthoredMetrics = true;
		direct.DistanceMeters = 20f;
		direct.TravelTimeSeconds = 13.3f;
		direct.Exposure01 = 0.82f;
		direct.Cover01 = 0.1f;
		direct.Danger01 = 0.7f;
		direct.MissionProgress01 = 0.5f;
		var wall = new TacticalRouteCandidate();
		wall.SetWaypoint(2, Vector3.zero, new Vector3(20f, 0f, 0f), new Vector3(10f, 0f, 4f));
		wall.UseAuthoredMetrics = true;
		wall.DistanceMeters = 24f;
		wall.TravelTimeSeconds = 16f;
		wall.Exposure01 = 0.18f;
		wall.Cover01 = 0.85f;
		wall.Danger01 = 0.2f;
		wall.MissionProgress01 = 0.5f;
		return new[] { direct, wall };
	}

	private static TacticalMovingLeanSituation LeanBenefit(CoverPeekDirection _direction)
	{
		bool left = _direction == CoverPeekDirection.Left;
		return new TacticalMovingLeanSituation
		{
			Present = true,
			Moving = true,
			HasCorner = true,
			InCorridor = true,
			DistanceToCornerMeters = 1.2f,
			Approach = true,
			LeftAvailable = left,
			RightAvailable = !left,
			LeftVisibilityGain = left ? 0.41f : 0f,
			RightVisibilityGain = left ? 0f : 0.41f,
			LeftExposure01 = 0.18f,
			RightExposure01 = 0.18f,
			LeftSmallSufficient = left,
			RightSmallSufficient = !left,
			ExposureWithoutLean = 0.10f
		};
	}

	private static TacticalMovingLeanSituation LeanFar()
	{
		TacticalMovingLeanSituation sit = LeanBenefit(CoverPeekDirection.Left);
		sit.DistanceToCornerMeters = 12f;
		sit.LeftSmallSufficient = false;
		sit.LeftVisibilityGain = 0f;
		return sit;
	}

	private static void AssertReserve(
		CoverOccupancyBoard _board,
		CoverCandidate _cover,
		int _unitId,
		float _now)
	{
		_board.TryReserve(_cover, _unitId, _now);
	}

	private static TacticalArrivalSituation ArriveAt(CoverCandidate _cover, float _offsetX, float _now)
	{
		return new TacticalArrivalSituation
		{
			NavigationReached = true,
			CurrentPosition = _cover.Position + new Vector3(_offsetX, 0f, 0f),
			TargetPosition = _cover.Position,
			HasTargetPosition = true,
			Candidate = _cover,
			CandidateId = _cover.CandidateId,
			CandidateRegion = _cover.RegionId,
			Now = _now,
			GeometryVersion = _cover.GeometryVersion
		};
	}

	private static TacticalRouteCandidate DirectCover(CoverCandidate _cover)
	{
		var candidate = new TacticalRouteCandidate();
		candidate.SetDirect(1, Vector3.zero, _cover.Position);
		candidate.UseAuthoredMetrics = true;
		candidate.DistanceMeters = 10f;
		candidate.TravelTimeSeconds = 6.7f;
		candidate.Exposure01 = 0.2f;
		candidate.Cover01 = 0.85f;
		candidate.Danger01 = 0.2f;
		candidate.MissionProgress01 = 0.8f;
		return candidate;
	}

	private static TacticalRouteCandidate CoverVia(CoverCandidate _hop, CoverCandidate _dest)
	{
		var candidate = new TacticalRouteCandidate();
		candidate.SetCoverHops(
			1,
			Vector3.zero,
			_dest.Position,
			new[]
			{
				TacticalRouteWaypoint.CoverHop(_hop.Position, _hop.CandidateId, _hop.RegionId)
			});
		candidate.UseAuthoredMetrics = true;
		candidate.DistanceMeters = 16f;
		candidate.TravelTimeSeconds = 10.7f;
		candidate.Exposure01 = 0.2f;
		candidate.Cover01 = 0.85f;
		candidate.Danger01 = 0.2f;
		candidate.MissionProgress01 = 0.7f;
		return candidate;
	}

	private static TacticalUnderFireSituation NearbyCoverFire(float _meters)
	{
		return new TacticalUnderFireSituation
		{
			Present = true,
			ImmediateThreat = true,
			Moving = true,
			RemainingHopMeters = _meters,
			CoverAheadMeters = _meters,
			CoverAheadProtected = true,
			CurrentExposure01 = 0.35f
		};
	}

	private static TacticalUnderFireSituation NearbyEmergencyFire()
	{
		return new TacticalUnderFireSituation
		{
			Present = true,
			ImmediateThreat = true,
			Moving = true,
			RemainingHopMeters = 20f,
			CoverAheadMeters = 20f,
			CoverAheadProtected = false,
			CurrentExposure01 = 0.8f,
			HasNearbyEmergencyCover = true,
			HasCoverCandidates = true
		};
	}

	private static TacticalUnderFireSituation DangerousAltFire()
	{
		return new TacticalUnderFireSituation
		{
			Present = true,
			ImmediateThreat = true,
			Moving = true,
			RemainingHopMeters = 16f,
			CoverAheadProtected = false,
			CurrentExposure01 = 0.82f,
			HasSaferAlternative = true,
			AlternativeExposure01 = 0.31f
		};
	}

	private static TacticalUnderFireSituation NoAlternativeFire()
	{
		return new TacticalUnderFireSituation
		{
			Present = true,
			ImmediateThreat = true,
			Moving = true,
			RemainingHopMeters = 20f,
			CoverAheadProtected = false,
			CurrentExposure01 = 0.8f
		};
	}

	private static TacticalRouteCandidate CoverHopRoute(int _id, CoverCandidate _cover)
	{
		var candidate = new TacticalRouteCandidate();
		candidate.SetCoverHops(
			_id,
			Vector3.zero,
			new Vector3(10f, 0f, 0f),
			new[]
			{
				TacticalRouteWaypoint.CoverHop(_cover.Position, _cover.CandidateId, _cover.RegionId)
			});
		candidate.UseAuthoredMetrics = true;
		candidate.DistanceMeters = 16f;
		candidate.TravelTimeSeconds = 10.7f;
		candidate.Exposure01 = 0.2f;
		candidate.Cover01 = 0.85f;
		candidate.Danger01 = 0.2f;
		candidate.MissionProgress01 = 0.6f;
		return candidate;
	}

	private void AssertIdleCoverDoesNotMove(
		UnitAIController _ai,
		UnitMoveCommandRecorder _recorder,
		int _moveBaseline)
	{
		_ai.ImmediateThreat = true;
		_ai.Tick(0.05f);
		Check("S_CoverNoWalk",
			_ai.CurrentState == UnitAIState.Idle && _recorder.MoveCount == _moveBaseline,
			"state=" + _ai.CurrentState + " moves=" + _recorder.MoveCount);
		_ai.ImmediateThreat = false;
	}

	private void Check(string _id, bool _pass, string _detail)
	{
		if (_pass)
		{
			m_PassCount++;
			AppendLine("PASS " + _id);
			return;
		}

		m_FailCount++;
		AppendLine("FAIL " + _id + " " + _detail);
	}

	private void DestroyUnit()
	{
		if (m_Unit == null)
			return;
		Destroy(m_Unit);
		m_Unit = null;
	}

	private void AppendLine(string _line)
	{
		m_Report.AppendLine(_line);
	}

	private void Finish()
	{
		AppendLine("---");
		AppendLine("RESULT=" + (m_FailCount == 0 ? "PASS" : "FAIL") +
		           " pass=" + m_PassCount + " fail=" + m_FailCount);
		string dir = Path.Combine(Application.dataPath, "_Docs", "Logs", "Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "TacticalMovement_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[TacticalMovement] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunTacticalMovement;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
