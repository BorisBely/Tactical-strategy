using UnityEngine;

namespace VehicleNavigation
{
	public enum ReverseState
	{
		Enter,
		Align,
		Reverse,
		SlowDown,
		Stop,
		Finished,
		Failed
	}

	public sealed class ReverseStateMachine
	{
		public ReverseState Current { get; private set; } = ReverseState.Enter;
		public float TimeInState { get; private set; }

		private const float c_AlignMaxSeconds = 1.5f;
		private const float c_SlowdownFraction = 0.3f;
		private const float c_SlowdownMin = 0.8f;
		private const float c_SlowdownMax = 4f;

		private float m_BestRemaining = float.MaxValue;
		private float m_NoProgressTimer;

		public void Reset()
		{
			Current = ReverseState.Enter;
			TimeInState = 0f;
			m_BestRemaining = float.MaxValue;
			m_NoProgressTimer = 0f;
		}

		public ReverseState Tick(float _dt, DriverContext _ctx, ReversePath _path)
		{
			TimeInState += _dt;
			float remaining = _path.RemainingDistance;

			// Progress tracking: best distance must decrease by at least 5cm
			if (remaining < m_BestRemaining - 0.05f)
			{
				m_BestRemaining = remaining;
				m_NoProgressTimer = 0f;
			}
			else
			{
				m_NoProgressTimer += _dt;
			}

			switch (Current)
			{
				case ReverseState.Enter:
					if (_ctx.SpeedKmh < 0.3f)
						Transition(ReverseState.Align);
					break;

				case ReverseState.Align:
					Vector3 alignDir = -_ctx.Forward;
					float angleToPath = _path.IsValid
						? Vector3.Angle(alignDir, _path.Points[0].Tangent)
						: 0f;
					if (angleToPath < 20f && _ctx.SpeedKmh < 1f)
						Transition(ReverseState.Reverse);
					else if (TimeInState > c_AlignMaxSeconds)
						Transition(ReverseState.Reverse);
					break;

				case ReverseState.Reverse:
					float slowdownDist = Mathf.Clamp(_path.TotalLength * c_SlowdownFraction, c_SlowdownMin, c_SlowdownMax);
					if (_path.IsComplete)
						Transition(ReverseState.SlowDown);
					else if (remaining < slowdownDist)
						Transition(ReverseState.SlowDown);
					break;

				case ReverseState.SlowDown:
					if (_ctx.SpeedKmh < 0.5f && remaining < 0.8f)
						Transition(ReverseState.Stop);
					break;

				case ReverseState.Stop:
					if (_ctx.SpeedKmh < 0.1f)
					{
						bool headingOk = true;
						if (_ctx.RequestedHeading.HasValue)
							headingOk = Mathf.Abs(Mathf.DeltaAngle(_ctx.Yaw, _ctx.RequestedHeading.Value)) < 5f;

						if (remaining < 0.6f && headingOk)
							Transition(ReverseState.Finished);
						else if (TimeInState > 1f && remaining > 0.6f && m_NoProgressTimer > 1f)
							Transition(ReverseState.Reverse);
					}
					break;

				case ReverseState.Finished:
				case ReverseState.Failed:
					break;
			}

			return Current;
		}

		public void ForceFail()
		{
			Transition(ReverseState.Failed);
		}

		private void Transition(ReverseState _next)
		{
			Current = _next;
			TimeInState = 0f;
		}
	}
}
