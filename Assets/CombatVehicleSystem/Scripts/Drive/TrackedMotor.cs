using UnityEngine;

namespace CombatVehicleSystem
{
	[System.Serializable]
	public class TrackSide
	{
		#region Public Fields
		public bool Enabled = true;
		public WheelCollider[] Colliders;
		public Transform[] Visuals;
		public Transform[] Bones;
		public Material ScrollMaterial;
		#endregion
	}

	public class TrackedMotor : MonoBehaviour
	{
		#region Serialized Fields
		[SerializeField] private TrackSide m_LeftTrack;
		[SerializeField] private TrackSide m_RightTrack;
		#endregion

		#region Private Fields
		private Rigidbody m_Body;
		private float m_MotorForce = 1500f;
		private float m_AccelerationForce = 1500f;
		private float m_ReverseForce = 800f;
		private float m_TopSpeedKmh = 55f;
		private float m_SoftBrakeTorque = 800f;
		private float m_HardBrakeTorque = 2000f;
		private float m_CoastDecelTorque = 400f;
		private float m_ScrollScale = 1f;
		private float m_CurrentSpeedKmh;
		#endregion

		#region Public Properties
		public float CurrentSpeedKmh => m_CurrentSpeedKmh;
		public TrackSide LeftTrack => m_LeftTrack;
		public TrackSide RightTrack => m_RightTrack;
		#endregion

		#region Unity Lifecycle
		private void Awake()
		{
			m_Body = GetComponent<Rigidbody>();
			if (m_LeftTrack != null)
				m_LeftTrack.Enabled = true;
			if (m_RightTrack != null)
				m_RightTrack.Enabled = true;
		}
		#endregion

		#region Public Methods
		public void ApplyTuning(VehicleTuning _tuning)
		{
			if (_tuning == null)
				return;
			m_MotorForce = _tuning.MotorForce;
			m_AccelerationForce = _tuning.AccelerationForce;
			m_ReverseForce = _tuning.ReverseForce;
			m_TopSpeedKmh = _tuning.TopSpeedKmh;
			m_SoftBrakeTorque = _tuning.SoftBrakeTorque;
			m_HardBrakeTorque = _tuning.HardBrakeTorque;
			m_CoastDecelTorque = _tuning.CoastDecelTorque;
			m_ScrollScale = _tuning.TrackScrollScale;
		}

		public void SetTracks(TrackSide _left, TrackSide _right)
		{
			m_LeftTrack = _left;
			m_RightTrack = _right;
		}

		public void SetTrackEnabled(bool _left, bool _enabled)
		{
			if (_left)
			{
				if (m_LeftTrack != null)
					m_LeftTrack.Enabled = _enabled;
			}
			else if (m_RightTrack != null)
			{
				m_RightTrack.Enabled = _enabled;
			}
		}

		public void TickDrive(VehicleCommand _command)
		{
			if (m_Body == null)
				return;

			m_CurrentSpeedKmh = m_Body.linearVelocity.magnitude * 3.6f;

			float leftTorque;
			float rightTorque;
			float brake = ResolveBrakeTorque(_command.BrakeMode, _command.Throttle);

			if (_command.BrakeMode != VehicleBrakeMode.None)
			{
				leftTorque = 0f;
				rightTorque = 0f;
			}
			else if (Mathf.Abs(_command.Throttle) < 0.02f && brake > 0.01f)
			{
				leftTorque = 0f;
				rightTorque = 0f;
			}
			else
			{
				float force = _command.Throttle >= 0f ? m_AccelerationForce : m_ReverseForce;
				if (force < 0.01f)
					force = m_MotorForce;

				if (Mathf.Abs(_command.Steer) > 0.01f)
				{
					leftTorque = force * (_command.Steer * 2f);
					rightTorque = force * (-_command.Steer * 2f);
				}
				else
				{
					leftTorque = force * _command.Throttle;
					rightTorque = force * _command.Throttle;
				}

				brake = 0f;
			}

			ApplySide(m_LeftTrack, leftTorque, brake);
			ApplySide(m_RightTrack, rightTorque, brake);
			ScrollTracks();
		}

		public void TickPhysics(bool _controlActive, VehicleCommand _command)
		{
			SyncSide(m_LeftTrack);
			SyncSide(m_RightTrack);
		}
		#endregion

		#region Private Methods
		private float ResolveBrakeTorque(VehicleBrakeMode _mode, float _throttle)
		{
			switch (_mode)
			{
				case VehicleBrakeMode.Hard:
					return m_HardBrakeTorque;
				case VehicleBrakeMode.Soft:
					return m_SoftBrakeTorque;
				case VehicleBrakeMode.Coast:
					return 0f;
				default:
					if (Mathf.Abs(_throttle) < 0.02f)
						return m_CoastDecelTorque;
					return 0f;
			}
		}

		private void ApplySide(TrackSide _side, float _torque, float _brake)
		{
			if (_side == null || _side.Colliders == null)
				return;

			for (int i = 0; i < _side.Colliders.Length; i++)
			{
				WheelCollider col = _side.Colliders[i];
				if (col == null)
					continue;

				if (_side.Enabled)
				{
					col.motorTorque = m_CurrentSpeedKmh < m_TopSpeedKmh ? _torque : 0f;
					col.brakeTorque = _brake;
				}
				else
				{
					col.motorTorque = 0f;
					col.brakeTorque = m_HardBrakeTorque;
				}
			}
		}

		private void SyncSide(TrackSide _side)
		{
			if (_side == null || _side.Colliders == null)
				return;

			for (int i = 0; i < _side.Colliders.Length; i++)
			{
				WheelCollider col = _side.Colliders[i];
				if (col == null)
					continue;

				col.GetWorldPose(out Vector3 pos, out Quaternion rot);

				if (_side.Visuals != null && i < _side.Visuals.Length && _side.Visuals[i] != null)
					_side.Visuals[i].SetPositionAndRotation(pos, rot);

				if (_side.Bones != null && i < _side.Bones.Length && _side.Bones[i] != null)
					_side.Bones[i].position = pos;
			}
		}

		private void ScrollTracks()
		{
			ScrollMaterial(m_LeftTrack);
			ScrollMaterial(m_RightTrack);
		}

		private void ScrollMaterial(TrackSide _side)
		{
			if (_side == null || _side.ScrollMaterial == null || _side.Colliders == null || _side.Colliders.Length < 2)
				return;
			if (_side.Colliders[1] == null)
				return;

			float offset = _side.Colliders[1].rpm * Time.deltaTime * m_ScrollScale;
			_side.ScrollMaterial.mainTextureOffset = new Vector2(1f, offset);
		}
		#endregion
	}
}
