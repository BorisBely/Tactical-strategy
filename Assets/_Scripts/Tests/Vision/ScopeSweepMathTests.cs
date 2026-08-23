using NUnit.Framework;
using UnityEngine;

namespace Vision.Tests
{
	public sealed class ScopeSweepMathTests
	{
		#region Private Fields
		private GameObject m_Go;
		private ScopeScanController m_Scan;
		#endregion

		#region Setup
		[SetUp]
		public void SetUp()
		{
			m_Go = new GameObject("ScopeSweepMath");
			m_Scan = m_Go.AddComponent<ScopeScanController>();
			m_Scan.SetAssignedSector(0f, 4f);
			m_Scan.ResetSweep();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_Go != null)
				Object.DestroyImmediate(m_Go);
		}
		#endregion

		#region Tests
		[Test]
		public void Q_ForwardSteps_Minus4ToPlus4()
		{
			Assert.AreEqual(-4f, m_Scan.ScanYawDegrees, 0.05f);
			Assert.AreEqual(1, m_Scan.Direction);

			float now = 10f;
			Assert.IsTrue(m_Scan.Tick(0f, true, false, now));
			Assert.AreEqual(-4f, m_Scan.ScanYawDegrees, 0.15f);
			m_Scan.MarkScanEmitted(now);

			float[] expected = { -2f, 0f, 2f, 4f };
			for (int i = 0; i < expected.Length; i++)
			{
				now += 0.08f;
				Assert.IsTrue(m_Scan.Tick(0.08f, true, false, now), "step " + i);
				Assert.AreEqual(expected[i], m_Scan.ScanYawDegrees, 0.2f);
				m_Scan.MarkScanEmitted(now);
			}

			Assert.AreEqual(-1, m_Scan.Direction);
		}

		[Test]
		public void R_ReverseSteps_Plus4ToMinus4()
		{
			float now = 10f;
			m_Scan.Tick(0f, true, false, now);
			m_Scan.MarkScanEmitted(now);
			for (int i = 0; i < 4; i++)
			{
				now += 0.08f;
				m_Scan.Tick(0.08f, true, false, now);
				m_Scan.MarkScanEmitted(now);
			}

			Assert.AreEqual(4f, m_Scan.ScanYawDegrees, 0.2f);
			Assert.AreEqual(-1, m_Scan.Direction);

			float[] expected = { 2f, 0f, -2f, -4f };
			for (int i = 0; i < expected.Length; i++)
			{
				now += 0.08f;
				Assert.IsTrue(m_Scan.Tick(0.08f, true, false, now), "rev " + i);
				Assert.AreEqual(expected[i], m_Scan.ScanYawDegrees, 0.2f);
				m_Scan.MarkScanEmitted(now);
			}

			Assert.AreEqual(1, m_Scan.Direction);
		}

		[Test]
		public void Contact_DoesNotWriteYaw_StopsSweepQueries()
		{
			m_Scan.SetScanYawForTest(1.5f);
			var target = new GameObject("lockT");
			try
			{
				float now = 20f;
				m_Scan.NotifyScopeContact(true, target.transform, Vector3.forward * 10f, now);
				Assert.AreEqual(ScopeScanMode.TrackTarget, m_Scan.Mode);
				Assert.AreEqual(1.5f, m_Scan.ScanYawDegrees, 0.05f);
				Assert.IsFalse(m_Scan.Tick(0.08f, true, false, now + 0.08f));
			}
			finally
			{
				Object.DestroyImmediate(target);
			}
		}

		[Test]
		public void LostHold_ThenResumeSameYaw()
		{
			m_Scan.SetAssignedSector(0f, 60f);
			m_Scan.SetScanYawForTest(12f);
			m_Scan.SetDirectionForTest(1);
			var target = new GameObject("holdT");
			try
			{
				float now = 30f;
				m_Scan.NotifyScopeContact(true, target.transform, Vector3.forward, now);
				m_Scan.NotifyScopeContact(false, null, Vector3.zero, now);
				Assert.AreEqual(ScopeScanMode.LostHold, m_Scan.Mode);

				m_Scan.Tick(0.08f, true, false, now + 0.2f);
				Assert.AreEqual(ScopeScanMode.LostHold, m_Scan.Mode);
				Assert.AreEqual(12f, m_Scan.ScanYawDegrees, 0.05f);

				now += m_Scan.LostTargetHoldSeconds + 0.02f;
				m_Scan.Tick(0f, true, false, now);
				Assert.AreEqual(ScopeScanMode.Sweep, m_Scan.Mode);
				Assert.AreEqual(12f, m_Scan.ScanYawDegrees, 0.05f);
				Assert.AreEqual(1, m_Scan.Direction);
			}
			finally
			{
				Object.DestroyImmediate(target);
			}
		}

		[Test]
		public void Frozen_TimerDueStillRequestsScanWithoutMovingYaw()
		{
			m_Scan.SetFrozenForTest(true);
			m_Scan.SetScanYawForTest(0f);
			float now = 40f;
			m_Scan.MarkScanEmitted(now);
			Assert.IsFalse(m_Scan.Tick(0.08f, true, false, now + 0.08f));
			Assert.IsTrue(m_Scan.Tick(0.08f, true, false, now + 0.25f));
			Assert.AreEqual(0f, m_Scan.ScanYawDegrees, 0.05f);
			Assert.AreEqual(ScopeScanMode.Sweep, m_Scan.Mode);
		}

		[Test]
		public void AssignedSector_DefaultIsSixty()
		{
			var fresh = new GameObject("freshSweep");
			try
			{
				ScopeScanController scan = fresh.AddComponent<ScopeScanController>();
				scan.ResetSweep();
				Assert.AreEqual(60f, scan.AssignedSectorHalfDegrees, 0.01f);
				Assert.AreEqual(-60f, scan.ScanYawDegrees, 0.05f);
			}
			finally
			{
				Object.DestroyImmediate(fresh);
			}
		}
		#endregion
	}
}
