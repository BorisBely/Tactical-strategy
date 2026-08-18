/// <summary>
/// Single source of truth for infantry hand-IK mode. Not a MonoBehaviour.
/// Does not write transforms, aim, or recoil.
/// </summary>
public static class UnitHandIkModeResolver
{
	public struct Query
	{
		public bool TunerHandsFrozen;
		public bool MagazineLoading;
		public bool Healing;
		public bool DraggingFallen;
		public bool CarryingFallen;
		public bool GrenadeThrow;
		public bool ReloadingWeapon;
		public bool LoadingLmgBelt;
		public bool CyclingBolt;
		public bool BoltHeld;
		public bool PoseBlending;
		public bool StanceBlending;
		public bool StanceBusy;
		public bool Running;
		public bool Reacquiring;
	}

	public struct Weights
	{
		public float GripLeftDefault;
		public float GripRightDefault;
		public float RightNotReadyWeight;
		public float ReadyBlend01;
		public float RunLeft;
		public float RunRight;
	}

	public struct Result
	{
		public HandIkMode Mode;
		public HandIkIntent LeftIntent;
		public HandIkIntent RightIntent;
		public float LeftWeightTarget;
		public float RightWeightTarget;
	}

	public static Result Resolve(in Query _query, in Weights _weights)
	{
		float holdLeft = _weights.GripLeftDefault;
		float holdRight = _weights.GripRightDefault *
		                  UnityEngine.Mathf.Lerp(_weights.RightNotReadyWeight, 1f, _weights.ReadyBlend01);

		if (_query.TunerHandsFrozen)
			return Zero(HandIkMode.Frozen, HandIkIntent.FullAnimation);

		if (_query.MagazineLoading || _query.Healing || _query.DraggingFallen ||
		    _query.CarryingFallen || _query.GrenadeThrow)
			return Zero(HandIkMode.Disabled, HandIkIntent.FullAnimation);

		if (_query.ReloadingWeapon || _query.LoadingLmgBelt ||
		    (_query.CyclingBolt && !_query.BoltHeld))
			return Zero(HandIkMode.Reload, HandIkIntent.WeaponManipulation);

		if (_query.BoltHeld)
		{
			return new Result
			{
				Mode = HandIkMode.BoltHold,
				LeftIntent = HandIkIntent.WeaponHold,
				RightIntent = HandIkIntent.WeaponManipulation,
				LeftWeightTarget = holdLeft,
				RightWeightTarget = 0f
			};
		}

		if (_query.PoseBlending || _query.StanceBlending || _query.StanceBusy || _query.Reacquiring)
		{
			return new Result
			{
				Mode = HandIkMode.Transition,
				LeftIntent = HandIkIntent.WeaponHold,
				RightIntent = HandIkIntent.WeaponHold,
				LeftWeightTarget = holdLeft,
				RightWeightTarget = holdRight
			};
		}

		if (_query.Running)
		{
			return new Result
			{
				Mode = HandIkMode.SoftHold,
				LeftIntent = HandIkIntent.WeaponHold,
				RightIntent = HandIkIntent.MovementRelaxation,
				LeftWeightTarget = _weights.RunLeft,
				RightWeightTarget = _weights.RunRight
			};
		}

		return new Result
		{
			Mode = HandIkMode.Hold,
			LeftIntent = HandIkIntent.WeaponHold,
			RightIntent = HandIkIntent.WeaponHold,
			LeftWeightTarget = holdLeft,
			RightWeightTarget = holdRight
		};
	}

	private static Result Zero(HandIkMode _mode, HandIkIntent _intent)
	{
		return new Result
		{
			Mode = _mode,
			LeftIntent = _intent,
			RightIntent = _intent,
			LeftWeightTarget = 0f,
			RightWeightTarget = 0f
		};
	}
}
