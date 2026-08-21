using UnityEngine;

/// <summary>
/// Thin infantry move sink. Production wraps <see cref="UnitNavLocomotionDriver"/>.
/// Missing command = tactical states stay decision-only (EditMode).
/// </summary>
public interface IUnitMoveCommand
{
	bool CanIssue { get; }
	bool HasMoveIntent { get; }
	UnitNavigationReason Reason { get; }

	bool TryMoveTo(Vector3 _destination, UnitNavigationReason _reason);
	void Stop();
}
