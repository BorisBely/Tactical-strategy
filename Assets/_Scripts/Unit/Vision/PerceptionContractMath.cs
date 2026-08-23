using UnityEngine;

/// <summary>
/// Vision Stage 18: frozen local-knowledge laws. No new channel. No Q retune.
/// Evidence stays per-channel; derived flags never treat Sound/Shared as vision.
/// </summary>
public static class PerceptionContractMath
{
	#region Public Methods
	public static bool IsVisibleNow(PerceivedContact _contact)
	{
		return AIPerceptionSemantics.IsVisibleNow(_contact);
	}

	public static bool HasVisualAimPoint(PerceivedContact _contact)
	{
		return TargetSelectionMath.TryGetObservedAimPoint(_contact, out _);
	}

	public static PerceivedIdentity CommittedIdentity(PerceivedContact _contact)
	{
		return _contact == null ? PerceivedIdentity.Unknown : _contact.Identity;
	}

	public static PerceivedIdentity SharedIdentityEvidence(PerceivedContact _contact)
	{
		return _contact == null ? PerceivedIdentity.Unknown : _contact.SharedIdentity;
	}

	public static bool SharedConfirmsVisualIdentity()
	{
		return false;
	}

	public static bool ContactStillKnown(PerceivedContact _contact)
	{
		return _contact != null &&
		       (_contact.HasKnowledge ||
		        _contact.HasUsefulSound ||
		        _contact.HasUsefulShared ||
		        IsVisibleNow(_contact));
	}

	public static Vector3 VisualLastKnown(PerceivedContact _contact)
	{
		return _contact == null ? Vector3.zero : _contact.LastKnownPosition;
	}

	public static Vector3 SoundPosition(PerceivedContact _contact)
	{
		return _contact == null ? Vector3.zero : _contact.SoundPosition;
	}

	public static Vector3 SharedPosition(PerceivedContact _contact)
	{
		return _contact == null ? Vector3.zero : _contact.SharedPosition;
	}
	#endregion
}
