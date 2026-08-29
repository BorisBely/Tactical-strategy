using System;
using UnityEngine;

/// <summary>
/// Geometric kind of a #13.2C protection record. Not a unit stance. Not a CoverType.
/// Wall = Surface. Edge = Boundary.
/// </summary>
public enum ProtectionZoneType
{
	Wall = 0,
	Edge = 1,
	Opening = 2,
	Window = 3,
	Corner = 4,
	Obstacle = 5
}

/// <summary>
/// Why this Boundary exists. Not a separate CoverType. #13.2C.11
/// </summary>
public enum ProtectionEdgeKind
{
	None = 0,
	WallEnd = 1,
	ObjectEnd = 2,
	BarrierEnd = 3,
	OpeningJamb = 4,
	RuinEdge = 5
}

/// <summary>
/// What the geometry allows. Not a unit action. #13.2C
/// </summary>
[Flags]
public enum ProtectionCapabilities
{
	None = 0,
	CanPeek = 1 << 0,
	CanFireThrough = 1 << 1,
	CanStepLeft = 1 << 2,
	CanStepRight = 1 << 3,
	CanOpen = 1 << 4,
	CanClose = 1 << 5,
	CanObserveThrough = 1 << 6
}

/// <summary>
/// Height and stance occlusion of a zone. Thresholds are prototype, not freeze.
/// </summary>
public struct ProtectionHeightProfile
{
	public float HeightMeters;
	public CoverProtectionProfile Standing;
	public CoverProtectionProfile Crouch;
	public float RearProtection;
	public float SideProtection;
}

/// <summary>
/// Scene gizmo colors for protection zones. #13.2C
/// </summary>
public static class ProtectionZoneVisual
{
	public static Color Color(ProtectionZoneType _type)
	{
		switch (_type)
		{
			case ProtectionZoneType.Edge:
				return new Color(0.15f, 0.95f, 0.65f, 1f);
			case ProtectionZoneType.Opening:
				return new Color(0.55f, 0.75f, 1f, 1f);
			case ProtectionZoneType.Window:
				return new Color(0.45f, 0.95f, 1f, 1f);
			case ProtectionZoneType.Corner:
				return new Color(1f, 0.55f, 0.15f, 1f);
			case ProtectionZoneType.Obstacle:
				return new Color(0.95f, 0.75f, 0.25f, 1f);
			default:
				return new Color(0.55f, 0.7f, 0.85f, 1f);
		}
	}

	public static string FormatLabel(int _zoneId, ProtectionZoneType _type)
	{
		return FormatLabel(_zoneId, _type, ProtectionEdgeKind.None);
	}

	public static string FormatLabel(int _zoneId, ProtectionZoneType _type, ProtectionEdgeKind _edgeKind)
	{
		if (_type == ProtectionZoneType.Wall)
			return "S" + _zoneId + " Surface";
		if (_type == ProtectionZoneType.Edge)
		{
			string kind = _edgeKind == ProtectionEdgeKind.None ? "Boundary" : _edgeKind.ToString();
			return "B" + _zoneId + " " + kind;
		}

		return "Z" + _zoneId + " " + _type;
	}
}
