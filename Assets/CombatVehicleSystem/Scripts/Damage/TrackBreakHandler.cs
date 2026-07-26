using UnityEngine;

namespace CombatVehicleSystem
{
	public enum TrackBreakSide
	{
		Left = 0,
		Right = 1
	}

	public class TrackBreakHandler : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private GameObject m_BrokenTrackPrefab;
		[SerializeField] private GameObject m_LeftTrackVisual;
		[SerializeField] private GameObject m_RightTrackVisual;
		#endregion

		#region Private Fields
		private Rigidbody m_Body;
		private TrackedMotor m_Motor;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Body = GetComponent<Rigidbody>();
			TryGetComponent(out m_Motor);
		}
		#endregion

		#region Public Methods
		public void Configure(GameObject _brokenPrefab, GameObject _leftVisual, GameObject _rightVisual)
		{
			m_BrokenTrackPrefab = _brokenPrefab;
			m_LeftTrackVisual = _leftVisual;
			m_RightTrackVisual = _rightVisual;
		}

		public void BreakTrack(TrackBreakSide _side, Vector3 _spawnPos, Quaternion _spawnRot, float _explosionForce, Vector3 _explosionOrigin)
		{
			switch (_side)
			{
				case TrackBreakSide.Left:
					if (m_LeftTrackVisual != null)
						m_LeftTrackVisual.SetActive(false);
					if (m_Motor != null)
						m_Motor.SetTrackEnabled(true, false);
					break;
				case TrackBreakSide.Right:
					if (m_RightTrackVisual != null)
						m_RightTrackVisual.SetActive(false);
					if (m_Motor != null)
						m_Motor.SetTrackEnabled(false, false);
					break;
			}

			if (m_Body != null)
				m_Body.AddForceAtPosition(Vector3.one * _explosionForce, _explosionOrigin, ForceMode.Impulse);

			if (m_BrokenTrackPrefab != null)
				Instantiate(m_BrokenTrackPrefab, _spawnPos, _spawnRot);
		}
		#endregion
	}
}
