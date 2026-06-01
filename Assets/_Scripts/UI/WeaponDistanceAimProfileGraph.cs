using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
	[SerializeField] private RectOffset m_Padding = new RectOffset(8, 8, 8, 8);
	[SerializeField, Min(0.5f)] private float m_LineWidth = 2.5f;
	[SerializeField] private bool m_ShowGrid = true;
	[SerializeField, Range(0, 12)] private int m_VerticalGridLines = 5;
	[SerializeField, Range(0, 12)] private int m_HorizontalGridLines = 4;
	[SerializeField] private Color m_GridColor = new Color(1f, 1f, 1f, 0.16f);
	[SerializeField] private Color m_AccuracyColor = new Color(0.25f, 0.85f, 1f, 1f);
	[SerializeField] private Color m_AimSpeedColor = new Color(1f, 0.72f, 0.18f, 1f);
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
		if (_attachments == null || _attachments.Count == 0)
		{
			m_Attachments = null;
			SetVerticesDirty();
			return;
		}

		m_Attachments = new WeaponAttachmentDefinition[_attachments.Count];
		for (int i = 0; i < _attachments.Count; i++)
			m_Attachments[i] = _attachments[i];

		SetVerticesDirty();
	}

	public void SetLoadout(WeaponDefinition _weaponDefinition, IReadOnlyList<WeaponAttachmentDefinition> _attachments)
	{
		m_WeaponDefinition = _weaponDefinition;
		SetAttachments(_attachments);
	}

	public void SetRuntimeState(WeaponRuntimeState _weaponState)
	{
		m_WeaponDefinition = _weaponState != null ? _weaponState.WeaponDefinition : null;
		SetAttachments(_weaponState != null ? _weaponState.EquippedAttachments : null);
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
	#endregion

	#region Unity Lifecycle
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

		if (m_ShowAccuracy)
			DrawCurve(_vh, graphRect, EvaluateAccuracyQuality, m_AccuracyColor);
		if (m_ShowAimSpeed)
			DrawCurve(_vh, graphRect, EvaluateAimSpeedQuality, m_AimSpeedColor);
	}
	#endregion

	#region Private Methods
	private Rect GetGraphRect()
	{
		if (m_Padding == null)
			m_Padding = new RectOffset(8, 8, 8, 8);

		Rect rect = rectTransform.rect;
		float xMin = rect.xMin + Mathf.Max(0, m_Padding.left);
		float xMax = rect.xMax - Mathf.Max(0, m_Padding.right);
		float yMin = rect.yMin + Mathf.Max(0, m_Padding.bottom);
		float yMax = rect.yMax - Mathf.Max(0, m_Padding.top);
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

	private float EvaluateAccuracyQuality(float _distanceMeters)
	{
		float dispersionMultiplier = 1f;
		if (m_WeaponDefinition != null)
			dispersionMultiplier *= Mathf.Max(0.01f, m_WeaponDefinition.GetDistanceDispersionMultiplier(_distanceMeters));
		if (m_Attachments != null)
		{
			for (int i = 0; i < m_Attachments.Length; i++)
			{
				WeaponAttachmentDefinition attachment = m_Attachments[i];
				if (attachment != null)
					dispersionMultiplier *= Mathf.Max(0.01f, attachment.GetDistanceDispersionMultiplier(_distanceMeters));
			}
		}

		return 1f / Mathf.Max(0.01f, dispersionMultiplier);
	}

	private float EvaluateAimSpeedQuality(float _distanceMeters)
	{
		float aimTimeMultiplier = 1f;
		if (m_WeaponDefinition != null)
			aimTimeMultiplier *= Mathf.Max(0.01f, m_WeaponDefinition.GetDistanceAimTimeMultiplier(_distanceMeters));
		if (m_Attachments != null)
		{
			for (int i = 0; i < m_Attachments.Length; i++)
			{
				WeaponAttachmentDefinition attachment = m_Attachments[i];
				if (attachment == null)
					continue;

				aimTimeMultiplier *= Mathf.Max(0.01f, attachment.AimTimeModifier);
				aimTimeMultiplier *= Mathf.Max(0.01f, attachment.GetDistanceAimTimeMultiplier(_distanceMeters));
			}
		}

		return 1f / Mathf.Max(0.01f, aimTimeMultiplier);
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
