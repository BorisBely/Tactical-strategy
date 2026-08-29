using UnityEngine;

/// <summary>
/// Compact #14C snapshot. Consumers should not care which source produced it.
/// </summary>
public readonly struct ThreatDirectionKnowledge
{
	#region Public Fields
	public readonly Vector3 Direction;
	public readonly ThreatDirectionCompass Compass;
	public readonly float Confidence;
	public readonly float UncertaintyDegrees;
	public readonly float Age;
	public readonly ThreatDirectionSource Source;
	public readonly ThreatDirectionState State;
	#endregion

	#region Constructors
	public ThreatDirectionKnowledge(
		Vector3 _direction,
		ThreatDirectionCompass _compass,
		float _confidence,
		float _uncertaintyDegrees,
		float _age,
		ThreatDirectionSource _source,
		ThreatDirectionState _state)
	{
		Direction = _direction;
		Compass = _compass;
		Confidence = _confidence;
		UncertaintyDegrees = _uncertaintyDegrees;
		Age = _age;
		Source = _source;
		State = _state;
	}
	#endregion

	#region Public Properties
	public bool HasValue => State != ThreatDirectionState.None;

	public ThreatDirectionSector Sector =>
		new ThreatDirectionSector(Direction, UncertaintyDegrees);
	#endregion
}

/// <summary>Direction cone. Half-angle is <see cref="ThreatDirectionKnowledge.UncertaintyDegrees"/>.</summary>
public readonly struct ThreatDirectionSector
{
	#region Public Fields
	public readonly Vector3 Direction;
	public readonly float HalfAngleDegrees;
	#endregion

	#region Constructors
	public ThreatDirectionSector(Vector3 _direction, float _halfAngleDegrees)
	{
		Direction = _direction;
		HalfAngleDegrees = _halfAngleDegrees;
	}
	#endregion
}
