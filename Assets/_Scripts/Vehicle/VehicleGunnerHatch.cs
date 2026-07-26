using UnityEngine;

/// <summary>Скрывает Plug-люк, пока стрелок на турели.</summary>
[DisallowMultipleComponent]
public sealed class VehicleGunnerHatch : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleSeatLayout m_Seats;
	[SerializeField] private GameObject m_PlugObject;
	#endregion

	#region Public Properties
	public bool IsGunnerRaised => m_Seats != null && m_Seats.HasGunner;
	public bool CanToggle => m_Seats != null && (m_Seats.CanPromoteToGunner() || m_Seats.CanDemoteGunner());
	#endregion

	#region Public Methods
	public void Configure(VehicleSeatLayout _seats, GameObject _plug)
	{
		m_Seats = _seats;
		m_PlugObject = _plug;
		if (m_Seats != null)
		{
			m_Seats.OccupancyChanged -= OnOccupancyChanged;
			m_Seats.OccupancyChanged += OnOccupancyChanged;
		}

		ApplyVisual();
	}

	public void ToggleGunnerRaised()
	{
		// Совместимость: тумблер теперь = наличие стрелка на слоте (управляется VehicleController).
		ApplyVisual();
	}

	public void SetGunnerRaised(bool _raised)
	{
		ApplyVisual();
	}
	#endregion

	#region Unity Lifecycle
	private void OnDestroy()
	{
		if (m_Seats != null)
			m_Seats.OccupancyChanged -= OnOccupancyChanged;
	}
	#endregion

	#region Private Methods
	private void OnOccupancyChanged() => ApplyVisual();

	private void ApplyVisual()
	{
		bool hidePlug = m_Seats != null && m_Seats.HasGunner;
		if (m_PlugObject != null)
			m_PlugObject.SetActive(!hidePlug);
	}
	#endregion
}
