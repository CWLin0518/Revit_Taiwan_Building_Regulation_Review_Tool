using System;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview.Features
{
    /// <summary>Runs one action in Revit's API context; same shape as an <c>IExternalCommand</c>.</summary>
    internal delegate Result ReviewActionRunner(UIApplication application, ref string message);

    /// <summary>One button of a tool in the 檢討面板.</summary>
    internal sealed class ReviewFeatureAction
    {
        public ReviewFeatureAction(string id, string label, string description, string dialogTitle, ReviewActionRunner run)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("動作 Id 不可空白。", nameof(id));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("動作名稱不可空白。", nameof(label));
            Id = id;
            Label = label;
            Description = description ?? string.Empty;
            DialogTitle = string.IsNullOrWhiteSpace(dialogTitle) ? label : dialogTitle;
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        /// <summary>Unique within the tool, e.g. <c>review</c>, <c>drawing</c>.</summary>
        public string Id { get; }

        /// <summary>Button text: starts with a verb and says what actually happens (DESIGN.md §2.3).</summary>
        public string Label { get; }

        /// <summary>Tooltip: what it does, what it needs first, what it changes.</summary>
        public string Description { get; }

        /// <summary>Title of the message shown when the action fails or Revit is busy.</summary>
        public string DialogTitle { get; }

        public ReviewActionRunner Run { get; }
    }
}
