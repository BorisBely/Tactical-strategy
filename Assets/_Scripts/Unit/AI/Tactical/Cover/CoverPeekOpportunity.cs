using UnityEngine;

/// <summary>
/// Which tangent sides can peek around the cover edge.
/// </summary>
public struct CoverPeekSides
{
	public bool Left;
	public bool Right;

	public bool Any => Left || Right;

	public static CoverPeekSides None => default;

	public static CoverPeekSides Both => new CoverPeekSides { Left = true, Right = true };

	public static CoverPeekSides OnlyLeft => new CoverPeekSides { Left = true };

	public static CoverPeekSides OnlyRight => new CoverPeekSides { Right = true };
}

/// <summary>
/// Possibility vs usefulness. Corner creates opportunity, not a lean command.
/// </summary>
public struct CoverPeekOpportunity
{
	public bool Available;
	public CoverPeekDirection Direction;
	public float ExpectedVisibilityGain;
	public float ExpectedExposure;
	public float Risk;
}

/// <summary>
/// One imagined lean pose. Not a shot.
/// </summary>
public struct CoverPeekDepthSample
{
	public bool Visible;
	public float Exposure;
	public float Risk;
}

/// <summary>
/// Debug overlay snapshot for one occupying candidate.
/// </summary>
public struct CoverPeekDebugSnapshot
{
	public int CandidateId;
	public CoverType CoverType;
	public bool VisibleWithoutLean;
	public bool LeftAvailable;
	public bool RightAvailable;
	public CoverPeekDepthSample LeftSmall;
	public CoverPeekDepthSample LeftMedium;
	public CoverPeekDepthSample LeftDeep;
	public CoverPeekDepthSample RightSmall;
	public CoverPeekDepthSample RightMedium;
	public CoverPeekDepthSample RightDeep;
	public CoverPeekDirection SelectedDirection;
	public CoverLeanLevel SelectedDepth;
}
