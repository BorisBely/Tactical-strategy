using UnityEngine;

/// <summary>
/// Which parameter set feeds the single VisionSystem. Not a second scanner.
/// </summary>
public enum VisionSourceKind
{
	InfantryEye = 0,
	Passenger = 1,
	Turret = 2
}

/// <summary>
/// Resolved eye / optic envelope for one observer this frame.
/// Eye is the wide search cone. Optic is a narrow sweep, never a second Knowledge.
/// </summary>
public readonly struct ResolvedVisionProfile
{
	public readonly float EyeRangeMeters;
	public readonly float EyeFovDegrees;
	public readonly float EyeHalfFovDegrees;
	public readonly float ScopeRangeMeters;
	public readonly float ScopeFovDegrees;
	public readonly float ScopeHalfFovDegrees;
	public readonly bool IsScopeActive;
	public readonly float MaxRangeMeters;
	public readonly WeaponPoseState Pose;
	public readonly float RawScopeRangeMeters;

	public ResolvedVisionProfile(
		float _eyeRangeMeters,
		float _eyeFovDegrees,
		float _scopeRangeMeters,
		float _scopeFovDegrees,
		bool _isScopeActive,
		WeaponPoseState _pose,
		float _rawScopeRangeMeters)
	{
		EyeRangeMeters = Mathf.Max(0.5f, _eyeRangeMeters);
		EyeFovDegrees = Mathf.Clamp(_eyeFovDegrees, 1f, 179f);
		EyeHalfFovDegrees = EyeFovDegrees * 0.5f;
		ScopeRangeMeters = Mathf.Max(EyeRangeMeters, _scopeRangeMeters);
		ScopeFovDegrees = Mathf.Clamp(_scopeFovDegrees, 1f, 179f);
		ScopeHalfFovDegrees = ScopeFovDegrees * 0.5f;
		IsScopeActive = _isScopeActive;
		MaxRangeMeters = IsScopeActive ? Mathf.Max(EyeRangeMeters, ScopeRangeMeters) : EyeRangeMeters;
		Pose = _pose;
		RawScopeRangeMeters = _rawScopeRangeMeters;
	}
}

/// <summary>
/// Single place for eye 150/120 and optic 150–300 / 8° contract. Does not retune Q.
/// Passenger uses the same envelope as infantry. Turret optic is data, not Aiming pose.
/// </summary>
public static class UnitVisionProfile
{
	#region Constants
	public const float BaseRangeMeters = 150f;
	public const float BaseFovDegrees = 120f;
	public const float ScopeFovDegrees = 8f;
	public const float MinScopeRangeMeters = 150f;
	public const float MaxScopeRangeMeters = 300f;
	public const float ObservationEpsilonMeters = 0.01f;
	#endregion

	#region Public Methods
	public static float ClampScopeRange(float _rawMeters)
	{
		return Mathf.Clamp(_rawMeters, MinScopeRangeMeters, MaxScopeRangeMeters);
	}

	public static bool HasMagnifiedScopeBonus(float _rawMeters)
	{
		return _rawMeters > BaseRangeMeters + ObservationEpsilonMeters;
	}

	public static bool IsWithinResolvedRange(float _distanceMeters, float _resolvedMaxRangeMeters)
	{
		return _distanceMeters <= _resolvedMaxRangeMeters + ObservationEpsilonMeters;
	}

	public static float ReadRawScopeRange(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null || _attachments.Length == 0)
			return 0f;

		float best = 0f;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition attachment = _attachments[i];
			if (attachment == null || attachment.AttachmentType != WeaponAttachmentType.Optic)
				continue;
			if (attachment.ResolvedScopeVisionRangeMeters > best)
				best = attachment.ResolvedScopeVisionRangeMeters;
		}

		return best;
	}

	public static bool ResolveTreatAsAlwaysAimed(VisionSourceKind _source, bool _passengerReady)
	{
		switch (_source)
		{
			case VisionSourceKind.Turret:
				return true;
			case VisionSourceKind.Passenger:
				return _passengerReady;
			default:
				return false;
		}
	}

	public static float ResolveAdditionalRawScope(VisionSourceKind _source, float _turretOpticMeters)
	{
		return _source == VisionSourceKind.Turret ? Mathf.Max(0f, _turretOpticMeters) : 0f;
	}

	public static ResolvedVisionProfile ResolveForSource(
		VisionSourceKind _source,
		float _eyeRangeMeters,
		float _eyeFovDegrees,
		WeaponPoseState _pose,
		WeaponAttachmentDefinition[] _attachments,
		bool _passengerReady,
		float _turretOpticMeters)
	{
		bool treatAsAlwaysAimed = ResolveTreatAsAlwaysAimed(_source, _passengerReady);
		float additionalRawScope = ResolveAdditionalRawScope(_source, _turretOpticMeters);
		return Resolve(
			_eyeRangeMeters,
			_eyeFovDegrees,
			_pose,
			_attachments,
			treatAsAlwaysAimed,
			additionalRawScope);
	}

	public static ResolvedVisionProfile Resolve(
		float _eyeRangeMeters,
		float _eyeFovDegrees,
		WeaponPoseState _pose,
		WeaponAttachmentDefinition[] _attachments,
		bool _treatAsAlwaysAimed)
	{
		return Resolve(
			_eyeRangeMeters,
			_eyeFovDegrees,
			_pose,
			_attachments,
			_treatAsAlwaysAimed,
			0f);
	}

	public static ResolvedVisionProfile Resolve(
		float _eyeRangeMeters,
		float _eyeFovDegrees,
		WeaponPoseState _pose,
		WeaponAttachmentDefinition[] _attachments,
		bool _treatAsAlwaysAimed,
		float _additionalRawScopeMeters)
	{
		float rawScope = Mathf.Max(ReadRawScopeRange(_attachments), Mathf.Max(0f, _additionalRawScopeMeters));
		bool poseAllowsScope = _treatAsAlwaysAimed || _pose == WeaponPoseState.Aiming;
		bool scopeActive = poseAllowsScope && HasMagnifiedScopeBonus(rawScope);
		float scopeRange = scopeActive ? ClampScopeRange(rawScope) : Mathf.Max(0.5f, _eyeRangeMeters);
		return new ResolvedVisionProfile(
			_eyeRangeMeters,
			_eyeFovDegrees,
			scopeRange,
			ScopeFovDegrees,
			scopeActive,
			_pose,
			rawScope);
	}
	#endregion
}
