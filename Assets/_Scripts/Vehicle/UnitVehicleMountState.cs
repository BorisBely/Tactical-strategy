using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Состояние посадки юнита в машину: отключает NavMesh/RTS-перемещение и крепит к слоту.
/// </summary>
[DisallowMultipleComponent]
public sealed class UnitVehicleMountState : MonoBehaviour
{
	#region Private Fields
	private bool m_IsMounted;
	private VehicleController m_Vehicle;
	private VehicleSeatId m_SeatId;
	private Transform m_CachedParent;
	private bool m_HadAgentEnabled;
	private bool m_HadRtsEnabled;
	private NavMeshAgent m_Agent;
	private RtsUnitMember m_RtsMember;
	private UnitClickToMove m_ClickToMove;
	private Collider m_SelectionCollider;
	private readonly List<Collider> m_DisabledSolidColliders = new List<Collider>(8);
	private UnitVehicleSeatPoseController m_SeatPose;
	#endregion

	#region Public Properties
	public bool IsMounted => m_IsMounted;
	public VehicleController Vehicle => m_Vehicle;
	public VehicleSeatId SeatId => m_SeatId;
	#endregion

	#region Public Methods
	public void Mount(VehicleController _vehicle, VehicleSeatId _seatId, Transform _seatAnchor, bool _isLitter)
	{
		if (_vehicle == null || _seatAnchor == null)
			return;

		if (m_IsMounted)
			DismountWorldPosition(transform.position, transform.rotation);

		CacheRefs();

		if (m_RtsMember != null)
		{
			m_RtsMember.HardStop();
			m_HadRtsEnabled = m_RtsMember.enabled;
			m_RtsMember.SetSelected(false);
			RtsUnitSelectionManager.Instance?.NotifyUnitBecameNonControllable(m_RtsMember);
			m_RtsMember.enabled = false;
		}

		if (m_ClickToMove != null)
			m_ClickToMove.SetDirectInputEnabled(false);

		if (m_Agent != null)
		{
			m_HadAgentEnabled = m_Agent.enabled;
			if (m_Agent.enabled && m_Agent.isOnNavMesh)
				m_Agent.ResetPath();
			m_Agent.enabled = false;
		}

		if (m_SelectionCollider != null)
			m_SelectionCollider.enabled = false;

		DisableSolidCollidersForMount();

		m_CachedParent = transform.parent;
		transform.SetParent(_seatAnchor, false);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;

		m_Vehicle = _vehicle;
		m_SeatId = _seatId;
		m_IsMounted = true;

		m_SeatPose = UnitVehicleSeatPoseController.GetOrAdd(gameObject);
		m_SeatPose.ApplySeatPose(_seatId, _vehicle);


	}

	public void TransferToSeat(VehicleSeatId _seatId, Transform _seatAnchor, bool _isLitter)
	{
		if (!m_IsMounted || _seatAnchor == null)
			return;

		m_SeatId = _seatId;
		transform.SetParent(_seatAnchor, false);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;

		if (m_SeatPose == null)
			m_SeatPose = UnitVehicleSeatPoseController.GetOrAdd(gameObject);
		m_SeatPose.ApplySeatPose(_seatId, m_Vehicle);
	}

	public void DismountWorldPosition(Vector3 _worldPosition, Quaternion _worldRotation)
	{
		if (!m_IsMounted)
			return;

		m_SeatPose?.ClearSeatPose();

		transform.SetParent(m_CachedParent, true);
		transform.SetPositionAndRotation(_worldPosition, _worldRotation);

		if (m_Agent != null)
		{
			m_Agent.enabled = m_HadAgentEnabled;
			if (m_Agent.enabled && m_Agent.isOnNavMesh)
				m_Agent.Warp(_worldPosition);
			else if (m_Agent.enabled &&
			         NavMesh.SamplePosition(_worldPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
			{
				m_Agent.Warp(hit.position);
			}
		}

		if (m_RtsMember != null)
			m_RtsMember.enabled = m_HadRtsEnabled;

		if (m_SelectionCollider != null)
			m_SelectionCollider.enabled = true;

		RestoreSolidCollidersAfterDismount();

		m_IsMounted = false;
		m_Vehicle = null;
		m_CachedParent = null;
	}

	public static UnitVehicleMountState GetOrAdd(RtsUnitMember _unit)
	{
		if (_unit == null)
			return null;

		if (!_unit.TryGetComponent(out UnitVehicleMountState state))
			state = _unit.gameObject.AddComponent<UnitVehicleMountState>();
		return state;
	}

	public static bool IsUnitMounted(RtsUnitMember _unit)
	{
		return _unit != null &&
		       _unit.TryGetComponent(out UnitVehicleMountState state) &&
		       state.IsMounted;
	}
	#endregion

	#region Private Methods
	private void CacheRefs()
	{
		if (m_RtsMember == null)
			TryGetComponent(out m_RtsMember);
		if (m_ClickToMove == null)
			TryGetComponent(out m_ClickToMove);
		if (m_Agent == null)
			TryGetComponent(out m_Agent);
		if (m_SelectionCollider == null && m_RtsMember != null)
		{
			Collider[] colliders = GetComponentsInChildren<Collider>(true);
			for (int i = 0; i < colliders.Length; i++)
			{
				if (colliders[i] != null && !colliders[i].isTrigger)
				{
					m_SelectionCollider = colliders[i];
					break;
				}
			}
		}
	}

	private void DisableSolidCollidersForMount()
	{
		m_DisabledSolidColliders.Clear();
		Collider[] colliders = GetComponentsInChildren<Collider>(true);
		for (int i = 0; i < colliders.Length; i++)
		{
			Collider col = colliders[i];
			if (col == null || col.isTrigger || !col.enabled)
				continue;
			col.enabled = false;
			m_DisabledSolidColliders.Add(col);
		}
	}

	private void RestoreSolidCollidersAfterDismount()
	{
		for (int i = 0; i < m_DisabledSolidColliders.Count; i++)
		{
			if (m_DisabledSolidColliders[i] != null)
				m_DisabledSolidColliders[i].enabled = true;
		}

		m_DisabledSolidColliders.Clear();
	}
	#endregion
}
