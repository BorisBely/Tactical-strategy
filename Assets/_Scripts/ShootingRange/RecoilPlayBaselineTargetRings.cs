using UnityEngine;

/// <summary>
/// Face rings 10 / 25 / 50 / 100 cm on a <see cref="ShootingRangeTarget"/>. Aim origin = face center.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ShootingRangeTarget))]
public sealed class RecoilPlayBaselineTargetRings : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private Color m_Ring10 = new Color(0.2f, 0.9f, 0.3f, 0.9f);
	[SerializeField] private Color m_Ring25 = new Color(0.9f, 0.85f, 0.2f, 0.9f);
	[SerializeField] private Color m_Ring50 = new Color(0.95f, 0.55f, 0.15f, 0.9f);
	[SerializeField] private Color m_Ring100 = new Color(0.95f, 0.2f, 0.2f, 0.9f);
	[SerializeField, Min(16)] private int m_Segments = 48;
	#endregion

	#region Unity Lifecycle
	private void OnDrawGizmos()
	{
		Collider col = GetComponent<Collider>();
		Vector3 center = col != null ? col.bounds.center : transform.position;
		Vector3 normal = -transform.forward;
		DrawRing(center, normal, RecoilPlayBaselineProtocol.Ring10Cm, m_Ring10);
		DrawRing(center, normal, RecoilPlayBaselineProtocol.Ring25Cm, m_Ring25);
		DrawRing(center, normal, RecoilPlayBaselineProtocol.Ring50Cm, m_Ring50);
		DrawRing(center, normal, RecoilPlayBaselineProtocol.Ring100Cm, m_Ring100);
	}
	#endregion

	#region Private Methods
	private void DrawRing(Vector3 _center, Vector3 _normal, float _radiusMeters, Color _color)
	{
		Vector3 n = _normal.sqrMagnitude > 1e-8f ? _normal.normalized : Vector3.forward;
		Vector3 tangent = Vector3.Cross(n, Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up)
			.normalized;
		Vector3 bitangent = Vector3.Cross(n, tangent);
		Gizmos.color = _color;
		Vector3 prev = _center + tangent * _radiusMeters;
		for (int i = 1; i <= m_Segments; i++)
		{
			float a = i * Mathf.PI * 2f / m_Segments;
			Vector3 next = _center + (tangent * Mathf.Cos(a) + bitangent * Mathf.Sin(a)) * _radiusMeters;
			Gizmos.DrawLine(prev, next);
			prev = next;
		}
	}
	#endregion
}
