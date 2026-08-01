using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет маркером прицеливания гранаты под курсором и прерывистой линией траектории.
/// </summary>
public sealed class GrenadeAimMarkerController : MonoBehaviour
{
	#region Serialized Fields
	[Header("Marker")]
	[SerializeField] private GameObject m_MarkerPrefab;
	[SerializeField, Min(0.1f)] private float m_MarkerScale = 0.3f;
	[SerializeField] private float m_MarkerYOffset = 0.05f;

	[Header("Trajectory Line")]
	[SerializeField] private LineRenderer m_TrajectoryLine;
	[SerializeField, Min(4)] private int m_TrajectorySegments = 30;
	[SerializeField, Min(0.01f)] private float m_DashSegmentLength = 0.5f;
	[SerializeField, Min(0.01f)] private float m_GapSegmentLength = 0.3f;

	[Header("Range Indicator")]
	[SerializeField] private float m_MinRangeFlashSpeed = 4f;
	#endregion

	#region Private Fields
	private GameObject m_MarkerInstance;
	private Renderer m_MarkerRenderer;
	private Color m_CurrentColor;
	private bool m_IsVisible;
	private bool m_HasValidAimTarget;
	private Vector3 m_LastAimWorldPosition;
	private Camera m_SelectionCamera;
	private UnitGrenadeThrowController m_ThrowController;
	private static readonly int s_Color = Shader.PropertyToID("_Color");
	private static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
	#endregion

	#region Public Properties
	public bool IsVisible => m_IsVisible;
	public bool HasValidAimTarget => m_HasValidAimTarget;
	public Vector3 LastAimWorldPosition => m_LastAimWorldPosition;
	public Vector3 MarkerWorldPosition =>
		m_HasValidAimTarget
			? m_LastAimWorldPosition
			: (m_MarkerInstance != null ? m_MarkerInstance.transform.position : Vector3.zero);
	#endregion

	#region Unity Lifecycle
	private void Update()
	{
		if (!m_IsVisible)
			return;

		UpdateMarkerFlash();
	}
	#endregion

	#region Public Methods
	public void Initialize(Camera _camera, UnitGrenadeThrowController _throwController, GameObject _markerPrefab = null)
	{
		m_SelectionCamera = _camera;
		m_ThrowController = _throwController;

		if (_markerPrefab != null)
			m_MarkerPrefab = _markerPrefab;

		EnsureTrajectoryLine();
	}

	public void SetThrowController(UnitGrenadeThrowController _throwController)
	{
		m_ThrowController = _throwController;
	}

	public void Show()
	{
		if (m_IsVisible)
			return;

		m_IsVisible = true;
		m_HasValidAimTarget = false;
		EnsureMarkerInstance();

		if (m_MarkerInstance != null)
			m_MarkerInstance.SetActive(true);

		if (m_TrajectoryLine != null)
			m_TrajectoryLine.enabled = true;
	}

	public void Hide()
	{
		if (!m_IsVisible)
			return;

		m_IsVisible = false;
		m_HasValidAimTarget = false;

		if (m_MarkerInstance != null)
			m_MarkerInstance.SetActive(false);

		if (m_TrajectoryLine != null)
			m_TrajectoryLine.enabled = false;
	}

	public void SetColor(Color _color)
	{
		m_CurrentColor = _color;

		if (m_MarkerRenderer != null)
		{
			MaterialPropertyBlock block = new MaterialPropertyBlock();
			block.SetColor(s_Color, _color);
			block.SetColor(s_BaseColor, _color);
			m_MarkerRenderer.SetPropertyBlock(block);
		}

		if (m_TrajectoryLine != null)
		{
			m_TrajectoryLine.startColor = _color;
			m_TrajectoryLine.endColor = _color;
		}
	}

	public void UpdateAiming(Vector3 _throwerPosition, float _releaseHeight, float _arcHeight, float _minRange, float _maxRange)
	{
		if (!m_IsVisible)
			return;

		Vector3? cursorWorldPos = GetCursorWorldPosition();
		if (!cursorWorldPos.HasValue)
			return;

		Vector3 target = cursorWorldPos.Value;
		Vector3 origin = _throwerPosition + Vector3.up * _releaseHeight;
		float dist = Vector3.Distance(origin, target);

		bool inRange = dist >= _minRange && dist <= _maxRange;

		if (dist > _maxRange)
			target = origin + (target - origin).normalized * _maxRange;

		m_LastAimWorldPosition = target;
		m_HasValidAimTarget = true;

		if (m_MarkerInstance != null)
		{
			Vector3 markerPos = target;
			markerPos.y += m_MarkerYOffset;
			m_MarkerInstance.transform.position = markerPos;
		}

		UpdateTrajectoryLine(origin, target, _arcHeight, inRange);

		if (m_ThrowController != null)
			m_ThrowController.SetTargetPosition(target);
	}

	public void UpdateTrajectoryForRoute(Vector3 _startWorld, Vector3 _targetWorld, float _arcHeight)
	{
		if (!m_IsVisible)
			return;

		UpdateTrajectoryLine(_startWorld, _targetWorld, _arcHeight, true);
	}
	#endregion

	#region Private Methods
	private Vector3? GetCursorWorldPosition()
	{
		if (m_SelectionCamera == null)
			m_SelectionCamera = Camera.main;

		if (m_SelectionCamera == null)
			return null;

		Ray ray = m_SelectionCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
		if (Physics.Raycast(ray, out RaycastHit hit, 500f))
			return hit.point;

		Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
		if (groundPlane.Raycast(ray, out float enter))
			return ray.GetPoint(enter);

		return null;
	}

	private void UpdateTrajectoryLine(Vector3 _origin, Vector3 _target, float _arcHeight, bool _inRange)
	{
		if (m_TrajectoryLine == null)
			return;

		Color lineColor = _inRange ? m_CurrentColor : Color.red;
		m_TrajectoryLine.startColor = lineColor;
		m_TrajectoryLine.endColor = lineColor;

		int totalPoints = m_TrajectorySegments;
		float totalLength = 0f;
		Vector3[] points = new Vector3[totalPoints];

		for (int i = 0; i < totalPoints; i++)
		{
			float t = (float)i / (totalPoints - 1);
			points[i] = ComputeParabolaPoint(_origin, _target, _arcHeight, t);
			if (i > 0)
				totalLength += Vector3.Distance(points[i - 1], points[i]);
		}

		int renderedPoints = 0;
		float accumulated = 0f;
		bool drawing = true;
		float dashLen = Mathf.Max(0.01f, m_DashSegmentLength);
		float gapLen = Mathf.Max(0.01f, m_GapSegmentLength);
		float segmentLength = 0f;

		System.Collections.Generic.List<Vector3> linePoints = new System.Collections.Generic.List<Vector3>();
		linePoints.Add(points[0]);
		renderedPoints = 1;

		for (int i = 1; i < totalPoints; i++)
		{
			float segDist = Vector3.Distance(points[i - 1], points[i]);
			accumulated += segDist;
			segmentLength += segDist;

			float targetLen = drawing ? dashLen : gapLen;

			if (segmentLength >= targetLen)
			{
				segmentLength -= targetLen;
				drawing = !drawing;

				if (drawing)
				{
					linePoints.Add(points[i]);
					renderedPoints++;
				}
				else
				{
					linePoints.Add(points[i]);
					renderedPoints++;
					linePoints.Add(points[i]);
					renderedPoints++;
				}
			}
			else if (drawing)
			{
				linePoints.Add(points[i]);
				renderedPoints++;
			}
		}

		if (linePoints.Count < 2)
		{
			linePoints.Clear();
			linePoints.Add(_origin);
			linePoints.Add(_target);
		}

		m_TrajectoryLine.positionCount = linePoints.Count;
		m_TrajectoryLine.SetPositions(linePoints.ToArray());
	}

	private static Vector3 ComputeParabolaPoint(Vector3 _origin, Vector3 _target, float _arcHeight, float _t)
	{
		Vector3 linear = Vector3.Lerp(_origin, _target, _t);
		float heightOffset = 4f * _arcHeight * _t * (1f - _t);
		return linear + Vector3.up * heightOffset;
	}

	private void UpdateMarkerFlash()
	{
		if (m_ThrowController == null || m_MarkerRenderer == null)
			return;

		Vector3 origin = m_ThrowController.transform.position;
		float minRange = m_ThrowController.Data != null ? m_ThrowController.Data.MinRange : 5f;
		float dist = Vector3.Distance(origin, m_MarkerInstance.transform.position);

		if (dist < minRange)
		{
			float flash = Mathf.PingPong(Time.time * m_MinRangeFlashSpeed, 1f);
			Color c = Color.Lerp(m_CurrentColor, Color.red, flash);
			MaterialPropertyBlock block = new MaterialPropertyBlock();
			block.SetColor(s_Color, c);
			block.SetColor(s_BaseColor, c);
			m_MarkerRenderer.SetPropertyBlock(block);
		}
		else
		{
			SetColor(m_CurrentColor);
		}
	}

	private void EnsureMarkerInstance()
	{
		if (m_MarkerInstance != null)
			return;

		if (m_MarkerPrefab != null)
		{
			m_MarkerInstance = Instantiate(m_MarkerPrefab);
			m_MarkerInstance.name = "GrenadeAimMarker";
			m_MarkerInstance.transform.localScale = Vector3.one * m_MarkerScale;

			Collider col = m_MarkerInstance.GetComponent<Collider>();
			if (col != null)
				col.enabled = false;

			m_MarkerRenderer = m_MarkerInstance.GetComponentInChildren<Renderer>();
			return;
		}

		// Prefab may be unset on the selection manager — still need a world aim point.
		m_MarkerInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		m_MarkerInstance.name = "GrenadeAimMarkerFallback";
		m_MarkerInstance.transform.localScale = Vector3.one * m_MarkerScale;

		Collider fallbackCol = m_MarkerInstance.GetComponent<Collider>();
		if (fallbackCol != null)
			Destroy(fallbackCol);

		m_MarkerRenderer = m_MarkerInstance.GetComponent<Renderer>();
	}

	private void EnsureTrajectoryLine()
	{
		if (m_TrajectoryLine != null)
			return;

		GameObject lineGo = new GameObject("GrenadeTrajectoryLine");
		lineGo.transform.SetParent(transform, false);
		m_TrajectoryLine = lineGo.AddComponent<LineRenderer>();
		m_TrajectoryLine.useWorldSpace = true;
		m_TrajectoryLine.startWidth = 0.04f;
		m_TrajectoryLine.endWidth = 0.04f;
		m_TrajectoryLine.positionCount = 0;

		Shader shader = Shader.Find("Sprites/Default");
		if (shader != null)
		{
			Material mat = new Material(shader);
			m_TrajectoryLine.material = mat;
		}

		m_TrajectoryLine.enabled = false;
	}

	private void OnDestroy()
	{
		if (m_MarkerInstance != null)
			Destroy(m_MarkerInstance);
	}
	#endregion
}
