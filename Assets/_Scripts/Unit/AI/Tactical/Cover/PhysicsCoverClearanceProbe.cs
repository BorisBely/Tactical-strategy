using UnityEngine;

/// <summary>
/// Capsule occupancy: can a body stand here. Ignores the wall behind the candidate normal.
/// Not crouch vs standing. Tests may inject ICoverClearanceProbe.
/// </summary>
public sealed class PhysicsCoverClearanceProbe : ICoverClearanceProbe
{
	#region Constants
	private const int c_HitCapacity = 24;
	private const float c_BehindDot = -0.2f;
	#endregion

	#region Private Fields
	private readonly Collider[] m_Hits = new Collider[c_HitCapacity];
	private readonly float m_Radius;
	private readonly float m_Height;
	private readonly LayerMask m_Mask;
	#endregion

	#region Public Constructors
	public PhysicsCoverClearanceProbe(
		float _radiusMeters = 0.28f,
		float _heightMeters = 1.8f,
		LayerMask _mask = default)
	{
		m_Radius = Mathf.Max(0.05f, _radiusMeters);
		m_Height = Mathf.Max(m_Radius * 2f + 0.05f, _heightMeters);
		m_Mask = _mask.value == 0 ? (LayerMask)(~0) : _mask;
	}
	#endregion

	#region Public Methods
	public bool HasBodyClearance(Vector3 _position, Vector3 _normal)
	{
		Vector3 p0 = _position + Vector3.up * m_Radius;
		Vector3 p1 = _position + Vector3.up * (m_Height - m_Radius);
		int hitCount = Physics.OverlapCapsuleNonAlloc(
			p0,
			p1,
			m_Radius,
			m_Hits,
			m_Mask,
			QueryTriggerInteraction.Ignore);

		Vector3 nrm = _normal;
		nrm.y = 0f;
		if (nrm.sqrMagnitude > 0.0001f)
			nrm.Normalize();

		for (int i = 0; i < hitCount; i++)
		{
			Collider hit = m_Hits[i];
			if (hit == null || hit.isTrigger)
				continue;
			if (PhysicsCoverGeometrySource.IsCharacterOrVehicle(hit))
				continue;
			if (TacticalTransparency.IsMarked(hit))
				continue;
			if (IsCoverWallBehind(_position, nrm, hit))
				continue;
			return false;
		}

		return true;
	}
	#endregion

	#region Private Methods
	private static bool IsCoverWallBehind(Vector3 _position, Vector3 _planarNormal, Collider _hit)
	{
		if (_planarNormal.sqrMagnitude < 0.0001f)
			return false;

		Vector3 probe = _position + Vector3.up * 0.9f;
		Vector3 closest;
		MeshCollider mesh = _hit as MeshCollider;
		if (mesh != null && !mesh.convex)
			closest = mesh.bounds.ClosestPoint(probe);
		else
			closest = _hit.ClosestPoint(probe);
		Vector3 planar = closest - _position;
		planar.y = 0f;
		if (planar.sqrMagnitude < 0.0001f)
		{
			Vector3 toCenter = _hit.bounds.center - _position;
			toCenter.y = 0f;
			if (toCenter.sqrMagnitude < 0.0001f)
				return false;
			return Vector3.Dot(toCenter.normalized, _planarNormal) < c_BehindDot;
		}

		return Vector3.Dot(planar.normalized, _planarNormal) < c_BehindDot;
	}
	#endregion
}
