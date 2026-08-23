using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// Stage 1 N9: WeaponAutoModeSelectionUtility MATH + SIM (contract vs runtime-mirror input).
/// </summary>
public static class RecoilPlayN9AutoSelectorRunner
{
	#region Constants
	public const string M4WeaponAssetName = "Weapon_M4_ModA_1";
	public const string Ak47WeaponAssetName = "Weapon_AK47";
	public const string Ammo556AssetName = "Ammo_556x45mmNATO";
	public const string Ammo762AssetName = "Ammo_762x39mm";

	private static readonly float[] s_DistancesMeters = { 20f, 50f, 80f, 100f, 150f };
	private const float c_BoundaryMinMeters = 20f;
	private const float c_BoundaryMaxMeters = 150f;
	private const float c_BoundaryStepMeters = 5f;
	private const float c_ContextDistanceMeters = 50f;
	private const float c_HipFireContextDistanceMeters = 25f;
	private const float c_GroupDiameterEpsilon = 0.002f;
	#endregion

	#region Nested Types
	private struct SelectionSample
	{
		public string WeaponLabel;
		public float DistanceMeters;
		public WeaponFireMode FireMode;
		public WeaponAimMode AimMode;
		public float GroupDiameterMeters;
		public bool IsAcceptable;
	}

	private struct PlayPair
	{
		public WeaponAutoModeSelectionResult Contract;
		public WeaponAutoModeSelectionResult Mirror;
		public string TriageNote;
	}
	#endregion

	#region Public Methods
	public static string Run(
		WeaponDefinition _m4,
		AmmoDefinition _m4Ammo,
		WeaponDefinition _ak47,
		AmmoDefinition _ak47Ammo)
	{
		var sb = new StringBuilder(8192);
		CultureInfo culture = CultureInfo.InvariantCulture;
		float threshold = WeaponAutoModeSelectionUtility.AcceptableSpreadDiameterMeters;

		sb.AppendLine("RecoilPlayN9AutoSelector MATH + SIM_PLAY");
		sb.AppendLine("Stage 1 N9: WeaponAutoModeSelectionUtility (threshold ≈ 0.775 m). Not planner E1/E2.");
		sb.AppendLine("Representative shots: FA=5, Burst=3, Semi=1. groupHalfAngle = θ + |predicted Offset|.");
		sb.AppendLine("Threshold = " + threshold.ToString("F3", culture) + " m.");
		sb.AppendLine();

		sb.AppendLine("N9-MATH (contract builder):");
		sb.AppendLine("Weapon | Distance | PredictedMode | PredictedAim | GroupDiameter | Threshold | IsAcceptable | PASS/FAIL");
		var mathRows = new System.Collections.Generic.List<SelectionSample>(10);
		AppendMathWeapon(sb, culture, threshold, mathRows, "M4", _m4, _m4Ammo);
		AppendMathWeapon(sb, culture, threshold, mathRows, "AK-47", _ak47, _ak47Ammo);

		sb.AppendLine();
		sb.AppendLine("N9-PLAY (contract vs runtime-mirror):");
		sb.AppendLine("Weapon | Distance | PredictedFire | ActualFire | PredictedAim | ActualAim | PASS/FAIL");
		int playPass = 0;
		int playTotal = 0;
		for (int i = 0; i < mathRows.Count; i++)
		{
			SelectionSample row = mathRows[i];
			WeaponDefinition weapon = row.WeaponLabel == "M4" ? _m4 : _ak47;
			AmmoDefinition ammo = row.WeaponLabel == "M4" ? _m4Ammo : _ak47Ammo;
			PlayPair pair = EvaluatePlayPair(weapon, ammo, row.DistanceMeters);
			bool pass = pair.Contract.EffectiveFireMode == pair.Mirror.EffectiveFireMode &&
			            pair.Contract.EffectiveAimMode == pair.Mirror.EffectiveAimMode;
			if (pass)
				playPass++;
			playTotal++;
			sb.AppendLine(
				row.WeaponLabel + " | " + row.DistanceMeters.ToString("F0", culture) + " m" +
				" | " + pair.Contract.EffectiveFireMode +
				" | " + pair.Mirror.EffectiveFireMode +
				" | " + pair.Contract.EffectiveAimMode +
				" | " + pair.Mirror.EffectiveAimMode +
				" | " + (pass ? "PASS" : "FAIL"));
			if (!pass && !string.IsNullOrEmpty(pair.TriageNote))
				sb.AppendLine("  triage: " + pair.TriageNote);
		}

		sb.AppendLine();
		sb.AppendLine("N9 boundary sweep (5 m steps, observed):");
		AppendBoundary(sb, culture, "M4", _m4, _m4Ammo);
		AppendBoundary(sb, culture, "AK-47", _ak47, _ak47Ammo);

		sb.AppendLine();
		sb.AppendLine("N9 context tests:");
		AppendContextTests(sb, culture, _m4, _m4Ammo);

		sb.AppendLine();
		sb.AppendLine("Form checks:");
		int mathPass = 0;
		for (int i = 0; i < mathRows.Count; i++)
		{
			SelectionSample row = mathRows[i];
			bool determinism = CheckDeterminism(row.WeaponLabel == "M4" ? _m4 : _ak47,
				row.WeaponLabel == "M4" ? _m4Ammo : _ak47Ammo, row.DistanceMeters);
			bool acceptableOrFallback = row.IsAcceptable ||
			                            (row.FireMode == WeaponFireMode.SemiAuto &&
			                             row.AimMode == WeaponAimMode.FullAim);
			bool formulaOk = CheckGroupDiameterFormula(
				row.WeaponLabel == "M4" ? _m4 : _ak47,
				row.WeaponLabel == "M4" ? _m4Ammo : _ak47Ammo,
				row);
			AppendCheck(sb, row.WeaponLabel + " @" + row.DistanceMeters.ToString("F0", culture) + " m determinism",
				determinism);
			AppendCheck(sb, row.WeaponLabel + " @" + row.DistanceMeters.ToString("F0", culture) +
			            " m acceptable or Semi+FullAim fallback", acceptableOrFallback);
			AppendCheck(sb, row.WeaponLabel + " @" + row.DistanceMeters.ToString("F0", culture) +
			            " m groupDiameter formula", formulaOk);
			if (determinism && acceptableOrFallback && formulaOk)
				mathPass++;
		}

		AppendCheck(sb, "SIM PLAY Actual==Predicted (" + playPass + "/" + playTotal + ")", playPass == playTotal);
		AppendCheck(sb, "MATH rows PASS (" + mathPass + "/" + mathRows.Count + ")", mathPass == mathRows.Count);
		sb.AppendLine();
		sb.AppendLine("FAIL triage: 1) selector 2) context builder 3) prediction 4) runtime path 5) selector bug.");
		sb.AppendLine("Do not change AutoRecoilMultiplier or B0–B14 V/H/Rec without proven asset bug.");
		sb.AppendLine("Phase G not opened.");
		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static void AppendMathWeapon(
		StringBuilder _sb,
		CultureInfo _culture,
		float _threshold,
		System.Collections.Generic.List<SelectionSample> _rows,
		string _label,
		WeaponDefinition _weapon,
		AmmoDefinition _ammo)
	{
		for (int i = 0; i < s_DistancesMeters.Length; i++)
		{
			SelectionSample sample = SampleContract(_label, _weapon, _ammo, s_DistancesMeters[i]);
			_rows.Add(sample);
			bool rowPass = sample.IsAcceptable ||
			               (sample.FireMode == WeaponFireMode.SemiAuto && sample.AimMode == WeaponAimMode.FullAim);
			_sb.AppendLine(
				sample.WeaponLabel + " | " + sample.DistanceMeters.ToString("F0", _culture) + " m" +
				" | " + sample.FireMode +
				" | " + sample.AimMode +
				" | " + sample.GroupDiameterMeters.ToString("F3", _culture) +
				" | " + _threshold.ToString("F3", _culture) +
				" | " + sample.IsAcceptable +
				" | " + (rowPass ? "PASS" : "FAIL") +
				(rowPass && !sample.IsAcceptable ? " (NOTE fallback)" : ""));
		}
	}

	private static SelectionSample SampleContract(
		string _label,
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _distanceMeters)
	{
		var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_weapon, _ammo, _distanceMeters);
		WeaponAutoModeSelectionInput input = RecoilAutoSelectorInputBuilder.BuildContract(scenario);
		WeaponAutoModeSelectionResult result = WeaponAutoModeSelectionUtility.Select(input);
		return new SelectionSample
		{
			WeaponLabel = _label,
			DistanceMeters = _distanceMeters,
			FireMode = result.EffectiveFireMode,
			AimMode = result.EffectiveAimMode,
			GroupDiameterMeters = ComputeGroupDiameterMeters(in input, result),
			IsAcceptable = result.IsAcceptable
		};
	}

	private static PlayPair EvaluatePlayPair(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		float _distanceMeters)
	{
		var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_weapon, _ammo, _distanceMeters);
		WeaponAutoModeSelectionInput contract = RecoilAutoSelectorInputBuilder.BuildContract(scenario);
		WeaponAutoModeSelectionInput mirror = RecoilAutoSelectorInputBuilder.BuildRuntimeMirror(scenario);
		var pair = new PlayPair
		{
			Contract = WeaponAutoModeSelectionUtility.Select(contract),
			Mirror = WeaponAutoModeSelectionUtility.Select(mirror)
		};
		if (pair.Contract.EffectiveFireMode != pair.Mirror.EffectiveFireMode ||
		    pair.Contract.EffectiveAimMode != pair.Mirror.EffectiveAimMode)
		{
			pair.TriageNote = ClassifyMismatch(in contract, in mirror, in pair.Contract, in pair.Mirror);
		}

		return pair;
	}

	private static string ClassifyMismatch(
		in WeaponAutoModeSelectionInput _contract,
		in WeaponAutoModeSelectionInput _mirror,
		in WeaponAutoModeSelectionResult _contractResult,
		in WeaponAutoModeSelectionResult _mirrorResult)
	{
		if (!Mathf.Approximately(_contract.StanceKickMultiplier, _mirror.StanceKickMultiplier) ||
		    !Mathf.Approximately(_contract.PoseKickMultiplier, _mirror.PoseKickMultiplier) ||
		    !Mathf.Approximately(_contract.AccuracyInput.PoseSpreadMultiplier,
			    _mirror.AccuracyInput.PoseSpreadMultiplier))
			return "stance/pose mismatch";
		float contractDiameter = ComputeGroupDiameterMeters(in _contract, _contractResult);
		float mirrorDiameter = ComputeGroupDiameterMeters(in _mirror, _mirrorResult);
		if (Mathf.Abs(contractDiameter - mirrorDiameter) > 0.01f)
			return "prediction/groupDiameter divergence";
		return "selector or alternate path";
	}

	private static void AppendBoundary(
		StringBuilder _sb,
		CultureInfo _culture,
		string _label,
		WeaponDefinition _weapon,
		AmmoDefinition _ammo)
	{
		WeaponFireMode? lastFire = null;
		var segments = new StringBuilder();
		float segmentStart = c_BoundaryMinMeters;
		for (float d = c_BoundaryMinMeters; d <= c_BoundaryMaxMeters + 0.01f; d += c_BoundaryStepMeters)
		{
			SelectionSample sample = SampleContract(_label, _weapon, _ammo, d);
			if (lastFire.HasValue && lastFire.Value != sample.FireMode)
			{
				segments.Append("  ").Append(segmentStart.ToString("F0", _culture))
					.Append("–").Append(d.ToString("F0", _culture)).Append(" m: ")
					.Append(lastFire.Value).AppendLine();
				segmentStart = d;
			}

			lastFire = sample.FireMode;
		}

		if (lastFire.HasValue)
		{
			segments.Append("  ").Append(segmentStart.ToString("F0", _culture))
				.Append("–").Append(c_BoundaryMaxMeters.ToString("F0", _culture)).Append(" m: ")
				.Append(lastFire.Value);
		}

		_sb.AppendLine(_label + ":");
		_sb.AppendLine(segments.Length > 0 ? segments.ToString() : "  (no weapon)");
	}

	private static void AppendContextTests(
		StringBuilder _sb,
		CultureInfo _culture,
		WeaponDefinition _m4,
		AmmoDefinition _m4Ammo)
	{
		var stand = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_m4, _m4Ammo, c_ContextDistanceMeters);
		var walk = stand;
		walk.IsMoving = true;
		walk.Stance = LocomotionStance.Standing;
		WeaponAutoModeSelectionResult standResult = WeaponAutoModeSelectionUtility.Select(
			RecoilAutoSelectorInputBuilder.BuildContract(stand));
		WeaponAutoModeSelectionResult walkResult = WeaponAutoModeSelectionUtility.Select(
			RecoilAutoSelectorInputBuilder.BuildContract(walk));
		bool walkNotBetter = ModeConservatism(walkResult) >= ModeConservatism(standResult);
		_sb.AppendLine("Stand → Walk @50 m: stand=" + standResult.EffectiveFireMode + "/" +
		               standResult.EffectiveAimMode + " walk=" + walkResult.EffectiveFireMode + "/" +
		               walkResult.EffectiveAimMode + " | " + (walkNotBetter ? "PASS" : "FAIL"));

		var aiming = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(
			_m4, _m4Ammo, c_HipFireContextDistanceMeters);
		var hip = aiming;
		hip.Pose = WeaponPoseState.HipFire;
		WeaponAutoModeSelectionInput aimInput = RecoilAutoSelectorInputBuilder.BuildContract(aiming);
		WeaponAutoModeSelectionInput hipInput = RecoilAutoSelectorInputBuilder.BuildContract(hip);
		WeaponAutoModeSelectionResult aimResult = WeaponAutoModeSelectionUtility.Select(aimInput);
		WeaponAutoModeSelectionResult hipResult = WeaponAutoModeSelectionUtility.Select(hipInput);
		float aimDiameter = ComputeGroupDiameterMeters(in aimInput, aimResult);
		float hipDiameter = ComputeGroupDiameterMeters(in hipInput, hipResult);
		bool hipNotOptimistic = hipDiameter + c_GroupDiameterEpsilon >= aimDiameter;
		_sb.AppendLine("Aiming → HipFire @25 m: aimD=" + aimDiameter.ToString("F3", _culture) +
		               " hipD=" + hipDiameter.ToString("F3", _culture) + " | " +
		               (hipNotOptimistic ? "PASS" : "FAIL"));

		WeaponAutoModeSelectionResult skill0 = WeaponAutoModeSelectionUtility.Select(
			RecoilAutoSelectorInputBuilder.BuildContract(
				RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_m4, _m4Ammo, c_ContextDistanceMeters, 0f)));
		WeaponAutoModeSelectionResult skill50 = WeaponAutoModeSelectionUtility.Select(
			RecoilAutoSelectorInputBuilder.BuildContract(
				RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_m4, _m4Ammo, c_ContextDistanceMeters, 50f)));
		WeaponAutoModeSelectionResult skill100 = WeaponAutoModeSelectionUtility.Select(
			RecoilAutoSelectorInputBuilder.BuildContract(
				RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_m4, _m4Ammo, c_ContextDistanceMeters, 100f)));
		bool skillMonotone = ModeConservatism(skill100) <= ModeConservatism(skill50) &&
		                     ModeConservatism(skill50) <= ModeConservatism(skill0);
		_sb.AppendLine("RecoilControl 0/50/100 @50 m: 0=" + skill0.EffectiveFireMode + "/" + skill0.EffectiveAimMode +
		               " 50=" + skill50.EffectiveFireMode + "/" + skill50.EffectiveAimMode +
		               " 100=" + skill100.EffectiveFireMode + "/" + skill100.EffectiveAimMode +
		               " | " + (skillMonotone ? "PASS" : "FAIL"));
	}

	private static int ModeConservatism(WeaponAutoModeSelectionResult _result)
	{
		int fireRank = _result.EffectiveFireMode switch
		{
			WeaponFireMode.FullAuto => 0,
			WeaponFireMode.Burst => 1,
			_ => 2
		};
		int aimRank = _result.EffectiveAimMode switch
		{
			WeaponAimMode.SnapShot => 0,
			WeaponAimMode.QuickAim => 1,
			_ => 2
		};
		return fireRank * 10 + aimRank;
	}

	private static bool CheckDeterminism(WeaponDefinition _weapon, AmmoDefinition _ammo, float _distanceMeters)
	{
		var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(_weapon, _ammo, _distanceMeters);
		WeaponAutoModeSelectionInput input = RecoilAutoSelectorInputBuilder.BuildContract(scenario);
		WeaponAutoModeSelectionResult a = WeaponAutoModeSelectionUtility.Select(input);
		WeaponAutoModeSelectionResult b = WeaponAutoModeSelectionUtility.Select(input);
		return a.EffectiveFireMode == b.EffectiveFireMode && a.EffectiveAimMode == b.EffectiveAimMode;
	}

	private static bool CheckGroupDiameterFormula(
		WeaponDefinition _weapon,
		AmmoDefinition _ammo,
		SelectionSample _row)
	{
		var scenario = RecoilAutoSelectorInputBuilder.CreateBaselineScenario(
			_weapon, _ammo, _row.DistanceMeters);
		WeaponAutoModeSelectionInput input = RecoilAutoSelectorInputBuilder.BuildContract(scenario);
		WeaponAutoModeSelectionResult result = WeaponAutoModeSelectionUtility.Select(input);
		WeaponRecoilContext context = RecoilAutoSelectorInputBuilder.BuildRecoilContext(scenario, result.EffectiveFireMode);
		int repShot = result.EffectiveFireMode switch
		{
			WeaponFireMode.FullAuto => WeaponAutoModeSelectionUtility.RepresentativeFullAutoShotIndex,
			WeaponFireMode.Burst => WeaponAutoModeSelectionUtility.RepresentativeBurstShotIndex,
			_ => 1
		};
		float predictedOffset = WeaponRecoilMath.PredictOffsetMagnitudeBeforeShot(in context, repShot);
		float manual = WeaponRecoilMath.SpreadDiameterMeters(
			_row.DistanceMeters,
			result.AccuracyContext.HalfAngleDegrees + predictedOffset);
		return Mathf.Abs(manual - _row.GroupDiameterMeters) <= c_GroupDiameterEpsilon + 0.01f;
	}

	private static float ComputeGroupDiameterMeters(
		in WeaponAutoModeSelectionInput _input,
		in WeaponAutoModeSelectionResult _result)
	{
		return ComputeGroupDiameterMeters(in _input, _result, _result.EffectiveFireMode);
	}

	private static float ComputeGroupDiameterMeters(
		in WeaponAutoModeSelectionInput _input,
		in WeaponAutoModeSelectionResult _result,
		WeaponFireMode _fireMode)
	{
		WeaponRecoilContext context = WeaponRecoilContext.CreateFromAttachments(
			_input.AccuracyInput.WeaponDefinition,
			null,
			_fireMode);
		context.StanceKickMultiplier = _input.StanceKickMultiplier;
		context.StanceRecoveryMultiplier = _input.StanceRecoveryMultiplier;
		context.PoseKickMultiplier = _input.PoseKickMultiplier;
		context.PoseRecoveryMultiplier = _input.PoseRecoveryMultiplier;
		context.SkillKickMultiplier = _input.SkillKickMultiplier > 0f ? _input.SkillKickMultiplier : 1f;
		context.SkillRecoveryMultiplier = _input.SkillRecoveryMultiplier > 0f ? _input.SkillRecoveryMultiplier : 1f;
		int repShot = _fireMode switch
		{
			WeaponFireMode.FullAuto => WeaponAutoModeSelectionUtility.RepresentativeFullAutoShotIndex,
			WeaponFireMode.Burst => WeaponAutoModeSelectionUtility.RepresentativeBurstShotIndex,
			_ => 1
		};
		float predictedOffset = WeaponRecoilMath.PredictOffsetMagnitudeBeforeShot(in context, repShot);
		float halfAngle = _result.AccuracyContext.HalfAngleDegrees + predictedOffset;
		return WeaponRecoilMath.SpreadDiameterMeters(_input.TargetDistanceMeters, halfAngle);
	}

	private static void AppendCheck(StringBuilder _sb, string _label, bool _pass)
	{
		_sb.AppendLine("  [" + (_pass ? "PASS" : "FAIL") + "] " + _label);
	}
	#endregion
}
