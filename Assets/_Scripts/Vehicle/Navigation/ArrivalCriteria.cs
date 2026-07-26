using UnityEngine;

namespace VehicleNavigation
{
	public sealed class ArrivalCriteria
	{
		public float PositionTolerance { get; set; } = 0.6f;
		public float HeadingToleranceDeg { get; set; } = 8f;

		public bool RequireFaceHeading { get; set; }
		public float HeadingBlendStartDistance { get; set; } = 6f;
		public float HeadingBlendMaxSpeedKmh { get; set; } = 5f;

		public Vector3 TargetForward { get; set; }
		public bool HasTargetForward { get; set; }

		public static ArrivalCriteria FromRequest(NavigationRequest _request)
		{
			var criteria = new ArrivalCriteria
			{
				PositionTolerance = _request.MinArrivalDistance > 0f
					? _request.MinArrivalDistance : 0.6f,
				HeadingToleranceDeg = _request.MinArrivalHeading > 0f
					? _request.MinArrivalHeading : 8f
			};

			if (_request.FacingMode == ArrivalFacingMode.FaceHeading && _request.HasHeading)
			{
				criteria.RequireFaceHeading = true;
				criteria.HasTargetForward = true;
				criteria.TargetForward = AngleToForward(_request.HeadingYaw.Value);
			}

			return criteria;
		}

		private static Vector3 AngleToForward(float _yawDeg)
		{
			float rad = _yawDeg * Mathf.Deg2Rad;
			return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
		}
	}
}
