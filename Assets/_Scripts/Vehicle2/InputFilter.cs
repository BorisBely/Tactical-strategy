/// <summary>
/// Pure smoothing. No mode, no physics — just MoveTowards with asymmetric rates.
/// </summary>
public class InputFilter
{
	private float m_Value;
	private readonly float m_RisePerSec;
	private readonly float m_FallPerSec;

	public float Value => m_Value;

	public InputFilter(float risePerSec, float fallPerSec)
	{
		m_RisePerSec = risePerSec;
		m_FallPerSec = fallPerSec;
	}

	public float Update(float target, float dt)
	{
		float rate = target > m_Value ? m_RisePerSec : m_FallPerSec;
		m_Value = UnityEngine.Mathf.MoveTowards(m_Value, target, rate * dt);
		return m_Value;
	}

	public void Snap(float v) => m_Value = v;
}
