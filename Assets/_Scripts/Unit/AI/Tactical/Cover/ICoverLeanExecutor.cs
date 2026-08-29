/// <summary>
/// Applies a tactical lean request to the existing spine lean. Not a new LeanController.
/// </summary>
public interface ICoverLeanExecutor
{
	void SetLean(CoverLeanLevel _level, CoverPeekDirection _direction);
}
