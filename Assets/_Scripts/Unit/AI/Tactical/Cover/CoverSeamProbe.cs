using UnityEngine;

/// <summary>
/// #13.2B.5A: is a collider-AABB gap still solid wall (mesh/overlap), not a walkable door.
/// </summary>
public interface ICoverSeamProbe
{
	bool HasSolidInGap(Vector3 _alongStart, Vector3 _alongEnd, Vector3 _normal);
}

/// <summary>
/// Physics seam check for editor bake. Not classification. Not unit action.
/// </summary>
public sealed class PhysicsCoverSeamProbe : ICoverSeamProbe
{
	#region Constants
	private const float c_StandHeight = 0.9f;
	private const float c_ThroughMeters = 0.9f;
	#endregion

	#region Private Fields
	private readonly LayerMask m_Mask;
	private readonly RaycastHit[] m_Hits = new RaycastHit[8];
	#endregion

	#region Public Constructors
	public PhysicsCoverSeamProbe(LayerMask _mask)
	{
		m_Mask = _mask;
	}
	#endregion

	#region Public Methods
	public bool HasSolidInGap(Vector3 _alongStart, Vector3 _alongEnd, Vector3 _normal)
	{
		Vector3 mid = (_alongStart + _alongEnd) * 0.5f;
		mid.y = 0f;
		Vector3 n = CoverOcclusionMath.PlanarNormal(_normal);
		Vector3 from = mid + n * c_ThroughMeters + Vector3.up * c_StandHeight;
		Vector3 to = mid - n * c_ThroughMeters + Vector3.up * c_StandHeight;
		Vector3 delta = to - from;
		float dist = delta.magnitude;
		if (dist < 0.05f)
			return false;

		int hitCount = Physics.RaycastNonAlloc(
			from,
			delta / dist,
			m_Hits,
			dist,
			m_Mask,
			QueryTriggerInteraction.Ignore);
		for (int i = 0; i < hitCount; i++)
		{
			Collider collider = m_Hits[i].collider;
			if (collider == null || collider.isTrigger)
				continue;
			if (PhysicsCoverGeometrySource.IsCharacterOrVehicle(collider))
				continue;
			if (TacticalTransparency.IsMarked(collider))
				continue;
			return true;
		}

		return false;
	}
	#endregion
}
