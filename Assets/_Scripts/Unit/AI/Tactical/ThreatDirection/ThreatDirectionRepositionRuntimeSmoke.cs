using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C.5 Play: FaceOnly / Stay / RepositionAllowed. Occupied stays unless #13 is allowed.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirectionReposition_LAST.txt
/// </summary>
[DefaultExecutionOrder(72)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionRepositionRuntimeSmoke : MonoBehaviour
{
	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private static readonly Vector3 s_Origin = Vector3.zero;
	private static readonly Vector3 s_North = Vector3.forward;
	private static readonly Vector3 s_East = Vector3.right;
	private static readonly Vector3 s_South = Vector3.back;
	private static readonly Vector3 s_NorthEast = new Vector3(1f, 0f, 1f).normalized;
	private static readonly Vector3 s_CoverPos = new Vector3(4f, 0f, 0f);
	private static readonly Vector3 s_NorthPoint = new Vector3(0f, 0f, 10f);
	private static readonly Vector3 s_EastPoint = new Vector3(10f, 0f, 0f);

	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirectionReposition;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirectionReposition)
			return;
		if (FindAnyObjectByType<ThreatDirectionRepositionRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionRepositionRuntimeSmoke");
		go.AddComponent<ThreatDirectionRepositionRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirectionReposition)
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
		AppendLine("STAGE 14C.5 — THREAT DIRECTION REPOSITION DECISION");
		AppendLine("==================================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Stay / FaceOnly / RepositionAllowed. Occupied stays until #13 is allowed. Not Move / scan.");
		AppendLine("---");

		RunFaceOnly();
		RunStayAndAllowed();
		RunOccupiedLifecycle();
		RunIsolation();

		Finish();
		yield break;
	}

	private void RunFaceOnly()
	{
		AppendLine("[A] FaceOnly deadband / weak signal");
		Check("P1_FiveDegreesFaceOnly",
			Decide(SlightNorth(5f), 5f).Kind == ThreatDirectionRepositionKind.FaceOnly,
			"kind");
		Check("P2_NorthEastFaceOnly",
			Decide(VisualNorthEast(), 45f).Kind == ThreatDirectionRepositionKind.FaceOnly,
			"kind");
		Check("P3_LowConfidenceFaceOnly",
			Decide(WeakEast(), 90f).Kind == ThreatDirectionRepositionKind.FaceOnly,
			"kind");
	}

	private void RunStayAndAllowed()
	{
		AppendLine("[B] Stay vs RepositionAllowed");
		CoverCandidate eastHold = StandingCover(1, s_Origin, s_East);
		CoverCandidate south = StandingCover(2, s_CoverPos, s_South);
		CoverSituation eastSit = IsolatedEqualLook(VisualEast());
		ThreatDirectionRepositionResult good = new ThreatDirectionReposition().Evaluate(
			VisualEast(),
			eastHold,
			new[] { eastHold, south },
			in eastSit,
			90f);
		Check("P4_GoodFitStay",
			good.Kind == ThreatDirectionRepositionKind.Stay && good.ThreatFit == CoverThreatFit.Good,
			good.Kind + " " + good.ThreatFit);

		ThreatDirectionRepositionResult east = Decide(VisualEast(), 90f);
		Check("P5_EastRepositionAllowed",
			east.Kind == ThreatDirectionRepositionKind.RepositionAllowed &&
			east.ThreatFit == CoverThreatFit.Poor &&
			east.BestCandidateId == 2,
			east.Kind + " best=" + east.BestCandidateId);

		CoverCandidate north = StandingCover(1, s_Origin, s_North);
		CoverCandidate southCover = StandingCover(2, s_Origin, s_South);
		CoverSituation southSit = IsolatedEqualLook(VisualSouth());
		ThreatDirectionRepositionResult southResult = new ThreatDirectionReposition().Evaluate(
			VisualSouth(),
			north,
			new[] { north, southCover },
			in southSit,
			180f);
		Check("P6_SouthRepositionAllowed",
			southResult.Kind == ThreatDirectionRepositionKind.RepositionAllowed,
			southResult.Kind.ToString());

		Check("P7_TinyDeltaStay",
			ThreatDirectionRepositionMath.Decide(
				90f,
				ThreatDirectionMath.VisualConfidence,
				CoverThreatFit.Poor,
				true,
				5f,
				5f,
				5.01f,
				5.01f,
				1,
				2) == ThreatDirectionRepositionKind.Stay,
			"tiny");

		ThreatDirectionReposition hold = new ThreatDirectionReposition();
		DecideOn(hold, VisualEast(), 90f);
		int logs = hold.LogCount;
		DecideOn(hold, VisualEast(), 0f);
		Check("P8_NoOscillation",
			hold.LogCount == logs && hold.AllowsCoverReevaluation,
			"logs=" + hold.LogCount);
	}

	private void RunOccupiedLifecycle()
	{
		AppendLine("[C] Occupied stays; #13 only when allowed");
		CoverCandidate current = StandingCover(1, s_Origin, s_North);
		CoverCandidate other = StandingCover(2, s_Origin, s_East);
		var board = new CoverOccupancyBoard();
		board.TryReserve(current, 21, 0f);
		board.ConfirmOccupied(current, 21, 0f);
		Decide(VisualEast(), 90f);
		bool occupied = board.TryGetHeld(21, 0f, out CoverReservation held) &&
		                held.State == CoverOccupancy.Occupied &&
		                held.CandidateId == 1;
		Check("P9_OccupiedNotReleased", occupied, "state=" + held.State);

		CoverSituation blocked = IsolatedEqualLook(VisualEast());
		blocked.UnitPosition = s_Origin;
		blocked.ThreatRepositionAllowed = false;
		TacticalCoverDecision stay = new TacticalCoverSolver().Decide(
			in blocked,
			new[] { current, other },
			CurrentTacticalPosition.FromCandidate(current, true));
		Check("P10_StayCommittedWithoutFlag",
			stay.Decision == TacticalCoverDecisionKind.Stay &&
			stay.SelectedCandidateId == 1 &&
			!stay.HasDestination,
			stay.Decision + " sel=" + stay.SelectedCandidateId);

		CoverSituation allowed = IsolatedEqualLook(VisualEast());
		allowed.UnitPosition = s_Origin;
		allowed.ThreatRepositionAllowed = true;
		TacticalCoverDecision move = new TacticalCoverSolver().Decide(
			in allowed,
			new[] { current, other },
			CurrentTacticalPosition.FromCandidate(current, true));
		Check("P11_AllowedRepositions",
			move.Decision == TacticalCoverDecisionKind.Reposition &&
			move.SelectedCandidateId == 2 &&
			move.HasDestination,
			move.Decision + " sel=" + move.SelectedCandidateId);
	}

	private void RunIsolation()
	{
		AppendLine("[D] Isolation / live permission");
		Check("P12_CoverScoreUnchanged",
			Mathf.Abs(
				CoverScoreMath.EvaluateOne(StandingCover(1, s_CoverPos, s_North), IsolatedEqualLook(ExpectedNorth()), null).Score -
				CoverScoreMath.EvaluateOne(StandingCover(1, s_CoverPos, s_North), IsolatedEqualLook(VisualEast()), null).Score) < 0.0001f,
			"score");
		Check("P13_AcquireUnchanged",
			Mathf.Abs(TacticalArrivalMath.DefaultAcquireToleranceMeters - 0.6f) < 0.0001f,
			"acquire");
		Check("P14_SearchUntouched",
			!TacticalCoverSolver.AllowsTactical(UnitAIState.Search, false),
			"search");

		var knowledge = new ThreatDirectionController();
		knowledge.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		int logs = knowledge.LogCount;
		for (int i = 0; i < 40; i++)
			knowledge.Tick(i * 0.05f, s_Origin, AIPerceptionFrame.Empty);
		Check("P15_NoPolling",
			knowledge.LogCount == logs &&
			knowledge.GetThreatCompass() == ThreatDirectionCompass.North,
			"logs=" + knowledge.LogCount);

		Check("P16_Channel",
			ThreatDirectionRepositionLog.Channel == "THREAT_REPOSITION",
			ThreatDirectionRepositionLog.Channel);

		var go = new GameObject("ThreatDirectionReposition_PlayLive");
		try
		{
			UnitAIController ai = go.AddComponent<UnitAIController>();
			ai.EnsureStarted();
			ai.ThreatReposition.Reset();
			CoverCandidate current = StandingCover(1, s_Origin, s_North);
			CoverCandidate other = StandingCover(2, s_Origin, s_East);
			CoverSituation situation = IsolatedEqualLook(VisualEast());
			ai.ThreatReposition.Evaluate(
				VisualEast(),
				current,
				new[] { current, other },
				in situation,
				90f);
			situation.ThreatRepositionAllowed = ai.ThreatRepositionAllowed;
			situation.UnitPosition = s_Origin;
			TacticalCoverDecision cover = new TacticalCoverSolver().Decide(
				in situation,
				new[] { current, other },
				CurrentTacticalPosition.FromCandidate(current, true));
			Check("P17_LivePermissionDrivesSolver",
				ai.ThreatRepositionAllowed &&
				cover.Decision == TacticalCoverDecisionKind.Reposition &&
				cover.SelectedCandidateId == 2,
				"allowed=" + ai.ThreatRepositionAllowed + " dec=" + cover.Decision);
			Check("P18_ReadinessUnchanged",
				ai.CurrentState == UnitAIState.Idle,
				ai.CurrentState.ToString());
		}
		finally
		{
			Destroy(go);
		}
	}

	private void Check(string _name, bool _pass, string _detail)
	{
		if (_pass)
			m_PassCount++;
		else
			m_FailCount++;
		AppendLine((_pass ? "PASS " : "FAIL ") + _name + (_pass ? string.Empty : " " + _detail));
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
		string dir = Path.Combine(Application.dataPath, "_Docs/Logs/Tests");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "ThreatDirectionReposition_LAST.txt");
		File.WriteAllText(path, m_Report.ToString());
		Debug.Log(
			"[ThreatDirectionReposition] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);
#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirectionReposition;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}

	private static ThreatDirectionRepositionResult Decide(
		ThreatDirectionKnowledge _knowledge,
		float _angle)
	{
		return DecideOn(new ThreatDirectionReposition(), _knowledge, _angle);
	}

	private static ThreatDirectionRepositionResult DecideOn(
		ThreatDirectionReposition _decision,
		ThreatDirectionKnowledge _knowledge,
		float _angle)
	{
		CoverCandidate current = StandingCover(1, s_Origin, s_North);
		CoverCandidate other = _knowledge.Compass == ThreatDirectionCompass.South
			? StandingCover(2, s_Origin, s_South)
			: StandingCover(2, s_Origin, s_East);
		CoverSituation situation = IsolatedEqualLook(_knowledge);
		situation.UnitPosition = s_Origin;
		return _decision.Evaluate(
			_knowledge,
			current,
			new[] { current, other },
			in situation,
			_angle);
	}

	private static CoverSituation IsolatedEqualLook(ThreatDirectionKnowledge _knowledge)
	{
		var situation = new CoverSituation
		{
			UnitPosition = s_Origin,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			HasTarget = false,
			SectorForward = s_NorthEast,
			HostileDirection = s_East,
			GeometryVersion = 1
		};
		ThreatDirectionCoverMath.Bind(ref situation, in _knowledge);
		return situation;
	}

	private static ThreatDirectionKnowledge ExpectedNorth()
	{
		return new ThreatDirectionKnowledge(
			s_North,
			ThreatDirectionCompass.North,
			ThreatDirectionMath.ExpectedConfidence,
			ThreatDirectionMath.ExpectedUncertaintyDegrees,
			0f,
			ThreatDirectionSource.InitialEstimate,
			ThreatDirectionState.Expected);
	}

	private static ThreatDirectionKnowledge VisualEast()
	{
		return new ThreatDirectionKnowledge(
			s_East,
			ThreatDirectionCompass.East,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge VisualSouth()
	{
		return new ThreatDirectionKnowledge(
			s_South,
			ThreatDirectionCompass.South,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge VisualNorthEast()
	{
		return new ThreatDirectionKnowledge(
			s_NorthEast,
			ThreatDirectionCompass.NorthEast,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge WeakEast()
	{
		return new ThreatDirectionKnowledge(
			s_East,
			ThreatDirectionCompass.East,
			0.2f,
			ThreatDirectionMath.SoundUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Sound,
			ThreatDirectionState.Known);
	}

	private static ThreatDirectionKnowledge SlightNorth(float _degrees)
	{
		Vector3 slight = new Vector3(
			Mathf.Sin(_degrees * Mathf.Deg2Rad),
			0f,
			Mathf.Cos(_degrees * Mathf.Deg2Rad));
		return new ThreatDirectionKnowledge(
			slight,
			ThreatDirectionCompass.North,
			ThreatDirectionMath.VisualConfidence,
			ThreatDirectionMath.VisualUncertaintyDegrees,
			0f,
			ThreatDirectionSource.Visual,
			ThreatDirectionState.Known);
	}

	private static CoverCandidate StandingCover(int _id, Vector3 _position, Vector3 _normal)
	{
		return new CoverCandidate
		{
			CandidateId = _id,
			Position = _position,
			Normal = _normal,
			CoverType = CoverType.Standing,
			StandingValid = true,
			CrouchValid = true,
			NavMeshValid = true,
			StandingProfile = new CoverProtectionProfile
			{
				Head = 1f,
				Torso = 1f,
				Pelvis = 1f,
				Legs = 1f
			},
			CrouchProfile = new CoverProtectionProfile
			{
				Head = 1f,
				Torso = 1f,
				Pelvis = 1f,
				Legs = 1f
			},
			GeometryVersion = 1
		};
	}
	#endregion
}
