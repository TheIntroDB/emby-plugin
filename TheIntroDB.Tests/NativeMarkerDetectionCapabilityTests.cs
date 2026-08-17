using Xunit;
using TheIntroDB.Services;

namespace TheIntroDB.Tests
{
    public class NativeMarkerDetectionCapabilityTests
    {
        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public void SupportedStatusRequiresEverySafetyCapability(
            bool supportsSingleItem,
            bool supportsExactCompletion,
            bool supportsExactOutputReceipt)
        {
            var capability = new EmbyNativeMarkerDetectionCapability(
                supportsSingleItem,
                supportsExactCompletion,
                supportsExactOutputReceipt,
                "test capability");

            Assert.False(capability.IsSupported);
        }

        [Fact]
        public void SupportedStatusIsTrueOnlyWhenEverySafetyCapabilityIsTrue()
        {
            var capability = new EmbyNativeMarkerDetectionCapability(
                true,
                true,
                true,
                "all requirements proven");

            Assert.True(capability.IsSupported);
        }

        [Fact]
        public void CurrentPublicSdkCapabilityFailsClosedWithSpecificReason()
        {
            var capability = EmbyNativeMarkerDetectionCapability.ForEmby49190PublicSdk();

            Assert.False(capability.IsSupported);
            Assert.False(capability.SupportsSingleItem);
            Assert.False(capability.SupportsExactCompletion);
            Assert.False(capability.SupportsExactOutputReceipt);
            Assert.Contains("public", capability.Reason, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exact", capability.Reason, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
