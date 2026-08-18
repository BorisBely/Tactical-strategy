using UnityEngine;

/// <summary>
/// Ordered speed modes for RTS vehicle move orders. Higher index = faster.
/// </summary>
public enum VehicleSpeedMode
{
	Slow = 0,
	Medium = 1,
	Fast = 2,
	Max = 3
}

public static class VehicleSpeedModeUtil
{
	#region Public Methods
	public static float Fraction(VehicleSpeedMode _mode)
	{
		switch (_mode)
		{
			case VehicleSpeedMode.Slow: return 0.32f;
			case VehicleSpeedMode.Medium: return 0.65f;
			case VehicleSpeedMode.Fast: return 0.85f;
			case VehicleSpeedMode.Max: return 1f;
			default: return 0.65f;
		}
	}

	public static VehicleSpeedMode Cap(VehicleSpeedMode _order, VehicleSpeedMode _ceiling)
	{
		return (VehicleSpeedMode)Mathf.Min((int)_order, (int)_ceiling);
	}

	public static VehicleSpeedMode Next(VehicleSpeedMode _mode)
	{
		int next = ((int)_mode + 1) % 4;
		return (VehicleSpeedMode)next;
	}

	public static string LabelRu(VehicleSpeedMode _mode)
	{
		switch (_mode)
		{
			case VehicleSpeedMode.Slow: return "Медл";
			case VehicleSpeedMode.Medium: return "Средн";
			case VehicleSpeedMode.Fast: return "Быстр";
			case VehicleSpeedMode.Max: return "Макс";
			default: return "Средн";
		}
	}

	public static Color PathColor(VehicleSpeedMode _mode, float _alpha)
	{
		switch (_mode)
		{
			case VehicleSpeedMode.Slow:
				return new Color(0.55f, 0.75f, 1f, _alpha);
			case VehicleSpeedMode.Medium:
				return new Color(0.85f, 0.85f, 0.35f, _alpha);
			case VehicleSpeedMode.Fast:
				return new Color(1f, 0.65f, 0.15f, _alpha);
			case VehicleSpeedMode.Max:
				return new Color(1f, 0.35f, 0.2f, _alpha);
			default:
				return new Color(0.85f, 0.85f, 0.35f, _alpha);
		}
	}

	public static float PathWidth(VehicleSpeedMode _mode)
	{
		switch (_mode)
		{
			case VehicleSpeedMode.Slow: return 0.07f;
			case VehicleSpeedMode.Medium: return 0.09f;
			case VehicleSpeedMode.Fast: return 0.11f;
			case VehicleSpeedMode.Max: return 0.13f;
			default: return 0.09f;
		}
	}
	#endregion
}
