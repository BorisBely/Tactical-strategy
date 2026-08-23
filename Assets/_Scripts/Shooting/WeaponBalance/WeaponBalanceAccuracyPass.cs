using UnityEngine;

public static class WeaponBalanceAccuracyPass
{
	#region Public Methods
	public static AccuracySampleResult Evaluate(in WeaponBalanceCase _case)
	{
		WeaponShotAccuracyInput input = WeaponBalanceContextFactory.CreateAccuracyInput(_case);
		float theta = WeaponShotAccuracyEvaluator.Evaluate(input).HalfAngleDegrees;
		return new AccuracySampleResult
		{
			ThetaHalfAngleDegrees = theta,
			SpreadDiameterMeters = WeaponRecoilMath.SpreadDiameterMeters(_case.DistanceMeters, theta)
		};
	}
	#endregion
}
