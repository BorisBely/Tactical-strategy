using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Remembers recent driver choices to prevent forward/reverse oscillation.
	/// </summary>
	public sealed class VehicleDriverMemory
	{
		#region Constants
		private const int c_MaxOscillationPairs = 3;
		private const float c_ReverseAvoidSeconds = 2.5f;
		#endregion

		#region Private Fields
		private VehicleDrivingMode m_LastMode = VehicleDrivingMode.Forward;
		private VehicleManeuverType m_LastManeuver = VehicleManeuverType.Forward;
		private float m_ReverseSeconds;
		private int m_ForwardReverseFlips;
		private float m_AvoidReverseUntil;
		private int m_UnstuckAttempts;
		private float m_UnstuckSteerSign = 1f;
		private string m_LastDecisionReason = string.Empty;
		#endregion

		#region Public Properties
		public VehicleDrivingMode LastMode => m_LastMode;
		public VehicleManeuverType LastManeuver => m_LastManeuver;
		public float ReverseSeconds => m_ReverseSeconds;
		public int UnstuckAttempts => m_UnstuckAttempts;
		public float UnstuckSteerSign => m_UnstuckSteerSign;
		public string LastDecisionReason => m_LastDecisionReason;
		#endregion

		#region Public Methods
		public void ResetForNewOrder()
		{
			m_ReverseSeconds = 0f;
			m_ForwardReverseFlips = 0;
			m_AvoidReverseUntil = 0f;
			m_UnstuckAttempts = 0;
			m_LastDecisionReason = string.Empty;
		}

		public void RecordDecision(VehicleDrivingMode _mode, string _reason)
		{
			if ((_mode == VehicleDrivingMode.Reverse && m_LastMode == VehicleDrivingMode.Forward) ||
			    (_mode == VehicleDrivingMode.Forward && m_LastMode == VehicleDrivingMode.Reverse))
			{
				m_ForwardReverseFlips++;
				if (m_ForwardReverseFlips >= c_MaxOscillationPairs)
					m_AvoidReverseUntil = Time.time + c_ReverseAvoidSeconds;
			}
			else if (_mode == m_LastMode)
			{
				m_ForwardReverseFlips = Mathf.Max(0, m_ForwardReverseFlips - 1);
			}

			m_LastMode = _mode;
			m_LastDecisionReason = _reason ?? string.Empty;
		}

		public void RecordManeuver(VehicleManeuverType _type)
		{
			m_LastManeuver = _type;
		}

		public void TickReverse(bool _isReversing, float _dt)
		{
			if (_isReversing)
				m_ReverseSeconds += _dt;
			else
				m_ReverseSeconds = Mathf.Max(0f, m_ReverseSeconds - _dt * 0.5f);
		}

		public bool ShouldAvoidReverse()
		{
			return Time.time < m_AvoidReverseUntil || m_ReverseSeconds > 6f;
		}

		public float NextUnstuckSteerSign()
		{
			m_UnstuckAttempts++;
			if (m_UnstuckAttempts % 2 == 0)
				m_UnstuckSteerSign = -m_UnstuckSteerSign;
			else if (Mathf.Abs(m_UnstuckSteerSign) < 0.1f)
				m_UnstuckSteerSign = 1f;
			return m_UnstuckSteerSign;
		}

		public bool HasToggledGearRecently(int _pairs, float _windowSeconds)
		{
			return m_ForwardReverseFlips >= _pairs && Time.time < m_AvoidReverseUntil + _windowSeconds;
		}
		#endregion
	}
}
