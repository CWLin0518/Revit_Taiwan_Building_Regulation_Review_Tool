using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BuildingRegulationReview.Application.ProjectSetup;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// spec 8.2「檢查／建立參數」的差異預覽：列出每個參數現在的狀態、要做什麼，經確認後才建立或補綁。
    /// </summary>
    /// <remarks>
    /// 視窗自己不碰 Revit。<see cref="Create"/> 與 <see cref="Reinspect"/> 由指令注入，交易在指令的
    /// API context 裡開（與「防火參數批次設定」同一個模式）。
    /// </remarks>
    public partial class FireReviewParameterSetupWindow : Window
    {
        private FireReviewParameterSetupPlan _plan;

        public FireReviewParameterSetupWindow(FireReviewParameterSetupPlan plan)
        {
            InitializeComponent();
            Load(plan);
        }

        /// <summary>建立與補綁。由指令實作，回傳結果摘要讓視窗顯示。</summary>
        public Action<FireReviewParameterSetupPlan> Create { get; set; }

        /// <summary>重新讀一次專案現況。</summary>
        public Func<FireReviewParameterSetupPlan> Reinspect { get; set; }

        /// <summary>目前顯示的差異預覽裡還有沒有可以建立或補綁的項目。</summary>
        public bool HasWork => _plan != null && _plan.HasWork;

        /// <summary>把最新的差異預覽顯示出來。</summary>
        public void Load(FireReviewParameterSetupPlan plan)
        {
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));

            Rows.ItemsSource = plan.Actions.Select(x => new Row(x)).ToList();
            StatusLine.Text = plan.Summary;
            CreateButton.IsEnabled = plan.HasWork;

            var conflicts = plan.Conflicts;
            if (conflicts.Count == 0)
            {
                ConflictBox.Visibility = Visibility.Collapsed;
                ConflictText.Text = string.Empty;
                return;
            }

            ConflictBox.Visibility = Visibility.Visible;
            ConflictText.Text =
                $"有 {conflicts.Count} 個參數同名但定義不符，這個功能不會覆蓋它們——Revit 不允許同名的兩個專案參數並存，" +
                "換掉一個等於先移除舊的，原本填在上面的值會跟著消失。請照下表的修正方式先處理，再按「重新檢查」。" +
                "其餘可以建立的參數不受影響，現在按「建立並綁定」就會建好。";
        }

        /// <summary>寫入之後顯示結果，並把表格換成寫入後的現況。</summary>
        public void ReportResult(string summary, IReadOnlyList<string> failures, IReadOnlyList<string> warnings)
        {
            var lines = new List<string> { summary };
            if (warnings != null && warnings.Count > 0) lines.AddRange(warnings.Select(x => "提醒：" + x));
            if (failures != null && failures.Count > 0) lines.AddRange(failures.Select(x => "未能處理：" + x));
            StatusLine.Text = string.Join("\n", lines);

            Refresh(keepStatus: true);
        }

        /// <summary>
        /// 例外在這裡就地處理完。這是 Click handler，而指令的 try/catch 只包住 <c>ShowDialog</c>——例外
        /// 能不能沿著巢狀 dispatcher frame 冒回指令要看 dispatcher 行為，賭輸的代價是 Revit 的未處理
        /// 例外對話框。<see cref="Create"/> 自己會回滾交易，所以這裡接住之後模型仍是乾淨的。
        /// </summary>
        private void OnCreate(object sender, RoutedEventArgs e)
        {
            if (Create == null || _plan == null || !_plan.HasWork) return;

            var plan = _plan;
            CreateButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            try
            {
                Create(plan);
            }
            catch (Exception exception)
            {
                StatusLine.Text = "建立失敗，模型未被改動：" + exception.Message + "\n請按「重新檢查」確認現況。";
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                CreateButton.IsEnabled = HasWork;
            }
        }

        private void OnRefresh(object sender, RoutedEventArgs e) => Refresh(keepStatus: false);

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void Refresh(bool keepStatus)
        {
            if (Reinspect == null) return;

            var status = StatusLine.Text;
            Load(Reinspect());
            if (keepStatus) StatusLine.Text = status;
        }

        /// <summary>表格的一列。狀態與說明的文字都由 Application 層決定，這裡只是搬過來。</summary>
        private sealed class Row
        {
            public Row(FireReviewParameterAction action)
            {
                Name = action.Definition.Name;
                ValueTypeText = action.Definition.ValueTypeText;
                LevelText = action.Definition.LevelText;
                HostsText = action.Definition.HostsText;
                StatusText = action.StatusText;
                DetailText = action.DetailText;
                IsConflict = action.Kind == FireReviewParameterActionKind.Conflict;
                IsWork = action.IsWork;
            }

            public string Name { get; }
            public string ValueTypeText { get; }
            public string LevelText { get; }
            public string HostsText { get; }
            public string StatusText { get; }
            public string DetailText { get; }
            public bool IsConflict { get; }
            public bool IsWork { get; }
        }
    }
}
