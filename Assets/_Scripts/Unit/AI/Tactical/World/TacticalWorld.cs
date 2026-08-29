using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Scene host for editor-baked cover geometry and a runtime occupancy board.
/// Play does not create this object and does not scan the whole arena for walls.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public sealed class TacticalWorld : MonoBehaviour
{
	#region Constants
	public const string DefaultChildName = "TacticalWorld";
	#endregion

	#region Serialized
	[SerializeField] private TacticalWorldProfile m_Profile;
	[SerializeField] private Bounds m_BakeBounds = new Bounds(new Vector3(0f, 1f, 75f), new Vector3(50f, 4f, 150f));
	[SerializeField] private bool m_BakeBoundsAreLocal = true;
	[SerializeField] private List<BakedCoverCandidateRecord> m_Baked =
		new List<BakedCoverCandidateRecord>(128);
	[SerializeField] private bool m_DrawBaked = true;
	#endregion

	#region Private Fields
	private static readonly List<TacticalWorld> s_Worlds = new List<TacticalWorld>(4);
	private SharedCoverSpatialCache m_Cache;
	private CoverOccupancyBoard m_Occupancy;
	private bool m_RuntimeReady;
	#endregion

	#region Public Properties
	public TacticalWorldProfile Profile => m_Profile;
	public SharedCoverSpatialCache Cache
	{
		get
		{
			EnsureRuntime();
			return m_Cache;
		}
	}

	public CoverOccupancyBoard Occupancy
	{
		get
		{
			EnsureRuntime();
			return m_Occupancy;
		}
	}

	public int BakedCount => m_Baked != null ? m_Baked.Count : 0;
	public IReadOnlyList<BakedCoverCandidateRecord> Baked => m_Baked;
	public bool IsBaked => m_Baked != null && m_Baked.Count > 0;
	public Bounds BakeBounds => m_BakeBounds;
	public bool BakeBoundsAreLocal => m_BakeBoundsAreLocal;
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		EnsureRuntime();
	}

	private void OnEnable()
	{
		Register(this);
	}

	private void OnDisable()
	{
		Unregister(this);
	}

	private void OnDrawGizmos()
	{
		if (!m_DrawBaked || m_Baked == null)
			return;
		for (int i = 0; i < m_Baked.Count; i++)
			DrawRecord(m_Baked[i], false);
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.25f);
		Bounds world = ResolveWorldBakeBounds();
		Gizmos.DrawWireCube(world.center, world.size);
		if (m_Baked == null)
			return;
		for (int i = 0; i < m_Baked.Count; i++)
			DrawRecord(m_Baked[i], true);
#if UNITY_EDITOR
		int labelCap = Mathf.Min(m_Baked.Count, 80);
		for (int i = 0; i < labelCap; i++)
		{
			BakedCoverCandidateRecord record = m_Baked[i];
			Handles.Label(
				record.Position + Vector3.up * 0.35f,
				"C" + record.CandidateId + " " + record.CoverType);
		}
#endif
	}
	#endregion

	#region Public Methods
	public static TacticalWorld Find(TacticalWorldProfile _profile)
	{
		TacticalWorld any = null;
		for (int i = 0; i < s_Worlds.Count; i++)
		{
			TacticalWorld world = s_Worlds[i];
			if (world == null)
				continue;
			if (_profile != null && world.m_Profile == _profile)
				return world;
			if (any == null)
				any = world;
		}

		TacticalWorld[] found = FindObjectsByType<TacticalWorld>(FindObjectsInactive.Exclude);
		for (int i = 0; i < found.Length; i++)
		{
			if (found[i] == null)
				continue;
			Register(found[i]);
			if (_profile != null && found[i].m_Profile == _profile)
				return found[i];
			if (any == null)
				any = found[i];
		}

		return _profile == null ? any : null;
	}

	public void AssignProfile(TacticalWorldProfile _profile)
	{
		m_Profile = _profile;
	}

	public void SetBakeBounds(Bounds _bounds, bool _local)
	{
		m_BakeBounds = _bounds;
		m_BakeBoundsAreLocal = _local;
	}

	public Bounds ResolveWorldBakeBounds()
	{
		if (!m_BakeBoundsAreLocal)
			return m_BakeBounds;
		Transform t = transform;
		Vector3 center = t.TransformPoint(m_BakeBounds.center);
		Vector3 size = t.TransformVector(m_BakeBounds.size);
		size.x = Mathf.Abs(size.x);
		size.y = Mathf.Abs(size.y);
		size.z = Mathf.Abs(size.z);
		return new Bounds(center, size);
	}

	public int ReplaceBake(IReadOnlyList<BakedCoverCandidateRecord> _records)
	{
		if (m_Baked == null)
			m_Baked = new List<BakedCoverCandidateRecord>(128);
		m_Baked.Clear();
		if (_records != null)
		{
			for (int i = 0; i < _records.Count; i++)
				m_Baked.Add(_records[i]);
		}

		m_RuntimeReady = false;
		m_Cache = null;
		return m_Baked.Count;
	}

	public void EnsureRuntime()
	{
		if (m_RuntimeReady && m_Cache != null && m_Occupancy != null)
			return;
		var source = new BakedCoverCandidateSource(m_Baked);
		m_Cache = new SharedCoverSpatialCache(source);
		m_Occupancy = new CoverOccupancyBoard();
		m_RuntimeReady = true;
		Register(this);
		// #region agent log
		AgentDebugNdjson.Write(
			"B",
			"TacticalWorld.EnsureRuntime",
			"world ready",
			"{\"baked\":" + BakedCount +
			",\"hasProfile\":" + (m_Profile != null ? "true" : "false") +
			",\"hasCache\":true,\"hasOccupancy\":true}");
		// #endregion
	}
	#endregion

	#region Private Methods
	private static void Register(TacticalWorld _world)
	{
		if (_world == null)
			return;
		if (!s_Worlds.Contains(_world))
			s_Worlds.Add(_world);
	}

	private static void Unregister(TacticalWorld _world)
	{
		s_Worlds.Remove(_world);
	}

	private void DrawRecord(BakedCoverCandidateRecord _record, bool _selected)
	{
		Color color = TypeColor(_record.CoverType);
		if (Application.isPlaying && m_Occupancy != null)
		{
			CoverOccupancy occupancy = m_Occupancy.GetState(
				new CoverRegionId(_record.RegionX, _record.RegionZ),
				_record.CandidateId,
				Time.time);
			if (occupancy == CoverOccupancy.Occupied)
				color = new Color(0.95f, 0.2f, 0.15f, 1f);
			else if (occupancy == CoverOccupancy.Reserved)
				color = new Color(1f, 0.75f, 0.15f, 1f);
		}
		if (!_selected)
			color.a = 0.7f;
		Gizmos.color = color;
		Gizmos.DrawSphere(_record.Position, _selected ? 0.22f : 0.16f);
		Gizmos.DrawLine(_record.Position, _record.Position + _record.Normal.normalized * 0.8f);
	}

	private static Color TypeColor(CoverType _type)
	{
		if (_type == CoverType.Corner)
			return new Color(1f, 0.55f, 0.15f, 1f);
		if (_type == CoverType.Standing)
			return new Color(0.2f, 0.85f, 1f, 1f);
		if (_type == CoverType.Crouch)
			return new Color(1f, 0.85f, 0.2f, 1f);
		if (_type == CoverType.Partial)
			return new Color(0.95f, 0.35f, 0.9f, 1f);
		return new Color(0.45f, 0.9f, 0.4f, 1f);
	}
	#endregion
}
