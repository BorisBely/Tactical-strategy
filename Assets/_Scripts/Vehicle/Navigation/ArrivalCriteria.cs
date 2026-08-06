using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ArrivalCriteria
	{
		/// <summary>Equivalent radius for coarse near-goal checks.</summary>
		public float PositionTolerance { get; set; } = ArrivalPositionBand.DefaultLateral;
		public float LongitudinalTolerance { get; set; } = ArrivalPositionBand.DefaultLongitudinal;
		public float LateralTolerance { get; set; } = ArrivalPositionBand.DefaultLateral;
		public float HeadingToleranceDeg { get; set; } = 5f;

		public bool RequireFaceHeading { get; set; }
		public float HeadingBlendStartDistance { get; set; } = 6f;
		public float HeadingBlendMaxSpeedKmh { get; set; } = 5f;

		public Vector3 TargetForward { get; set; }
		public bool HasTargetForward { get; set; }

		public static ArrivalCriteria FromRequest(NavigationRequest _request)
		{
			float lat = _request.MinArrivalDistance > 0f
				? _request.MinArrivalDistance
				: ArrivalPositionBand.DefaultLateral;
			var criteria = new ArrivalCriteria
			{
				LongitudinalTolerance = ArrivalPositionBand.DefaultLongitudinal,
				LateralTolerance = lat,
				PositionTolerance = ArrivalPositionBand.EquivalentRadius(
					ArrivalPositionBand.DefaultLongitudinal, lat),
				HeadingToleranceDeg = _request.MinArrivalHeading > 0f
					? _request.MinArrivalHeading : 8f
			};

			if (_request.FacingMode == ArrivalFacingMode.FaceHeading && _request.HasHeading)
			{
				criteria.RequireFaceHeading = true;
				criteria.HasTargetForward = true;
				criteria.TargetForward = Quaternion.Euler(0f, _request.HeadingYaw.Value, 0f) * Vector3.forward;
			}

			return criteria;
		}
	}
}
