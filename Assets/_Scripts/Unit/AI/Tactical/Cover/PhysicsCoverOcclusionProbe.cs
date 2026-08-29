using UnityEngine;

/// <summary>
/// Physics linecast for #13.2. Skips characters and vehicles.
/// </summary>
public sealed class PhysicsCoverOcclusionProbe : ICoverOcclusionProbe
{
	#region Private Fields
	private readonly LayerMask m_Mask;
	private readonly RaycastHit[] m_Hits = new RaycastHit[8];
	#endregion

	#region Public Constructors
	public PhysicsCoverOcclusionProbe(LayerMask _mask = default)
	{
		m_Mask = _mask.value == 0 ? (LayerMask)(~0) : _mask;
	}
	#endregion

	#region Public Methods
	public bool IsBlocked(Vector3 _from, Vector3 _to)
	{
		Vector3 delta = _to - _from;
		float length = delta.magnitude;
		if (length < 0.01f)
			return false;

		int hits = Physics.RaycastNonAlloc(
			_from,
			delta / length,
			m_Hits,
			length - 0.04f,
			m_Mask,
			QueryTriggerInteraction.Ignore);
		for (int i = 0; i < hits; i++)
		{
			Collider collider = m_Hits[i].collider;
			if (collider == null || collider.isTrigger)
				continue;
			if (PhysicsCoverGeometrySource.IsCharacterOrVehicle(collider))
				continue;
			return true;
		}

		return false;
	}
	#endregion
}
