using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// One Type as the batch panel edits it. Holds what the user has typed so far; what the model
    /// held is kept in <see cref="Source"/> so <see cref="Edits"/> can write only what changed.
    /// </summary>
    /// <remarks>
    /// Lengths are shown and typed in centimetres because 建築技術規則第71～73條 states every threshold
    /// in 公分; they are converted back to metres on the way out, which is the unit the adapter
    /// converts to Revit's internal feet.
    /// </remarks>
    internal sealed class FireReviewTypeRowViewModel : INotifyPropertyChanged
    {
        private string _material;
        private string _coverCm;
        private string _rating;
        private string _protection;

        public FireReviewTypeRowViewModel(FireReviewTypeRow source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _material = source.ParsedMaterial.HasValue ? StructuralMaterialText.Code(source.ParsedMaterial.Value) : source.Material ?? "";
            _coverCm = Centimetres(source.CoverMeters);
            _rating = source.ProvidedRating ?? "";
            _protection = source.ProvidedProtection ?? "";
        }

        public FireReviewTypeRow Source { get; }

        public string CategoryLabel => Source.CategoryLabel;
        public string DisplayName => Source.DisplayName;
        public bool IsOpening => Source.IsOpening;

        /// <summary>「視圖 3 / 專案 12」 — the second number is the real reach of an edit.</summary>
        public string Counts => Source.InstanceCount == Source.ProjectInstanceCount
            ? Source.InstanceCount.ToString(CultureInfo.InvariantCulture)
            : $"{Source.InstanceCount} / {Source.ProjectInstanceCount}";

        public string CountsTooltip => IsOpening
            ? $"此視圖有 {Source.InstanceCount} 個實體。防火保護是實體參數，寫入只會影響這些實體。"
            : $"此視圖有 {Source.InstanceCount} 個實體，整個專案有 {Source.ProjectInstanceCount} 個。" +
              "這些是類型參數，變更會套用到專案中所有實體。";

        /// <summary>牆厚／板厚／柱短邊 as the Type reports it, in centimetres.</summary>
        public string DimensionText => Source.DimensionMeters.HasValue
            ? $"{FireRatingDeriver.DimensionLabel(Source.Category)} {Centimetres(Source.DimensionMeters)} cm"
            : FireRatingDeriver.IsDerivable(Source.Category) ? "（尺寸讀不到）" : "—";

        public bool SupportsDerivation => FireRatingDeriver.IsDerivable(Source.Category);

        public IReadOnlyList<string> MaterialChoices { get; } =
            new[] { "" }.Concat(StructuralMaterialText.All.Select(StructuralMaterialText.Code)).ToList();

        public IReadOnlyList<string> ProtectionChoices { get; } = new[] { "", "是", "否" };

        public string Material
        {
            get => _material;
            set
            {
                if (!Set(ref _material, value ?? "")) return;
                Raise(nameof(NeedsCover));
                RaiseDerived();
            }
        }

        /// <summary>SC is rated by its cover, so the cover box only matters then.</summary>
        public bool NeedsCover => StructuralMaterialText.Parse(_material) == StructuralMaterial.Steel;

        public string CoverCm
        {
            get => _coverCm;
            set
            {
                if (!Set(ref _coverCm, value ?? "")) return;
                RaiseDerived();
            }
        }

        public string Rating
        {
            get => _rating;
            set
            {
                if (!Set(ref _rating, value ?? "")) return;
                Raise(nameof(RatingDiffers));
            }
        }

        public string Protection
        {
            get => _protection;
            set => Set(ref _protection, value ?? "");
        }

        /// <summary>What the clauses derive from what is currently typed in this row.</summary>
        public FireRatingDerivation Derivation => FireRatingDeriver.Derive(
            Source.Category, StructuralMaterialText.Parse(_material), Source.DimensionMeters, Meters(_coverCm));

        public string DerivedText
        {
            get
            {
                var derived = Derivation;
                return derived.HasRating ? derived.ParameterText : "—";
            }
        }

        /// <summary>The clause, or the reason nothing was derived — the panel's 依據 column.</summary>
        public string DerivedBasis
        {
            get
            {
                var derived = Derivation;
                return derived.HasRating ? $"{derived.LegalReference}｜{derived.Explanation}" : derived.Explanation;
            }
        }

        public bool CanApplyDerived => Derivation.HasRating;

        /// <summary>The typed rating is not what the clauses derive, so the row is worth a second look.</summary>
        public bool RatingDiffers
        {
            get
            {
                var derived = Derivation;
                if (!derived.HasRating) return false;
                var typed = FireRatingText.Parse(_rating, FireRatingUnit.Minute);
                return typed.Kind != ProvidedFireRatingKind.Rated || typed.Minutes != derived.Minutes;
            }
        }

        /// <summary>Which review parameters this Type does not carry, so an edit to them cannot land.</summary>
        public string MissingParameters
        {
            get
            {
                var missing = new List<string>();
                if (IsOpening)
                {
                    if ((Source.Present & FireReviewTypeParameters.Protection) == 0) missing.Add(FireProtectionParameters.Provided);
                }
                else
                {
                    if ((Source.Present & FireReviewTypeParameters.Rating) == 0) missing.Add(FireRatingParameters.Provided);
                    if ((Source.Present & FireReviewTypeParameters.Material) == 0) missing.Add(StructuralMaterialParameters.Material);
                    if (NeedsCover && (Source.Present & FireReviewTypeParameters.Cover) == 0) missing.Add(StructuralMaterialParameters.Cover);
                }

                return missing.Count == 0 ? "" : "缺少參數：" + string.Join("、", missing);
            }
        }

        public bool HasMissingParameters => MissingParameters.Length > 0;

        /// <summary>Writes the derived rating into the editable rating box; does not touch the model.</summary>
        public void ApplyDerived()
        {
            var derived = Derivation;
            if (derived.HasRating) Rating = derived.ParameterText;
        }

        /// <summary>Only the values that differ from what the model held — nothing is rewritten for its own sake.</summary>
        public IEnumerable<FireReviewParameterEdit> Edits()
        {
            if (IsOpening)
            {
                // 設計防火保護 is an instance parameter, so an opening row writes to the instances the
                // view showed — never to the Type, which does not carry it.
                if (!Same(_protection, Source.ProvidedProtection))
                    foreach (var instance in Source.InstanceUniqueIds)
                        yield return FireReviewParameterEdit.OfText(instance, FireProtectionParameters.Provided, _protection);
                yield break;
            }

            if (!Same(_rating, Source.ProvidedRating))
                yield return FireReviewParameterEdit.OfText(Source.TypeUniqueId, FireRatingParameters.Provided, _rating);

            if (!Same(_material, Source.Material))
                yield return FireReviewParameterEdit.OfText(Source.TypeUniqueId, StructuralMaterialParameters.Material, _material);

            var cover = Meters(_coverCm);
            if (!Same(cover, Source.CoverMeters))
                yield return FireReviewParameterEdit.OfLength(Source.TypeUniqueId, StructuralMaterialParameters.Cover, cover);
        }

        public bool IsDirty => Edits().Any();

        private void RaiseDerived()
        {
            Raise(nameof(DerivedText));
            Raise(nameof(DerivedBasis));
            Raise(nameof(CanApplyDerived));
            Raise(nameof(RatingDiffers));
            Raise(nameof(MissingParameters));
            Raise(nameof(HasMissingParameters));
        }

        private static bool Same(string typed, string stored) =>
            string.Equals((typed ?? "").Trim(), (stored ?? "").Trim(), StringComparison.Ordinal);

        private static bool Same(double? typed, double? stored)
        {
            if (typed == null && stored == null) return true;
            if (typed == null || stored == null) return false;
            return Math.Abs(typed.Value - stored.Value) < 1e-6;
        }

        private static string Centimetres(double? meters) => meters.HasValue
            ? (meters.Value * 100).ToString("0.##", CultureInfo.InvariantCulture)
            : "";

        /// <summary>Centimetres as typed, back to metres; blank or unreadable is no value, never zero.</summary>
        private static double? Meters(string centimetres)
        {
            if (string.IsNullOrWhiteSpace(centimetres)) return null;
            return double.TryParse(centimetres.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0
                ? value / 100
                : (double?)null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Set(ref string field, string value, [CallerMemberName] string name = null)
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return false;
            field = value;
            Raise(name);
            return true;
        }

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
