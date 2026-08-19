/// <summary>
/// Result of <see cref="UseOfForceEvaluator"/>. Allowed ≠ Fire.
/// </summary>
public readonly struct ForcePermission
{
	public readonly bool Allowed;
	public readonly ForcePermissionReason Reason;

	public ForcePermission(bool _allowed, ForcePermissionReason _reason)
	{
		Allowed = _allowed;
		Reason = _reason;
	}

	public static ForcePermission Deny(ForcePermissionReason _reason)
	{
		return new ForcePermission(false, _reason);
	}

	public static ForcePermission Allow(ForcePermissionReason _reason)
	{
		return new ForcePermission(true, _reason);
	}

	public override string ToString()
	{
		return Allowed ? "Allowed/" + Reason : "Denied/" + Reason;
	}
}
