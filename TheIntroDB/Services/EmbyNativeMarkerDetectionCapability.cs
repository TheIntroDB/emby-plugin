using System;

namespace TheIntroDB.Services
{
    public sealed class EmbyNativeMarkerDetectionCapability
    {
        public EmbyNativeMarkerDetectionCapability(
            bool supportsSingleItem,
            bool supportsExactCompletion,
            bool supportsExactOutputReceipt,
            string reason)
        {
            SupportsSingleItem = supportsSingleItem;
            SupportsExactCompletion = supportsExactCompletion;
            SupportsExactOutputReceipt = supportsExactOutputReceipt;
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public bool IsSupported =>
            SupportsSingleItem && SupportsExactCompletion && SupportsExactOutputReceipt;

        public bool SupportsSingleItem { get; }

        public bool SupportsExactCompletion { get; }

        public bool SupportsExactOutputReceipt { get; }

        public string Reason { get; }

        /// <summary>
        /// Returns the capability proven from the public Emby 4.9.1.90 plugin SDK.
        /// ITaskManager can execute registered scheduled tasks but cannot select one media item or return generated markers.
        /// BaseItem.RefreshMetadata returns only completion or ItemUpdateType and no detector run or exact marker receipt.
        /// IProviderManager.RefreshSingleItem is an awaitable metadata refresh, not a marker detector, and returns no marker receipt.
        /// ILibraryManager.GetIntros and IIntroProvider.GetIntros return intro media, not chapter-marker detection output.
        /// </summary>
        public static EmbyNativeMarkerDetectionCapability ForEmby49190PublicSdk()
        {
            return new EmbyNativeMarkerDetectionCapability(
                false,
                false,
                false,
                "The public Emby 4.9.1.90 plugin SDK has no one-item native marker detection API with exact completion and exact generated-marker output.");
        }
    }
}
