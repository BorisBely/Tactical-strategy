using UnityEngine;

public static class WeaponBalanceRecoilPass
{
	#region Constants
	private static readonly float[] s_PauseSeconds = { 0.2f, 0.4f, 0.8f };
	private const int c_PauseBurstShots = 5;
	#endregion

	#region Public Methods
	public static RecoilSampleResult Evaluate(in WeaponBalanceCase _case)
	{
		WeaponRecoilContext context = WeaponBalanceContextFactory.CreateRecoilContext(_case);
		var result = new RecoilSampleResult();

		WeaponRecoilKick kick1 = WeaponRecoilMath.ComputeKick(in context, 1, 0f);
		result.VerticalKickShot1 = kick1.Delta.y;
		result.HorizontalKickShot1 = kick1.Delta.x;
		result.RecoveryPerSecond = WeaponRecoilMath.ComposeRecoveryPerSecond(in context, false, true);

		result.OffsetAfter1 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 1);
		result.OffsetAfter3 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 3);
		result.OffsetAfter5 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 5);
		result.OffsetAfter8 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 8);
		result.OffsetAfter10 = WeaponRecoilMath.PredictOffsetAfterShots(in context, 10);

		result.OffsetMagAfter1 = result.OffsetAfter1.magnitude;
		result.OffsetMagAfter3 = result.OffsetAfter3.magnitude;
		result.OffsetMagAfter5 = result.OffsetAfter5.magnitude;
		result.OffsetMagAfter8 = result.OffsetAfter8.magnitude;
		result.OffsetMagAfter10 = result.OffsetAfter10.magnitude;

		result.RecoveryAfterPause02 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in context, c_PauseBurstShots, s_PauseSeconds[0]).magnitude;
		result.RecoveryAfterPause04 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in context, c_PauseBurstShots, s_PauseSeconds[1]).magnitude;
		result.RecoveryAfterPause08 = WeaponRecoilMath.PredictOffsetAfterBurstAndPause(
			in context, c_PauseBurstShots, s_PauseSeconds[2]).magnitude;

		result.DisplacementMetersAtDistance = WeaponRecoilMath.OffsetToDisplacementMeters(
			result.OffsetMagAfter5,
			_case.DistanceMeters);
		result.NetDriftPerShot = (result.OffsetMagAfter10 - result.OffsetMagAfter5) / 5f;

		float sumAbsX = 0f;
		result.MaxAbsYaw = 0f;
		for (int shot = 1; shot <= 10; shot++)
		{
			float absX = Mathf.Abs(WeaponRecoilMath.PredictOffsetAfterShots(in context, shot).x);
			sumAbsX += absX;
			result.MaxAbsYaw = Mathf.Max(result.MaxAbsYaw, absX);
		}

		result.MeanAbsYaw = sumAbsX / 10f;
		return result;
	}
	#endregion
}
