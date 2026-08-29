using System;
using UnityEngine;

/// <summary>
/// Shared geometric cover class. Not an individual score. #13 / #13.2B.
/// Integer values 0–4 are frozen for existing baked YAML. Standing is a legacy enum slot only;
/// bake primary types are Edge / Crouch / Opening / Window / Partial / Corner. StandingProfile is protection.
/// </summary>
public enum CoverType
{
	None = 0,
	Crouch = 1,
	Standing = 2,
	Partial = 3,
	Corner = 4,
	Edge = 5,
	Opening = 6,
	Window = 7
}

/// <summary>
/// Geometric fold of a two-surface Corner. Not a CoverType. #13.2B.4
/// </summary>
public enum CoverCornerOrientation
{
	None = 0,
	Inner = 1,
	Outer = 2
}

/// <summary>
/// Geometric capabilities of a baked position. Not unit action. Not peek/fire runtime. #13.2B.
/// </summary>
[Flags]
public enum CoverCapabilities
{
	None = 0,
	CanPeek = 1 << 0,
	CanFireThrough = 1 << 1,
	CanStepLeft = 1 << 2,
	CanStepRight = 1 << 3,
	CanStand = 1 << 4,
	CanCrouch = 1 << 5,
	CanOpen = 1 << 6,
	CanClose = 1 << 7,
	CanObserveThrough = 1 << 8
}

/// <summary>
/// Occupancy states for the #13.6 runtime board. Not stored as shared geometry truth.
/// Rank / squad / formation are not occupancy.
/// </summary>
public enum CoverOccupancy
{
	Available = 0,
	Reserved = 1,
	Occupied = 2
}

/// <summary>
/// Diagnostic slot lifecycle. Not a score. Not CoverOccupancy.
/// Board stores Available / Reserved / Occupied. Approaching and Acquired are observed, not occupancy.
/// Acquired ≠ Occupied.
/// </summary>
public enum CoverSlotPhase
{
	None = 0,
	Reserved = 1,
	Approaching = 2,
	Acquired = 3,
	Occupied = 4,
	Released = 5
}

/// <summary>
/// Editor gizmo colors for baked / generated cover types. #13.2B.
/// </summary>
public static class CoverTypeVisual
{
	public static Color Color(CoverType _type)
	{
		switch (_type)
		{
			case CoverType.Edge:
				return new Color(0.15f, 0.95f, 0.65f, 1f);
			case CoverType.Opening:
				return new Color(0.55f, 0.75f, 1f, 1f);
			case CoverType.Window:
				return new Color(0.45f, 0.95f, 1f, 1f);
			case CoverType.Crouch:
				return new Color(1f, 0.85f, 0.2f, 1f);
			case CoverType.Standing:
				return new Color(0.2f, 0.85f, 1f, 1f);
			case CoverType.Partial:
				return new Color(0.95f, 0.35f, 0.9f, 1f);
			case CoverType.Corner:
				return new Color(1f, 0.55f, 0.15f, 1f);
			default:
				return new Color(0.45f, 0.9f, 0.4f, 1f);
		}
	}

	public static void DrawGeometryAxes(
		Vector3 _position,
		Vector3 _normal,
		Vector3 _edgeDirection,
		float _openingWidth,
		Vector3 _openingAxis = default,
		Vector3 _openingCenter = default,
		bool _windowValid = false,
		Vector3 _windowCenter = default,
		Vector3 _windowAxis = default,
		float _windowWidth = 0f,
		bool _hasFrame = false,
		bool _hasTransparentPane = false,
		Vector3 _cornerFacing = default,
		Vector3 _cornerNormalA = default,
		Vector3 _cornerNormalB = default)
	{
		Vector3 origin = _position + Vector3.up * 0.05f;
		Vector3 n = _normal;
		n.y = 0f;
		if (n.sqrMagnitude > 0.01f)
		{
			n.Normalize();
			Gizmos.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
			Gizmos.DrawLine(origin, origin + n * 0.8f);
		}

		DrawCornerAxes(origin, _cornerFacing, _cornerNormalA, _cornerNormalB);

		Vector3 edge = _edgeDirection;
		edge.y = 0f;
		if (edge.sqrMagnitude > 0.01f)
		{
			edge.Normalize();
			Gizmos.color = new Color(0.15f, 0.95f, 0.65f, 1f);
			Gizmos.DrawLine(origin, origin + edge * 0.7f);
		}

		float width = _openingWidth;
		Vector3 axis = _openingAxis;
		Vector3 center = _openingCenter;
		if (_windowValid)
		{
			if (_windowWidth > 0.05f)
				width = _windowWidth;
			if (_windowAxis.sqrMagnitude > 0.01f)
				axis = _windowAxis;
			if (_windowCenter.sqrMagnitude > 0.01f || _windowCenter != default)
				center = _windowCenter;
		}

		if (width <= 0.05f)
			return;

		axis.y = 0f;
		if (axis.sqrMagnitude < 0.01f)
			axis = Vector3.Cross(Vector3.up, n.sqrMagnitude > 0.01f ? n : Vector3.forward);
		if (axis.sqrMagnitude < 0.01f)
			axis = Vector3.right;
		axis.Normalize();
		float half = width * 0.5f;
		Vector3 lineOrigin = origin;
		if (center.sqrMagnitude > 0.01f)
		{
			lineOrigin = center;
			lineOrigin.y = origin.y;
		}

		Gizmos.color = _windowValid
			? new Color(0.45f, 0.95f, 1f, 1f)
			: new Color(0.55f, 0.75f, 1f, 1f);
		Gizmos.DrawLine(lineOrigin - axis * half, lineOrigin + axis * half);
		Gizmos.DrawWireSphere(lineOrigin, 0.08f);
		if (n.sqrMagnitude > 0.01f)
			Gizmos.DrawLine(origin, origin + n * 0.35f);

		if (!_windowValid)
			return;

		if (_hasTransparentPane)
		{
			Gizmos.color = new Color(0.55f, 0.95f, 1f, 0.35f);
			Gizmos.DrawWireCube(
				lineOrigin + Vector3.up * 1.05f,
				new Vector3(Mathf.Max(0.2f, width * 0.9f), 1.1f, 0.06f));
		}

		if (_hasFrame)
		{
			Gizmos.color = new Color(0.85f, 0.55f, 0.2f, 1f);
			Vector3 frameUp = Vector3.up * 1.6f;
			Gizmos.DrawLine(lineOrigin - axis * half, lineOrigin - axis * half + frameUp);
			Gizmos.DrawLine(lineOrigin + axis * half, lineOrigin + axis * half + frameUp);
			Gizmos.DrawLine(lineOrigin - axis * half + frameUp, lineOrigin + axis * half + frameUp);
		}
	}

	private static void DrawCornerAxes(
		Vector3 _origin,
		Vector3 _facing,
		Vector3 _normalA,
		Vector3 _normalB)
	{
		Vector3 a = _normalA;
		a.y = 0f;
		if (a.sqrMagnitude > 0.01f)
		{
			a.Normalize();
			Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
			Gizmos.DrawLine(_origin, _origin + a * 0.55f);
		}

		Vector3 b = _normalB;
		b.y = 0f;
		if (b.sqrMagnitude > 0.01f)
		{
			b.Normalize();
			Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
			Gizmos.DrawLine(_origin, _origin + b * 0.55f);
		}

		Vector3 facing = _facing;
		facing.y = 0f;
		if (facing.sqrMagnitude < 0.01f)
			return;
		facing.Normalize();
		Gizmos.color = new Color(1f, 0.95f, 0.35f, 1f);
		Vector3 tip = _origin + facing * 1.1f;
		Gizmos.DrawLine(_origin, tip);
		Vector3 right = Vector3.Cross(Vector3.up, facing);
		if (right.sqrMagnitude > 0.01f)
		{
			right.Normalize();
			Gizmos.DrawLine(tip, tip - facing * 0.22f + right * 0.1f);
			Gizmos.DrawLine(tip, tip - facing * 0.22f - right * 0.1f);
		}
	}
}
