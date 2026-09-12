using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace BuildingRegulationReview
{
    public partial class ReviewPaneControl : UserControl
    {
        private readonly List<ReviewItem> _allItems;
        private readonly ExternalEvent _article164Event;
        private readonly ExternalEvent _article164DrawingEvent;

        public ReviewPaneControl(ExternalEvent article164Event, ExternalEvent article164DrawingEvent)
        {
            _article164Event = article164Event ?? throw new ArgumentNullException(nameof(article164Event));
            _article164DrawingEvent = article164DrawingEvent ?? throw new ArgumentNullException(nameof(article164DrawingEvent));
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
            var isArticle164 = item?.Id == "article-164-road-shadow";
            StartReviewButton.IsEnabled = isArticle164;
            DrawReviewButton.IsEnabled = isArticle164;
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
            if (item?.Id != "article-164-road-shadow") return;
            var request = _article164Event.Raise();
            if (request != ExternalEventRequest.Accepted)
                TaskDialog.Show("第164條檢討", "Revit 正在執行其他命令，請完成目前操作後再試一次。");
        }

        private void DrawReviewButton_OnClick(object sender, RoutedEventArgs e)
        {
            var item = ReviewItemsList.SelectedItem as ReviewItem;
            if (item?.Id != "article-164-road-shadow") return;
            var request = _article164DrawingEvent.Raise();
            if (request != ExternalEventRequest.Accepted)
                TaskDialog.Show("第164條圖說製作", "Revit 正在執行其他命令，請完成目前操作後再試一次。");
        }
    }
}
