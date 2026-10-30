using System;
using System.Diagnostics;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Features;

namespace BuildingRegulationReview.ExternalEvents
{
    /// <summary>
    /// The 檢討面板's single way into Revit's API context: a button raises (tool, action), and the action
    /// runs in <see cref="Execute"/>. One request at a time; a busy Revit is reported, not queued (DESIGN.md §2.5).
    /// </summary>
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

        public ExternalEventRequest Raise(string featureId, string actionId)
        {
            if (_externalEvent == null) throw new InvalidOperationException("ExternalEvent 尚未初始化。");
            if (!_registry.TryGetAction(featureId, actionId, out _, out _)) return ExternalEventRequest.Denied;

            lock (_syncRoot)
            {
                if (_pendingRequest != null) return ExternalEventRequest.Pending;
                _pendingRequest = new PendingRequest(featureId, actionId);
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

            if (request == null || !_registry.TryGetAction(request.FeatureId, request.ActionId, out _, out var action)) return;

            var message = string.Empty;
            Result result;
            try
            {
                result = action.Run(application, ref message);
            }
            catch (Exception exception)
            {
                // One tool's failure must not take the panel down with it (DESIGN.md §2.1).
                Trace.TraceError($"建築技術規則檢討 {request.FeatureId}/{request.ActionId} 失敗：{exception}");
                result = Result.Failed;
                message = "執行時發生錯誤：" + exception.Message;
            }

            if (result != Result.Failed) return;
            TaskDialog.Show(action.DialogTitle, string.IsNullOrWhiteSpace(message) ? "執行失敗。" : message);
        }

        public string GetName() => "建築技術規則檢討功能派送器";

        public void Dispose()
        {
            _externalEvent?.Dispose();
            _externalEvent = null;
        }

        private sealed class PendingRequest
        {
            public PendingRequest(string featureId, string actionId)
            {
                FeatureId = featureId;
                ActionId = actionId;
            }

            public string FeatureId { get; }
            public string ActionId { get; }
        }
    }
}
