using System;

/// <summary>Frozen G outputs consumed by Phase H (Reference + Attachments).</summary>
public sealed class WeaponBalanceHInput
{
	public WeaponBalanceReport ReferenceReport { get; }
	public WeaponBalanceReport AttachmentsReport { get; }
	public DateTime GeneratedUtc { get; }

	public WeaponBalanceHInput(
		WeaponBalanceReport _referenceReport,
		WeaponBalanceReport _attachmentsReport,
		DateTime _generatedUtc)
	{
		ReferenceReport = _referenceReport;
		AttachmentsReport = _attachmentsReport;
		GeneratedUtc = _generatedUtc;
	}
}
