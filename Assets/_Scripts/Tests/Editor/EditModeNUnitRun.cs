#if UNITY_EDITOR
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Vision.Tests.Editor
{
	/// <summary>
	/// One TestRunnerApi callback at a time so a previous menu run does not log the next suite.
	/// </summary>
	internal static class EditModeNUnitRun
	{
		private static TestRunnerApi s_Api;
		private static ICallbacks s_Active;

		public static void Execute(Filter _filter, ICallbacks _callbacks)
		{
			if (s_Api == null)
				s_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
			if (s_Active != null)
				s_Api.UnregisterCallbacks(s_Active);

			s_Active = _callbacks;
			s_Api.RegisterCallbacks(s_Active);
			s_Api.Execute(new ExecutionSettings(_filter));
		}

		public static void Release(ICallbacks _callbacks)
		{
			if (s_Api == null || s_Active != _callbacks)
				return;

			s_Api.UnregisterCallbacks(s_Active);
			s_Active = null;
		}
	}
}
#endif
