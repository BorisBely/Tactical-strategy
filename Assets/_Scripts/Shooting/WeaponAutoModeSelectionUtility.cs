using UnityEngine;

/// <summary>
/// Выбирает effective режимы Auto по ожидаемому диаметру рассеивания, а не по жёсткой дистанции.
/// </summary>
public static class WeaponAutoModeSelectionUtility
{
	#region Constants
	public const float HumanTargetWidthMeters = 0.60f;
	public const float RequiredHitFraction = 0.60f;
	public const int RepresentativeFullAutoShotIndex = 5;
	public const int RepresentativeBurstShotIndex = 3;
	#endregion

	#region Public Properties
	public static float AcceptableSpreadDiameterMeters =>
		HumanTargetWidthMeters / Mathf.Sqrt(RequiredHitFraction);
	#endregion

	#region Public Methods
	public static WeaponAutoModeSelectionResult Select(WeaponAutoModeSelectionInput _input)
	{
		WeaponFireMode[] fireCandidates = BuildFireCandidates(_input.SelectedFireMode);
		WeaponAimMode[] aimCandidates = BuildAimCandidates(_input.SelectedAimMode);

		for (int fireIndex = 0; fireIndex < fireCandidates.Length; fireIndex++)
		{
			WeaponFireMode fireMode = WeaponFireModeUtility.ResolveEffectiveMode(
				fireCandidates[fireIndex],
				_input.TargetDistanceMeters,
				_input.AvailableFireModes);
			if (fireMode != fireCandidates[fireIndex] && _input.SelectedFireMode != WeaponFireMode.Auto)
				continue;

			for (int aimIndex = 0; aimIndex < aimCandidates.Length; aimIndex++)
			{
				WeaponAimMode aimMode = aimCandidates[aimIndex];
				WeaponShotAccuracyContext accuracy = EvaluateCandidate(_input, fireMode, aimMode);
				WeaponAutoModeSelectionResult result = new WeaponAutoModeSelectionResult(
					fireMode,
					aimMode,
					accuracy,
					AcceptableSpreadDiameterMeters,
					accuracy.SpreadDiameterMeters <= AcceptableSpreadDiameterMeters);

				if (result.IsAcceptable)
					return result;
			}
		}

		return CreateFallbackFromCandidates(_input, fireCandidates, aimCandidates);
	}
	#endregion

	#region Private Methods
	private static WeaponShotAccuracyContext EvaluateCandidate(
		WeaponAutoModeSelectionInput _input,
		WeaponFireMode _fireMode,
		WeaponAimMode _aimMode)
	{
		WeaponShotAccuracyInput accuracyInput = _input.AccuracyInput;
		accuracyInput.SelectedFireMode = _input.SelectedFireMode;
		accuracyInput.FireMode = _fireMode;
		accuracyInput.SelectedAimMode = _input.SelectedAimMode;
		accuracyInput.AimMode = _aimMode;
		accuracyInput.AimProgress01 = WeaponAimModeUtility.GetRequiredAimProgress01(_aimMode, _input.TargetDistanceMeters);
		accuracyInput.BurstShotIndex = GetRepresentativeShotIndex(_fireMode);
		return WeaponShotAccuracyEvaluator.Evaluate(accuracyInput);
	}

	private static WeaponAutoModeSelectionResult CreateFallbackFromCandidates(
		WeaponAutoModeSelectionInput _input,
		WeaponFireMode[] _fireCandidates,
		WeaponAimMode[] _aimCandidates)
	{
		WeaponFireMode desiredFireMode = _fireCandidates != null && _fireCandidates.Length > 0
			? _fireCandidates[_fireCandidates.Length - 1]
			: WeaponFireMode.SemiAuto;
		WeaponAimMode aimMode = _aimCandidates != null && _aimCandidates.Length > 0
			? _aimCandidates[_aimCandidates.Length - 1]
			: WeaponAimMode.FullAim;
		WeaponFireMode fireMode = WeaponFireModeUtility.ResolveEffectiveMode(
			desiredFireMode,
			_input.TargetDistanceMeters,
			_input.AvailableFireModes);
		WeaponShotAccuracyContext accuracy = EvaluateCandidate(_input, fireMode, aimMode);
		return new WeaponAutoModeSelectionResult(
			fireMode,
			aimMode,
			accuracy,
			AcceptableSpreadDiameterMeters,
			accuracy.SpreadDiameterMeters <= AcceptableSpreadDiameterMeters);
	}

	private static WeaponFireMode[] BuildFireCandidates(WeaponFireMode _selectedFireMode)
	{
		if (_selectedFireMode != WeaponFireMode.Auto)
			return new[] { _selectedFireMode };

		return new[]
		{
			WeaponFireMode.FullAuto,
			WeaponFireMode.Burst,
			WeaponFireMode.SemiAuto
		};
	}

	private static WeaponAimMode[] BuildAimCandidates(WeaponAimMode _selectedAimMode)
	{
		if (_selectedAimMode != WeaponAimMode.Auto)
			return new[] { _selectedAimMode };

		return new[]
		{
			WeaponAimMode.SnapShot,
			WeaponAimMode.QuickAim,
			WeaponAimMode.FullAim
		};
	}

	private static int GetRepresentativeShotIndex(WeaponFireMode _fireMode)
	{
		switch (_fireMode)
		{
			case WeaponFireMode.FullAuto:
				return RepresentativeFullAutoShotIndex;
			case WeaponFireMode.Burst:
				return RepresentativeBurstShotIndex;
			default:
				return 1;
		}
	}
	#endregion
}

public struct WeaponAutoModeSelectionInput
{
	public WeaponShotAccuracyInput AccuracyInput;
	public WeaponFireMode SelectedFireMode;
	public WeaponAimMode SelectedAimMode;
	public WeaponFireMode[] AvailableFireModes;
	public float TargetDistanceMeters;
}

public readonly struct WeaponAutoModeSelectionResult
{
	public readonly WeaponFireMode EffectiveFireMode;
	public readonly WeaponAimMode EffectiveAimMode;
	public readonly WeaponShotAccuracyContext AccuracyContext;
	public readonly float AcceptableSpreadDiameterMeters;
	public readonly bool IsAcceptable;

	public WeaponAutoModeSelectionResult(
		WeaponFireMode _effectiveFireMode,
		WeaponAimMode _effectiveAimMode,
		WeaponShotAccuracyContext _accuracyContext,
		float _acceptableSpreadDiameterMeters,
		bool _isAcceptable)
	{
		EffectiveFireMode = _effectiveFireMode;
		EffectiveAimMode = _effectiveAimMode;
		AccuracyContext = _accuracyContext;
		AcceptableSpreadDiameterMeters = _acceptableSpreadDiameterMeters;
		IsAcceptable = _isAcceptable;
	}
}
