using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Frozen Vision contact registry. Implemented by DetectionProcessor.
/// Combat (TargetSelector / Engagement) may read this.
/// AI-0 reads <see cref="AIPerceptionFrame"/> via <see cref="AIPerceptionFrameBuilder"/>.
/// They must not own detection / identity / memory math. Vision ≠ orders / search / tactics.
/// </summary>
public interface IPerceivedContactRegistry
{
	IReadOnlyDictionary<Transform, PerceivedContact> Contacts { get; }

	bool TryGetContact(Transform _target, out PerceivedContact _contact);

	event Action ContactsChanged;
}
