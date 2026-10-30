using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using BuildingRegulationReview.ExternalEvents;
using BuildingRegulationReview.Features;

namespace BuildingRegulationReview
{
    public partial class ReviewPaneControl : UserControl
    {
        private readonly List<ReviewItem> _allItems;
        private readonly ReviewFeatureRegistry _featureRegistry;
        private readonly ReviewExternalEventDispatcher _eventDispatcher;

        internal ReviewPaneControl(ReviewFeatureRegistry featureRegistry, ReviewExternalEventDispatcher eventDispatcher)
        {
            _featureRegistry = featureRegistry ?? throw new ArgumentNullException(nameof(featureRegistry));
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            InitializeComponent();
            _allItems = LoadItems();
            ReportUnmatchedIds();
            ApplyFilter();
        }

        private static List<ReviewItem> LoadItems()
        {
            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var path = Path.Combine(directory ?? string.Empty, "Data", "review-items.json");
            if (!File.Exists(path))
            {
                Trace.TraceWarning("建築技術規則檢討：找不到工具清單 " + path);
                return new List<ReviewItem>();
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(List<ReviewItem>));
                    return serializer.ReadObject(stream) as List<ReviewItem> ?? new List<ReviewItem>();
                }
            }
            catch (Exception exception) when (exception is IOException || exception is SerializationException ||
                                              exception is UnauthorizedAccessException)
            {
                // A broken catalog must not take the whole pane down (DESIGN.md §2.1).
                Trace.TraceError("建築技術規則檢討：工具清單讀取失敗：" + exception);
                return new List<ReviewItem>();
            }
        }

        /// <summary>Warns about tools whose ID is in only one of review-items.json and the registry (DESIGN.md §2.2).</summary>
        private void ReportUnmatchedIds()
        {
            foreach (var item in _allItems.Where(item => !_featureRegistry.TryGet(item.Id, out _)))
                Trace.TraceWarning($"建築技術規則檢討：review-items.json 的 {item.Id} 沒有對應的 IReviewFeature。");
            foreach (var id in _featureRegistry.Ids.Where(id =>
                         !_allItems.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))))
                Trace.TraceWarning($"建築技術規則檢討：功能 {id} 不在 review-items.json，面板不會顯示它。");
        }

        private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (ReviewItemsList == null || CountText == null) return;
            var keyword = SearchBox?.Text?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrEmpty(keyword)
                ? _allItems
                : _allItems.Where(x => x.SearchText.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            ReviewItemsList.ItemsSource = filtered;
            CountText.Text = $"{filtered.Count} 項";
        }

        private void ReviewItemsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = ReviewItemsList.SelectedItem as ReviewItem;
            DetailPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            ShowActions(item);
            if (item == null) return;

            DetailTitle.Text = item.Title;
            DetailReference.Text = item.LegalReference;
            DetailDescription.Text = item.Description;
            DetailSteps.Text = item.Steps ?? string.Empty;
            StepsSection.Visibility = string.IsNullOrWhiteSpace(item.Steps) ? Visibility.Collapsed : Visibility.Visible;
            DetailOutput.Text = item.Output;
        }

        /// <summary>One button per action of the selected tool, numbered in workflow order (DESIGN.md §2.3).</summary>
        private void ShowActions(ReviewItem item)
        {
            IReviewFeature feature = null;
            var hasFeature = item != null && _featureRegistry.TryGet(item.Id, out feature);
            var actions = hasFeature ? feature.Actions : (IReadOnlyList<ReviewFeatureAction>)Array.Empty<ReviewFeatureAction>();
            var numbered = actions.Count > 1;

            ActionsList.ItemsSource = actions
                .Select((action, index) => new ActionButtonItem(
                    action.Id,
                    numbered ? $"{index + 1}. {action.Label}" : action.Label,
                    action.Description))
                .ToList();
            ActionsHeader.Visibility = actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoActionText.Visibility = item != null && actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ActionButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = ReviewItemsList.SelectedItem as ReviewItem;
            var actionId = (sender as FrameworkElement)?.Tag as string;
            if (item == null || !_featureRegistry.TryGetAction(item.Id, actionId, out _, out var action)) return;

            // The pane is modeless: the action runs later in Revit's API context, never here (DESIGN.md §2.5).
            var request = _eventDispatcher.Raise(item.Id, action.Id);
            if (request == ExternalEventRequest.Accepted) return;
            TaskDialog.Show(action.DialogTitle, request == ExternalEventRequest.Denied
                ? "這個動作目前無法執行（找不到對應的功能）。"
                : "Revit 正在執行其他命令，請完成目前操作後再試一次。");
        }

        /// <summary>Public because WPF bindings only read public types.</summary>
        public sealed class ActionButtonItem
        {
            internal ActionButtonItem(string actionId, string text, string description)
            {
                ActionId = actionId;
                Text = text;
                Description = string.IsNullOrWhiteSpace(description) ? null : description;
            }

            public string ActionId { get; }
            public string Text { get; }
            public string Description { get; }
        }
    }
}
