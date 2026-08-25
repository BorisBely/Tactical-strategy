using UnityEngine;

/// <summary>
/// #8 world combat fact. Not soldier knowledge. Not a <see cref="WorldSoundHub"/> packet.
/// </summary>
public enum CombatEventType
{
	Gunshot = 0,
	Hit = 1,
	Impact = 2,
	Death = 3
}

/// <summary>
/// World combat event. Consumers decide whether it becomes knowledge (#9) or ImmediateThreat (#7).
/// </summary>
public readonly struct CombatEvent
{
	#region Public Fields
	public readonly CombatEventType Type;
	public readonly Component Source;
	public readonly Component Instigator;
	public readonly Component Target;
	public readonly Vector3 Position;
	public readonly float Time;
	#endregion

	#region Constructors
	public CombatEvent(
		CombatEventType _type,
		Component _source,
		Component _instigator,
		Component _target,
		Vector3 _position,
		float _time)
	{
		Type = _type;
		Source = _source;
		Instigator = _instigator;
		Target = _target;
		Position = _position;
		Time = _time;
	}
	#endregion

	#region Public Methods
	public static CombatEvent Gunshot(Component _source, Component _instigator, Component _target, Vector3 _position)
	{
		return Create(CombatEventType.Gunshot, _source, _instigator, _target, _position);
	}

	public static CombatEvent Hit(Component _source, Component _instigator, Component _target, Vector3 _position)
	{
		return Create(CombatEventType.Hit, _source, _instigator, _target, _position);
	}

	public static CombatEvent Impact(Component _source, Component _instigator, Component _target, Vector3 _position)
	{
		return Create(CombatEventType.Impact, _source, _instigator, _target, _position);
	}

	public static CombatEvent Death(Component _source, Component _instigator, Component _target, Vector3 _position)
	{
		return Create(CombatEventType.Death, _source, _instigator, _target, _position);
	}

	public static CombatEvent Create(
		CombatEventType _type,
		Component _source,
		Component _instigator,
		Component _target,
		Vector3 _position)
	{
		return new CombatEvent(_type, _source, _instigator, _target, _position, UnityEngine.Time.time);
	}
	#endregion
}
