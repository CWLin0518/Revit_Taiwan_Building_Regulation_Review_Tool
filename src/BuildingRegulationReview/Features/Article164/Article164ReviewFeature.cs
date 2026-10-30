using System.Collections.Generic;

namespace BuildingRegulationReview.Features.Article164
{
    internal sealed class Article164ReviewFeature : IReviewFeature
    {
        public const string FeatureId = "article-164-road-shadow";

        public string Id => FeatureId;

        public IReadOnlyList<ReviewFeatureAction> Actions { get; } = new[]
        {
            new ReviewFeatureAction("review", "開始檢討",
                "選取建築線、點選道路側並輸入道路寬度，計算 3.6:1 道路陰影，建立／覆蓋「日照陰影檢討圖」。",
                "第164條檢討", Article164Command.Run),
            new ReviewFeatureAction("drawing", "圖說製作",
                "需先在本次 Revit 工作階段完成「開始檢討」且結果為符合。選圖框、排版預覽後建立含圖例的圖紙。",
                "第164條圖說製作", Article164DrawingCommand.Run),
        };
    }
}
