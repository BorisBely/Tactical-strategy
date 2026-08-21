using UnityEngine;

/// <summary>
/// Game / debug / scenario entry into tactical AI. Implementations must call
/// <see cref="UnitAIController.TryApplyCommand"/> — not a second state path.
/// </summary>
public interface IUnitTacticalCommand
{
	bool SetIdle();
	bool SetDefense(Vector3 _point);
	bool SetAttack(Vector3 _point, Transform _target = null);
	bool SetSearch();
	bool SetSearch(Vector3 _point);
	bool SetRetreat(Vector3 _point);
	bool SetFlee(Vector3 _point);
}
