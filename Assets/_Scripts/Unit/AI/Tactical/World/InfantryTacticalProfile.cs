using UnityEngine;

/// <summary>
/// Editor-time infantry tactical switches. Does not retune #13/#14 score formulas.
/// </summary>
[CreateAssetMenu(menuName = "Infantry/Tactical Profile", fileName = "InfantryTacticalProfile")]
public sealed class InfantryTacticalProfile : ScriptableObject
{
	#region Serialized
	[SerializeField] private bool m_UseCover = true;
	[SerializeField] private bool m_AllowCoverReservation = true;
	[SerializeField] private TacticalMovementMode m_MovementMode = TacticalMovementMode.Tactical;
	#endregion

	#region Public Properties
	public bool UseCover => m_UseCover;
	public bool AllowCoverReservation => m_AllowCoverReservation;
	public TacticalMovementMode MovementMode => m_MovementMode;
	#endregion
}
