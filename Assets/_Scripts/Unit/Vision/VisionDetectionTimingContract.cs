using UnityEngine;

/// <summary>
/// Stage 6 detection-time contract. Observation / FOV geometry stay on Stage 5.
/// These bands apply to full / static / center-FOV PerceivedContact Detected times.
/// </summary>
public static class VisionDetectionTimingContract
{
	#region Constants
	public const float EyeRangeMeters = 150f;
	public const float OpticRangeMeters = 300f;
	public const float RelativeAbsSeconds = 0.15f;
	public const float RelativeRel = 0.15f;
	public const float PlaySlackSeconds = 0.20f;
	#endregion

	#region Nested
	public readonly struct TimingAnchor
	{
		public readonly string Id;
		public readonly bool Optic;
		public readonly float DistanceMeters;
		public readonly float MinDetectedSeconds;
		public readonly float MaxDetectedSeconds;
		public readonly int Repeats;

		public TimingAnchor(
			string _id,
			bool _optic,
			float _distanceMeters,
			float _minDetectedSeconds,
			float _maxDetectedSeconds,
			int _repeats)
		{
			Id = _id;
			Optic = _optic;
			DistanceMeters = _distanceMeters;
			MinDetectedSeconds = _minDetectedSeconds;
			MaxDetectedSeconds = _maxDetectedSeconds;
			Repeats = _repeats;
		}

		public float NormalizedT =>
			DistanceMeters / (Optic ? OpticRangeMeters : EyeRangeMeters);

		public float MaxDetectedWithSlack => MaxDetectedSeconds + PlaySlackSeconds;
	}
	#endregion

	#region Anchors
	/// <summary>
	/// Full / static / center Detected bands. Eye 50 m uses a slightly wider near-band
	/// because DistanceCurve is still high at t≈0.33 (plan 50–80 m 0.5–0.8 с is the 75 m cell).
	/// </summary>
	public static TimingAnchor[] FullStaticCenterAnchors =>
		new[]
		{
			new TimingAnchor("Eye25", false, 25f, 0.30f, 0.50f, 5),
			new TimingAnchor("Eye50", false, 50f, 0.35f, 0.70f, 5),
			new TimingAnchor("Eye75", false, 75f, 0.50f, 0.80f, 20),
			new TimingAnchor("Eye100", false, 100f, 0.80f, 1.20f, 5),
			new TimingAnchor("Eye140", false, 140f, 2.00f, 3.20f, 5),
			new TimingAnchor("Eye149", false, 149f, 3.00f, 4.50f, 20),
			new TimingAnchor("Optic150", true, 150f, 0.50f, 0.80f, 5),
			new TimingAnchor("Optic200", true, 200f, 0.80f, 1.20f, 5),
			new TimingAnchor("Optic225", true, 225f, 1.00f, 1.50f, 20),
			new TimingAnchor("Optic250", true, 250f, 1.30f, 2.00f, 5),
			new TimingAnchor("Optic275", true, 275f, 2.00f, 3.20f, 5),
			new TimingAnchor("Optic299", true, 299f, 3.00f, 4.50f, 5)
		};

	public static readonly (string EyeId, string OpticId)[] RelativePairs =
	{
		("Eye75", "Optic150"),
		("Eye100", "Optic200"),
		("Eye140", "Optic275"),
		("Eye149", "Optic299")
	};
	#endregion

	#region Public Methods
	public static float NormalizedDistance(float _distanceMeters, bool _optic)
	{
		float range = _optic ? OpticRangeMeters : EyeRangeMeters;
		return Mathf.Max(0f, _distanceMeters) / Mathf.Max(0.5f, range);
	}

	public static float FullStaticCenterQuality(float _distanceMeters, bool _optic)
	{
		float range = _optic ? OpticRangeMeters : EyeRangeMeters;
		float d = DetectionQualityMath.DistanceFactor(_distanceMeters, range);
		float f = DetectionQualityMath.FovFactor(0f);
		float e = 1f;
		float m = DetectionQualityMath.MovementFactor(0f);
		return DetectionQualityMath.VisibilityQuality(d, f, e, m);
	}

	public static float EstimateDetectTimeSeconds(float _quality)
	{
		return DetectionQualityMath.EstimateDetectTimeSeconds(_quality);
	}

	public static bool FitsBand(float _seconds, float _minInclusive, float _maxInclusive)
	{
		if (_seconds < 0f)
			return false;
		return _seconds + 1e-4f >= _minInclusive && _seconds <= _maxInclusive + 1e-4f;
	}

	public static float RelativeToleranceSeconds(float _a, float _b)
	{
		float min = Mathf.Min(_a, _b);
		if (min < 0f)
			return RelativeAbsSeconds;
		return Mathf.Max(RelativeAbsSeconds, RelativeRel * min);
	}

	public static bool RelativeTimesMatch(float _a, float _b)
	{
		if (_a < 0f || _b < 0f)
			return false;
		return Mathf.Abs(_a - _b) <= RelativeToleranceSeconds(_a, _b) + 1e-4f;
	}

	public static bool TryFindAnchor(string _id, out TimingAnchor _anchor)
	{
		TimingAnchor[] anchors = FullStaticCenterAnchors;
		for (int i = 0; i < anchors.Length; i++)
		{
			if (anchors[i].Id == _id)
			{
				_anchor = anchors[i];
				return true;
			}
		}

		_anchor = default;
		return false;
	}
	#endregion
}
