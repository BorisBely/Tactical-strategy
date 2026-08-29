using UnityEngine;

/// <summary>
/// Peek sides from cover geometry. Same wall-continue rays as #13.2 classification.
/// Left / Right are from the unit looking along the candidate normal (into the open).
/// </summary>
public static class CoverPeekGeometry
{
	#region Public Methods
	public static bool CanPeek(CoverType _type)
	{
		return _type == CoverType.Corner || _type == CoverType.Partial;
	}

	public static Vector3 RightTangent(Vector3 _normal)
	{
		Vector3 tangent = Vector3.Cross(Vector3.up, CoverOcclusionMath.PlanarNormal(_normal));
		if (tangent.sqrMagnitude < 0.0001f)
			return Vector3.right;
		return tangent.normalized;
	}

	public static CoverPeekSides Sides(
		CoverCandidate _candidate,
		ICoverOcclusionProbe _probe,
		CoverPeekSettings _settings = null)
	{
		if (_candidate == null || !CanPeek(_candidate.CoverType))
			return CoverPeekSides.None;

		if (_probe == null)
			return CoverPeekSides.Both;

		CoverPeekSettings settings = _settings ?? new CoverPeekSettings();
		CoverClassificationSettings classification = settings.Classification ?? new CoverClassificationSettings();
		Vector3 normal = CoverOcclusionMath.PlanarNormal(_candidate.Normal);
		Vector3 tangent = RightTangent(normal);
		bool wallRight = WallContinues(_candidate.Position, normal, tangent, 1f, classification, _probe);
		bool wallLeft = WallContinues(_candidate.Position, normal, tangent, -1f, classification, _probe);
		return new CoverPeekSides
		{
			Right = !wallRight,
			Left = !wallLeft
		};
	}

	public static Vector3 EyeWithoutLean(
		CoverCandidate _candidate,
		CoverStance _stance,
		CoverPeekSettings _settings)
	{
		CoverPeekSettings settings = _settings ?? new CoverPeekSettings();
		Vector3 position = _candidate != null ? _candidate.Position : Vector3.zero;
		return position + Vector3.up * settings.EyeHeight(_stance);
	}

	public static Vector3 EyeWithLean(
		CoverCandidate _candidate,
		CoverStance _stance,
		CoverPeekDirection _direction,
		CoverLeanLevel _level,
		CoverPeekSettings _settings)
	{
		Vector3 eye = EyeWithoutLean(_candidate, _stance, _settings);
		if (_candidate == null || _direction == CoverPeekDirection.None || _level == CoverLeanLevel.None)
			return eye;

		CoverPeekSettings settings = _settings ?? new CoverPeekSettings();
		Vector3 right = RightTangent(_candidate.Normal);
		float sign = _direction == CoverPeekDirection.Left ? -1f : 1f;
		return eye + right * (sign * settings.OffsetMeters(_level));
	}
	#endregion

	#region Private Methods
	private static bool WallContinues(
		Vector3 _position,
		Vector3 _normal,
		Vector3 _tangent,
		float _sign,
		CoverClassificationSettings _settings,
		ICoverOcclusionProbe _probe)
	{
		Vector3 sample = _position
		                 - _normal * 0.35f
		                 + _tangent * (_sign * _settings.CornerSpanMeters)
		                 + Vector3.up * 0.9f;
		Vector3 from = sample + _normal * 0.45f;
		Vector3 to = sample - _normal * _settings.WallProbeMeters;
		return _probe.IsBlocked(from, to);
	}
	#endregion
}
