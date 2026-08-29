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
/// #13.2B.2 Play: bake Opening once, then read via BakedCoverCandidateSource. No unit behavior.
/// Report: Assets/_Docs/Logs/Tests/CoverOpening_LAST.txt
/// </summary>
[DefaultExecutionOrder(65)]
[DisallowMultipleComponent]
public sealed class CoverOpeningRuntimeSmoke : MonoBehaviour
{
	#region Constants
	private static readonly Vector3 s_Origin = new Vector3(8200f, 0f, 8200f);
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
		m_RunOnStart || DetectionHarnessPlayMode.RunCoverOpeningBake;
	#endregion

	#region Unity Lifecycle
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void BootIfFlagged()
	{
		if (!Application.isPlaying || !DetectionHarnessPlayMode.RunCoverOpeningBake)
			return;
		if (FindAnyObjectByType<CoverOpeningRuntimeSmoke>() != null)
			return;
		var go = new GameObject("CoverOpeningRuntimeSmoke");
		go.AddComponent<CoverOpeningRuntimeSmoke>();
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
		if (DetectionHarnessPlayMode.RunCoverOpeningBake)
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
		AppendLine("STAGE 13.2B.2 — COVER OPENING BAKE");
		AppendLine("=================================");
		AppendLine("stamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		AppendLine("Geometry Opening only. Not score. Not Fire. Not peek.");
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
			"sample=" + sampled);

		var settings = new CoverGenerationSettings { ConfirmSurfaceWithPhysics = true };
		var geometry = new PhysicsCoverGeometrySource();
		var generator = new CoverCandidateGenerator(
			geometry,
			new NavMeshCoverProbe(1.2f),
			new PhysicsCoverClearanceProbe(),
			settings,
			new PhysicsCoverOcclusionProbe());
		CoverRegionId region = CoverSpatialMath.WorldToRegion(
			regionBounds.center,
			CoverSpatialMath.DefaultRegionSizeMeters);
		var generated = new List<CoverCandidate>();
		generator.Generate(region, regionBounds, 1, generated);

		int openings = CountOpenings(generated);
		AppendLine("generated=" + generated.Count + " openings=" + openings + " geo=" + geometry.QueryCount);
		Check("S1_HasOpening", openings >= 1, "openings=" + openings);
		Check("S2_OneDoorCluster", UniqueOpeningClusters(generated) <= 2, "clusters=" + UniqueOpeningClusters(generated));

		var baked = new List<BakedCoverCandidateRecord>(generated.Count);
		for (int i = 0; i < generated.Count; i++)
			baked.Add(BakedCoverCandidateRecord.FromCandidate(generated[i]));

		int geoBeforePlay = geometry.QueryCount;
		var playDest = new List<CoverCandidate>();
		new BakedCoverCandidateSource(baked).Generate(region, regionBounds, 1, playDest);
		Check("S3_PlayReadsOpening", CountOpenings(playDest) >= 1, "playOpenings=" + CountOpenings(playDest));
		Check("S4_PlayNoGeometrySearch", geometry.QueryCount == geoBeforePlay,
			"geo=" + geometry.QueryCount);
		bool widthOk = false;
		for (int i = 0; i < playDest.Count; i++)
		{
			if (!playDest[i].OpeningValid)
				continue;
			widthOk = playDest[i].OpeningWidth >= 0.7f && playDest[i].OpeningWidth <= 3.5f;
			if (widthOk)
				break;
		}

		Check("S5_OpeningWidthBaked", widthOk, "width missing");

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
		m_Arena = new GameObject("CoverOpeningArena");
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
		CreateBox("DoorLeft", c + new Vector3(-2.5f, 1.1f, 0f), new Vector3(4f, 2.2f, 0.4f));
		CreateBox("DoorRight", c + new Vector3(2.5f, 1.1f, 0f), new Vector3(4f, 2.2f, 0.4f));
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

	private static int CountOpenings(List<CoverCandidate> _list)
	{
		int n = 0;
		for (int i = 0; i < _list.Count; i++)
		{
			if (_list[i] != null && _list[i].OpeningValid)
				n++;
		}

		return n;
	}

	private static int UniqueOpeningClusters(List<CoverCandidate> _list)
	{
		int n = 0;
		for (int i = 0; i < _list.Count; i++)
		{
			if (_list[i] == null || !_list[i].OpeningValid)
				continue;
			bool unique = true;
			for (int j = 0; j < i; j++)
			{
				if (_list[j] == null || !_list[j].OpeningValid)
					continue;
				if (CoverSpatialMath.PlanarDistanceSqr(_list[i].OpeningCenter, _list[j].OpeningCenter) > 0.8f)
					continue;
				if (Vector3.Dot(_list[i].Normal, _list[j].Normal) <= 0.5f)
					continue;
				unique = false;
				break;
			}

			if (unique)
				n++;
		}

		return n;
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
		string path = Path.Combine(dir, "CoverOpening_LAST.txt");
		File.WriteAllText(path, m_Report.ToString(), Encoding.UTF8);
		Debug.Log(
			"[CoverOpening] " + (m_FailCount == 0 ? "PASS" : "FAIL") +
			" pass=" + m_PassCount + " fail=" + m_FailCount + " → " + path,
			this);
#if UNITY_EDITOR
		bool exitPlay = m_ExitPlayModeWhenDone || DetectionHarnessPlayMode.RunCoverOpeningBake;
		if (exitPlay && EditorApplication.isPlaying)
			EditorApplication.isPlaying = false;
#endif
	}
	#endregion
}
