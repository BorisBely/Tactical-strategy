using UnityEngine;

/// <summary>
/// Snapshotted uncertainty region for one Search. One source. Not a live LastKnown.
/// </summary>
public readonly struct UnitAISearchArea
{
	#region Public Fields
	public readonly Vector3 Center;
	public readonly float Radius;
	public readonly UnitAISearchCue Source;
	public readonly float Confidence;
	public readonly float Timestamp;
	#endregion

	#region Constructors
	public UnitAISearchArea(
		Vector3 _center,
		float _radius,
		UnitAISearchCue _source,
		float _confidence,
		float _timestamp)
	{
		Center = _center;
		Radius = Mathf.Max(0f, _radius);
		Source = _source;
		Confidence = Mathf.Clamp01(_confidence);
		Timestamp = _timestamp;
	}
	#endregion
}
