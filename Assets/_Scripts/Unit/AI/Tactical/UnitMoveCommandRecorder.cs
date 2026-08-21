using UnityEngine;

/// <summary>
/// Runtime move sink for EditMode tactical navigation tests. Not an Editor script (AddComponent must work).
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitMoveCommandRecorder : MonoBehaviour, IUnitMoveCommand
{
	#region Public Fields
	public bool NextMoveFails;
	#endregion

	#region Private Fields
	private bool m_CanIssue = true;
	private bool m_HasMoveIntent;
	private UnitNavigationReason m_Reason;
	private int m_MoveCount;
	private int m_StopCount;
	private Vector3 m_LastDestination;
	#endregion

	#region Public Properties
	public bool CanIssue
	{
		get => m_CanIssue;
		set => m_CanIssue = value;
	}

	public bool HasMoveIntent => m_HasMoveIntent;
	public UnitNavigationReason Reason => m_Reason;
	public int MoveCount => m_MoveCount;
	public int StopCount => m_StopCount;
	public Vector3 LastDestination => m_LastDestination;
	#endregion

	#region Public Methods
	public bool TryMoveTo(Vector3 _destination, UnitNavigationReason _reason)
	{
		if (!m_CanIssue)
			return false;
		if (NextMoveFails)
		{
			NextMoveFails = false;
			return false;
		}

		m_LastDestination = _destination;
		m_Reason = _reason;
		m_HasMoveIntent = true;
		m_MoveCount++;
		return true;
	}

	public void Stop()
	{
		m_HasMoveIntent = false;
		m_Reason = UnitNavigationReason.None;
		m_StopCount++;
	}
	#endregion
}
