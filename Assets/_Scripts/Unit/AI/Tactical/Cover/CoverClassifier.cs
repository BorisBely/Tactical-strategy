using UnityEngine;

/// <summary>
/// #13.2 geometric classification. Shared potential vs candidate normal, not vs enemy E01.
/// Variant B: sample rays at head/torso/pelvis/legs. Thresholds are prototype, not freeze.
/// Lean is not classification.
/// </summary>
public sealed class CoverClassifier
{
	#region Public Methods
	public void Classify(
		CoverCandidate _candidate,
		ICoverOcclusionProbe _probe,
		CoverClassificationSettings _settings = null,
		CoverThreatFrame _frame = CoverThreatFrame.CoverBacked)
	{
		if (_candidate == null)
			return;

		CoverClassificationSettings settings = _settings ?? new CoverClassificationSettings();
		Vector3 n = CoverOcclusionMath.PlanarNormal(_candidate.Normal);
		float originSign = _frame == CoverThreatFrame.OpenSide ? 1f : -1f;
		Vector3 threatDir = n * originSign;

		CoverProtectionProfile standing = SampleStance(
			_candidate.Position,
			threatDir,
			settings,
			settings.StandingHeadMeters,
			settings.StandingTorsoMeters,
			settings.StandingPelvisMeters,
			settings.StandingLegsMeters,
			_probe);
		CoverProtectionProfile crouch = SampleStance(
			_candidate.Position,
			threatDir,
			settings,
			settings.CrouchHeadMeters,
			settings.CrouchTorsoMeters,
			settings.CrouchPelvisMeters,
			settings.CrouchLegsMeters,
			_probe);

		_candidate.StandingProfile = standing;
		_candidate.CrouchProfile = crouch;
		_candidate.StandingValid = IsStanceProtected(standing, settings.SegmentThreshold);
		_candidate.CrouchValid = IsStanceProtected(crouch, settings.SegmentThreshold);
		_candidate.PartialValid = !_candidate.StandingValid &&
		                          !_candidate.CrouchValid &&
		                          (standing.AnyProtected(settings.SegmentThreshold) ||
		                           crouch.AnyProtected(settings.SegmentThreshold));

		bool hasProtection = _candidate.StandingValid || _candidate.CrouchValid || _candidate.PartialValid;
		_candidate.CornerValid = hasProtection &&
		                         _frame == CoverThreatFrame.CoverBacked &&
		                         IsCorner(_candidate.Position, n, settings, _probe);

		FinalizeBake(_candidate);
	}

	public static CoverType ResolveType(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return CoverType.None;
		if (_candidate.WindowValid)
			return CoverType.Window;
		if (_candidate.OpeningValid)
			return CoverType.Opening;
		if (HasGeometricCorner(_candidate))
			return CoverType.Corner;
		if (_candidate.EdgeValid)
			return CoverType.Edge;
		if (_candidate.CornerValid)
			return CoverType.Corner;
		if (_candidate.CrouchValid && !_candidate.StandingValid)
			return CoverType.Crouch;
		if (_candidate.PartialValid)
			return CoverType.Partial;
		return CoverType.None;
	}

	public static void FinalizeBake(CoverCandidate _candidate)
	{
		if (_candidate == null)
			return;
		_candidate.CoverType = ResolveType(_candidate);
		if (_candidate.StandingValid)
			_candidate.Capabilities |= CoverCapabilities.CanStand;
		if (_candidate.CrouchValid)
			_candidate.Capabilities |= CoverCapabilities.CanCrouch;
	}

	public static bool IsTacticalType(CoverType _type)
	{
		switch (_type)
		{
			case CoverType.Edge:
			case CoverType.Opening:
			case CoverType.Window:
			case CoverType.Corner:
			case CoverType.Crouch:
			case CoverType.Partial:
				return true;
			default:
				return false;
		}
	}

	public static bool HasGeometricCorner(CoverCandidate _candidate)
	{
		return _candidate != null &&
		       _candidate.CornerValid &&
		       _candidate.CornerFacing.sqrMagnitude > 0.01f;
	}

	public static string FormatTypeLabel(int _candidateId, CoverType _type)
	{
		if (IsTacticalType(_type))
			return "C" + _candidateId + " " + _type;
		return "C" + _candidateId;
	}

	public static string FormatProtection(float _standing, float _crouch)
	{
		return "Protection: S=" + _standing.ToString("0.0") + " C=" + _crouch.ToString("0.0");
	}

	public static void SampleProtection(
		Vector3 _position,
		Vector3 _surfaceNormal,
		ICoverOcclusionProbe _probe,
		CoverClassificationSettings _settings,
		out CoverProtectionProfile _standing,
		out CoverProtectionProfile _crouch)
	{
		CoverClassificationSettings settings = _settings ?? new CoverClassificationSettings();
		Vector3 n = CoverOcclusionMath.PlanarNormal(_surfaceNormal);
		Vector3 threatDir = n * -1f;
		_standing = SampleStance(
			_position,
			threatDir,
			settings,
			settings.StandingHeadMeters,
			settings.StandingTorsoMeters,
			settings.StandingPelvisMeters,
			settings.StandingLegsMeters,
			_probe);
		_crouch = SampleStance(
			_position,
			threatDir,
			settings,
			settings.CrouchHeadMeters,
			settings.CrouchTorsoMeters,
			settings.CrouchPelvisMeters,
			settings.CrouchLegsMeters,
			_probe);
	}

	public static string FormatCapabilities(CoverCapabilities _capabilities)
	{
		string text = string.Empty;
		text = AppendCap(text, _capabilities, CoverCapabilities.CanPeek, "Peek");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanStepLeft, "StepL");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanStepRight, "StepR");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanOpen, "Open");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanClose, "Close");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanFireThrough, "FireThru");
		text = AppendCap(text, _capabilities, CoverCapabilities.CanObserveThrough, "ObserveThru");
		return text.Length == 0 ? "Capabilities: —" : "Capabilities: " + text;
	}
	#endregion

	#region Private Methods
	private static string AppendCap(
		string _text,
		CoverCapabilities _flags,
		CoverCapabilities _bit,
		string _name)
	{
		if ((_flags & _bit) == 0)
			return _text;
		return _text.Length == 0 ? _name : _text + " / " + _name;
	}

	private static bool IsStanceProtected(CoverProtectionProfile _profile, float _threshold)
	{
		return _profile.Head >= _threshold &&
		       _profile.Torso >= _threshold &&
		       _profile.Pelvis >= _threshold;
	}

	private static CoverProtectionProfile SampleStance(
		Vector3 _position,
		Vector3 _threatOriginDir,
		CoverClassificationSettings _settings,
		float _head,
		float _torso,
		float _pelvis,
		float _legs,
		ICoverOcclusionProbe _probe)
	{
		return new CoverProtectionProfile
		{
			Head = SampleSegment(_position, _head, _threatOriginDir, _settings, _probe),
			Torso = SampleSegment(_position, _torso, _threatOriginDir, _settings, _probe),
			Pelvis = SampleSegment(_position, _pelvis, _threatOriginDir, _settings, _probe),
			Legs = SampleSegment(_position, _legs, _threatOriginDir, _settings, _probe)
		};
	}

	private static float SampleSegment(
		Vector3 _position,
		float _height,
		Vector3 _threatOriginDir,
		CoverClassificationSettings _settings,
		ICoverOcclusionProbe _probe)
	{
		if (_probe == null)
			return 0f;

		Vector3 body = _position + Vector3.up * _height;
		Vector3 from = body + _threatOriginDir * _settings.ProbeDistanceMeters;
		return _probe.IsBlocked(from, body) ? 1f : 0f;
	}

	private static bool IsCorner(
		Vector3 _position,
		Vector3 _normal,
		CoverClassificationSettings _settings,
		ICoverOcclusionProbe _probe)
	{
		if (_probe == null)
			return false;

		Vector3 tangent = Vector3.Cross(Vector3.up, _normal);
		if (tangent.sqrMagnitude < 0.0001f)
			return false;
		tangent.Normalize();

		bool left = WallContinues(_position, _normal, tangent, 1f, _settings, _probe);
		bool right = WallContinues(_position, _normal, tangent, -1f, _settings, _probe);
		return left != right;
	}

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
