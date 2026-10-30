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

            foreach (var feature in featureList)
            {
                if (feature.Actions == null || feature.Actions.Count == 0)
                    throw new ArgumentException($"法規檢討功能 {feature.Id} 沒有任何動作。", nameof(features));
                var duplicateAction = feature.Actions.GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(group => group.Count() > 1)?.Key;
                if (duplicateAction != null)
                    throw new ArgumentException($"法規檢討功能 {feature.Id} 有重複的動作 Id：{duplicateAction}", nameof(features));
            }

            _features = featureList.ToDictionary(feature => feature.Id, StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> Ids => _features.Keys;

        public bool TryGet(string id, out IReviewFeature feature)
        {
            feature = null;
            return !string.IsNullOrWhiteSpace(id) && _features.TryGetValue(id, out feature);
        }

        public bool TryGetAction(string featureId, string actionId, out IReviewFeature feature, out ReviewFeatureAction action)
        {
            action = null;
            if (!TryGet(featureId, out feature) || string.IsNullOrWhiteSpace(actionId)) return false;
            action = feature.Actions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, actionId, StringComparison.OrdinalIgnoreCase));
            return action != null;
        }
    }
}
