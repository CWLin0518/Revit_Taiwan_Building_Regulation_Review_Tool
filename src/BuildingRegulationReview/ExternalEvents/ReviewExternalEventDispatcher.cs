using System;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Features;

namespace BuildingRegulationReview.ExternalEvents
{
    internal enum ReviewFeatureAction
    {
        Review,
        Drawing,
    }

    internal sealed class ReviewExternalEventDispatcher : IExternalEventHandler, IDisposable
    {
        private readonly ReviewFeatureRegistry _registry;
        private readonly object _syncRoot = new object();
        private ExternalEvent _externalEvent;
        private PendingRequest _pendingRequest;

        public ReviewExternalEventDispatcher(ReviewFeatureRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Initialize()
        {
            if (_externalEvent != null) throw new InvalidOperationException("ExternalEvent 已初始化。");
            _externalEvent = ExternalEvent.Create(this);
        }

        public ExternalEventRequest Raise(string featureId, ReviewFeatureAction action)
        {
            if (_externalEvent == null) throw new InvalidOperationException("ExternalEvent 尚未初始化。");
            if (!_registry.TryGet(featureId, out _)) return ExternalEventRequest.Denied;

            lock (_syncRoot)
            {
                if (_pendingRequest != null) return ExternalEventRequest.Pending;
                _pendingRequest = new PendingRequest(featureId, action);
                var request = _externalEvent.Raise();
                if (request != ExternalEventRequest.Accepted) _pendingRequest = null;
                return request;
            }
        }

        public void Execute(UIApplication application)
        {
            PendingRequest request;
            lock (_syncRoot)
            {
                request = _pendingRequest;
                _pendingRequest = null;
            }

            if (request == null || !_registry.TryGet(request.FeatureId, out var feature)) return;

            var message = string.Empty;
            var result = request.Action == ReviewFeatureAction.Review
                ? feature.RunReview(application, ref message)
                : feature.RunDrawing(application, ref message);

            if (result != Result.Failed) return;
            var title = request.Action == ReviewFeatureAction.Review
                ? feature.ReviewDialogTitle
                : feature.DrawingDialogTitle;
            TaskDialog.Show(title, string.IsNullOrWhiteSpace(message) ? "執行失敗。" : message);
        }

        public string GetName() => "建築技術規則檢討功能派送器";

        public void Dispose()
        {
            _externalEvent?.Dispose();
            _externalEvent = null;
        }

        private sealed class PendingRequest
        {
            public PendingRequest(string featureId, ReviewFeatureAction action)
            {
                FeatureId = featureId;
                Action = action;
            }

            public string FeatureId { get; }
            public ReviewFeatureAction Action { get; }
        }
    }
}
