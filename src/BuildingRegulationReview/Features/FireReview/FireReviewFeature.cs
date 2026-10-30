using System.Collections.Generic;

namespace BuildingRegulationReview.Features.FireReview
{
    /// <summary>
    /// 建築防火檢討: the five steps that used to be five ribbon buttons, in workflow order
    /// (docs/tools/review-panel/SPEC.md §2). Each action calls the same entry point the ribbon did.
    /// </summary>
    internal sealed class FireReviewFeature : IReviewFeature
    {
        public const string FeatureId = "fire-review";

        public string Id => FeatureId;

        public IReadOnlyList<ReviewFeatureAction> Actions { get; } = new[]
        {
            new ReviewFeatureAction("parameter-setup", "參數一鍵建立",
                "把防火區劃檢討要讀的共用參數一次加進目前專案並綁好類別。先顯示差異預覽，已綁好的不動，同名但定義不符的不覆蓋只給修正方式。",
                "防火檢討參數一鍵建立", FireReviewParameterSetupCommand.Run),
            new ReviewFeatureAction("setup", "防火區劃設定",
                "選擇來源樓層平面、面積配置與 Area Plan 選項，建立或重用各樓層的檢討套件。",
                "防火區劃設定", FireReviewSetupCommand.Run),
            new ReviewFeatureAction("parameters", "參數批次設定",
                "收集目前視圖的牆、柱、樓板、門窗類型與區劃，批次填寫結構材料、防火時效與區劃用途；RC／SRC／SC 加上斷面尺寸可依第71～73條自動推定時效。",
                "防火檢討參數批次設定", FireReviewParameterPanelCommand.Run),
            new ReviewFeatureAction("region-editor", "開啟防火區劃編輯器",
                "讀取 Area Plan 的牆、柱、房間分隔線與輔助線，求解封閉範圍，編輯防火區劃草稿；預覽確認後才寫回。",
                "防火區劃編輯器", RegionEditorCommand.Run),
            new ReviewFeatureAction("review", "開始檢討",
                "前置檢查後一次檢討防火區劃面積、構件防火時效與防火門窗，在專用檢討視圖標示未符合項目，並可定位與人工覆寫。",
                "防火區劃檢討", FireReviewCommand.Run),
        };
    }
}
