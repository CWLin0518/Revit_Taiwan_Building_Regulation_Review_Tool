using System.Collections.Generic;

namespace BuildingRegulationReview.Features
{
    /// <summary>
    /// One review tool in the 檢討面板. The panel builds its buttons from <see cref="Actions"/>, in order,
    /// so a new tool is a JSON entry plus one of these — no XAML change (DESIGN.md §2.2, §2.3).
    /// </summary>
    internal interface IReviewFeature
    {
        /// <summary>Tool ID: the same in review-items.json, docs/tools/&lt;id&gt;/ and the MCP tool prefix.</summary>
        string Id { get; }

        /// <summary>The tool's actions in workflow order.</summary>
        IReadOnlyList<ReviewFeatureAction> Actions { get; }
    }
}
