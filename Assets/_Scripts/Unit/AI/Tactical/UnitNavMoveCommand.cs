using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Search / Attack / Defense / Retreat / Flee → existing <see cref="UnitNavLocomotionDriver"/>. Not RTS ClickToMove.
/// Reason is diagnostic only. Walk only. Do not use vehicle NavigationRequest.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(26)]
public sealed class UnitNavMoveCommand : MonoBehaviour, IUnitMoveCommand
{
	#region Private Fields
	private UnitNavLocomotionDriver m_Driver;
	private UnitNavigationReason m_Reason;
	#endregion

	#region Public Properties
	public bool CanIssue
	{
		get
		{
			Bind();
			return m_Driver != null && m_Driver.enabled && m_Driver.gameObject.activeInHierarchy;
		}
	}

	public bool HasMoveIntent
	{
		get
		{
			Bind();
			return m_Driver != null && m_Driver.HasMoveIntent;
		}
	}

	public UnitNavigationReason Reason => m_Reason;
	#endregion

	#region Public Methods
	public bool TryMoveTo(Vector3 _destination, UnitNavigationReason _reason)
	{
		Bind();
		if (!CanIssue)
			return false;

		bool issued = m_Driver.IssueNavOrder(_destination, UnitNavLocomotionDriver.MoveTier.Walk);
		m_Reason = issued ? _reason : UnitNavigationReason.None;
		if (UnitActionLog.Enabled)
		{
			string path = "none";
			if (TryGetComponent(out NavMeshAgent agent) && agent != null)
				path = UnitActionLog.AgentPath(agent);
			string payload =
				"issue dest=" + UnitActionLog.Vec(_destination) +
				" reason=" + _reason +
				" ok=" + (issued ? "1" : "0") +
				" path=" + path +
				" source=Tactical";
			UnitActionLog.Write(this, UnitActionLog.Move, payload);
			UnitActionLog.Timeline(UnitActionLog.Move, "actor=" + UnitActionLog.Slot(this) + " " + payload);
		}
		return issued;
	}

	public void Stop()
	{
		Bind();
		if (m_Driver != null && m_Driver.enabled)
			m_Driver.HardStop();
		if (UnitActionLog.Enabled && m_Reason != UnitNavigationReason.None)
			UnitActionLog.Write(this, UnitActionLog.Move, "stop reasonWas=" + m_Reason + " source=Tactical");
		m_Reason = UnitNavigationReason.None;
	}
	#endregion

	#region Private Methods
	private void Bind()
	{
		if (m_Driver == null)
			TryGetComponent(out m_Driver);
	}
	#endregion
}
