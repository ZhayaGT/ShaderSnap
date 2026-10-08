using NUnit.Framework;
using ShaderSnap.Editor;

namespace ShaderSnap.Tests
{
    /// <summary>
    /// The export size guard.
    ///
    /// These are pure arithmetic, so unlike the export tests they run without a graphics device — which is
    /// the point: the guard exists to turn a graphics-API failure into an actionable message, and it can
    /// only be exercised on a device whose limit is large enough to break. Passing the limits in as
    /// parameters is what makes that possible.
    /// </summary>
    public class ExportSizeGuardTests
    {
        const int Limit = 16384;
        const long Budget = 96L * 1024 * 1024;

        [Test]
        public void SizeAtTheDeviceLimitIsAccepted()
        {
            Assert.IsTrue(PNGExportUtility.TryValidateSize(Limit, 1000, Limit, Budget, out string error), error);
            Assert.IsNull(error);
        }

        [Test]
        public void WidthPastTheDeviceLimitIsRefused()
        {
            Assert.IsFalse(PNGExportUtility.TryValidateSize(Limit + 1, 1000, Limit, Budget, out string error));
            StringAssert.Contains("maximum texture size", error);
            StringAssert.Contains(Limit.ToString(), error);
        }

        [Test]
        public void HeightPastTheDeviceLimitIsRefused()
        {
            // The guard has to check both edges: a tall graph is as likely as a wide one.
            Assert.IsFalse(PNGExportUtility.TryValidateSize(1000, Limit + 1, Limit, Budget, out string error));
            StringAssert.Contains("maximum texture size", error);
        }

        [Test]
        public void AreaPastTheBudgetIsRefused()
        {
            // 12000 x 12000 is 144 megapixels: inside the device edge limit, outside the memory budget.
            Assert.IsFalse(PNGExportUtility.TryValidateSize(12000, 12000, Limit, Budget, out string error));
            StringAssert.Contains("budget", error);
        }

        [Test]
        public void DimensionLimitIsReportedBeforeTheBudget()
        {
            // A size that breaks both limits should name the device limit, because that is the one the
            // user cannot work around by exporting in pieces.
            Assert.IsFalse(PNGExportUtility.TryValidateSize(20000, 20000, Limit, Budget, out string error));
            StringAssert.Contains("maximum texture size", error);
        }

        [Test]
        public void WideGraphAtTwoTimesIsRefusedByTheDimensionLimit()
        {
            // The case that exposed this: the 99-node reference graph lays out 9205 units wide, so a 2x
            // export asks for 18410 pixels. It fits the memory budget comfortably and still cannot be
            // allocated, and the failure used to surface as a raw UnityException from the graphics API.
            const int wide = 18410;
            const int tall = 3912;

            Assert.Less((long)wide * tall, Budget, "the case is only interesting if the budget would allow it");
            Assert.IsFalse(PNGExportUtility.TryValidateSize(wide, tall, Limit, Budget, out string error));
            StringAssert.Contains("maximum texture size", error);
            StringAssert.Contains("Resolution Multiplier", error, "the message must say what to change");
        }

        [Test]
        public void TheDeviceReportsAUsableLimit()
        {
            Assert.Greater(PNGExportUtility.MaxTextureDimension(), 0);
            Assert.Greater(PNGExportUtility.MaxPixelBudget, 0);
        }
    }
}
