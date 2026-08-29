using UnityEngine;

/// <summary>
/// Prototype knobs for #13.7. Not a freeze. Eye offsets are imagined poses, not root motion.
/// </summary>
public sealed class CoverPeekSettings
{
	#region Public Fields
	public float EyeHeightStandingMeters = 1.55f;
	public float EyeHeightCrouchMeters = 1.00f;
	public float SmallOffsetMeters = 0.18f;
	public float MediumOffsetMeters = 0.32f;
	public float DeepOffsetMeters = 0.48f;
	public float SmallExposure = 0.20f;
	public float MediumExposure = 0.35f;
	public float DeepExposure = 0.50f;
	public float CommitSeconds = 0.40f;
	public CoverClassificationSettings Classification = new CoverClassificationSettings();
	#endregion

	#region Public Methods
	public float EyeHeight(CoverStance _stance)
	{
		return _stance == CoverStance.Crouch ? EyeHeightCrouchMeters : EyeHeightStandingMeters;
	}

	public float OffsetMeters(CoverLeanLevel _level)
	{
		switch (_level)
		{
			case CoverLeanLevel.Small:
				return SmallOffsetMeters;
			case CoverLeanLevel.Medium:
				return MediumOffsetMeters;
			case CoverLeanLevel.Deep:
				return DeepOffsetMeters;
			default:
				return 0f;
		}
	}

	public float Exposure(CoverLeanLevel _level)
	{
		switch (_level)
		{
			case CoverLeanLevel.Small:
				return SmallExposure;
			case CoverLeanLevel.Medium:
				return MediumExposure;
			case CoverLeanLevel.Deep:
				return DeepExposure;
			default:
				return 0f;
		}
	}
	#endregion
}
