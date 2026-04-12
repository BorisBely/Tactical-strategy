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
		// Future: Melee = 1 << 2,
		// Future: Throw = 1 << 3,
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

		BusyReason before = m_Reasons;
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

