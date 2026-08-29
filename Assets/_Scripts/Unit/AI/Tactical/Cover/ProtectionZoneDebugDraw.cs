using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Scene gizmos for protection zones: boundary + normal, not a point every 2 m. #13.2C.8
/// </summary>
public static class ProtectionZoneDebugDraw
{
	#region Public Methods
	public static void Draw(ProtectionZone _zone, bool _selected)
	{
		if (_zone == null)
			return;
		Color color = ProtectionZoneVisual.Color(_zone.GeometryType);
		if (!_selected)
			color.a = 0.75f;
		Gizmos.color = color;

		Vector3 origin = _zone.Center + Vector3.up * 0.08f;
		Vector3 axis = PlanarUnit(_zone.Axis);
		if (axis.sqrMagnitude < 0.5f)
			axis = Vector3.right;
		Vector3 n = PlanarUnit(_zone.SurfaceNormal);

		switch (_zone.GeometryType)
		{
			case ProtectionZoneType.Wall:
				DrawSurfaceVolume(origin, axis, n, _zone.Width, _zone.Depth);
				break;
			case ProtectionZoneType.Edge:
				DrawBoundary(origin, axis, n, _zone);
				break;
			case ProtectionZoneType.Opening:
				DrawOpeningGap(origin, axis, n, _zone, false);
				break;
			case ProtectionZoneType.Window:
				DrawOpeningGap(origin, axis, n, _zone, true);
				break;
			case ProtectionZoneType.Corner:
				DrawCorner(origin, _zone);
				break;
			case ProtectionZoneType.Obstacle:
				DrawObstacle(origin, _zone);
				break;
		}

#if UNITY_EDITOR
		if (_selected)
		{
			Handles.color = color;
			Handles.Label(
				origin + Vector3.up * 0.35f,
				ProtectionZoneVisual.FormatLabel(_zone.ZoneId, _zone.GeometryType, _zone.EdgeKind));
		}
#endif
	}
	#endregion

	#region Private Methods
	private static void DrawSurfaceVolume(
		Vector3 _origin,
		Vector3 _axis,
		Vector3 _normal,
		float _width,
		float _depth)
	{
		Vector3 normal = _normal;
		if (normal.sqrMagnitude < 0.5f)
			normal = Vector3.Cross(_axis, Vector3.up).normalized;
		if (normal.sqrMagnitude < 0.5f)
			normal = Vector3.forward;
		float thickness = Mathf.Clamp(_depth * 0.25f, 0.08f, 0.18f);
		Vector3 center = _origin + normal * (thickness * 0.5f);
		DrawWireBand(
			center,
			normal,
			new Vector3(Mathf.Max(0.1f, _width), 0.08f, thickness));
		Gizmos.DrawLine(center, center + normal * 0.3f);
	}

	private static void DrawBoundary(
		Vector3 _origin,
		Vector3 _axis,
		Vector3 _normal,
		ProtectionZone _zone)
	{
		if (_zone.EdgeKind == ProtectionEdgeKind.OpeningJamb)
		{
			DrawOpeningJamb(_origin, _axis, _normal, _zone.EdgeDirection);
			return;
		}

		Vector3 outward = PlanarUnit(_zone.EdgeDirection);
		if (outward.sqrMagnitude < 0.5f)
			outward = _normal.sqrMagnitude > 0.5f ? _normal : Vector3.forward;
		Vector3 across = PlanarUnit(_zone.Axis);
		if (across.sqrMagnitude < 0.5f || Mathf.Abs(Vector3.Dot(across, outward)) > 0.75f)
			across = Vector3.Cross(Vector3.up, outward).normalized;

		float width = Mathf.Max(0.1f, _zone.Width);
		float depth = Mathf.Max(0.15f, _zone.Depth);
		Vector3 center = _origin + outward * (depth * 0.5f);
		Color previous = Gizmos.color;
		if (_zone.EdgeKind == ProtectionEdgeKind.WallEnd)
			Gizmos.color = new Color(0.15f, 0.75f, 1f, previous.a);
		DrawWireBand(center, outward, new Vector3(width, 0.1f, depth));
		DrawArrow(_origin, outward, Mathf.Min(0.5f, depth));
		Gizmos.color = previous;
	}

	private static void DrawOpeningJamb(
		Vector3 _origin,
		Vector3 _axis,
		Vector3 _normal,
		Vector3 _edgeDirection)
	{
		Vector3 wallNormal = _normal.sqrMagnitude > 0.5f ? _normal : Vector3.forward;
		Vector3 intoOpening = PlanarUnit(_edgeDirection);
		if (intoOpening.sqrMagnitude < 0.5f)
			intoOpening = _axis;
		float markerHalf = 0.22f;
		Gizmos.DrawLine(_origin - wallNormal * markerHalf, _origin + wallNormal * markerHalf);
		Gizmos.DrawLine(_origin, _origin + intoOpening * 0.2f);
	}

	private static void DrawOpeningGap(
		Vector3 _origin,
		Vector3 _fallbackAxis,
		Vector3 _normal,
		ProtectionZone _zone,
		bool _window)
	{
		Vector3 axis = PlanarUnit(_zone.OpeningAxis);
		if (axis.sqrMagnitude < 0.5f)
			axis = _fallbackAxis;
		Vector3 normal = _normal.sqrMagnitude > 0.5f ? _normal : Vector3.Cross(_fallbackAxis, Vector3.up);
		if (normal.sqrMagnitude < 0.5f)
			normal = Vector3.forward;
		Vector3 center = _zone.OpeningCenter.sqrMagnitude > 0.01f
			? _zone.OpeningCenter + Vector3.up * 0.08f
			: _origin;
		float width = Mathf.Max(0.1f, _zone.OpeningWidth > 0.05f ? _zone.OpeningWidth : _zone.Width);
		float half = width * 0.5f;
		Vector3 left = center - axis * half;
		Vector3 right = center + axis * half;
		float jambHalf = Mathf.Max(0.2f, _zone.Depth * 0.35f);
		Gizmos.DrawLine(left - normal * jambHalf, left + normal * jambHalf);
		Gizmos.DrawLine(right - normal * jambHalf, right + normal * jambHalf);
		Gizmos.DrawLine(left, left + axis * Mathf.Min(0.22f, width * 0.2f));
		Gizmos.DrawLine(right, right - axis * Mathf.Min(0.22f, width * 0.2f));

		if (!_window)
			return;
		Color previous = Gizmos.color;
		Gizmos.color = new Color(0.3f, 0.95f, 1f, previous.a);
		DrawWireBand(center, normal, new Vector3(width, 0.08f, 0.06f));
		Gizmos.color = previous;
	}

	private static void DrawObstacle(Vector3 _origin, ProtectionZone _zone)
	{
		Vector3 size = _zone.ObstacleExtents;
		if (size.sqrMagnitude < 0.01f)
			size = new Vector3(_zone.Depth * 2f, 0.08f, _zone.Width);
		else
			size.y = 0.08f;
		Vector3 forward = PlanarUnit(_zone.Axis);
		if (forward.sqrMagnitude < 0.5f)
			forward = Vector3.forward;
		DrawWireBand(_origin, forward, size);
		DrawObstacleBoundaries(_origin, forward, size);
	}

	private static void DrawCorner(Vector3 _origin, ProtectionZone _zone)
	{
		Vector3 directionA = PlanarUnit(_zone.CornerDirectionA);
		Vector3 directionB = PlanarUnit(_zone.CornerDirectionB);
		if (directionA.sqrMagnitude < 0.5f)
			directionA = PerpendicularToward(_zone.CornerNormalA, _zone.CornerFacing);
		if (directionB.sqrMagnitude < 0.5f)
			directionB = PerpendicularToward(_zone.CornerNormalB, _zone.CornerFacing);

		float outerRadius = Mathf.Max(0.35f, _zone.CornerMaxRadius);
		float innerRadius = Mathf.Clamp(_zone.CornerMinRadius, 0f, outerRadius);
		float armLength = Mathf.Max(0.65f, outerRadius);
		Color previous = Gizmos.color;
		Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.95f);
		if (directionA.sqrMagnitude > 0.5f)
			Gizmos.DrawLine(_origin, _origin + directionA * armLength);
		Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.95f);
		if (directionB.sqrMagnitude > 0.5f)
			Gizmos.DrawLine(_origin, _origin + directionB * armLength);

		Vector3 facing = PlanarUnit(_zone.CornerFacing);
		if (facing.sqrMagnitude < 0.5f)
		{
			Gizmos.color = previous;
			return;
		}
		Gizmos.color = new Color(1f, 0.95f, 0.35f, 1f);
		float halfAngle = Mathf.Clamp(_zone.CornerHalfAngleDegrees, 10f, 80f);
		DrawArc(_origin, facing, innerRadius, halfAngle);
		DrawArc(_origin, facing, outerRadius, halfAngle);
		Vector3 left = Quaternion.AngleAxis(-halfAngle, Vector3.up) * facing;
		Vector3 right = Quaternion.AngleAxis(halfAngle, Vector3.up) * facing;
		Gizmos.DrawLine(_origin + left * innerRadius, _origin + left * outerRadius);
		Gizmos.DrawLine(_origin + right * innerRadius, _origin + right * outerRadius);
		DrawArrow(_origin + facing * innerRadius, facing, outerRadius - innerRadius);
		Gizmos.color = previous;
	}

	private static Vector3 PerpendicularToward(Vector3 _normal, Vector3 _facing)
	{
		Vector3 normal = PlanarUnit(_normal);
		if (normal.sqrMagnitude < 0.5f)
			return Vector3.zero;
		Vector3 tangent = Vector3.Cross(Vector3.up, normal).normalized;
		Vector3 facing = PlanarUnit(_facing);
		return Vector3.Dot(tangent, facing) >= 0f ? tangent : -tangent;
	}

	private static void DrawWireBand(Vector3 _center, Vector3 _forward, Vector3 _size)
	{
		Vector3 forward = PlanarUnit(_forward);
		if (forward.sqrMagnitude < 0.5f)
			forward = Vector3.forward;
		Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
		Matrix4x4 previous = Gizmos.matrix;
		Gizmos.matrix = Matrix4x4.TRS(_center, rotation, Vector3.one);
		Gizmos.DrawWireCube(Vector3.zero, _size);
		Gizmos.matrix = previous;
	}

	private static void DrawArrow(Vector3 _origin, Vector3 _direction, float _length)
	{
		Vector3 direction = PlanarUnit(_direction);
		float length = Mathf.Max(0.08f, _length);
		if (direction.sqrMagnitude < 0.5f)
			return;
		Vector3 tip = _origin + direction * length;
		Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
		float head = Mathf.Min(0.16f, length * 0.35f);
		Gizmos.DrawLine(_origin, tip);
		Gizmos.DrawLine(tip, tip - direction * head + right * (head * 0.45f));
		Gizmos.DrawLine(tip, tip - direction * head - right * (head * 0.45f));
	}

	private static void DrawArc(
		Vector3 _origin,
		Vector3 _facing,
		float _radius,
		float _halfAngleDegrees)
	{
		if (_radius <= 0.01f)
			return;
		const int c_Segments = 12;
		Vector3 previous = _origin +
		                   Quaternion.AngleAxis(-_halfAngleDegrees, Vector3.up) * _facing * _radius;
		for (int i = 1; i <= c_Segments; i++)
		{
			float angle = Mathf.Lerp(-_halfAngleDegrees, _halfAngleDegrees, i / (float)c_Segments);
			Vector3 next = _origin + Quaternion.AngleAxis(angle, Vector3.up) * _facing * _radius;
			Gizmos.DrawLine(previous, next);
			previous = next;
		}
	}

	private static void DrawObstacleBoundaries(Vector3 _origin, Vector3 _forward, Vector3 _size)
	{
		Vector3 side = Vector3.Cross(Vector3.up, _forward);
		if (side.sqrMagnitude < 0.01f)
			return;
		side.Normalize();
		Color previous = Gizmos.color;
		Gizmos.color = new Color(0.15f, 0.95f, 0.65f, 0.85f);
		float halfL = Mathf.Max(0.1f, _size.z * 0.5f);
		float halfT = Mathf.Max(0.1f, _size.x * 0.5f);
		Vector3 a = _origin + _forward * halfL;
		Vector3 b = _origin - _forward * halfL;
		Vector3 c = _origin + side * halfT;
		Vector3 d = _origin - side * halfT;
		Gizmos.DrawLine(a, a + _forward * 0.28f);
		Gizmos.DrawLine(b, b - _forward * 0.28f);
		Gizmos.DrawLine(c, c + side * 0.28f);
		Gizmos.DrawLine(d, d - side * 0.28f);
		Gizmos.color = previous;
	}

	private static Vector3 PlanarUnit(Vector3 _value)
	{
		Vector3 v = _value;
		v.y = 0f;
		return v.sqrMagnitude < 0.01f ? Vector3.zero : v.normalized;
	}
	#endregion
}
