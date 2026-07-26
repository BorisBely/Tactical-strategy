using UnityEngine;

namespace VehicleNavigation
{
	/// <summary>
	/// Handles stuck / flipped / blocked recovery by running an UnstuckManeuver.
	/// </summary>
	public sealed class RecoveryController
	{
		private readonly float m_MaxRecoveryDuration;
		private readonly float m_Cooldown;

		private float m_RecoveryTimer;
		private float m_CooldownLeft;
		private UnstuckManeuver m_ActiveUnstuck;

		public bool IsRecovering { get; private set; }

		public RecoveryController(float _maxRecoveryDuration = 6f, float _cooldown = 3f)
		{
			m_MaxRecoveryDuration = _maxRecoveryDuration;
			m_Cooldown = _cooldown;
		}

		public void Reset()
		{
			IsRecovering = false;
			m_RecoveryTimer = 0f;
			m_CooldownLeft = 0f;
			m_ActiveUnstuck = null;
		}

		public void Update(float _dt)
		{
			m_CooldownLeft = Mathf.Max(0f, m_CooldownLeft - _dt);
			if (!IsRecovering)
				return;

			m_RecoveryTimer += _dt;
			if (m_RecoveryTimer >= m_MaxRecoveryDuration)
			{
				EndRecovery();
			}
		}

		public UnstuckManeuver TryStartRecovery(FeedbackState _feedback, VehicleDriverMemory _memory)
		{
			if (IsRecovering)
				return m_ActiveUnstuck;

			if (!_feedback.IsStuck)
				return null;
			if (m_CooldownLeft > 0f)
				return null;

			float sign = _memory != null ? _memory.NextUnstuckSteerSign() : 1f;
			m_ActiveUnstuck = new UnstuckManeuver(sign);
			m_RecoveryTimer = 0f;
			IsRecovering = true;
			return m_ActiveUnstuck;
		}

		public bool CheckRecoveryComplete(FeedbackState _feedback)
		{
			if (!IsRecovering)
				return false;

			if (_feedback.SpeedKmh > 2.5f && _feedback.Geometry.FrontClearance > 2f)
			{
				EndRecovery();
				return true;
			}

			return false;
		}

		private void EndRecovery()
		{
			IsRecovering = false;
			m_RecoveryTimer = 0f;
			m_CooldownLeft = m_Cooldown;
			m_ActiveUnstuck = null;
		}
	}
}
