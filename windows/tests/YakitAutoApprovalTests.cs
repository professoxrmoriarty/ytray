using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YTray.Core;
using YTray.Models;

namespace YTray.Tests
{
    [TestClass]
    public class YakitAutoApprovalTests
    {
        [TestMethod]
        public void LaunchApprovalIsShortLivedAndBoundToOnePairingRequest()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ytray-yakit-approval-" + Guid.NewGuid());
            try
            {
                using var store = new InstanceStore(directory, false, false);
                var id = Guid.NewGuid();
                var now = DateTime.UtcNow;
                store.Instances.Add(new BrowserInstance { Id = id, Status = InstanceStatus.Running });
                store.RememberYakitLaunch(id, now);

                Assert.IsTrue(store.TryClaimYakitLaunch(id, "request-a", out _, now));
                Assert.IsTrue(store.TryClaimYakitLaunch(id, "request-a", out _, now));
                Assert.IsFalse(store.TryClaimYakitLaunch(id, "request-b", out var claimedReason, now));
                Assert.AreEqual("already_claimed", claimedReason);

                var stoppedID = Guid.NewGuid();
                store.Instances.Add(new BrowserInstance { Id = stoppedID, Status = InstanceStatus.Stopped });
                store.RememberYakitLaunch(stoppedID, now);
                Assert.IsFalse(store.TryClaimYakitLaunch(stoppedID, "request-stopped", out var stoppedReason, now));
                Assert.AreEqual("not_running", stoppedReason);

                var expiredID = Guid.NewGuid();
                store.Instances.Add(new BrowserInstance { Id = expiredID, Status = InstanceStatus.Running });
                store.RememberYakitLaunch(expiredID, now);
                Assert.IsFalse(store.TryClaimYakitLaunch(expiredID, "request-c", out var expiredReason,
                    now.AddMinutes(3)));
                Assert.AreEqual("expired", expiredReason);

                store.SetAutoConnectYakit(false);
                Assert.IsFalse(store.TryClaimYakitLaunch(id, "request-a", out var disabledReason, now));
                Assert.AreEqual("disabled", disabledReason);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
