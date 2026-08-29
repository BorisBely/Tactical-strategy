using UnityEngine;

/// <summary>
/// One local inspect point inside a <see cref="UnitAISearchArea"/>.
/// </summary>
public readonly struct UnitAISearchCandidate
{
	#region Public Fields
	public readonly Vector3 Position;
	public readonly float Score;
	#endregion

	#region Constructors
	public UnitAISearchCandidate(Vector3 _position, float _score)
	{
		Position = _position;
		Score = _score;
	}
	#endregion
}
