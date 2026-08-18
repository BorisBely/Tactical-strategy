using UnityEngine;

/// <summary>
/// Starting pose distance multipliers 0–500 m (PLAN §89).
/// Accuracy curves multiply spread; aim-time curves multiply CurrentPoseAimTime.
/// </summary>
public static class WeaponPoseDistanceCurves
{
	private struct Key
	{
		public float Meters;
		public float Value;

		public Key(float _meters, float _value)
		{
			Meters = _meters;
			Value = _value;
		}
	}

	private static readonly Key[] s_HipFireAccuracy =
	{
		new Key(0f, 1f),
		new Key(3f, 1f),
		new Key(5f, 1.10f),
		new Key(10f, 1.50f),
		new Key(15f, 2.20f),
		new Key(25f, 3.50f),
		new Key(50f, 6f),
		new Key(100f, 10f),
		new Key(500f, 16f),
	};

	private static readonly Key[] s_PointAimAccuracy =
	{
		new Key(0f, 1f),
		new Key(5f, 1f),
		new Key(10f, 1.05f),
		new Key(25f, 1.20f),
		new Key(50f, 1.45f),
		new Key(100f, 1.90f),
		new Key(200f, 3f),
		new Key(300f, 4.20f),
		new Key(500f, 7f),
	};

	private static readonly Key[] s_AimingAccuracy =
	{
		new Key(0f, 1f),
		new Key(25f, 1f),
		new Key(50f, 1.02f),
		new Key(100f, 1.05f),
		new Key(200f, 1.08f),
		new Key(300f, 1.10f),
		new Key(500f, 1.15f),
	};

	private static readonly Key[] s_HipFireAimTime =
	{
		new Key(0f, 1f),
		new Key(15f, 1f),
		new Key(50f, 1.05f),
		new Key(100f, 1.10f),
		new Key(500f, 1.20f),
	};

	private static readonly Key[] s_PointAimAimTime =
	{
		new Key(0f, 1f),
		new Key(25f, 1f),
		new Key(100f, 1.08f),
		new Key(300f, 1.15f),
		new Key(500f, 1.25f),
	};

	private static readonly Key[] s_AimingAimTime =
	{
		new Key(0f, 1f),
		new Key(25f, 1f),
		new Key(100f, 1.05f),
		new Key(300f, 1.10f),
		new Key(500f, 1.15f),
	};

	private static readonly Key[] s_PreAimAimTime =
	{
		new Key(0f, 1f),
		new Key(50f, 1.02f),
		new Key(200f, 1.08f),
		new Key(500f, 1.12f),
	};

	public static float GetAccuracyMultiplier(WeaponPoseState _pose, float _distanceMeters)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				return Evaluate(s_HipFireAccuracy, _distanceMeters);
			case WeaponPoseState.PointAim:
				return Evaluate(s_PointAimAccuracy, _distanceMeters);
			case WeaponPoseState.Aiming:
				return Evaluate(s_AimingAccuracy, _distanceMeters);
			case WeaponPoseState.PreAim:
				return Evaluate(s_AimingAccuracy, _distanceMeters);
			default:
				return 1f;
		}
	}

	public static float GetAimTimeMultiplier(WeaponPoseState _pose, float _distanceMeters)
	{
		switch (_pose)
		{
			case WeaponPoseState.HipFire:
			case WeaponPoseState.HipFireWalk:
			case WeaponPoseState.HipFireCrouchWalk:
				return Evaluate(s_HipFireAimTime, _distanceMeters);
			case WeaponPoseState.PointAim:
				return Evaluate(s_PointAimAimTime, _distanceMeters);
			case WeaponPoseState.Aiming:
				return Evaluate(s_AimingAimTime, _distanceMeters);
			case WeaponPoseState.PreAim:
				return Evaluate(s_PreAimAimTime, _distanceMeters);
			default:
				return 1f;
		}
	}

	private static float Evaluate(Key[] _keys, float _distanceMeters)
	{
		if (_keys == null || _keys.Length == 0)
			return 1f;

		float d = Mathf.Clamp(_distanceMeters, 0f, 500f);
		if (d <= _keys[0].Meters)
			return _keys[0].Value;
		if (d >= _keys[_keys.Length - 1].Meters)
			return _keys[_keys.Length - 1].Value;

		for (int i = 0; i < _keys.Length - 1; i++)
		{
			float a = _keys[i].Meters;
			float b = _keys[i + 1].Meters;
			if (d < a || d > b)
				continue;
			if (Mathf.Approximately(a, b))
				return _keys[i].Value;
			float t = (d - a) / (b - a);
			return Mathf.Lerp(_keys[i].Value, _keys[i + 1].Value, t);
		}

		return _keys[_keys.Length - 1].Value;
	}
}
