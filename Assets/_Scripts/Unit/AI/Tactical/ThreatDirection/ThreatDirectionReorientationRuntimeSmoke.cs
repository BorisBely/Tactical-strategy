using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #14C.4 Play: deadband, significant change, facing, occupied ThreatFit, no Move.
/// Report: Assets/_Docs/Logs/Tests/ThreatDirectionReorientation_LAST.txt
/// </summary>
[DefaultExecutionOrder(71)]
[DisallowMultipleComponent]
public sealed class ThreatDirectionReorientationRuntimeSmoke : MonoBehaviour
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
		m_RunOnStart || DetectionHarnessPlayMode.RunThreatDirectionReorientation;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunThreatDirectionReorientation)
			return;
		if (FindAnyObjectByType<ThreatDirectionReorientationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("ThreatDirectionReorientationRuntimeSmoke");
		go.AddComponent<ThreatDirectionReorientationRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		if (DetectionHarnessPlayMode.RunThreatDirectionReorientation)
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
		AppendLine("STAGE 14C.4 — DYNAMIC THREAT REORIENTATION");
		AppendLine("==========================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Significant change → facing + ThreatFit. Occupied stays. Not Move / scan / Fire.");
		AppendLine("---");

		RunSmoothAndFront();
		RunOccupiedFit();
		RunSources();
		RunIsolation();

		Finish();
		yield break;
	}

	private void RunSmoothAndFront()
	{
		AppendLine("[A] N → NE → E and N → S");
		ThreatDirectionReorientation reorient = SeededNorth();
		ThreatDirectionReorientationResult five = reorient.Observe(SlightNorth(5f));
		Check("P1_DeadbandFive",
			!five.TacticalChanged && !five.FacingUpdated,
			"changed=" + five.TacticalChanged);

		ThreatDirectionReorientationResult ne = reorient.Observe(VisualNorthEast());
		Check("P2_NorthEastFacingNoChanged",
			!ne.TacticalChanged && ne.FacingUpdated && reorient.Facing.DesiredFacing.x > 0.5f,
			"changed=" + ne.TacticalChanged + " facing=" + ne.FacingUpdated);

		ThreatDirectionReorientationResult east = reorient.Observe(VisualEast());
		Check("P3_EastSignificant",
			east.TacticalChanged && east.FacingUpdated && reorient.Facing.DesiredFacing.x > 0.9f,
			"changed=" + east.TacticalChanged);

		ThreatDirectionReorientation south = SeededNorth();
		south.Observe(VisualSouth());
		Check("P4_SouthFacing",
			south.ChangeCount == 1 && south.Facing.DesiredFacing.z < -0.9f,
			"change=" + south.ChangeCount);
		Check("P5_LowConfidenceNoReaction",
			!SeededNorth().Observe(WeakEast()).TacticalChanged,
			"low conf reacted");
	}

	private void RunOccupiedFit()
	{
		AppendLine("[B] Occupied cover ThreatFit");
		CoverCandidate hold = StandingCover(2, s_Origin, s_North);
		CoverCandidate other = StandingCover(3, s_CoverPos, s_East);
		CoverSituation occupiedSit = Isolated(ExpectedNorth());
		occupiedSit.UnitPosition = s_Origin;
		CurrentTacticalPosition occupying = CurrentTacticalPosition.FromCandidate(hold, true);
		var solver = new TacticalCoverSolver();
		solver.Decide(in occupiedSit, new[] { hold, other }, in occupying);
		CoverSituation eastSit = Isolated(VisualEast());
		eastSit.UnitPosition = s_Origin;
		TacticalCoverDecision second = solver.Decide(in eastSit, new[] { hold, other }, in occupying);
		Check("P6_StayOccupied",
			second.Decision == TacticalCoverDecisionKind.Stay &&
			second.SelectedCandidateId == 2 &&
			!second.HasDestination,
			second.Decision + " sel=" + second.SelectedCandidateId);

		ThreatDirectionReorientation reorient = SeededNorth();
		reorient.Observe(ExpectedNorth(), hold);
		ThreatDirectionReorientationResult fit = reorient.Observe(VisualEast(), hold);
		Check("P7_ThreatFitPoor",
			fit.ThreatFit == CoverThreatFit.Poor && fit.ThreatFitChanged,
			fit.ThreatFit.ToString());
	}

	private void RunSources()
	{
		AppendLine("[C] Visual override / weak sound");
		var controller = new ThreatDirectionController();
		controller.ApplyBattleStart(s_Origin, s_NorthPoint, 0f);
		controller.ApplyHostileVisible(s_Origin, s_NorthPoint, 1f);
		Check("P8_WeakSoundIgnored",
			!controller.ApplyGunshot(s_Origin, s_EastPoint, 2f) &&
			controller.GetThreatCompass() == ThreatDirectionCompass.North,
			controller.GetThreatCompass().ToString());
		Check("P9_VisualEastOverride",
			controller.ApplyHostileVisible(s_Origin, s_EastPoint, 3f) &&
			controller.GetThreatCompass() == ThreatDirectionCompass.East,
			controller.GetThreatCompass().ToString());
	}

	private void RunIsolation()
	{
		AppendLine("[D] Fatigue / CoverScore / no polling");
		ArmFatigueProfile profile = ArmFatigueProfile.PlayPrototype();
		Check("P10_FatigueSlowsTurn",
			ThreatDirectionReorientationMath.TurnDuration(1f, in profile) >
			ThreatDirectionReorientationMath.TurnDuration(0f, in profile),
			"turn");
		ThreatDirectionReorientation reorient = SeededNorth();
		reorient.Observe(VisualEast());
		Vector3 facing = reorient.Facing.DesiredFacing;
		Check("P11_FatigueKeepsFacing",
			Mathf.Abs(facing.x - reorient.Facing.DesiredFacing.x) < 0.0001f &&
			ThreatDirectionReorientationMath.TurnDuration(1f, in profile) >
			ThreatDirectionReorientationMath.TurnDuration(0f, in profile),
			"facing changed");

		CoverCandidate cover = StandingCover(1, s_CoverPos, s_North);
		Check("P12_CoverScoreUnchanged",
			Mathf.Abs(
				CoverScoreMath.EvaluateOne(cover, Isolated(ExpectedNorth()), null).Score -
				CoverScoreMath.EvaluateOne(cover, Isolated(VisualEast()), null).Score) < 0.0001f,
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
		Check("P16_ChangedChannel",
			ThreatDirectionReorientationLog.ChangedChannel == "THREAT_DIRECTION_CHANGED",
			ThreatDirectionReorientationLog.ChangedChannel);
		Check("P17_FitPayload",
			reorient.LastChangePayload.IndexOf("to=E", StringComparison.Ordinal) >= 0,
			reorient.LastChangePayload);
		Check("P18_ReadinessUnchanged",
			new ReadinessController() is ReadinessController r &&
			ResetReadiness(r) == ReadinessState.Patrol,
			"readiness");
	}

	private static ReadinessState ResetReadiness(ReadinessController _readiness)
	{
		_readiness.Reset(ReadinessRankKind.Soldier, 0f);
		SeededNorth().Observe(VisualEast());
		return _readiness.CurrentState;
	}

	private static ThreatDirectionReorientation SeededNorth()
	{
		var reorient = new ThreatDirectionReorientation(new ThreatDirectionFacingController());
		reorient.Observe(ExpectedNorth());
		return reorient;
	}

	private static CoverSituation Isolated(ThreatDirectionKnowledge _knowledge)
	{
		var situation = new CoverSituation
		{
			UnitPosition = s_Origin,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			HasTarget = false,
			SectorForward = s_East,
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
			StandingProfile = Profile(1f),
			CrouchProfile = Profile(1f),
			GeometryVersion = 1
		};
	}

	private static CoverProtectionProfile Profile(float _value)
	{
		return new CoverProtectionProfile
		{
			Head = _value,
			Torso = _value,
			Pelvis = _value,
			Legs = _value
		};
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
		string path = Path.Combine(dir, "ThreatDirectionReorientation_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[ThreatDirectionReorientation] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunThreatDirectionReorientation;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
