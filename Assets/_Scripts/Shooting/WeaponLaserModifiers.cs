using UnityEngine;

/// <summary>
/// Pose-specific ЛЦУ modifiers from equipped attachments. PointAim spread/aim-time fall off with distance;
/// Aiming gets a small acquisition bonus only (no ADS precision).
/// </summary>
public static class WeaponLaserModifiers
{
	public static float GetPointAimSpreadProduct(WeaponAttachmentDefinition[] _attachments, float _distanceMeters)
	{
		return Product(_attachments, a => a.EvaluateLaserPointAimSpread(_distanceMeters));
	}

	public static float GetPointAimAimTimeProduct(WeaponAttachmentDefinition[] _attachments, float _distanceMeters)
	{
		return Product(_attachments, a => a.EvaluateLaserPointAimAimTime(_distanceMeters));
	}

	public static float GetAimingAimTimeProduct(WeaponAttachmentDefinition[] _attachments)
	{
		return Product(_attachments, a => a.EvaluateLaserAimingAimTime());
	}

	public static bool HasLaserDesignator(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return false;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType == WeaponAttachmentType.LaserDesignator)
				return true;
		}

		return false;
	}

	public static bool HasImprovedLaser(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return false;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType == WeaponAttachmentType.LaserDesignator &&
			    (a.IsImprovedLaser || RailAttachmentDistanceCurveLibrary.IsImprovedLaser(a)))
				return true;
		}

		return false;
	}

	public static WeaponAttachmentDefinition FindLaser(WeaponAttachmentDefinition[] _attachments)
	{
		if (_attachments == null)
			return null;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a != null && a.AttachmentType == WeaponAttachmentType.LaserDesignator)
				return a;
		}

		return null;
	}

	public static float GetLaserDotMaxRangeMeters(WeaponAttachmentDefinition _laser)
	{
		if (_laser == null || _laser.AttachmentType != WeaponAttachmentType.LaserDesignator)
			return 0f;
		if (_laser.LaserDotMaxRangeMeters > 0f)
			return _laser.LaserDotMaxRangeMeters;

		return c_DefaultLaserDotRangeMeters;
	}

	private const float c_DefaultLaserDotRangeMeters = 50f;

	private static float Product(
		WeaponAttachmentDefinition[] _attachments,
		System.Func<WeaponAttachmentDefinition, float> _selector)
	{
		if (_attachments == null || _attachments.Length == 0)
			return 1f;

		float product = 1f;
		bool any = false;
		for (int i = 0; i < _attachments.Length; i++)
		{
			WeaponAttachmentDefinition a = _attachments[i];
			if (a == null || a.AttachmentType != WeaponAttachmentType.LaserDesignator)
				continue;
			any = true;
			product *= Mathf.Max(0.01f, _selector(a));
		}

		return any ? product : 1f;
	}
}
