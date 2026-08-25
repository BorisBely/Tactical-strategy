using UnityEngine;

/// <summary>
/// Spreads periodic perception / Auto-pose work across frames.
/// Does not change Q, FOV, Acquire, or scan meaning. A skipped tick keeps the last vision frame.
/// ImmediateScan still bypasses this.
/// </summary>
public static class PerceptionWorkStagger
{
	#region Constants
	public const float DefaultMinSeconds = 0.2f;
	public const float DefaultMaxSeconds = 0.3f;
	private const float c_Golden = 0.6180339887f;
	private const float c_FrameJitterSeconds = 0.05f;
	#endregion

	#region Public Methods
	public static float Phase01(int _idHash)
	{
		uint u = unchecked((uint)_idHash);
		float x = u * c_Golden;
		return x - Mathf.Floor(x);
	}

	/// <summary>0…50 ms stable offset so neighbors do not share the same frame.</summary>
	public static float FrameJitterSeconds(int _idHash) =>
		Phase01(_idHash) * c_FrameJitterSeconds;

	/// <summary>
	/// Random delay in <paramref name="_minSeconds"/>…<paramref name="_maxSeconds"/> plus per-unit jitter.
	/// </summary>
	public static float NextIntervalSeconds(int _idHash, float _minSeconds, float _maxSeconds)
	{
		float min = Mathf.Max(0.05f, _minSeconds);
		float max = Mathf.Max(min, _maxSeconds);
		return UnityEngine.Random.Range(min, max) + FrameJitterSeconds(_idHash);
	}

	public static float NextIntervalSeconds(int _idHash) =>
		NextIntervalSeconds(_idHash, DefaultMinSeconds, DefaultMaxSeconds);
	#endregion
}
