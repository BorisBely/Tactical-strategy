using UnityEngine;

/// <summary>
/// Vision Stage 15: facing multiplier on DetectionProgress grow rate.
/// Not a Q factor. Floor 1 keeps 45–60° equal to current acquire speed.
/// </summary>
public static class AttentionMath
{
	#region Constants
	public const float MultiplierMin = 1f;
	public const float MultiplierMax = 2.5f;
	public const float NeutralDegrees = 60f;

	private static readonly float[] s_AngleKnots =
	{
		0f, 10f, 20f, 30f, 45f, 60f
	};

	private static readonly float[] s_MultiplierKnots =
	{
		2.50f, 2.15f, 1.55f, 1.12f, 1.00f, 1.00f
	};
	#endregion

	#region Public Methods
	public static float ClampMultiplier(float _multiplier)
	{
		return Mathf.Clamp(_multiplier, MultiplierMin, MultiplierMax);
	}

	public static float EvaluateMultiplier(float _angleDegrees)
	{
		float angle = Mathf.Abs(_angleDegrees);
		if (angle >= s_AngleKnots[s_AngleKnots.Length - 1])
			return MultiplierMin;

		for (int i = 0; i < s_AngleKnots.Length - 1; i++)
		{
			float a0 = s_AngleKnots[i];
			float a1 = s_AngleKnots[i + 1];
			if (angle > a1)
				continue;
			float t = a1 > a0 ? Mathf.InverseLerp(a0, a1, angle) : 0f;
			return ClampMultiplier(Mathf.Lerp(s_MultiplierKnots[i], s_MultiplierKnots[i + 1], t));
		}

		return MultiplierMin;
	}

	public static AttentionBand EvaluateBand(float _angleDegrees)
	{
		float mul = EvaluateMultiplier(_angleDegrees);
		if (mul >= 2f)
			return AttentionBand.High;
		if (mul > 1.05f)
			return AttentionBand.Normal;
		return AttentionBand.Low;
	}
	#endregion
}

public enum AttentionBand
{
	High = 0,
	Normal = 1,
	Low = 2
}
