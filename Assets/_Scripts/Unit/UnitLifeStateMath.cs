using UnityEngine;

/// <summary>
/// Resolves Alive / Unconscious / Dead from existing Health + Consciousness.
/// Does not change injury rules.
/// </summary>
public static class UnitLifeStateMath
{
	#region Public Methods
	public static UnitLifeState Resolve(Component _host)
	{
		if (_host == null)
			return UnitLifeState.Alive;
		_host.TryGetComponent(out UnitHealth health);
		_host.TryGetComponent(out UnitConsciousness consciousness);
		return Resolve(health, consciousness);
	}

	public static UnitLifeState Resolve(UnitHealth _health, UnitConsciousness _consciousness)
	{
		if (_health != null && _health.IsDead)
			return UnitLifeState.Dead;
		if (_consciousness != null && !_consciousness.IsConscious)
			return UnitLifeState.Unconscious;
		return UnitLifeState.Alive;
	}

	public static bool AllowsTactical(UnitLifeState _state)
	{
		return _state == UnitLifeState.Alive;
	}

	public static bool AllowsPerception(UnitLifeState _state)
	{
		return _state == UnitLifeState.Alive;
	}

	public static bool AllowsCombatDecision(UnitLifeState _state)
	{
		return _state == UnitLifeState.Alive;
	}

	public static bool AllowsMovement(UnitLifeState _state)
	{
		return _state == UnitLifeState.Alive;
	}

	public static bool RequiresCoverRelease(UnitLifeState _state)
	{
		return _state == UnitLifeState.Unconscious || _state == UnitLifeState.Dead;
	}
	#endregion
}
