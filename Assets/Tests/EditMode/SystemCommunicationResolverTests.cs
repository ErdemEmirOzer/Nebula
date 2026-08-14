using NUnit.Framework;
using Nebula.Extraction;

namespace Nebula.Tests.EditMode
{
    /// <summary>EditMode tests for the pure system-communication (Live/LastKnown/Disconnected) resolver (VS-010 §1/§10.7).</summary>
    public class SystemCommunicationResolverTests
    {
        [Test]
        public void Live_WhenTargetIsTheShipsOwnCurrentSystem_RegardlessOfCommand()
        {
            SystemLinkState link = SystemCommunicationResolver.Resolve("sys.A", "sys.A",
                targetHasActiveCommand: false, shipSystemHasActiveCommand: false, targetIsKnown: false, targetHasValidSnapshot: false);
            Assert.AreEqual(SystemLinkState.Live, link);
        }

        [Test]
        public void Live_WhenBothSystemsHaveActiveCommand_AndTargetIsKnown()
        {
            SystemLinkState link = SystemCommunicationResolver.Resolve("sys.B", "sys.A",
                targetHasActiveCommand: true, shipSystemHasActiveCommand: true, targetIsKnown: true, targetHasValidSnapshot: true);
            Assert.AreEqual(SystemLinkState.Live, link);
        }

        [Test]
        public void NotLive_WhenTargetIsUnknown_EvenWithBothCommandsActive()
        {
            SystemLinkState link = SystemCommunicationResolver.Resolve("sys.B", "sys.A",
                targetHasActiveCommand: true, shipSystemHasActiveCommand: true, targetIsKnown: false, targetHasValidSnapshot: true);
            Assert.AreNotEqual(SystemLinkState.Live, link);
        }

        [Test]
        public void LastKnown_WhenNotLive_ButAValidSnapshotExists()
        {
            SystemLinkState link = SystemCommunicationResolver.Resolve("sys.B", "sys.A",
                targetHasActiveCommand: false, shipSystemHasActiveCommand: true, targetIsKnown: true, targetHasValidSnapshot: true);
            Assert.AreEqual(SystemLinkState.LastKnown, link);
        }

        [Test]
        public void Disconnected_WhenNotLive_AndNoValidSnapshot()
        {
            SystemLinkState link = SystemCommunicationResolver.Resolve("sys.B", "sys.A",
                targetHasActiveCommand: false, shipSystemHasActiveCommand: false, targetIsKnown: true, targetHasValidSnapshot: false);
            Assert.AreEqual(SystemLinkState.Disconnected, link);
        }

        [Test]
        public void NoInformationLeak_LastKnownAndDisconnectedNeverClaimLive()
        {
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                    for (int k = 0; k < 2; k++)
                    {
                        bool targetCmd = a == 1, shipCmd = b == 1, known = k == 1;
                        SystemLinkState link = SystemCommunicationResolver.Resolve("sys.TARGET", "sys.SHIP",
                            targetCmd, shipCmd, known, targetHasValidSnapshot: true);
                        bool expectLive = targetCmd && shipCmd && known;
                        Assert.AreEqual(expectLive, link == SystemLinkState.Live, $"targetCmd={targetCmd} shipCmd={shipCmd} known={known}");
                    }
        }

        [Test]
        public void ResolveMany_EachSystemResolvesConsistentlyWithTheSingleResolveCall()
        {
            var systems = new[] { "sys.A", "sys.B", "sys.C" };
            var result = SystemCommunicationResolver.ResolveMany(systems, "sys.SHIP",
                hasActiveCommand: id => id == "sys.A" || id == "sys.SHIP",
                isKnown: id => id != "sys.C",
                hasValidSnapshot: id => id == "sys.B");

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(SystemLinkState.Live, result["sys.A"], "active Command both sides + known");
            Assert.AreEqual(SystemLinkState.LastKnown, result["sys.B"], "not Live, but has a valid snapshot");
            Assert.AreEqual(SystemLinkState.Disconnected, result["sys.C"], "not Live and no snapshot");
        }
    }
}
