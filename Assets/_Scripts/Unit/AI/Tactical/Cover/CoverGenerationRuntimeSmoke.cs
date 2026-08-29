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
/// #13.1 Play: local walls → CoverCandidate → shared cache. No score. No classification. No Fire.
/// Report: Assets/_Docs/Logs/Tests/CoverGeneration_LAST.txt
/// Menu: Tools/Tests/Run Dynamic Cover (Play)
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class CoverGenerationRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(6000f, 0f, 6000f);
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
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverGeneration;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverGeneration)
			return;
		if (FindAnyObjectByType<CoverGenerationRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverGenerationRuntimeSmoke");
		go.AddComponent<CoverGenerationRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverGeneration)
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
		AppendLine("STAGE 13.1 — CANDIDATE GENERATION");
		AppendLine("=================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Geometry → CoverCandidate → shared cache. Not score. Not Fire.");
		AppendLine("---");

		Bounds regionBounds = SpawnArena();
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			regionBounds.center,
			CoverSpatialMath.DefaultRegionSizeMeters);
		Physics.SyncTransforms();

		NavMeshSurface surface = m_Arena.GetComponent<NavMeshSurface>();
		surface.BuildNavMesh();
		yield return null;

		NavMeshHit navHit;
		bool sampled = NavMesh.SamplePosition(
			regionBounds.center + Vector3.up * 0.1f,
			out navHit,
			2f,
			NavMesh.AllAreas);
		bool baked = surface.navMeshData != null && sampled;
		Check("S0_NavMeshBake", baked, "navMeshData=" + (surface.navMeshData != null) + " sample=" + sampled);

		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings);
		SharedCoverSpatialCache cache = new SharedCoverSpatialCache(generator);
		CoverCandidateDebugDraw debug = m_Arena.GetComponent<CoverCandidateDebugDraw>();

		AppendLine("[S1] First query generates from local geometry");
		IReadOnlyList<CoverCandidate> first = cache.GetCandidates(regionBounds.center);
		debug.Capture(regionBounds, first, false, generator.LastRejected);
		AppendLine(
			"candidates=" + first.Count +
			" samples=" + generator.LastSampleCount +
			" navReject=" + generator.LastRejectedNavMeshCount +
			" clearReject=" + generator.LastRejectedClearanceCount +
			" ms=" + generator.LastGenerationMilliseconds.ToString("F2"));
		Check("S1_Miss", cache.CacheMissCount == 1 && cache.GenerationCount == 1,
			"miss=" + cache.CacheMissCount + " gen=" + cache.GenerationCount);
		Check("S1_GeometryQuery", geometry.QueryCount == 1, "queries=" + geometry.QueryCount);
		Check("S1_HasCandidates", first.Count > 0, "count=" + first.Count);
		Check("S1_Cap", first.Count <= CoverSpatialMath.DefaultMaxCoverCandidates, "count=" + first.Count);
		if (first.Count > 0)
		{
			CoverCandidate c = first[0];
			Check("S1_Position", c.Position.sqrMagnitude > 0.01f, c.Position.ToString());
			Check("S1_Normal", c.Normal.sqrMagnitude > 0.5f, c.Normal.ToString());
			Check("S1_Region", c.RegionId == region, c.RegionId.LogLabel);
			Check("S1_Version", c.GeometryVersion == cache.GeometryVersion, "v=" + c.GeometryVersion);
			Check("S1_NavMeshValid", c.NavMeshValid, "nav=0");
			Check("S1_NoClassification", c.CoverType == CoverType.None, c.CoverType.ToString());
		}

		AppendLine("[S2] Second query reuses cache, no geometry query");
		IReadOnlyList<CoverCandidate> second = cache.GetCandidates(regionBounds.center + new Vector3(0.4f, 0f, 0.2f));
		debug.Capture(regionBounds, second, true, generator.LastRejected);
		Check("S2_Hit", cache.CacheHitCount == 1, "hit=" + cache.CacheHitCount);
		Check("S2_NoRegen", cache.GenerationCount == 1 && geometry.QueryCount == 1,
			"gen=" + cache.GenerationCount + " geo=" + geometry.QueryCount);
		Check("S2_SameList", ReferenceEquals(first, second), "identity");

		AppendLine("[S3] 20 units / 3 regions → 3 generations");
		Vector3 r2 = regionBounds.center + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 0f, 0f);
		Vector3 r3 = regionBounds.center + new Vector3(0f, 0f, CoverSpatialMath.DefaultRegionSizeMeters);
		QueryMany(cache, regionBounds.center, 6);
		QueryMany(cache, r2, 7);
		QueryMany(cache, r3, 5);
		Check("S3_Generations", cache.GenerationCount == 3, "gen=" + cache.GenerationCount);
		Check("S3_GeometryQueries", geometry.QueryCount == 3, "geo=" + geometry.QueryCount);
		Check("S3_NotPerUnit", cache.GenerationCount < 20, "gen=" + cache.GenerationCount);

		AppendLine("[S4] Debug capture (candidate + normal, no score)");
		Check("S4_Debug", debug.CandidateCount == first.Count && debug.FromCache,
			"drawn=" + debug.CandidateCount);

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

		m_Arena = new GameObject("CoverGenerationArena");
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
		CreateBox("WallNorth", c + new Vector3(0f, 1f, 6f), new Vector3(12f, 2f, 0.4f));
		CreateBox("WallWest", c + new Vector3(-6f, 1f, 0f), new Vector3(0.4f, 2f, 10f));
		CreateBox("WallEast", c + new Vector3(6f, 1f, -1f), new Vector3(0.4f, 2f, 8f));
		CreateBox("Obstacle", c + new Vector3(2f, 0.6f, -2f), new Vector3(1.4f, 1.2f, 1.4f));
		CreateBox("WallR2", c + new Vector3(CoverSpatialMath.DefaultRegionSizeMeters, 1f, 0f), new Vector3(6f, 2f, 0.4f));
		CreateBox("WallR3", c + new Vector3(0f, 1f, CoverSpatialMath.DefaultRegionSizeMeters), new Vector3(0.4f, 2f, 6f));
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
		string path = Path.Combine(dir, "CoverGeneration_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverGeneration] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);

#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverGeneration;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
