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

	/// <summary>
	/// FSM that drives the reverse process through its lifecycle.
	/// Without this, the code would be a pile of if-if-if.
	/// </summary>
	public sealed class ReverseStateMachine
	{
		public ReverseState Current { get; private set; } = ReverseState.Enter;
		public float TimeInState { get; private set; }

	private const float c_AlignMaxSeconds = 1.5f;
	private const float c_StopMaxSeconds = 1f;
	private const float c_SlowdownFraction = 0.3f;
	private const float c_SlowdownMin = 0.8f;
	private const float c_SlowdownMax = 4f;

		public void Reset()
		{
			Current = ReverseState.Enter;
			TimeInState = 0f;
		}

		public ReverseState Tick(float _dt, DriverContext _ctx, ReversePath _path)
		{
			TimeInState += _dt;

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
					{
						Debug.Log($"[RevState] Reverse→SlowDown: path.IsComplete=true remaining={_path.RemainingDistance:F2}m seg={_path.CurrentSegment}/{_path.Points.Count}");
						Transition(ReverseState.SlowDown);
					}
					else if (_path.RemainingDistance < slowdownDist)
					{
						Debug.Log($"[RevState] Reverse→SlowDown: remaining={_path.RemainingDistance:F2}m < slowdown={slowdownDist:F2}m total={_path.TotalLength:F1}m seg={_path.CurrentSegment}/{_path.Points.Count}");
						Transition(ReverseState.SlowDown);
					}
					break;

				case ReverseState.SlowDown:
					if (_ctx.SpeedKmh < 0.5f && _path.RemainingDistance < 0.8f)
						Transition(ReverseState.Stop);
					break;

				case ReverseState.Stop:
					if (_ctx.SpeedKmh < 0.1f)
					{
						if (_path.RemainingDistance < 0.6f)
							Transition(ReverseState.Finished);
						else if (TimeInState > c_StopMaxSeconds)
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
