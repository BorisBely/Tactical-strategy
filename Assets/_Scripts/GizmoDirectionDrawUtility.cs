using UnityEngine;

/// <summary>Общая отрисовка направляющих стрелок в Scene Gizmos.</summary>
public static class GizmoDirectionDrawUtility
{
	public static void DrawArrow(Vector3 _origin, Vector3 _direction, float _length, Color _color, float _headSize = 0.12f)
	{
		if (_length <= 0.0001f || _direction.sqrMagnitude < 1e-8f)
			return;

		Vector3 dir = _direction.normalized;
		Vector3 end = _origin + dir * _length;

		Gizmos.color = _color;
		Gizmos.DrawLine(_origin, end);

		Vector3 side = Vector3.Cross(dir, Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up);
		if (side.sqrMagnitude < 1e-8f)
			return;

		side.Normalize();
		Vector3 up = Vector3.Cross(side, dir).normalized;
		float wing = _headSize * 0.4f;
		Vector3 back = dir * _headSize;

		Gizmos.DrawLine(end, end - back + side * wing);
		Gizmos.DrawLine(end, end - back - side * wing);
		Gizmos.DrawLine(end, end - back + up * wing);
		Gizmos.DrawLine(end, end - back - up * wing);
	}
}
