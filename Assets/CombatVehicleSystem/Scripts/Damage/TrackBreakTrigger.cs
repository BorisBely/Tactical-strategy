using UnityEngine;

namespace CombatVehicleSystem
{
	public class TrackBreakTrigger : MonoBehaviour
	{
		#region Constants
		private const string c_MineTag = "Mine";
		#endregion

		#region Serialized Fields
		[SerializeField] private Transform m_BreakSpawnPoint;
		[SerializeField] private TrackBreakSide m_Side = TrackBreakSide.Left;
		#endregion

		#region Private Fields
		private TrackBreakHandler m_Handler;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Handler = GetComponentInParent<TrackBreakHandler>();
		}

		private void OnTriggerEnter(Collider _other)
		{
			if (m_Handler == null)
				return;
			if (!_other.CompareTag(c_MineTag))
				return;

			ExplosiveMine mine = _other.GetComponentInParent<ExplosiveMine>();
			if (mine == null)
				return;

			Transform spawn = m_BreakSpawnPoint != null ? m_BreakSpawnPoint : transform;
			m_Handler.BreakTrack(m_Side, spawn.position, spawn.rotation, mine.ExplosionForce, mine.transform.position);
			Destroy(mine.gameObject);
		}
		#endregion

		#region Public Methods
		public void Configure(Transform _spawn, TrackBreakSide _side)
		{
			m_BreakSpawnPoint = _spawn;
			m_Side = _side;
		}
		#endregion
	}
}
