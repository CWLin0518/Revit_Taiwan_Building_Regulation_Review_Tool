using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingRegulationReview.Features
{
    internal sealed class ReviewFeatureRegistry
    {
        private readonly IReadOnlyDictionary<string, IReviewFeature> _features;

        public ReviewFeatureRegistry(IEnumerable<IReviewFeature> features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));

            var featureList = features.ToList();
            var duplicateId = featureList.GroupBy(feature => feature.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateId != null)
                throw new ArgumentException($"重複的法規檢討功能 Id：{duplicateId}", nameof(features));

            _features = featureList.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryGet(string id, out IReviewFeature feature)
        {
            feature = null;
            return !string.IsNullOrWhiteSpace(id) && _features.TryGetValue(id, out feature);
        }
    }
}
