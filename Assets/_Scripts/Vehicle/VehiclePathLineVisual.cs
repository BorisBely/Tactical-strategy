using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vehicle-only route line. Independent from RtsUnitMember path visuals.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehiclePathLineVisual : MonoBehaviour
{
	#region Constants
	private static readonly Vector3 s_YOffset = Vector3.up * 0.05f;
	private const float c_PreviewAlpha = 0.35f;
	private const float c_NormalAlpha = 0.85f;
	#endregion

	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField] private VehicleNavigation.VehicleNavigation m_Follower;
	#endregion

	#region Private Fields
	private static Material s_Material;
	private LineRenderer m_CommittedLine;
	private LineRenderer m_PreviewLine;
	private readonly List<Vector3> m_PointBuffer = new List<Vector3>(32);
	private readonly VehicleMovePoseGhostVisual m_PoseGhost = new VehicleMovePoseGhostVisual();
	#endregion

	#region Unity Lifecycle
	private void Awake()
	{
		if (m_Vehicle == null)
			TryGetComponent(out m_Vehicle);
		if (m_Follower == null)
			TryGetComponent(out m_Follower);
		EnsureLines();
	}

	private void OnEnable()
	{
		if (m_Follower != null)
			m_Follower.PathChanged += OnPathChanged;
		if (m_Vehicle != null)
			m_Vehicle.SelectionChanged += OnSelectionChanged;
		RefreshCommitted();
	}

	private void OnDisable()
	{
		if (m_Follower != null)
			m_Follower.PathChanged -= OnPathChanged;
		if (m_Vehicle != null)
			m_Vehicle.SelectionChanged -= OnSelectionChanged;
		m_PoseGhost.Clear();
	}

	private void LateUpdate()
	{
		m_PoseGhost.Draw();

		if (m_CommittedLine == null || m_Vehicle == null || !m_Vehicle.IsSelected)
			return;
		if (m_Follower == null || !m_Follower.HasDestination)
			return;

		RefreshDynamicLine();
	}

	private void RefreshDynamicLine()
	{
		if (m_Follower == null)
			return;

		var maneuver = m_Follower.CurrentManeuver;
		if (maneuver == null || maneuver.Waypoints == null || maneuver.Waypoints.Count == 0)
		{
			m_PointBuffer.Clear();
			m_PointBuffer.Add(transform.position);
			m_PointBuffer.Add(m_Follower.Destination);
			ApplyLine(m_CommittedLine, m_PointBuffer, m_Follower.ActiveSpeedMode, _preview: false);
			return;
		}

		var waypoints = maneuver.Waypoints;
		var debug = m_Follower.PursuitDebug;
		int startIndex = 0;

		if (debug.TotalWaypoints > 0)
			startIndex = Mathf.Max(0, debug.NearestWaypointIndex);

		m_PointBuffer.Clear();
		m_PointBuffer.Add(transform.position);

		for (int i = startIndex; i < waypoints.Count; i++)
			m_PointBuffer.Add(waypoints[i]);

		if (m_PointBuffer.Count < 2)
			m_PointBuffer.Add(m_Follower.Destination);

		ApplyLine(m_CommittedLine, m_PointBuffer, m_Follower.ActiveSpeedMode, _preview: false);
	}
	#endregion

	#region Public Methods
	public void Configure(VehicleController _vehicle, VehicleNavigation.VehicleNavigation _follower)
	{
		if (m_Follower != null)
			m_Follower.PathChanged -= OnPathChanged;
		if (m_Vehicle != null)
			m_Vehicle.SelectionChanged -= OnSelectionChanged;

		m_Vehicle = _vehicle;
		m_Follower = _follower;
		EnsureLines();

		if (isActiveAndEnabled)
		{
			if (m_Follower != null)
				m_Follower.PathChanged += OnPathChanged;
			if (m_Vehicle != null)
				m_Vehicle.SelectionChanged += OnSelectionChanged;
			RefreshCommitted();
		}
	}

	public void SetPreviewDestination(Vector3 _worldPoint, VehicleSpeedMode _mode)
	{
		SetPreviewDestination(_worldPoint, _mode, null);
	}

	public void SetPreviewDestination(Vector3 _worldPoint, VehicleSpeedMode _mode, float? _headingYawDegrees)
	{
		EnsureLines();
		m_PointBuffer.Clear();
		m_PointBuffer.Add(transform.position);
		m_PointBuffer.Add(_worldPoint);
		ApplyLine(m_PreviewLine, m_PointBuffer, _mode, _preview: true);
		UpdatePoseGhost(_worldPoint, _headingYawDegrees);
	}

	public void ClearPreview()
	{
		if (m_PreviewLine != null)
		{
			m_PreviewLine.positionCount = 0;
			m_PreviewLine.enabled = false;
		}

		m_PoseGhost.Clear();
	}

	public void RefreshCommitted()
	{
		EnsureLines();
		bool selected = m_Vehicle != null && m_Vehicle.IsSelected;
		if (!selected || m_Follower == null || !m_Follower.HasDestination)
		{
			if (m_CommittedLine != null)
			{
				m_CommittedLine.positionCount = 0;
				m_CommittedLine.enabled = false;
			}
			return;
		}

		RefreshDynamicLine();
	}
	#endregion

	#region Private Methods
	private void OnPathChanged() => RefreshCommitted();

	private void OnSelectionChanged()
	{
		RefreshCommitted();
		if (m_Vehicle != null && !m_Vehicle.IsSelected)
			ClearPreview();
	}

	private void EnsureLines()
	{
		EnsureMaterial();
		if (m_CommittedLine == null)
			m_CommittedLine = CreateLine("VehiclePathLine");
		if (m_PreviewLine == null)
			m_PreviewLine = CreateLine("VehiclePathPreviewLine");
		DestroyLegacyFacingArrow();
	}

	private void DestroyLegacyFacingArrow()
	{
		Transform existing = transform.Find("VehiclePreviewFacingArrow");
		if (existing != null)
			Destroy(existing.gameObject);
	}

	private void UpdatePoseGhost(Vector3 _worldPoint, float? _headingYawDegrees)
	{
		bool selected = m_Vehicle != null && m_Vehicle.IsSelected;
		if (!selected || !_headingYawDegrees.HasValue)
		{
			m_PoseGhost.Clear();
			return;
		}

		m_PoseGhost.SetPose(transform, _worldPoint, _headingYawDegrees.Value);
	}

	private LineRenderer CreateLine(string _name)
	{
		Transform existing = transform.Find(_name);
		GameObject go = existing != null ? existing.gameObject : new GameObject(_name);
		if (existing == null)
			go.transform.SetParent(transform, false);

		LineRenderer line = go.GetComponent<LineRenderer>();
		if (line == null)
			line = go.AddComponent<LineRenderer>();

		line.useWorldSpace = true;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.receiveShadows = false;
		line.sharedMaterial = s_Material;
		line.positionCount = 0;
		line.enabled = false;
		return line;
	}

	private static void EnsureMaterial()
	{
		if (s_Material != null)
			return;
		s_Material = new Material(Shader.Find("Sprites/Default"));
		s_Material.hideFlags = HideFlags.HideAndDontSave;
	}

	private void ApplyLine(LineRenderer _line, List<Vector3> _points, VehicleSpeedMode _mode, bool _preview)
	{
		if (_line == null)
			return;
		if (_points == null || _points.Count < 2)
		{
			_line.positionCount = 0;
			_line.enabled = false;
			return;
		}

		bool selected = m_Vehicle != null && m_Vehicle.IsSelected;
		float alpha = _preview ? c_PreviewAlpha : c_NormalAlpha;
		Color color = VehicleSpeedModeUtil.PathColor(_mode, alpha);
		float width = VehicleSpeedModeUtil.PathWidth(_mode) * (_preview ? 0.7f : 1f);

		_line.positionCount = _points.Count;
		for (int i = 0; i < _points.Count; i++)
			_line.SetPosition(i, _points[i] + s_YOffset);
		_line.startWidth = width;
		_line.endWidth = width;
		_line.startColor = color;
		_line.endColor = color;
		_line.enabled = selected;
	}
	#endregion
}
