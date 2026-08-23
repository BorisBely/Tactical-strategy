using System.Collections.Generic;
using UnityEngine;

/// <summary>Valid attachment set for one weapon balance row.</summary>
public readonly struct WeaponBalanceLoadout
{
	public readonly string Label;
	public readonly WeaponAttachmentDefinition[] Attachments;

	public WeaponBalanceLoadout(string _label, WeaponAttachmentDefinition[] _attachments)
	{
		Label = string.IsNullOrEmpty(_label) ? "Base" : _label;
		Attachments = _attachments ?? System.Array.Empty<WeaponAttachmentDefinition>();
	}

	public static WeaponBalanceLoadout Base => new WeaponBalanceLoadout("Base", null);
}
