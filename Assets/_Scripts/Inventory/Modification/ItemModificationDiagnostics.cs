using System.Text;
using UnityEngine;

/// <summary>
/// Диагностика установки/снятия модулей оружия. Логи в Console по тегу [WeaponMod].
/// </summary>
public static class ItemModificationDiagnostics
{
	#region Constants
	public const string LogTag = "[WeaponMod]";
	public const string AcceptedReason = "accepted";
	#endregion

	#region Public Properties
#if UNITY_EDITOR || DEVELOPMENT_BUILD
	public static bool VerboseLogging { get; set; } = true;
#else
	public static bool VerboseLogging { get; set; }
#endif
	#endregion

	#region Public Methods
	public static void LogInstallAccepted(string _context, ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weapon, InventorySlotRuntimeData _item)
	{
		if (!VerboseLogging)
			return;

		Debug.Log(
			$"{LogTag} INSTALL OK | {_context} | {FormatSlot(_slot, _weapon)} | item={FormatItem(_item)} | {FormatAttachmentState(_weapon)}",
			GetContextObject(_weapon));
	}

	public static void LogInstallRejected(
		string _context,
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weapon,
		InventorySlotRuntimeData _item,
		string _reason)
	{
		if (!VerboseLogging)
			return;

		Debug.LogWarning(
			$"{LogTag} INSTALL REJECT | {_context} | {FormatSlot(_slot, _weapon)} | item={FormatItem(_item)} | reason={_reason} | {FormatAttachmentState(_weapon)}",
			GetContextObject(_weapon));
	}

	public static void LogClearAccepted(string _context, ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weapon, InventorySlotRuntimeData _removed)
	{
		if (!VerboseLogging)
			return;

		Debug.Log(
			$"{LogTag} CLEAR OK | {_context} | {FormatSlot(_slot, _weapon)} | removed={FormatItem(_removed)} | {FormatAttachmentState(_weapon)}",
			GetContextObject(_weapon));
	}

	public static void LogClearRejected(
		string _context,
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weapon,
		string _reason)
	{
		if (!VerboseLogging)
			return;

		Debug.LogWarning(
			$"{LogTag} CLEAR REJECT | {_context} | {FormatSlot(_slot, _weapon)} | reason={_reason} | {FormatAttachmentState(_weapon)}",
			GetContextObject(_weapon));
	}

	public static void LogFlowRejected(string _context, string _step, string _reason)
	{
		if (!VerboseLogging)
			return;

		Debug.LogWarning($"{LogTag} FLOW REJECT | {_context} | step={_step} | reason={_reason}");
	}

	public static string ExplainCanAcceptItem(
		ItemModificationSlotDescriptor _slot,
		InventorySlotRuntimeData _weaponSlot,
		InventorySlotRuntimeData _candidate)
	{
		WeaponRuntimeState weaponState = _weaponSlot.InstanceState != null ? _weaponSlot.InstanceState.WeaponState : null;
		if (weaponState == null)
			return "weapon has no WeaponRuntimeState (InstanceState missing or not a weapon)";

		if (_candidate.IsEmpty)
			return "candidate item is empty";

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
		{
			if (WeaponBuiltInMagazineUtility.IsMagazineSlotLocked(weaponState.WeaponDefinition))
				return "built-in magazine cannot be replaced";

			int slotIndex = _slot.WeaponSlotIndex == -2
				? WeaponRuntimeState.SecondaryMagazineSlotIndex
				: WeaponRuntimeState.PrimaryMagazineSlotIndex;

			return weaponState.CanAcceptMagazineItem(_candidate, slotIndex)
				? AcceptedReason
				: $"magazine incompatible (type/caliber/ammo) [slot={slotIndex}]";
		}

		if (_candidate.Definition == null)
			return "candidate has no ItemDefinition";

		WeaponAttachmentDefinition attachment = _candidate.Definition.WeaponAttachmentDefinition;
		if (attachment == null)
			return $"candidate '{FormatItemName(_candidate)}' is not an attachment item (WeaponAttachmentDefinition is null)";

		WeaponDefinition weapon = weaponState.WeaponDefinition;
		if (weapon == null)
			return "weapon WeaponDefinition is null";

		if (_slot.WeaponSlotIndex < 0)
			return $"invalid weapon slot index {_slot.WeaponSlotIndex}";

		if (!WeaponAttachmentSlotPolicy.IsModificationSlotEnabled(weapon, _slot))
			return $"slot {_slot.AttachmentSlotType} is disabled for weapon '{weapon.name}'";

		if (!WeaponAttachmentSlotPolicy.IsWeaponSlotEnabled(weapon, _slot.WeaponSlotIndex))
			return $"weapon slot index {_slot.WeaponSlotIndex} is disabled for weapon '{weapon.name}'";

		int railSocketIndex = ItemModificationUtility.ResolveRailSocketIndexForSlot(weapon, _slot);
		if (!attachment.SupportsWeapon(weapon))
			return $"attachment '{attachment.name}' not compatible with weapon '{weapon.name}' (SupportsWeapon=false, explicit={attachment.UseExplicitWeaponCompatibility})";

		if (!attachment.SupportsSlot(_slot.AttachmentSlotType))
			return $"attachment '{attachment.name}' does not support slot type {_slot.AttachmentSlotType} (RequiredSlot={attachment.RequiredSlot}, CompatibleSlots={FormatCompatibleSlots(attachment)})";

		if (!attachment.SupportsWeaponSlot(_slot.AttachmentSlotType, railSocketIndex))
		{
			if (_slot.AttachmentSlotType == WeaponAttachmentSlotType.Rail)
				return $"attachment '{attachment.name}' does not fit rail socket index {railSocketIndex} (UI: Rail {railSocketIndex + 1}); allowed rails={FormatRailIndices(attachment)}";

			return $"attachment '{attachment.name}' rejected for slot {_slot.AttachmentSlotType} railIndex={railSocketIndex}";
		}

		if (attachment.AttachmentType == WeaponAttachmentType.Optic &&
		    WeaponOpticSlotUtility.IsOpticSlotType(_slot.AttachmentSlotType) &&
		    WeaponOpticSlotUtility.CountEquippedOpticAttachments(weaponState) >= 2)
			return "weapon already has two optic mounts occupied (only one allowed)";

		return AcceptedReason;
	}

	public static string FormatAttachmentState(InventorySlotRuntimeData _weaponSlot)
	{
		WeaponRuntimeState state = _weaponSlot.InstanceState != null ? _weaponSlot.InstanceState.WeaponState : null;
		if (state == null)
			return "attachments=[]";

		WeaponAttachmentDefinition[] attachments = state.EquippedAttachments;
		ItemDefinition[] items = state.EquippedAttachmentItems;
		if (attachments == null || attachments.Length == 0)
			return "attachments=empty";

		var sb = new StringBuilder(128);
		sb.Append("attachments[");
		sb.Append(attachments.Length);
		sb.Append("]: ");
		for (int i = 0; i < attachments.Length; i++)
		{
			if (i > 0)
				sb.Append(", ");

			sb.Append(i);
			sb.Append('=');
			sb.Append(attachments[i] != null ? attachments[i].name : "null");
			if (items != null && i < items.Length && items[i] != null)
			{
				sb.Append(" (");
				sb.Append(items[i].name);
				sb.Append(')');
			}
		}

		return sb.ToString();
	}
	#endregion

	#region Private Methods
	private static string FormatSlot(ItemModificationSlotDescriptor _slot, InventorySlotRuntimeData _weapon)
	{
		WeaponDefinition weapon = _weapon.Definition != null ? _weapon.Definition.WeaponDefinition : null;
		string weaponName = weapon != null ? weapon.name : (_weapon.Definition != null ? _weapon.Definition.name : "unknown");

		if (_slot.Kind == ItemModificationSlotKind.Magazine)
			return $"weapon={weaponName}, slot={ItemModificationUtility.GetSlotLabel(_slot, weapon)} (Magazine, index={_slot.WeaponSlotIndex})";

		int railIndex = weapon != null ? ItemModificationUtility.ResolveRailSocketIndexForSlot(weapon, _slot) : -1;
		string railSuffix = _slot.AttachmentSlotType == WeaponAttachmentSlotType.Rail && railIndex >= 0
			? $", railSocket={railIndex}"
			: string.Empty;

		return $"weapon={weaponName}, slot={ItemModificationUtility.GetSlotLabel(_slot, weapon)} ({_slot.AttachmentSlotType}, index={_slot.WeaponSlotIndex}{railSuffix})";
	}

	private static string FormatItem(InventorySlotRuntimeData _item)
	{
		if (_item.IsEmpty)
			return "empty";

		string defName = _item.Definition != null ? _item.Definition.name : _item.DisplayName;
		WeaponAttachmentDefinition attachment = _item.Definition != null ? _item.Definition.WeaponAttachmentDefinition : null;
		return attachment != null ? $"{defName} / {attachment.name}" : defName;
	}

	private static string FormatItemName(InventorySlotRuntimeData _item)
	{
		if (_item.Definition != null)
			return _item.Definition.name;

		return string.IsNullOrWhiteSpace(_item.DisplayName) ? "?" : _item.DisplayName;
	}

	private static string FormatCompatibleSlots(WeaponAttachmentDefinition _attachment)
	{
		WeaponAttachmentSlotType[] slots = _attachment.CompatibleSlots;
		if (slots == null || slots.Length == 0)
			return $"fallback:{_attachment.RequiredSlot}";

		var sb = new StringBuilder();
		for (int i = 0; i < slots.Length; i++)
		{
			if (i > 0)
				sb.Append('|');
			sb.Append(slots[i]);
		}

		return sb.ToString();
	}

	private static string FormatRailIndices(WeaponAttachmentDefinition _attachment)
	{
		int[] rails = _attachment.CompatibleRailSocketIndices;
		if (rails == null || rails.Length == 0)
			return "any";

		var sb = new StringBuilder();
		for (int i = 0; i < rails.Length; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append(rails[i]);
		}

		return sb.ToString();
	}

	private static Object GetContextObject(InventorySlotRuntimeData _weapon)
	{
		return _weapon.Definition;
	}
	#endregion
}
