using System;
using UnityEngine;

/// <summary>
/// Состояние юнита как пассажира машины: место, готовность, прицеливание, возможность огня.
/// Хранит данные; не управляет аниматором и IK.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehiclePassengerState : MonoBehaviour
{
	#region Serialized Fields
	[SerializeField] private VehicleController m_Vehicle;
	[SerializeField, Min(0.05f)] private float m_PrepareMinDuration = 0.35f;

	[Header("Diagnostics")]
	[SerializeField] private bool m_LogDiagnostics = true;
	#endregion

	#region Private Fields
	private float m_PrepareElapsed;
	#endregion

	#region Events
	/// <summary>
	/// Fire-capable seat attach/detach or ready intent changed (preparing / ready / relax).
	/// Used by weapon pose / hand IK to resync NotReady↔Ready blend.
	/// </summary>
	public event Action ReadyIntentChanged;
	#endregion

	#region Public Properties
	public VehicleSeatId Seat { get; private set; }
	public bool WantsVehicleReady { get; private set; }
	public bool IsVehicleReady { get; private set; }
	public bool IsPreparing { get; private set; }

	public bool IsLeftSide { get; private set; }
	public float AimSectorMin { get; private set; }
	public float AimSectorMax { get; private set; }
	public float AimYaw { get; set; }

	public float RawAimYaw { get; set; }

	public bool CanFire { get; set; }

	public bool IsFireCapable { get; private set; }

	/// <summary>True while passenger should use vehicle Ready pose/IK (preparing or fully ready).</summary>
	public bool WantsReadyPose => IsFireCapable && (IsVehicleReady || IsPreparing || WantsVehicleReady);
	#endregion

	#region Unity Lifecycle
	private void Update()
	{
		if (!IsFireCapable)
			return;

		if (IsPreparing && WantsVehicleReady)
			TickPrepareToReady();
	}
	#endregion

	#region Public Methods
	public void Attach(VehicleController _vehicle, VehicleSeatId _seat)
	{
		m_Vehicle = _vehicle;
		Seat = _seat;

		bool wasFireCapable = IsFireCapable;
		IsFireCapable = _seat == VehicleSeatId.Commander
		             || _seat == VehicleSeatId.RearLeft
		             || _seat == VehicleSeatId.RearRight;

		if (!IsFireCapable)
		{
			WantsVehicleReady = false;
			IsVehicleReady = false;
			IsPreparing = false;
			CanFire = false;
			if (wasFireCapable)
				RaiseReadyIntentChanged();
			return;
		}

		IsLeftSide = _seat == VehicleSeatId.RearLeft;

		if (IsLeftSide)
		{
			AimSectorMin = -10f;
			AimSectorMax = 30f;
		}
		else
		{
			AimSectorMin = -10f;
			AimSectorMax = 45f;
		}

		WantsVehicleReady = false;
		IsVehicleReady = false;
		IsPreparing = false;
		CanFire = false;
		AimYaw = 0f;
		RawAimYaw = 0f;
		m_PrepareElapsed = 0f;

		if (m_LogDiagnostics)
			Debug.Log($"[VehPassReady] {name} ATTACH seat={_seat} side={(IsLeftSide ? "LEFT" : "RIGHT")} sector=[{AimSectorMin}°..{AimSectorMax}°]", this);

		RaiseReadyIntentChanged();
	}

	public void Detach()
	{
		bool wasFireCapable = IsFireCapable;
		bool hadReadyIntent = WantsReadyPose;

		WantsVehicleReady = false;
		IsVehicleReady = false;
		IsPreparing = false;
		CanFire = false;
		AimYaw = 0f;
		RawAimYaw = 0f;
		IsFireCapable = false;
		m_PrepareElapsed = 0f;
		m_Vehicle = null;

		if (wasFireCapable || hadReadyIntent)
			RaiseReadyIntentChanged();
	}

	public void SetWantsReady(bool _wants)
	{
		if (!IsFireCapable)
		{
			if (m_LogDiagnostics)
				Debug.Log($"[VehPassReady] {name} SetWantsReady({_wants}) IGNORED — not fire-capable", this);
			return;
		}

		if (m_LogDiagnostics)
			Debug.Log($"[VehPassReady] {name} SetWantsReady({_wants})", this);

		bool previousIntent = WantsReadyPose;
		WantsVehicleReady = _wants;
		if (_wants)
		{
			IsVehicleReady = false;
			IsPreparing = true;
			CanFire = false;
			m_PrepareElapsed = 0f;
		}
		else
		{
			IsVehicleReady = false;
			IsPreparing = false;
			CanFire = false;
			m_PrepareElapsed = 0f;
		}

		if (previousIntent != WantsReadyPose)
			RaiseReadyIntentChanged();
	}

	public static VehiclePassengerState GetOrAdd(GameObject _unitObject)
	{
		if (_unitObject == null)
			return null;
		if (!_unitObject.TryGetComponent(out VehiclePassengerState state))
			state = _unitObject.AddComponent<VehiclePassengerState>();
		return state;
	}
	#endregion

	#region Private Methods
	private void TickPrepareToReady()
	{
		m_PrepareElapsed += Time.deltaTime;

		bool glassReady = true;
		if (m_Vehicle != null && m_Vehicle.GlassController != null)
			glassReady = m_Vehicle.GlassController.IsFullyOpen;

		bool wasNotReady = !IsVehicleReady;

		if (m_PrepareElapsed >= m_PrepareMinDuration && glassReady)
		{
			IsPreparing = false;
			IsVehicleReady = true;

			if (m_LogDiagnostics)
				Debug.Log($"[VehPassReady] {name} READY! elapsed={m_PrepareElapsed:F2}s glass={glassReady}", this);

			m_PrepareElapsed = 0f;
			// Pose blend target stays Ready (WantsReadyPose unchanged); no Raise needed.
		}
		else if (wasNotReady && m_LogDiagnostics && m_PrepareElapsed >= m_PrepareMinDuration * 0.5f)
		{
			if (!glassReady)
				Debug.Log($"[VehPassReady] {name} PREPARING... elapsed={m_PrepareElapsed:F2}s (min={m_PrepareMinDuration:F2}s) glass=NOT_READY", this);
			else
				Debug.Log($"[VehPassReady] {name} PREPARING... elapsed={m_PrepareElapsed:F2}s (min={m_PrepareMinDuration:F2}s) glass=OK", this);
		}
	}

	private void RaiseReadyIntentChanged()
	{
		ReadyIntentChanged?.Invoke();
	}
	#endregion
}
