using System;
using UnityEngine;

/// <summary>
/// Единый флаг "юнит занят" для гейтов: стрельба, смена стойки, перезарядка и т.п.
/// Источники занятости выставляют/снимают свои причины, итоговое состояние — объединение.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitBusyState : MonoBehaviour
{
	#region Types
	[Flags]
	public enum BusyReason
	{
		None = 0,
		StanceTransition = 1 << 0,
		Reload = 1 << 1,
		SelfStabilization = 1 << 4,
		DraggingFallen = 1 << 5,
		StabilizeOther = 1 << 6,
		CarryingFallen = 1 << 7,
		ProximityRelax = 1 << 8,
		Throw = 1 << 3,
		// Future: Melee = 1 << 2,
	}
	#endregion

	#region Serialized Fields
	[SerializeField] private bool m_IsBusy;
	#endregion

	#region Private Fields
	[SerializeField] private BusyReason m_Reasons;
	#endregion

	#region Public Properties
	public BusyReason Reasons => m_Reasons;
	public bool IsBusy => m_IsBusy;
	#endregion

	#region Public Methods
	public void SetReasonActive(BusyReason _reason, bool _active)
	{
		if (_reason == BusyReason.None)
			return;

		if (_active)
			m_Reasons |= _reason;
		else
			m_Reasons &= ~_reason;

		SyncBusyFlag();
	}

	public void ClearAll()
	{
		m_Reasons = BusyReason.None;
		SyncBusyFlag();
	}

	public bool HasReason(BusyReason _reason)
	{
		if (_reason == BusyReason.None)
			return false;

		return (m_Reasons & _reason) != 0;
	}
	#endregion

	#region Private Methods
	private void OnValidate()
	{
		SyncBusyFlag();
	}

	private void SyncBusyFlag()
	{
		m_IsBusy = m_Reasons != BusyReason.None;
	}
	#endregion
}

