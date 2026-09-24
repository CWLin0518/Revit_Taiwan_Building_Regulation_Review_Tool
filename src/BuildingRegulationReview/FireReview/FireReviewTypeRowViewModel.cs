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
        private bool _protection;

        public FireReviewTypeRowViewModel(FireReviewTypeRow source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _material = source.ParsedMaterial.HasValue ? StructuralMaterialText.Code(source.ParsedMaterial.Value) : source.Material ?? "";
            _coverCm = Centimetres(source.CoverMeters);
            _rating = source.ProvidedRating ?? "";
            _protection = source.ProvidedProtection == true;
        }

        public FireReviewTypeRow Source { get; }

        public string CategoryLabel => Source.CategoryLabel;
        public string DisplayName => Source.DisplayName;
        public bool IsOpening => Source.IsOpening;

        /// <summary>帷幕嵌板 fill in 設計防火時效 like the 主要構造 do; 門窗 do not (帷幕牆規格 §6).</summary>
        public bool CarriesRating => Source.CarriesRating;

        public bool CarriesProtection => Source.CarriesProtection;

        /// <summary>「視圖 3 / 專案 12」 — the second number is the real reach of an edit.</summary>
        public string Counts => Source.InstanceCount == Source.ProjectInstanceCount
            ? Source.InstanceCount.ToString(CultureInfo.InvariantCulture)
            : $"{Source.InstanceCount} / {Source.ProjectInstanceCount}";

        public string CountsTooltip =>
            $"此視圖有 {Source.InstanceCount} 個實體，整個專案有 {Source.ProjectInstanceCount} 個。" +
            "這些是類型參數，變更會套用到專案中所有實體。";

        /// <summary>牆厚／板厚／柱短邊 as the Type reports it, in centimetres.</summary>
        public string DimensionText => Source.DimensionMeters.HasValue
            ? $"{FireRatingDeriver.DimensionLabel(Source.Category)} {Centimetres(Source.DimensionMeters)} cm"
            : FireRatingDeriver.IsDerivable(Source.Category) ? "（尺寸讀不到）" : "—";

        public bool SupportsDerivation => FireRatingDeriver.IsDerivable(Source.Category);

        public IReadOnlyList<string> MaterialChoices { get; } =
            new[] { "" }.Concat(StructuralMaterialText.All.Select(StructuralMaterialText.Code)).ToList();


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

        /// <summary>
        /// 防火檢討_設計防火保護 on the Type: ticked means this 型號 is a 防火門窗. A Type that never
        /// carried the parameter shows unticked, and stays unwritten until the user ticks it, which
        /// is why <see cref="FireReviewTypeRow.ProvidedProtection"/> keeps null apart from false.
        /// </summary>
        public bool Protection
        {
            get => _protection;
            set
            {
                if (_protection == value) return;
                _protection = value;
                Raise(nameof(Protection));
            }
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
                if (CarriesProtection && (Source.Present & FireReviewTypeParameters.Protection) == 0)
                    missing.Add(FireProtectionParameters.Provided);
                if (CarriesRating && (Source.Present & FireReviewTypeParameters.Rating) == 0)
                    missing.Add(FireRatingParameters.Provided);
                if (!IsOpening)
                {
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
            // 設計防火保護 is a Type parameter: 防火門窗 is a property of the 型號, so one tick
            // answers for every instance of it in the project.
            // A Type that never carried the parameter reads as null; leaving such a row alone
            // must write nothing, but ticking it still writes, so the failure names the missing
            // binding instead of silently doing nothing.
            if (CarriesProtection && (Source.ProvidedProtection ?? false) != _protection)
                yield return FireReviewParameterEdit.OfYesNo(Source.TypeUniqueId, FireProtectionParameters.Provided, _protection);

            // 帷幕嵌板 answer both: 防火門窗 for an openable panel, and 設計防火時效 because the 交接帶
            // of 第79條第4項／第79條之3第2項 is measured by the panels' own rating (帷幕牆規格 §6).
            if (CarriesRating && !Same(_rating, Source.ProvidedRating))
                yield return FireReviewParameterEdit.OfText(Source.TypeUniqueId, FireRatingParameters.Provided, _rating);

            if (IsOpening) yield break;

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
