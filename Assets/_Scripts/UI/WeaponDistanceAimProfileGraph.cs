using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum WeaponDistanceAimGraphMetric
{
	Both = 0,
	Accuracy = 1,
	AimTime = 2
}

/// <summary>
/// UI-график дистанционного поведения оружия: точность и скорость прицеливания на 0..100 м.
/// Формулы — Assets/Docs/CombatBalance/OpticDistanceBalance.md, расчёт — <see cref="WeaponDistanceAimEvaluator"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponDistanceAimProfileGraph : Graphic
{
	#region Constants
	private const float c_DefaultMinDistanceMeters = 0f;
	private const float c_DefaultMaxDistanceMeters = 100f;
	private const float c_MinLineLengthSqr = 0.0001f;
	#endregion

	#region Serialized Fields
	[Header("Data Preview")]
	[SerializeField] private WeaponDefinition m_WeaponDefinition;
	[SerializeField] private WeaponAttachmentDefinition[] m_Attachments;
	[SerializeField] private WeaponDefinition m_PreviewWeaponDefinition;
	[SerializeField] private WeaponAttachmentDefinition[] m_PreviewAttachments;
	[SerializeField] private MissionPrepLoadoutCoordinator m_Coordinator;
	[SerializeField] private bool m_AutoBindMissionPrepSelection = true;
	[SerializeField] private WeaponDistanceAimGraphMetric m_Metric = WeaponDistanceAimGraphMetric.Both;

	[Header("Distance")]
	[SerializeField, Min(0f)] private float m_MinDistanceMeters = c_DefaultMinDistanceMeters;
	[SerializeField, Min(1f)] private float m_MaxDistanceMeters = c_DefaultMaxDistanceMeters;
	[SerializeField, Range(8, 128)] private int m_SampleCount = 64;

	[Header("Value Scale")]
	[Tooltip("Подгоняет вертикальную шкалу под min/max отображаемых линий. В Mission Prep включается автоматически.")]
	[SerializeField] private bool m_AutoFitQualityScale = true;
	[Tooltip("Доля отступа сверху/снизу относительно диапазона данных.")]
	[SerializeField, Range(0f, 0.35f)] private float m_AutoFitQualityPaddingRatio = 0.12f;
	[Tooltip("Минимальная высота шкалы качества, чтобы мелкие отличия были видны.")]
	[SerializeField, Min(0.01f)] private float m_AutoFitQualityMinSpan = 0.08f;
	[Tooltip("Нижняя граница качества на графике, если auto-fit выключен или нет данных.")]
	[SerializeField, Min(0.01f)] private float m_MinDisplayedQuality = 0.15f;
	[Tooltip("Верхняя граница качества на графике, если auto-fit выключен или нет данных.")]
	[SerializeField, Min(0.02f)] private float m_MaxDisplayedQuality = 2.75f;

	[Header("Distance Auto Fit")]
	[Tooltip("Сужает ось дистанции к диапазону, где линии заметно меняются. В Mission Prep включается автоматически.")]
	[SerializeField] private bool m_AutoFitDistanceRange = true;
	[Tooltip("Минимальная ширина окна дистанции при auto-fit.")]
	[SerializeField, Min(5f)] private float m_AutoFitMinDistanceSpanMeters = 30f;
	[Tooltip("Доля отступа слева/справа относительно найденного диапазона дистанции.")]
	[SerializeField, Range(0f, 0.25f)] private float m_AutoFitDistancePaddingRatio = 0.08f;

	[Header("Line Colors")]
	[Tooltip("Текущий экипированный loadout (первая линия).")]
	[SerializeField] private Color m_CurrentLineColor = new Color(0.55f, 0.55f, 0.55f, 1f);
	[Tooltip("Временный preview при наведении на оружие или модуль (вторая линия).")]
	[SerializeField] private Color m_PreviewLineColor = new Color(1f, 1f, 1f, 1f);
	[SerializeField, Min(0.5f)] private float m_PreviewLineWidthMultiplier = 1.2f;

	[Header("Style")]
	[SerializeField, Min(0)] private int m_PaddingLeft = 8;
	[SerializeField, Min(0)] private int m_PaddingRight = 8;
	[SerializeField, Min(0)] private int m_PaddingTop = 8;
	[SerializeField, Min(0)] private int m_PaddingBottom = 8;
	[SerializeField, Min(0.5f)] private float m_LineWidth = 2.5f;
	[SerializeField] private bool m_ShowGrid = true;
	[SerializeField, Range(0, 12)] private int m_VerticalGridLines = 5;
	[SerializeField, Range(0, 12)] private int m_HorizontalGridLines = 4;
	[SerializeField] private Color m_GridColor = new Color(1f, 1f, 1f, 0.16f);
	[SerializeField] private bool m_ShowAccuracy = true;
	[SerializeField] private bool m_ShowAimSpeed = true;
	#endregion

	#region Public Methods
	public void SetWeapon(WeaponDefinition _weaponDefinition)
	{
		m_WeaponDefinition = _weaponDefinition;
		SetVerticesDirty();
	}

	public void SetAttachments(IReadOnlyList<WeaponAttachmentDefinition> _attachments)
	{
		m_Attachments = CopyAttachments(_attachments);
		SetVerticesDirty();
	}

	public void SetLoadout(WeaponDefinition _weaponDefinition, IReadOnlyList<WeaponAttachmentDefinition> _attachments)
	{
		m_WeaponDefinition = _weaponDefinition;
		m_Attachments = CopyAttachments(_attachments);
		m_PreviewAttachments = null;
		SetVerticesDirty();
	}

	public void SetRuntimeState(WeaponRuntimeState _weaponState)
	{
		m_WeaponDefinition = _weaponState != null ? _weaponState.WeaponDefinition : null;
		m_Attachments = CopyAttachments(_weaponState != null ? _weaponState.EquippedAttachments : null);
		m_PreviewWeaponDefinition = null;
		m_PreviewAttachments = null;
		SetVerticesDirty();
	}

	public void SetItem(ItemDefinition _itemDefinition)
	{
		if (_itemDefinition == null)
		{
			m_WeaponDefinition = null;
			m_Attachments = null;
			SetVerticesDirty();
			return;
		}

		if (_itemDefinition.WeaponDefinition != null)
		{
			m_WeaponDefinition = _itemDefinition.WeaponDefinition;
			m_Attachments = null;
		}
		else if (_itemDefinition.WeaponAttachmentDefinition != null)
		{
			m_WeaponDefinition = null;
			m_Attachments = new[] { _itemDefinition.WeaponAttachmentDefinition };
		}
		else
		{
			m_WeaponDefinition = null;
			m_Attachments = null;
		}

		SetVerticesDirty();
	}

	public void SetPreviewLoadout(
		WeaponDefinition _previewWeaponDefinition,
		IReadOnlyList<WeaponAttachmentDefinition> _previewAttachments)
	{
		m_PreviewWeaponDefinition = _previewWeaponDefinition;
		m_PreviewAttachments = CopyAttachments(_previewAttachments);
		SetVerticesDirty();
	}

	public void SetPreviewAttachments(IReadOnlyList<WeaponAttachmentDefinition> _attachments)
	{
		SetPreviewLoadout(null, _attachments);
	}

	public void ClearPreview()
	{
		m_PreviewWeaponDefinition = null;
		m_PreviewAttachments = null;
		SetVerticesDirty();
	}
	#endregion

	#region Unity Lifecycle
	protected override void Awake()
	{
		base.Awake();
		ResolveCoordinator();
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		SubscribeCoordinator();
		RefreshFromCoordinator();
	}

	protected override void OnDisable()
	{
		UnsubscribeCoordinator();
		base.OnDisable();
	}

	private void Update()
	{
		if (!m_AutoBindMissionPrepSelection || m_Coordinator != null)
			return;

		ResolveCoordinator();
		if (m_Coordinator == null)
			return;

		SubscribeCoordinator();
		RefreshFromCoordinator();
	}

#if UNITY_EDITOR
	protected override void OnValidate()
	{
		base.OnValidate();
		m_MaxDistanceMeters = Mathf.Max(m_MinDistanceMeters + 1f, m_MaxDistanceMeters);
		m_MaxDisplayedQuality = Mathf.Max(m_MinDisplayedQuality + 0.01f, m_MaxDisplayedQuality);
		m_AutoFitMinDistanceSpanMeters = Mathf.Clamp(m_AutoFitMinDistanceSpanMeters, 5f, m_MaxDistanceMeters - m_MinDistanceMeters);
		SetVerticesDirty();
	}
#endif
	#endregion

	#region Graphic
	protected override void OnPopulateMesh(VertexHelper _vh)
	{
		_vh.Clear();

		Rect graphRect = GetGraphRect();
		if (graphRect.width <= 1f || graphRect.height <= 1f)
			return;

		if (m_ShowGrid)
			DrawGrid(_vh, graphRect);

		bool showAccuracy = m_Metric == WeaponDistanceAimGraphMetric.Both || m_Metric == WeaponDistanceAimGraphMetric.Accuracy;
		bool showAimTime = m_Metric == WeaponDistanceAimGraphMetric.Both || m_Metric == WeaponDistanceAimGraphMetric.AimTime;
		bool hasCurrentLoadout = m_WeaponDefinition != null;
		bool hasPreviewLoadout = HasPreviewCurve;
		bool hasAccuracyPreview = hasPreviewLoadout && (!hasCurrentLoadout || CurvesDiffer(EvaluateCurrentAccuracyQuality, EvaluatePreviewAccuracyQuality));
		bool hasAimTimePreview = hasPreviewLoadout && (!hasCurrentLoadout || CurvesDiffer(EvaluateCurrentAimSpeedQuality, EvaluatePreviewAimSpeedQuality));
		ComputeDisplayBounds(
			showAccuracy,
			showAimTime,
			hasCurrentLoadout,
			hasAccuracyPreview,
			hasAimTimePreview,
			out float minDistanceMeters,
			out float maxDistanceMeters,
			out float minQuality,
			out float maxQuality);

		if (hasCurrentLoadout && m_ShowAccuracy && showAccuracy)
		{
			DrawCurve(_vh, graphRect, EvaluateCurrentAccuracyQuality, m_CurrentLineColor, m_LineWidth, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
			if (hasAccuracyPreview)
				DrawCurve(_vh, graphRect, EvaluatePreviewAccuracyQuality, m_PreviewLineColor, m_LineWidth * m_PreviewLineWidthMultiplier, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
		}
		else if (!hasCurrentLoadout && hasAccuracyPreview && m_ShowAccuracy && showAccuracy)
		{
			DrawCurve(_vh, graphRect, EvaluatePreviewAccuracyQuality, m_PreviewLineColor, m_LineWidth * m_PreviewLineWidthMultiplier, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
		}

		if (hasCurrentLoadout && m_ShowAimSpeed && showAimTime)
		{
			DrawCurve(_vh, graphRect, EvaluateCurrentAimSpeedQuality, m_CurrentLineColor, m_LineWidth, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
			if (hasAimTimePreview)
				DrawCurve(_vh, graphRect, EvaluatePreviewAimSpeedQuality, m_PreviewLineColor, m_LineWidth * m_PreviewLineWidthMultiplier, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
		}
		else if (!hasCurrentLoadout && hasAimTimePreview && m_ShowAimSpeed && showAimTime)
		{
			DrawCurve(_vh, graphRect, EvaluatePreviewAimSpeedQuality, m_PreviewLineColor, m_LineWidth * m_PreviewLineWidthMultiplier, minDistanceMeters, maxDistanceMeters, minQuality, maxQuality);
		}
	}
	#endregion

	#region Private Methods
	private void ResolveCoordinator()
	{
		if (!m_AutoBindMissionPrepSelection || m_Coordinator != null)
			return;

		m_Coordinator = GetComponentInParent<MissionPrepLoadoutCoordinator>();
		if (m_Coordinator == null)
			m_Coordinator = MissionPrepLoadoutCoordinator.Instance;
	}

	private void SubscribeCoordinator()
	{
		if (!m_AutoBindMissionPrepSelection)
			return;

		ResolveCoordinator();
		if (m_Coordinator == null)
			return;

		m_Coordinator.ModificationGraphDataChanged += HandleCoordinatorGraphDataChanged;
	}

	private void UnsubscribeCoordinator()
	{
		if (m_Coordinator == null)
			return;

		m_Coordinator.ModificationGraphDataChanged -= HandleCoordinatorGraphDataChanged;
	}

	private void HandleCoordinatorGraphDataChanged()
	{
		RefreshFromCoordinator();
	}

	private void RefreshFromCoordinator()
	{
		if (!m_AutoBindMissionPrepSelection)
			return;

		ResolveCoordinator();
		if (m_Coordinator == null)
			return;

		if (!m_Coordinator.TryGetModificationGraphLoadout(
			    out WeaponDefinition weaponDefinition,
			    out WeaponAttachmentDefinition[] currentAttachments,
			    out WeaponDefinition previewWeaponDefinition,
			    out WeaponAttachmentDefinition[] previewAttachments))
		{
			m_WeaponDefinition = null;
			m_Attachments = null;
			m_PreviewWeaponDefinition = null;
			m_PreviewAttachments = null;
			SetVerticesDirty();
			return;
		}

		m_WeaponDefinition = weaponDefinition;
		m_Attachments = currentAttachments;
		m_PreviewWeaponDefinition = previewWeaponDefinition;
		m_PreviewAttachments = previewAttachments;

		SetVerticesDirty();
	}

	private bool HasPreviewCurve =>
		m_PreviewAttachments != null || m_PreviewWeaponDefinition != null;

	private Rect GetGraphRect()
	{
		Rect rect = rectTransform.rect;
		float xMin = rect.xMin + m_PaddingLeft;
		float xMax = rect.xMax - m_PaddingRight;
		float yMin = rect.yMin + m_PaddingBottom;
		float yMax = rect.yMax - m_PaddingTop;
		return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
	}

	private void DrawGrid(VertexHelper _vh, Rect _rect)
	{
		Color32 gridColor = m_GridColor;
		float gridWidth = Mathf.Max(0.5f, m_LineWidth * 0.45f);

		for (int i = 0; i <= m_VerticalGridLines; i++)
		{
			float t = m_VerticalGridLines > 0 ? i / (float)m_VerticalGridLines : 0f;
			float x = Mathf.Lerp(_rect.xMin, _rect.xMax, t);
			AddLine(_vh, new Vector2(x, _rect.yMin), new Vector2(x, _rect.yMax), gridWidth, gridColor);
		}

		for (int i = 0; i <= m_HorizontalGridLines; i++)
		{
			float t = m_HorizontalGridLines > 0 ? i / (float)m_HorizontalGridLines : 0f;
			float y = Mathf.Lerp(_rect.yMin, _rect.yMax, t);
			AddLine(_vh, new Vector2(_rect.xMin, y), new Vector2(_rect.xMax, y), gridWidth, gridColor);
		}
	}

	private void DrawCurve(
		VertexHelper _vh,
		Rect _rect,
		System.Func<float, float> _qualityEvaluator,
		Color _lineColor,
		float _lineWidth,
		float _minDistanceMeters,
		float _maxDistanceMeters,
		float _minQuality,
		float _maxQuality)
	{
		int sampleCount = Mathf.Max(2, m_SampleCount);
		Vector2 previousPoint = Vector2.zero;
		bool hasPreviousPoint = false;
		Color32 lineColor = _lineColor;

		for (int i = 0; i < sampleCount; i++)
		{
			float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
			float distance = Mathf.Lerp(_minDistanceMeters, _maxDistanceMeters, t);
			float quality = _qualityEvaluator(distance);
			float y01 = Mathf.InverseLerp(_minQuality, _maxQuality, quality);
			Vector2 point = new Vector2(
				Mathf.Lerp(_rect.xMin, _rect.xMax, t),
				Mathf.Lerp(_rect.yMin, _rect.yMax, Mathf.Clamp01(y01)));

			if (hasPreviousPoint)
				AddLine(_vh, previousPoint, point, _lineWidth, lineColor);

			previousPoint = point;
			hasPreviousPoint = true;
		}
	}

	private bool ShouldAutoFitQualityScale =>
		m_AutoFitQualityScale || m_AutoBindMissionPrepSelection;

	private bool ShouldAutoFitDistanceRange =>
		m_AutoFitDistanceRange || m_AutoBindMissionPrepSelection;

	private void ComputeDisplayBounds(
		bool _showAccuracy,
		bool _showAimTime,
		bool _hasCurrentLoadout,
		bool _hasAccuracyPreview,
		bool _hasAimTimePreview,
		out float _minDistanceMeters,
		out float _maxDistanceMeters,
		out float _minQuality,
		out float _maxQuality)
	{
		CollectActiveEvaluators(
			_showAccuracy,
			_showAimTime,
			_hasCurrentLoadout,
			_hasAccuracyPreview,
			_hasAimTimePreview,
			out System.Func<float, float>[] evaluators);

		if (evaluators.Length == 0)
		{
			_minDistanceMeters = m_MinDistanceMeters;
			_maxDistanceMeters = m_MaxDistanceMeters;
			_minQuality = m_MinDisplayedQuality;
			_maxQuality = m_MaxDisplayedQuality;
			return;
		}

		float minQuality = float.PositiveInfinity;
		float maxQuality = float.NegativeInfinity;
		int sampleCount = Mathf.Max(2, m_SampleCount);
		for (int i = 0; i < sampleCount; i++)
		{
			float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
			float distance = Mathf.Lerp(m_MinDistanceMeters, m_MaxDistanceMeters, t);
			for (int j = 0; j < evaluators.Length; j++)
				IncludeQualitySample(evaluators[j](distance), ref minQuality, ref maxQuality);
		}

		if (ShouldAutoFitDistanceRange && minQuality <= maxQuality)
			ComputeAutoFitDistanceRange(evaluators, minQuality, maxQuality, out _minDistanceMeters, out _maxDistanceMeters);
		else
		{
			_minDistanceMeters = m_MinDistanceMeters;
			_maxDistanceMeters = m_MaxDistanceMeters;
		}

		if (ShouldAutoFitQualityScale && minQuality <= maxQuality)
			ComputeAutoFitQualityRange(minQuality, maxQuality, out _minQuality, out _maxQuality);
		else
		{
			_minQuality = m_MinDisplayedQuality;
			_maxQuality = m_MaxDisplayedQuality;
		}
	}

	private void CollectActiveEvaluators(
		bool _showAccuracy,
		bool _showAimTime,
		bool _hasCurrentLoadout,
		bool _hasAccuracyPreview,
		bool _hasAimTimePreview,
		out System.Func<float, float>[] _evaluators)
	{
		var evaluators = new List<System.Func<float, float>>(4);

		if (_showAccuracy)
		{
			if (_hasCurrentLoadout)
				evaluators.Add(EvaluateCurrentAccuracyQuality);
			if (_hasAccuracyPreview)
				evaluators.Add(EvaluatePreviewAccuracyQuality);
		}

		if (_showAimTime)
		{
			if (_hasCurrentLoadout)
				evaluators.Add(EvaluateCurrentAimSpeedQuality);
			if (_hasAimTimePreview)
				evaluators.Add(EvaluatePreviewAimSpeedQuality);
		}

		_evaluators = evaluators.ToArray();
	}

	private void ComputeAutoFitQualityRange(float _minQuality, float _maxQuality, out float _minDisplayedQuality, out float _maxDisplayedQuality)
	{
		float range = Mathf.Max(0f, _maxQuality - _minQuality);
		float padding = Mathf.Max(range * m_AutoFitQualityPaddingRatio, 0.01f);
		float minQuality = Mathf.Max(0.05f, _minQuality - padding);
		float maxQuality = _maxQuality + padding;

		if (maxQuality - minQuality < m_AutoFitQualityMinSpan)
		{
			float center = (_minQuality + _maxQuality) * 0.5f;
			minQuality = center - m_AutoFitQualityMinSpan * 0.5f;
			maxQuality = center + m_AutoFitQualityMinSpan * 0.5f;
		}

		minQuality = Mathf.Max(0.05f, minQuality);
		maxQuality = Mathf.Max(minQuality + 0.01f, maxQuality);
		_minDisplayedQuality = minQuality;
		_maxDisplayedQuality = maxQuality;
	}

	private void ComputeAutoFitDistanceRange(
		System.Func<float, float>[] _evaluators,
		float _minQuality,
		float _maxQuality,
		out float _minDistanceMeters,
		out float _maxDistanceMeters)
	{
		float qualitySpan = Mathf.Max(0.01f, _maxQuality - _minQuality);
		float threshold = qualitySpan * 0.06f;
		float leftDistance = m_MaxDistanceMeters;
		float rightDistance = m_MinDistanceMeters;
		bool found = false;
		int sampleCount = Mathf.Max(2, m_SampleCount);

		for (int i = 0; i < sampleCount; i++)
		{
			float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
			float distance = Mathf.Lerp(m_MinDistanceMeters, m_MaxDistanceMeters, t);
			for (int j = 0; j < _evaluators.Length; j++)
			{
				float quality = _evaluators[j](distance);
				if (quality > _minQuality + threshold && quality < _maxQuality - threshold)
					continue;

				leftDistance = Mathf.Min(leftDistance, distance);
				rightDistance = Mathf.Max(rightDistance, distance);
				found = true;
			}
		}

		if (!found || leftDistance >= rightDistance)
		{
			_minDistanceMeters = m_MinDistanceMeters;
			_maxDistanceMeters = m_MaxDistanceMeters;
			return;
		}

		float span = rightDistance - leftDistance;
		float padding = Mathf.Max(5f, span * m_AutoFitDistancePaddingRatio);
		float minDistance = Mathf.Max(m_MinDistanceMeters, leftDistance - padding);
		float maxDistance = Mathf.Min(m_MaxDistanceMeters, rightDistance + padding);

		if (maxDistance - minDistance < m_AutoFitMinDistanceSpanMeters)
		{
			float center = (leftDistance + rightDistance) * 0.5f;
			minDistance = center - m_AutoFitMinDistanceSpanMeters * 0.5f;
			maxDistance = center + m_AutoFitMinDistanceSpanMeters * 0.5f;
		}

		minDistance = Mathf.Clamp(minDistance, m_MinDistanceMeters, m_MaxDistanceMeters - 1f);
		maxDistance = Mathf.Clamp(maxDistance, minDistance + 1f, m_MaxDistanceMeters);
		_minDistanceMeters = minDistance;
		_maxDistanceMeters = maxDistance;
	}

	private static void IncludeQualitySample(float _quality, ref float _minQuality, ref float _maxQuality)
	{
		if (!float.IsFinite(_quality))
			return;

		_minQuality = Mathf.Min(_minQuality, _quality);
		_maxQuality = Mathf.Max(_maxQuality, _quality);
	}

	private bool CurvesDiffer(System.Func<float, float> _currentEvaluator, System.Func<float, float> _previewEvaluator)
	{
		const float epsilon = 0.001f;
		int sampleCount = Mathf.Max(2, m_SampleCount);
		for (int i = 0; i < sampleCount; i++)
		{
			float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
			float distance = Mathf.Lerp(m_MinDistanceMeters, m_MaxDistanceMeters, t);
			if (Mathf.Abs(_currentEvaluator(distance) - _previewEvaluator(distance)) > epsilon)
				return true;
		}

		return false;
	}

	private float EvaluatePreviewAccuracyQuality(float _distanceMeters)
	{
		WeaponDefinition weaponDefinition = m_PreviewWeaponDefinition != null ? m_PreviewWeaponDefinition : m_WeaponDefinition;
		return WeaponDistanceAimEvaluator.EvaluateAccuracyQuality(weaponDefinition, m_PreviewAttachments, _distanceMeters);
	}

	private float EvaluateCurrentAimSpeedQuality(float _distanceMeters)
	{
		return WeaponDistanceAimEvaluator.EvaluateAimSpeedQuality(m_WeaponDefinition, m_Attachments, _distanceMeters);
	}

	private float EvaluatePreviewAimSpeedQuality(float _distanceMeters)
	{
		WeaponDefinition weaponDefinition = m_PreviewWeaponDefinition != null ? m_PreviewWeaponDefinition : m_WeaponDefinition;
		return WeaponDistanceAimEvaluator.EvaluateAimSpeedQuality(weaponDefinition, m_PreviewAttachments, _distanceMeters);
	}

	private float EvaluateCurrentAccuracyQuality(float _distanceMeters)
	{
		return WeaponDistanceAimEvaluator.EvaluateAccuracyQuality(m_WeaponDefinition, m_Attachments, _distanceMeters);
	}

	private static WeaponAttachmentDefinition[] CopyAttachments(IReadOnlyList<WeaponAttachmentDefinition> _attachments)
	{
		if (_attachments == null || _attachments.Count == 0)
			return null;

		WeaponAttachmentDefinition[] copy = new WeaponAttachmentDefinition[_attachments.Count];
		for (int i = 0; i < _attachments.Count; i++)
			copy[i] = _attachments[i];

		return copy;
	}

	private static void AddLine(VertexHelper _vh, Vector2 _start, Vector2 _end, float _width, Color32 _color)
	{
		Vector2 direction = _end - _start;
		if (direction.sqrMagnitude <= c_MinLineLengthSqr)
			return;

		Vector2 normal = new Vector2(-direction.y, direction.x).normalized * (_width * 0.5f);
		int index = _vh.currentVertCount;

		AddVertex(_vh, _start - normal, _color);
		AddVertex(_vh, _start + normal, _color);
		AddVertex(_vh, _end + normal, _color);
		AddVertex(_vh, _end - normal, _color);

		_vh.AddTriangle(index, index + 1, index + 2);
		_vh.AddTriangle(index + 2, index + 3, index);
	}

	private static void AddVertex(VertexHelper _vh, Vector2 _position, Color32 _color)
	{
		UIVertex vertex = UIVertex.simpleVert;
		vertex.position = _position;
		vertex.color = _color;
		_vh.AddVert(vertex);
	}
	#endregion
}
