using UnityEngine;

namespace CombatVehicleSystem
{
	public class TurretAim : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private Transform m_CameraFollowTarget;
		[SerializeField] private Transform m_YawPivot;
		[SerializeField] private Transform m_PitchPivot;
		[SerializeField] private Vector3 m_RestYawEuler;
		[SerializeField] private Vector3 m_RestPitchEuler;
		#endregion

		#region Private Fields
		private bool m_Active;
		private float m_TurnRate = 120f;
		private bool m_LimitYaw;
		private float m_LeftYawLimit = 60f;
		private float m_RightYawLimit = 60f;
		private float m_UpPitchLimit = 60f;
		private float m_DownPitchLimit = 12f;
		private float m_DefaultAimDistance = 200f;
		private Vector3 m_AimPoint;
		#endregion

		#region Public Properties
		public Transform CameraFollowTarget => m_CameraFollowTarget;
		#endregion

		#region Public Methods
		public void ApplyTuning(VehicleTuning _tuning)
		{
			if (_tuning == null)
				return;
			m_TurnRate = _tuning.TurnRate;
			m_LimitYaw = _tuning.LimitYaw;
			m_LeftYawLimit = _tuning.LeftYawLimit;
			m_RightYawLimit = _tuning.RightYawLimit;
			m_UpPitchLimit = _tuning.UpPitchLimit;
			m_DownPitchLimit = _tuning.DownPitchLimit;
			m_DefaultAimDistance = _tuning.DefaultAimDistance;
		}

		public void Configure(Transform _yaw, Transform _pitch, Transform _followTarget, Vector3 _restYaw, Vector3 _restPitch)
		{
			m_YawPivot = _yaw;
			m_PitchPivot = _pitch;
			m_CameraFollowTarget = _followTarget;
			m_RestYawEuler = _restYaw;
			m_RestPitchEuler = _restPitch;
		}

		public void SetActive(bool _active)
		{
			m_Active = _active;
		}

		public void TickAim(VehicleCommand _command)
		{
			if (m_YawPivot == null || m_PitchPivot == null)
				return;

			if (m_Active && _command.HasAimPoint)
				m_AimPoint = _command.AimWorldPoint;
			else if (m_Active)
				m_AimPoint = m_PitchPivot.position + m_PitchPivot.forward * m_DefaultAimDistance;

			RotateYaw();
			RotatePitch();
		}
		#endregion

		#region Private Methods
		private void RotateYaw()
		{
			if (!m_Active)
			{
				m_YawPivot.localRotation = Quaternion.RotateTowards(
					m_YawPivot.localRotation,
					Quaternion.Euler(m_RestYawEuler),
					m_TurnRate * Time.deltaTime);
				return;
			}

			Vector3 targetPos = transform.InverseTransformPoint(m_AimPoint);
			targetPos.y = 0f;

			Vector3 clamped = targetPos;
			if (m_LimitYaw)
			{
				float limit = targetPos.x >= 0f ? m_RightYawLimit : m_LeftYawLimit;
				clamped = Vector3.RotateTowards(Vector3.forward, targetPos, Mathf.Deg2Rad * limit, float.MaxValue);
			}

			Quaternion goal = Quaternion.LookRotation(clamped);
			m_YawPivot.localRotation = Quaternion.RotateTowards(m_YawPivot.localRotation, goal, m_TurnRate * Time.deltaTime);
		}

		private void RotatePitch()
		{
			if (!m_Active)
			{
				m_PitchPivot.localRotation = Quaternion.RotateTowards(
					m_PitchPivot.localRotation,
					Quaternion.Euler(m_RestPitchEuler),
					m_TurnRate * Time.deltaTime);
				return;
			}

			Vector3 targetPos = m_YawPivot.InverseTransformPoint(m_AimPoint);
			targetPos.x = 0f;

			float limit = targetPos.y <= 0f ? m_DownPitchLimit : m_UpPitchLimit;
			Vector3 clamped = Vector3.RotateTowards(Vector3.forward, targetPos, Mathf.Deg2Rad * limit, float.MaxValue);
			Quaternion goal = Quaternion.LookRotation(clamped);
			m_PitchPivot.localRotation = Quaternion.RotateTowards(m_PitchPivot.localRotation, goal, m_TurnRate * Time.deltaTime);
		}
		#endregion
	}
}
