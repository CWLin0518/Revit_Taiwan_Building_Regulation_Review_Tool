namespace BuildingRegulationReview
{
    public sealed class ReviewItem
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string LegalReference { get; set; }
        public string Description { get; set; }
        public string Output { get; set; }

        public string SearchText =>
            $"{Id} {Category} {Title} {LegalReference} {Description} {Output}";
    }
}
