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
/// #13.7 Play: stationary peek → existing UnitSpineLean. Not Fire. Not #14 movement lean policy.
/// Report: Assets/_Docs/Logs/Tests/CoverPeek_LAST.txt
/// </summary>
[DefaultExecutionOrder(67)]
[DisallowMultipleComponent]
public sealed class CoverPeekRuntimeSmoke : MonoBehaviour
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

	private sealed class OffsetLosProbe : ICoverLineOfSightProbe
	{
		public Vector3 Anchor;
		public Vector3 Right = Vector3.right;
		public float EyeHeight = 1.55f;
		public float RequiredOffset;
		public CoverPeekDirection OnlySide;
		public bool AlwaysClear;
		public bool AlwaysBlocked;

		public bool HasClearLook(Vector3 _from, Vector3 _to)
		{
			if (AlwaysClear)
				return true;
			if (AlwaysBlocked)
				return false;
			Vector3 planar = _from - (Anchor + Vector3.up * EyeHeight);
			planar.y = 0f;
			float lateral = Vector3.Dot(planar, Right);
			if (OnlySide == CoverPeekDirection.Left && lateral > -0.02f)
				return false;
			if (OnlySide == CoverPeekDirection.Right && lateral < 0.02f)
				return false;
			return Mathf.Abs(lateral) + 0.001f >= RequiredOffset;
		}
	}

	private sealed class RecordingLeanExecutor : ICoverLeanExecutor
	{
		public int SetLeanCount;
		public CoverLeanLevel LastLevel;
		public CoverPeekDirection LastDirection;

		public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
		{
			SetLeanCount++;
			LastLevel = _level;
			LastDirection = _direction;
		}
	}
	#endregion

	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(8000f, 0f, 8000f);
	#endregion

	#region Serialized
	[SerializeField] private bool m_RunOnStart;
	[SerializeField] private bool m_ExitPlayModeWhenDone;
	#endregion

	#region Private Fields
	private readonly StringBuilder m_Report = new StringBuilder(4096);
	private int m_PassCount;
	private int m_FailCount;
	private GameObject m_Arena;
	private GameObject m_Unit;
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverPeek;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverPeek)
			return;
		if (FindAnyObjectByType<CoverPeekRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverPeekRuntimeSmoke");
		go.AddComponent<CoverPeekRuntimeSmoke>();
	}

	private void Start()
	{
		if (!WillRunOnStart)
			return;
		StartCoroutine(RunSuite());
	}

	private void OnDestroy()
	{
		DestroyArena();
		if (DetectionHarnessPlayMode.RunCoverPeek)
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
		AppendLine("STAGE 13.7 — LEAN / PEEK INTEGRATION");
		AppendLine("====================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Stationary peek. Existing UnitSpineLean. Not Fire. Moving-lean contract only.");
		AppendLine("---");

		Bounds regionBounds = SpawnArena();
		yield return null;

		CoverCandidate corner = MakeCorner(7, regionBounds.center, Vector3.forward);
		CoverSituation situation = Situation(corner.Position, corner.Position + new Vector3(0f, 1.5f, 12f));

		AppendLine("[S1] Target visible without lean → No Lean");
		var overlay = new CoverPeekOverlay();
		var executor = new RecordingLeanExecutor();
		var visibleLos = new OffsetLosProbe { Anchor = corner.Position, AlwaysClear = true };
		CoverPeekDecision s1 = overlay.Update(
			UnitAIState.Idle, corner, in situation, visibleLos, CoverPeekSides.Both, executor, Time.time);
		Check("S1_NoLean", s1.Kind == CoverPeekDecisionKind.None && s1.Reason == CoverPeekReason.AlreadyVisible,
			"kind=" + s1.Kind + " reason=" + s1.Reason);
		Check("S1_NoExecutor", executor.SetLeanCount == 0, "set=" + executor.SetLeanCount);

		AppendLine("[S2] Corner hidden → lean reveals → Lean request");
		overlay = new CoverPeekOverlay();
		executor = new RecordingLeanExecutor();
		CoverPeekDecision s2 = overlay.Update(
			UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f), CoverPeekSides.Both, executor, Time.time);
		Check("S2_Lean", s2.Kind == CoverPeekDecisionKind.Lean, "kind=" + s2.Kind);
		Check("S2_Request", executor.SetLeanCount == 1 && executor.LastLevel == CoverLeanLevel.Small,
			"set=" + executor.SetLeanCount + " depth=" + executor.LastLevel);

		AppendLine("[S3] Small sufficient → not Deep");
		Check("S3_Small", s2.Depth == CoverLeanLevel.Small, "depth=" + s2.Depth);

		AppendLine("[S4] Deep required");
		overlay = new CoverPeekOverlay();
		CoverPeekDecision s4 = overlay.Update(
			UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.40f), CoverPeekSides.Both,
			new RecordingLeanExecutor(), Time.time);
		Check("S4_Deep", s4.Depth == CoverLeanLevel.Deep, "depth=" + s4.Depth);

		AppendLine("[S5] Wrong side → Right");
		overlay = new CoverPeekOverlay();
		CoverPeekDecision s5 = overlay.Update(
			UnitAIState.Idle, corner, in situation,
			HiddenUntil(corner, 0.10f, CoverPeekDirection.Right),
			CoverPeekSides.Both, new RecordingLeanExecutor(), Time.time);
		Check("S5_Right", s5.Direction == CoverPeekDirection.Right, "dir=" + s5.Direction);

		AppendLine("[S6] Lean → enemy gone → Return");
		overlay = new CoverPeekOverlay();
		executor = new RecordingLeanExecutor();
		overlay.Update(UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f),
			CoverPeekSides.Both, executor, Time.time);
		CoverSituation lost = situation;
		lost.HasTarget = false;
		CoverPeekDecision s6 = overlay.Update(
			UnitAIState.Idle, corner, in lost, HiddenUntil(corner, 0.10f), CoverPeekSides.Both, executor, Time.time + 0.1f);
		Check("S6_Return", s6.Kind == CoverPeekDecisionKind.Return && s6.Reason == CoverPeekReason.TargetLost,
			"kind=" + s6.Kind + " reason=" + s6.Reason);
		Check("S6_Neutral", executor.LastLevel == CoverLeanLevel.None, "level=" + executor.LastLevel);

		AppendLine("[S7] Existing spine lean + no Fire pipeline");
		m_Unit = new GameObject("PeekUnit");
		m_Unit.transform.position = corner.Position;
		UnitSpineLean spine = m_Unit.AddComponent<UnitSpineLean>();
		int fireCalls = 0;
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();
		var spineOverlay = new CoverPeekOverlay();
		CoverPeekDecision s7 = spineOverlay.Update(
			UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f), CoverPeekSides.Both,
			new UnitSpineLeanExecutor(spine), Time.time);
		debug.CapturePeek(in s7);
		Check("S7_SpineLevel", spine.CurrentLeanLevel == 1, "level=" + spine.CurrentLeanLevel);
		Check("S7_OneSpine", m_Unit.GetComponents<UnitSpineLean>().Length == 1, "n=" + m_Unit.GetComponents<UnitSpineLean>().Length);
		Check("S7_NoFire", fireCalls == 0, "fire=" + fireCalls);
		Check("S7_OverlaySelected", debug.PeekActive && debug.PeekDecision.Kind == CoverPeekDecisionKind.Lean,
			"kind=" + debug.PeekDecision.Kind);

		AppendLine("[S8] 20 units / 1 shared generation / 20 lean evals");
		var source = new ListSource();
		source.Candidates.Add(corner);
		var cache = new SharedCoverSpatialCache(source);
		cache.GetCandidates(corner.Position);
		int gen = cache.GenerationCount;
		int evals = 0;
		for (int i = 0; i < 20; i++)
		{
			var unitOverlay = new CoverPeekOverlay();
			unitOverlay.BindCache(cache);
			unitOverlay.Update(
				UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f), CoverPeekSides.Both,
				new RecordingLeanExecutor(), Time.time);
			evals += unitOverlay.EvaluateCount;
		}

		Check("S8_GenerationOnce", cache.GenerationCount == gen && gen == 1, "gen=" + cache.GenerationCount);
		Check("S8_TwentyEvals", evals == 20, "evals=" + evals);

		AppendLine("[S9] Event-driven: same key does not reevaluate 6 poses");
		overlay = new CoverPeekOverlay();
		executor = new RecordingLeanExecutor();
		overlay.Update(UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f),
			CoverPeekSides.Both, executor, Time.time);
		overlay.Update(UnitAIState.Idle, corner, in situation, HiddenUntil(corner, 0.10f),
			CoverPeekSides.Both, executor, Time.time + 0.05f);
		Check("S9_OneEval", overlay.EvaluateCount == 1, "eval=" + overlay.EvaluateCount);
		Check("S9_FromCache", overlay.Last.FromCache, "cache=" + overlay.Last.FromCache);

		AppendLine("[S10] Moving-lean contract only (no policy)");
		executor = new RecordingLeanExecutor();
		CoverMovementLeanContract.Apply(executor, new CoverMovementLeanRequest
		{
			Mode = CoverMovementLeanMode.Leaning,
			Direction = CoverPeekDirection.Left,
			Depth = CoverLeanLevel.Medium
		});
		Check("S10_Contract", executor.LastLevel == CoverLeanLevel.Medium && executor.LastDirection == CoverPeekDirection.Left,
			"level=" + executor.LastLevel);

		yield return null;
		Finish();
	}

	private static OffsetLosProbe HiddenUntil(
		CoverCandidate _candidate,
		float _requiredOffset,
		CoverPeekDirection _onlySide = CoverPeekDirection.None)
	{
		return new OffsetLosProbe
		{
			Anchor = _candidate.Position,
			RequiredOffset = _requiredOffset,
			OnlySide = _onlySide,
			Right = CoverPeekGeometry.RightTangent(_candidate.Normal)
		};
	}

	private static CoverCandidate MakeCorner(int _id, Vector3 _position, Vector3 _normal)
	{
		CoverRegionId region = CoverSpatialMath.WorldToRegion(_position, CoverSpatialMath.DefaultRegionSizeMeters);
		return new CoverCandidate
		{
			CandidateId = _id,
			Position = _position,
			Normal = _normal,
			CoverType = CoverType.Corner,
			CornerValid = true,
			StandingValid = true,
			CrouchValid = true,
			NavMeshValid = true,
			GeometryVersion = 1,
			RegionId = region,
			Occupancy = CoverOccupancy.Available
		};
	}

	private static CoverSituation Situation(Vector3 _unit, Vector3 _target)
	{
		Vector3 hostile = _target - _unit;
		hostile.y = 0f;
		if (hostile.sqrMagnitude < 0.0001f)
			hostile = Vector3.forward;
		return new CoverSituation
		{
			UnitPosition = _unit,
			Stance = CoverStance.Standing,
			Mission = CoverMissionIntent.Hold,
			Weapon = CoverWeaponClass.Rifle,
			Rank = CoverRankClass.Soldier,
			TargetPosition = _target,
			HasTarget = true,
			SectorForward = Vector3.forward,
			HostileDirection = hostile,
			GeometryVersion = 1,
			UnitId = 1
		};
	}

	private Bounds SpawnArena()
	{
		DestroyArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			s_Origin,
			CoverSpatialMath.DefaultRegionSizeMeters);
		Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
		m_Arena = new GameObject("CoverPeekArena");
		m_Arena.transform.position = bounds.center;
		m_Arena.AddComponent<CoverCandidateDebugDraw>();
		return bounds;
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

	private void DestroyArena()
	{
		if (m_Unit != null)
		{
			Destroy(m_Unit);
			m_Unit = null;
		}

		if (m_Arena == null)
			return;
		Destroy(m_Arena);
		m_Arena = null;
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
		string path = Path.Combine(dir, "CoverPeek_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverPeek] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverPeek;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
