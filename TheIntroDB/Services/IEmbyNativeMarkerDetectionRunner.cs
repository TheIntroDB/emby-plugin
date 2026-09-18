using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;

namespace TheIntroDB.Services
{
    public interface IEmbyNativeMarkerDetectionRunner
    {
        EmbyNativeMarkerDetectionCapability Capability { get; }

        Task<NativeMarkerDetectionRunResult> DetectAsync(long itemInternalId, CancellationToken cancellationToken);
    }

    public sealed class NativeMarkerDetectionRunResult
    {
        public NativeMarkerDetectionRunResult(string runId, IReadOnlyList<NativeDetectedMarker> generatedMarkers)
        {
            RunId = runId;
            GeneratedMarkers = generatedMarkers;
        }

        public string RunId { get; }

        public IReadOnlyList<NativeDetectedMarker> GeneratedMarkers { get; }
    }

    public sealed class NativeDetectedMarker
    {
        public NativeDetectedMarker(MarkerType markerType, long startPositionTicks)
        {
            MarkerType = markerType;
            StartPositionTicks = startPositionTicks;
        }

        public MarkerType MarkerType { get; }

        public long StartPositionTicks { get; }
    }
}
