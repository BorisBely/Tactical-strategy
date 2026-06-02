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
/// Чем выше линия, тем лучше: для точности используется 1 / dispersion multiplier,
/// для скорости прицеливания используется 1 / aim time multiplier.
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
	[Tooltip("Нижняя граница качества на графике. 1 = базовое значение без бонуса/штрафа.")]
	[SerializeField, Min(0.01f)] private float m_MinDisplayedQuality = 0.5f;
	[Tooltip("Верхняя граница качества на графике. 2 = в два раза лучше базового значения.")]
	[SerializeField, Min(0.02f)] private float m_MaxDisplayedQuality = 2f;

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
	[SerializeField] private Color m_AccuracyColor = new Color(0.25f, 0.85f, 1f, 1f);
	[SerializeField] private Color m_AimSpeedColor = new Color(1f, 0.72f, 0.18f, 1f);
	[SerializeField] private Color m_PreviewColor = new Color(0.65f, 1f, 0.35f, 1f);
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

		if (m_ShowAccuracy && showAccuracy)
		{
			DrawCurve(_vh, graphRect, EvaluateCurrentAccuracyQuality, m_AccuracyColor);
			if (HasPreviewCurve)
				DrawCurve(_vh, graphRect, EvaluatePreviewAccuracyQuality, m_PreviewColor);
		}

		if (m_ShowAimSpeed && showAimTime)
		{
			DrawCurve(_vh, graphRect, EvaluateCurrentAimSpeedQuality, m_AimSpeedColor);
			if (HasPreviewCurve)
				DrawCurve(_vh, graphRect, EvaluatePreviewAimSpeedQuality, m_PreviewColor);
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

	private void DrawCurve(VertexHelper _vh, Rect _rect, System.Func<float, float> _qualityEvaluator, Color _lineColor)
	{
		int sampleCount = Mathf.Max(2, m_SampleCount);
		Vector2 previousPoint = Vector2.zero;
		bool hasPreviousPoint = false;
		Color32 lineColor = _lineColor;

		for (int i = 0; i < sampleCount; i++)
		{
			float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
			float distance = Mathf.Lerp(m_MinDistanceMeters, m_MaxDistanceMeters, t);
			float quality = _qualityEvaluator(distance);
			float y01 = Mathf.InverseLerp(m_MinDisplayedQuality, m_MaxDisplayedQuality, quality);
			Vector2 point = new Vector2(
				Mathf.Lerp(_rect.xMin, _rect.xMax, t),
				Mathf.Lerp(_rect.yMin, _rect.yMax, Mathf.Clamp01(y01)));

			if (hasPreviousPoint)
				AddLine(_vh, previousPoint, point, m_LineWidth, lineColor);

			previousPoint = point;
			hasPreviousPoint = true;
		}
	}

	private float EvaluatePreviewAccuracyQuality(float _distanceMeters)
	{
		WeaponDefinition weaponDefinition = m_PreviewWeaponDefinition != null ? m_PreviewWeaponDefinition : m_WeaponDefinition;
		return EvaluateAccuracyQuality(weaponDefinition, _distanceMeters, m_PreviewAttachments);
	}

	private float EvaluateCurrentAimSpeedQuality(float _distanceMeters)
	{
		return EvaluateAimSpeedQuality(m_WeaponDefinition, _distanceMeters, m_Attachments);
	}

	private float EvaluatePreviewAimSpeedQuality(float _distanceMeters)
	{
		WeaponDefinition weaponDefinition = m_PreviewWeaponDefinition != null ? m_PreviewWeaponDefinition : m_WeaponDefinition;
		return EvaluateAimSpeedQuality(weaponDefinition, _distanceMeters, m_PreviewAttachments);
	}

	private float EvaluateCurrentAccuracyQuality(float _distanceMeters)
	{
		return EvaluateAccuracyQuality(m_WeaponDefinition, _distanceMeters, m_Attachments);
	}

	private float EvaluateAccuracyQuality(
		WeaponDefinition _weaponDefinition,
		float _distanceMeters,
		WeaponAttachmentDefinition[] _attachments)
	{
		float dispersionMultiplier = 1f;
		if (_weaponDefinition != null)
			dispersionMultiplier *= Mathf.Max(0.01f, _weaponDefinition.GetDistanceDispersionMultiplier(_distanceMeters));

		ApplyAttachmentMultipliers(_attachments, _distanceMeters, ref dispersionMultiplier, _aimTime: false);
		return 1f / Mathf.Max(0.01f, dispersionMultiplier);
	}

	private float EvaluateAimSpeedQuality(
		WeaponDefinition _weaponDefinition,
		float _distanceMeters,
		WeaponAttachmentDefinition[] _attachments)
	{
		float aimTimeMultiplier = 1f;
		if (_weaponDefinition != null)
			aimTimeMultiplier *= Mathf.Max(0.01f, _weaponDefinition.GetDistanceAimTimeMultiplier(_distanceMeters));

		ApplyAttachmentMultipliers(_attachments, _distanceMeters, ref aimTimeMultiplier, _aimTime: true);
		return 1f / Mathf.Max(0.01f, aimTimeMultiplier);
	}

	private static void ApplyAttachmentMultipliers(WeaponAttachmentDefinition[] _attachments, float _distanceMeters, ref float _multiplier, bool _aimTime)
	{
		if (_attachments == null)
			return;

		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = _attachments[i];
			if (attachment == null)
				continue;

			if (_aimTime)
			{
				_multiplier *= Mathf.Max(0.01f, attachment.AimTimeModifier);
				_multiplier *= Mathf.Max(0.01f, attachment.GetDistanceAimTimeMultiplier(_distanceMeters));
			}
			else
			{
				_multiplier *= Mathf.Max(0.01f, attachment.GetDistanceDispersionMultiplier(_distanceMeters));
			}
		}
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
