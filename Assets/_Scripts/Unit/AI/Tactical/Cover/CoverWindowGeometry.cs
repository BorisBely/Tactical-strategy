using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local inspect of one Opening for a window pane / frame. Not a scene-wide search.
/// </summary>
public interface ICoverWindowProbe
{
	bool TryInspect(CoverCandidate _opening, out CoverWindowHit _hit);
}

/// <summary>
/// Geometry of a window pane found on an Opening. Not a fire/LOS result.
/// </summary>
public struct CoverWindowHit
{
	public bool HasTransparentPane;
	public bool HasFrame;
	public Vector3 Center;
	public Vector3 Axis;
	public float Width;
}

/// <summary>
/// #13.2B.3 Window: Opening + transparent pane. One candidate. No Vision/Fire.
/// </summary>
public static class CoverWindowGeometry
{
	#region Public Methods
	public static void TagWindows(
		List<CoverCandidate> _candidates,
		ICoverWindowProbe _probe,
		CoverGenerationSettings _settings)
	{
		if (_candidates == null || _probe == null)
			return;

		_ = _settings;
		for (int i = 0; i < _candidates.Count; i++)
		{
			CoverCandidate candidate = _candidates[i];
			if (candidate == null || !candidate.OpeningValid)
				continue;
			if (!_probe.TryInspect(candidate, out CoverWindowHit hit) || !hit.HasTransparentPane)
				continue;

			candidate.WindowValid = true;
			candidate.HasTransparentPane = true;
			candidate.HasFrame = hit.HasFrame;
			candidate.WindowCenter = hit.Center;
			candidate.WindowAxis = hit.Axis.sqrMagnitude > 0.01f ? hit.Axis : candidate.OpeningAxis;
			candidate.WindowWidth = hit.Width > 0.05f ? hit.Width : candidate.OpeningWidth;
			candidate.Capabilities |= CoverCapabilities.CanFireThrough | CoverCapabilities.CanObserveThrough;
			candidate.CoverType = CoverClassifier.ResolveType(candidate);
		}
	}
	#endregion
}
