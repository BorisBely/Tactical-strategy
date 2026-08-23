using System;
using System.Collections.Generic;

/// <summary>Phase H orchestration — runs frozen G presets and builds H report. No asset writes.</summary>
public static class WeaponBalanceHRunner
{
	#region Public Methods
	public static WeaponBalanceHReport Run(
		IReadOnlyList<WeaponDefinition> _referenceWeapons,
		IReadOnlyList<WeaponAttachmentDefinition> _attachmentCatalog)
	{
		WeaponBalanceReport referenceReport = WeaponBalanceRunner.Run(
			WeaponBalanceRunConfig.CreateReferencePreset(),
			_referenceWeapons,
			_attachmentCatalog,
			"Reference");

		WeaponBalanceReport attachmentsReport = WeaponBalanceRunner.Run(
			WeaponBalanceRunConfig.CreateAttachmentsPreset(),
			_referenceWeapons,
			_attachmentCatalog,
			"Attachments");

		var input = new WeaponBalanceHInput(
			referenceReport,
			attachmentsReport,
			DateTime.UtcNow);

		return WeaponBalanceHReportBuilder.Build(in input, _referenceWeapons);
	}

	public static WeaponBalanceHInput RunFrozenGInput(
		IReadOnlyList<WeaponDefinition> _referenceWeapons,
		IReadOnlyList<WeaponAttachmentDefinition> _attachmentCatalog)
	{
		WeaponBalanceReport referenceReport = WeaponBalanceRunner.Run(
			WeaponBalanceRunConfig.CreateReferencePreset(),
			_referenceWeapons,
			_attachmentCatalog,
			"Reference");

		WeaponBalanceReport attachmentsReport = WeaponBalanceRunner.Run(
			WeaponBalanceRunConfig.CreateAttachmentsPreset(),
			_referenceWeapons,
			_attachmentCatalog,
			"Attachments");

		return new WeaponBalanceHInput(referenceReport, attachmentsReport, DateTime.UtcNow);
	}
	#endregion
}
