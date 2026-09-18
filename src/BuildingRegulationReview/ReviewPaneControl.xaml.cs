using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            ApplyFilter();
        }

        private static List<ReviewItem> LoadItems()
        {
            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var path = Path.Combine(directory ?? string.Empty, "Data", "review-items.json");
            if (!File.Exists(path)) return new List<ReviewItem>();
            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(List<ReviewItem>));
                return serializer.ReadObject(stream) as List<ReviewItem> ?? new List<ReviewItem>();
            }
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
            IReviewFeature feature = null;
            var hasFeature = item != null && _featureRegistry.TryGet(item.Id, out feature);
            StartReviewButton.IsEnabled = hasFeature;
            DrawReviewButton.IsEnabled = hasFeature && feature.SupportsDrawing;
            DetailPanel.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
            if (item == null) return;
            DetailTitle.Text = item.Title;
            DetailReference.Text = item.LegalReference;
            DetailDescription.Text = item.Description;
            DetailOutput.Text = item.Output;
        }

        private void StartReviewButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = ReviewItemsList.SelectedItem as ReviewItem;
            if (item == null || !_featureRegistry.TryGet(item.Id, out var feature)) return;
            var request = _eventDispatcher.Raise(item.Id, ReviewFeatureAction.Review);
            if (request != ExternalEventRequest.Accepted)
                TaskDialog.Show(feature.ReviewDialogTitle, "Revit 正在執行其他命令，請完成目前操作後再試一次。");
        }

        private void DrawReviewButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = ReviewItemsList.SelectedItem as ReviewItem;
            if (item == null || !_featureRegistry.TryGet(item.Id, out var feature) || !feature.SupportsDrawing) return;
            var request = _eventDispatcher.Raise(item.Id, ReviewFeatureAction.Drawing);
            if (request != ExternalEventRequest.Accepted)
                TaskDialog.Show(feature.DrawingDialogTitle, "Revit 正在執行其他命令，請完成目前操作後再試一次。");
        }
    }
}
