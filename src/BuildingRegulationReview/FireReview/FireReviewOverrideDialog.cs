using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.FireReview
{
    internal enum OverrideAction
    {
        Override,
        Reconfirm,
        Withdraw
    }

    /// <summary>
    /// Asks for what spec 11.8 requires of a manual override: the status put in place (for a new
    /// override), a reason — always — and an optional comment. The operator and the time are taken
    /// by the caller, never typed.
    /// </summary>
    internal sealed class FireReviewOverrideDialog : Window
    {
        private readonly ComboBox _status;
        private readonly TextBox _reason;
        private readonly TextBox _comment;
        private readonly TextBlock _error;
        private readonly OverrideAction _action;

        private FireReviewOverrideDialog(OverrideAction action, ReviewStatus computed, ReviewStatus? current, string currentReason)
        {
            _action = action;
            Title = action == OverrideAction.Override ? "人工覆寫" : action == OverrideAction.Reconfirm ? "重新確認人工覆寫" : "撤回人工覆寫";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = $"計算結果：{ReviewStatusText.Label(computed)}" +
                       (current.HasValue ? $"；目前覆寫：{ReviewStatusText.Label(current.Value)}" : string.Empty),
                Margin = new Thickness(0, 0, 0, 8)
            });

            _status = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            if (action == OverrideAction.Override)
            {
                panel.Children.Add(new TextBlock { Text = "覆寫為：" });
                foreach (var status in Enum.GetValues(typeof(ReviewStatus)).Cast<ReviewStatus>()
                             .Where(s => s != ReviewStatus.NotRun && s != computed))
                {
                    _status.Items.Add(new ComboBoxItem { Content = ReviewStatusText.Label(status), Tag = status });
                }
                _status.SelectedIndex = 0;
                panel.Children.Add(_status);
            }

            panel.Children.Add(new TextBlock { Text = action == OverrideAction.Reconfirm ? "原因（留空沿用原本的原因）：" : "原因（必填）：" });
            _reason = new TextBox
            {
                Text = action == OverrideAction.Reconfirm ? currentReason ?? string.Empty : string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 60,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(_reason);

            _comment = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 40, Margin = new Thickness(0, 0, 0, 8) };
            if (action != OverrideAction.Withdraw)
            {
                panel.Children.Add(new TextBlock { Text = "註解（選填）：" });
                panel.Children.Add(_comment);
            }

            _error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "確定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, __) => Confirm();
            buttons.Children.Add(ok);
            buttons.Children.Add(new Button { Content = "取消", Width = 80, IsCancel = true });
            panel.Children.Add(buttons);
            Content = panel;
        }

        public ReviewStatus Status { get; private set; }
        public string Reason { get; private set; }
        public string Comment { get; private set; }

        public static FireReviewOverrideDialog Ask(Window owner, OverrideAction action, ReviewStatus computed, ReviewStatus? current, string currentReason)
        {
            var dialog = new FireReviewOverrideDialog(action, computed, current, currentReason) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog : null;
        }

        private void Confirm()
        {
            var reason = _reason.Text?.Trim();
            if (_action != OverrideAction.Reconfirm && string.IsNullOrWhiteSpace(reason))
            {
                _error.Text = "人工覆寫必須填寫原因。";
                return;
            }

            if (_action == OverrideAction.Override) Status = (ReviewStatus)((ComboBoxItem)_status.SelectedItem).Tag;
            Reason = reason;
            Comment = string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text.Trim();
            DialogResult = true;
        }
    }
}
