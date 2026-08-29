/// <summary>
/// Adapter onto <see cref="UnitSpineLean"/>. Does not own bones, smoothing, or a second lean state.
/// </summary>
public sealed class UnitSpineLeanExecutor : ICoverLeanExecutor
{
	#region Private Fields
	private readonly UnitSpineLean m_Spine;
	#endregion

	#region Public Constructors
	public UnitSpineLeanExecutor(UnitSpineLean _spine)
	{
		m_Spine = _spine;
	}
	#endregion

	#region Public Methods
	public void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction)
	{
		if (m_Spine == null)
			return;
		m_Spine.SetLeanLevel(CoverLeanMapping.ToSpineLevel(_level), CoverLeanMapping.ToSpineSide(_direction));
	}
	#endregion
}
