namespace BuildingRegulationReview
{
    /// <summary>One tool in Data/review-items.json: what the 檢討面板 shows about it (DESIGN.md §2.2).</summary>
    public sealed class ReviewItem
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string LegalReference { get; set; }
        public string Description { get; set; }

        /// <summary>操作步驟, one per line; shown above the action buttons.</summary>
        public string Steps { get; set; }

        public string Output { get; set; }

        public string SearchText =>
            $"{Id} {Category} {Title} {LegalReference} {Description} {Steps} {Output}";
    }
}
