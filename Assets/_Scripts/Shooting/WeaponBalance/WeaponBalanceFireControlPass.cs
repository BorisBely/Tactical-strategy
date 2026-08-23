using UnityEngine;

public static class WeaponBalanceFireControlPass
{
	#region Public Methods
	public static FireControlSampleResult Evaluate(
		in WeaponBalanceCase _case,
		WeaponBalanceRunConfig _config)
	{
		var result = new FireControlSampleResult();
		if (_case.Weapon == null)
			return result;

		if (_config != null && _config.EvaluateAuto)
			EvaluateAuto(in _case, ref result);

		if (_config != null && _config.EvaluateDiscipline)
			EvaluateDiscipline(in _case, ref result);

		return result;
	}
	#endregion

	#region Private Methods
	private static void EvaluateAuto(in WeaponBalanceCase _case, ref FireControlSampleResult _result)
	{
		RecoilAutoSelectorInputBuilder.Scenario scenario =
			WeaponBalanceContextFactory.CreateSelectorScenario(_case);
		WeaponAutoModeSelectionInput input = RecoilAutoSelectorInputBuilder.BuildContract(scenario);
		WeaponAutoModeSelectionResult selection = WeaponAutoModeSelectionUtility.Select(input);
		_result.SelectedAutoFireMode = selection.EffectiveFireMode;
		_result.SelectedAutoAimMode = selection.EffectiveAimMode;
		_result.AutoIsAcceptable = selection.IsAcceptable;

		WeaponRecoilContext context = WeaponBalanceContextFactory.CreateRecoilContext(_case);
		int repShot = selection.EffectiveFireMode switch
		{
			WeaponFireMode.FullAuto => WeaponAutoModeSelectionUtility.RepresentativeFullAutoShotIndex,
			WeaponFireMode.Burst => WeaponAutoModeSelectionUtility.RepresentativeBurstShotIndex,
			_ => 1
		};
		float predictedOffset = WeaponRecoilMath.PredictOffsetMagnitudeBeforeShot(in context, repShot);
		float halfAngle = selection.AccuracyContext.HalfAngleDegrees + predictedOffset;
		_result.PredictedGroupDiameterMeters =
			WeaponRecoilMath.SpreadDiameterMeters(_case.DistanceMeters, halfAngle);
	}

	private static void EvaluateDiscipline(in WeaponBalanceCase _case, ref FireControlSampleResult _result)
	{
		WeaponFireDisciplinePlan plan = WeaponFireDisciplinePlanner.CreatePlan(
			_case.Weapon,
			_case.FireMode,
			WeaponFireDisciplineMode.Auto,
			_case.DistanceMeters,
			null,
			null,
			null,
			true,
			_case.Attachments);

		_result.PlannerEffectiveFireMode = plan.EffectiveFireMode;
		_result.PlannerSeriesLength = plan.SeriesShotCount;

		WeaponRecoilContext context = WeaponBalanceContextFactory.CreateRecoilContext(_case);
		context.FireMode = plan.EffectiveFireMode;
		float offsetDeg = WeaponRecoilMath.PredictOffsetMagnitudeAfterShots(
			in context,
			plan.SeriesShotCount);
		_result.PlannerDisplacementMeters =
			WeaponRecoilMath.OffsetToDisplacementMeters(offsetDeg, _case.DistanceMeters);
		_result.PlannerResidualOffsetAfterPause = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in context,
			5,
			0.4f).magnitude;
	}
	#endregion
}
