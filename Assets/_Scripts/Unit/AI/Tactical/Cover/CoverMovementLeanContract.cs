/// <summary>
/// #13.7 moving-lean executive contract. #14 decides when walking while leaning is useful.
/// </summary>
public enum CoverMovementLeanMode
{
	Normal = 0,
	Leaning = 1
}

/// <summary>
/// Pose to carry while moving. Not a path. Not CQB.
/// </summary>
public struct CoverMovementLeanRequest
{
	public CoverMovementLeanMode Mode;
	public CoverPeekDirection Direction;
	public CoverLeanLevel Depth;

	public static CoverMovementLeanRequest Idle => new CoverMovementLeanRequest
	{
		Mode = CoverMovementLeanMode.Normal,
		Direction = CoverPeekDirection.None,
		Depth = CoverLeanLevel.None
	};
}

/// <summary>
/// Applies a movement-lean pose through the existing lean executor. Tactical “when” is #14.
/// </summary>
public static class CoverMovementLeanContract
{
	#region Public Methods
	public static void Apply(ICoverLeanExecutor _executor, in CoverMovementLeanRequest _request)
	{
		if (_executor == null)
			return;
		if (_request.Mode == CoverMovementLeanMode.Normal || _request.Depth == CoverLeanLevel.None)
		{
			_executor.SetLean(CoverLeanLevel.None, CoverPeekDirection.None);
			return;
		}

		_executor.SetLean(_request.Depth, _request.Direction);
	}
	#endregion
}
