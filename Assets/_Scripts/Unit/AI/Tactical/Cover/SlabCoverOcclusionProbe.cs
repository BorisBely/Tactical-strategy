using UnityEngine;

/// <summary>
/// Axis-aligned wall slab for EditMode classification tests. Not a unit, not a score.
/// </summary>
public sealed class SlabCoverOcclusionProbe : ICoverOcclusionProbe
{
	#region Private Fields
	private readonly Bounds m_Wall;
	#endregion

	#region Public Constructors
	public SlabCoverOcclusionProbe(Bounds _wall)
	{
		m_Wall = _wall;
	}
	#endregion

	#region Public Methods
	public bool IsBlocked(Vector3 _from, Vector3 _to)
	{
		return CoverOcclusionMath.SegmentHitsAabb(_from, _to, m_Wall);
	}
	#endregion
}
