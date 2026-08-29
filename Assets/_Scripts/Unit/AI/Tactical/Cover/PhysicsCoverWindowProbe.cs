using UnityEngine;

/// <summary>
/// Local OverlapBox at an Opening. Does not scan the whole arena. #13.2B.3
/// </summary>
public sealed class PhysicsCoverWindowProbe : ICoverWindowProbe
{
	#region Constants
	private const int c_HitCapacity = 24;
	#endregion

	#region Private Fields
	private readonly Collider[] m_Hits = new Collider[c_HitCapacity];
	#endregion

	#region Public Methods
	public bool TryInspect(CoverCandidate _opening, out CoverWindowHit _hit)
	{
		_hit = default;
		if (_opening == null || !_opening.OpeningValid)
			return false;

		Vector3 normal = _opening.Normal;
		normal.y = 0f;
		if (normal.sqrMagnitude < 0.01f)
			return false;
		normal.Normalize();
		Vector3 axis = _opening.OpeningAxis;
		axis.y = 0f;
		if (axis.sqrMagnitude < 0.01f)
			axis = Vector3.Cross(Vector3.up, normal);
		if (axis.sqrMagnitude < 0.01f)
			return false;
		axis.Normalize();

		float width = Mathf.Max(0.2f, _opening.OpeningWidth);
		Vector3 center = _opening.OpeningCenter + Vector3.up * 1.1f;
		Vector3 half = new Vector3(width * 0.55f, 0.7f, 0.35f);
		Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
		Physics.SyncTransforms();
		int count = Physics.OverlapBoxNonAlloc(
			center,
			half,
			m_Hits,
			rotation,
			~0,
			QueryTriggerInteraction.Collide);

		bool pane = false;
		bool frame = false;
		Vector3 paneCenter = Vector3.zero;
		for (int i = 0; i < count; i++)
		{
			Collider collider = m_Hits[i];
			if (collider == null || PhysicsCoverGeometrySource.IsCharacterOrVehicle(collider))
				continue;
			if (TacticalTransparency.IsMarked(collider))
			{
				pane = true;
				paneCenter = collider.bounds.center;
				continue;
			}

			if (collider.enabled && !collider.isTrigger)
				frame = true;
		}

		if (!pane)
			return false;

		_hit = new CoverWindowHit
		{
			HasTransparentPane = true,
			HasFrame = frame,
			Center = Flatten(paneCenter),
			Axis = axis,
			Width = width
		};
		return true;
	}
	#endregion

	#region Private Methods
	private static Vector3 Flatten(Vector3 _value)
	{
		_value.y = 0f;
		return _value;
	}
	#endregion
}
