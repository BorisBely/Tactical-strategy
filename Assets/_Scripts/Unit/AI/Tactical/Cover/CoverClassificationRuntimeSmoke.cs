using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// #13.2 Play: low/high/corner/partial walls → CoverType on shared candidates. No score. No Fire.
/// Report: Assets/_Docs/Logs/Tests/CoverClassification_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class CoverClassificationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(7000f, 0f, 7000f);
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
	#endregion

	#region Public Properties
	public bool WillRunOnStart =>
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverClassification;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverClassification)
			return;
		if (FindAnyObjectByType<CoverClassificationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverClassificationRuntimeSmoke");
		go.AddComponent<CoverClassificationRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverClassification)
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
		AppendLine("STAGE 13.2 — COVER CLASSIFICATION");
		AppendLine("=================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Geometry type + protection profile. Not score. Not Fire. Not lean.");
		AppendLine("---");

		Bounds regionBounds = SpawnArena();
		Physics.SyncTransforms();
		NavMeshSurface surface = m_Arena.GetComponent<NavMeshSurface>();
		surface.BuildNavMesh();
		yield return null;

		bool sampled = NavMesh.SamplePosition(
			regionBounds.center + Vector3.up * 0.1f,
			out NavMeshHit navHit,
			2f,
			NavMesh.AllAreas);
		Check("S0_NavMeshBake", surface.navMeshData != null && sampled,
			"sample=" + sampled + " hit=" + navHit.hit);

		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();

		AppendLine("[S1] Shared generate classifies once");
		IReadOnlyList<CoverCandidate> first = cache.GetCandidates(regionBounds.center);
		debug.Capture(regionBounds, first, false, generator.LastRejected);
		int standing = 0;
		int crouch = 0;
		int partial = 0;
		int corner = 0;
		int none = 0;
		for (int i = 0; i < first.Count; i++)
		{
			switch (first[i].CoverType)
			{
				case CoverType.Standing:
					standing++;
					break;
				case CoverType.Crouch:
					crouch++;
					break;
				case CoverType.Partial:
					partial++;
					break;
				case CoverType.Corner:
					corner++;
					break;
				default:
					none++;
					break;
			}
		}

		AppendLine(
			"candidates=" + first.Count +
			" classified=" + generator.LastClassificationCount +
			" standing=" + standing +
			" crouch=" + crouch +
			" partial=" + partial +
			" corner=" + corner +
			" none=" + none);
		Check("S1_HasCandidates", first.Count > 0, "count=" + first.Count);
		Check("S1_ClassifiedOnce", generator.LastClassificationCount == first.Count,
			"class=" + generator.LastClassificationCount);
		Check("S1_HasStanding", standing > 0, "standing=" + standing);
		Check("S1_HasCrouch", crouch > 0, "crouch=" + crouch);
		Check("S1_NotAllNone", standing + crouch + partial + corner > 0, "none=" + none);

		AppendLine("[S2] Cache hit keeps the same classification");
		IReadOnlyList<CoverCandidate> second = cache.GetCandidates(regionBounds.center + new Vector3(0.3f, 0f, 0.2f));
		debug.Capture(regionBounds, second, true, generator.LastRejected);
		Check("S2_Hit", cache.GenerationCount == 1 && geometry.QueryCount == 1,
			"gen=" + cache.GenerationCount);
		Check("S2_SameList", ReferenceEquals(first, second), "identity");
		if (first.Count > 0)
			Check("S2_SameType", first[0].CoverType == second[0].CoverType, second[0].CoverType.ToString());

		AppendLine("[S3] 20 units / 3 regions → 3 classification batches");
		Vector3 r2 = regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
		Vector3 r3 = regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters);
		QueryMany(cache, regionBounds.center, 6);
		QueryMany(cache, r2, 7);
		QueryMany(cache, r3, 5);
		Check("S3_Generations", cache.GenerationCount == 3, "gen=" + cache.GenerationCount);
		Check("S3_NotPerUnit", cache.GenerationCount < 20, "gen=" + cache.GenerationCount);

		AppendLine("[S4] Debug overlay by CoverType, no score");
		Check("S4_Debug", debug.CandidateCount == first.Count, "drawn=" + debug.CandidateCount);

		yield return null;
		Finish();
	}

	private Bounds SpawnArena()
	{
		DestroyArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			s_Origin,
			CoverSpatialMath.DefaultRegionSizeMeters);
		Bounds bounds = CoverSpatialMath.RegionBounds(region, CoverSpatialMath.DefaultRegionSizeMeters);
		Vector3 c = bounds.center;

		m_Arena = new GameObject("CoverClassificationArena");
		m_Arena.transform.position = c;
		NavMeshSurface surface = m_Arena.AddComponent<NavMeshSurface>();
		surface.agentTypeID = 0;
		surface.collectObjects = CollectObjects.Children;
		surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
		surface.ignoreNavMeshAgent = true;
		surface.ignoreNavMeshObstacle = true;
		surface.minRegionArea = 0.5f;
		m_Arena.AddComponent<CoverCandidateDebugDraw>();

		CreateBox("Ground", c + new Vector3(0f, -0.1f, 0f), new Vector3(22f, 0.2f, 22f));
		CreateBox("TestWall_B", c + new Vector3(0f, 1.1f, 5.5f), new Vector3(8f, 2.2f, 0.4f));
		CreateBox("TestWall_A", c + new Vector3(-5.5f, 0.575f, 0f), new Vector3(0.4f, 1.15f, 8f));
		CreateBox("Corner_A", c + new Vector3(5.2f, 1.1f, -2.5f), new Vector3(2.4f, 2.2f, 0.4f));
		CreateBox("Partial_A", c + new Vector3(0f, 0.3f, -5.5f), new Vector3(6f, 0.6f, 0.4f));
		CreateBox("WallR2", c + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 1.1f, 0f), new Vector3(6f, 2.2f, 0.4f));
		CreateBox("WallR3", c + new Vector3(0f, 1.1f, CoverSpatialMath.DefaultRegionSizeMeters), new Vector3(0.4f, 2.2f, 6f));
		return bounds;
	}

	private void CreateBox(string _name, Vector3 _world, Vector3 _lossyScale)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = _name;
		go.transform.SetParent(m_Arena.transform, true);
		go.transform.position = _world;
		go.transform.localScale = _lossyScale;
	}

	private static void QueryMany(SharedCoverSpatialCache _cache, Vector3 _anchor, int _count)
	{
		for (int i = 0; i < _count; i++)
			_cache.GetCandidates(_anchor + Vector3.right * (i * 0.12f));
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
		string path = Path.Combine(dir, "CoverClassification_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverClassification] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverClassification;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
