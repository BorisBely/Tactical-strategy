using UnityEngine;

/// <summary>
/// #9 observer-local sound knowledge for one AI tick. Not a visual contact.
/// Does not imply Observed, AimPoint, Identity commit, or Fire.
/// </summary>
public readonly struct AISoundContact
{
	#region Public Fields
	public readonly Transform Source;
	public readonly Vector3 Position;
	public readonly SoundEventType Type;
	public readonly float Confidence;
	public readonly float Time;
	public readonly float Age;
	public readonly bool Hostile;
	#endregion

	#region Constructors
	public AISoundContact(
		Transform _source,
		Vector3 _position,
		SoundEventType _type,
		float _confidence,
		float _time,
		float _age,
		bool _hostile)
	{
		Source = _source;
		Position = _position;
		Type = _type;
		Confidence = _confidence;
		Time = _time;
		Age = Mathf.Max(0f, _age);
		Hostile = _hostile;
	}
	#endregion

	#region Public Properties
	public bool IsCombatCue =>
		Hostile &&
		Confidence > 0f &&
		(Type == SoundEventType.Gunshot || Type == SoundEventType.Explosion);
	#endregion
}
