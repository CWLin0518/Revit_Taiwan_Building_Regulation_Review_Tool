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
        private bool _smokeProtection;
        private string _panelKind;

        public FireReviewTypeRowViewModel(FireReviewTypeRow source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            // 已宣告的值照原樣顯示；讀不懂的字也照原樣留著，使用者才看得出模型裡填了什麼。只有完全
            // 空白時才用讀取層由材料算出的提案當初值——提案進得了下拉，但要按下套用才會寫進模型。
            _panelKind = source.ParsedPanelKind.HasValue
                ? CurtainPanelKinds.ParameterText(source.ParsedPanelKind.Value)
                : !string.IsNullOrWhiteSpace(source.PanelKind) ? source.PanelKind
                : source.PanelKindProposal.HasValue ? CurtainPanelKinds.ParameterText(source.PanelKindProposal.Value)
                : "";
            _material = source.ParsedMaterial.HasValue ? StructuralMaterialText.Code(source.ParsedMaterial.Value) : source.Material ?? "";
            _coverCm = Centimetres(source.CoverMeters);
            _rating = source.ProvidedRating ?? "";
            _protection = source.ProvidedProtection == true;
            _smokeProtection = source.ProvidedSmokeProtection == true;
        }

        public FireReviewTypeRow Source { get; }

        public string CategoryLabel => Source.CategoryLabel;
        public string DisplayName => Source.DisplayName;
        public bool IsOpening => Source.IsOpening;

        public bool IsCurtainPanel => Source.Category == CandidateCategory.CurtainPanel;

        /// <summary>實心／玻璃 as currently chosen in this row, not as the model holds it.</summary>
        private CurtainPanelKind? TypedPanelKind => CurtainPanelKinds.Parse(_panelKind);

        /// <summary>
        /// 模型裡還沒宣告種類的帷幕嵌板——**包含工具提案了但還沒寫入的列**，因為提案不是宣告（決議 16）。
        /// 面板的狀態列數的是這個：沒宣告之前，設計防火時效 與 防火門窗 兩邊都問不成，CW-O 答資料不足。
        /// </summary>
        public bool AwaitsPanelKind => Source.AwaitsPanelKind;

        /// <summary>
        /// 一片實心嵌板 fills in 設計防火時效 like the 主要構造 do (帷幕牆規格 §6、決議 16), and 門 do too,
        /// because a 管道間之維修門 owes one hour under 第79條之2第1項 (垂直區劃文件 §6). 玻璃嵌板 and 窗
        /// do not. An undeclared panel carries both, so neither gap is hidden before the kind is chosen.
        /// </summary>
        public bool CarriesRating => IsCurtainPanel
            ? TypedPanelKind != CurtainPanelKind.Glazed
            : Source.CarriesRating;

        /// <summary>防火門窗 is asked of every opening except a panel declared 實心, which is 構造.</summary>
        public bool CarriesProtection => IsCurtainPanel
            ? TypedPanelKind != CurtainPanelKind.Solid
            : Source.CarriesProtection;

        public bool CarriesSmokeProtection => Source.CarriesSmokeProtection;

        /// <summary>
        /// Whether 結構材料（and, for SC, 防火被覆厚度）is asked of this row: every 主要構造, plus a
        /// 帷幕嵌板 declared 實心, whose 時效 is derived on the 牆壁 thresholds.
        /// </summary>
        public bool CarriesMaterial => !IsOpening || TypedPanelKind == CurtainPanelKind.Solid;

        /// <summary>「視圖 3 / 專案 12」 — the second number is the real reach of an edit.</summary>
        public string Counts => Source.InstanceCount == Source.ProjectInstanceCount
            ? Source.InstanceCount.ToString(CultureInfo.InvariantCulture)
            : $"{Source.InstanceCount} / {Source.ProjectInstanceCount}";

        public string CountsTooltip =>
            $"此視圖有 {Source.InstanceCount} 個實體，整個專案有 {Source.ProjectInstanceCount} 個。" +
            "這些是類型參數，變更會套用到專案中所有實體。";

        /// <summary>牆厚／板厚／柱短邊／嵌板厚 as the Type reports it, in centimetres.</summary>
        public string DimensionText => Source.DimensionMeters.HasValue
            ? $"{FireRatingDeriver.DimensionLabel(Source.Category)} {Centimetres(Source.DimensionMeters)} cm"
            : SupportsDerivation ? "（尺寸讀不到）" : "—";

        /// <summary>
        /// Whether 結構材料＋尺寸 → 時效 is offered here. A 帷幕嵌板 has to declare 實心 first: until it
        /// does, the tool does not know whether it is looking at a piece of 構造 or at 防火設備.
        /// </summary>
        public bool SupportsDerivation =>
            FireRatingDeriver.IsDerivable(Source.Category) && CarriesRating && !(IsCurtainPanel && TypedPanelKind is null);

        public IReadOnlyList<string> MaterialChoices { get; } =
            new[] { "" }.Concat(StructuralMaterialText.All.Select(StructuralMaterialText.Code)).ToList();

        /// <summary>The two kinds a user may declare, blank first — 帷幕牆門窗 is never one of them.</summary>
        public IReadOnlyList<string> PanelKindChoices { get; } =
            new[] { "" }.Concat(CurtainPanelKinds.Declarable.Select(CurtainPanelKinds.ParameterText)).ToList();

        /// <summary>
        /// The kind shown here is the reader's proposal from the panel's material, not something the
        /// model holds — so the panel can say so, and so a row left alone is still listed as 待宣告
        /// by <see cref="Application.Parameters.FireReviewTypeTable.AwaitingPanelKind"/> (決議 16).
        /// </summary>
        public bool PanelKindIsProposed => Source.PanelKindProposal.HasValue &&
            Same(_panelKind, CurtainPanelKinds.ParameterText(Source.PanelKindProposal.Value));

        public string PanelKindProposalNote => PanelKindIsProposed
            ? $"由嵌板材料提案為「{CurtainPanelKinds.Label(Source.PanelKindProposal.Value)}」，尚未寫入模型；確認後請按套用。"
            : "";

        /// <summary>
        /// 防火檢討_嵌板種類 on the Type. Blank means 未宣告, which the review reads as 資料不足 rather
        /// than guessing — see <see cref="CarriesRating"/>.
        /// </summary>
        public string PanelKind
        {
            get => _panelKind;
            set
            {
                if (!Set(ref _panelKind, value ?? "")) return;
                Raise(nameof(PanelKindIsProposed));
                Raise(nameof(PanelKindProposalNote));
                Raise(nameof(CarriesRating));
                Raise(nameof(CarriesProtection));
                Raise(nameof(CarriesMaterial));
                Raise(nameof(SupportsDerivation));
                Raise(nameof(DimensionText));
                Raise(nameof(NeedsCover));
                RaiseDerived();
            }
        }


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
        public bool NeedsCover =>
            CarriesMaterial && StructuralMaterialText.Parse(_material) == StructuralMaterial.Steel;

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

        /// <summary>
        /// 防火檢討_遮煙性能 on the Type: ticked means this 型號 passed the 遮煙性能 test of 第1條第45款.
        /// A separate question from <see cref="Protection"/>, which asks whether the 型號 is a 防火門窗
        /// at all — 第79條之2第1項 requires both of a 昇降機道's 防火設備 and of a 管道間之維修門.
        /// </summary>
        public bool SmokeProtection
        {
            get => _smokeProtection;
            set
            {
                if (_smokeProtection == value) return;
                _smokeProtection = value;
                Raise(nameof(SmokeProtection));
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

        public bool CanApplyDerived => SupportsDerivation && Derivation.HasRating;

        /// <summary>The typed rating is not what the clauses derive, so the row is worth a second look.</summary>
        public bool RatingDiffers
        {
            get
            {
                if (!SupportsDerivation) return false;
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
                // 嵌板種類 comes first: without it the row cannot even say which of the two questions
                // below it owes an answer to (決議 16).
                if (IsCurtainPanel && (Source.Present & FireReviewTypeParameters.PanelKind) == 0)
                    missing.Add(CurtainPanelKindParameters.Provided);
                if (CarriesProtection && (Source.Present & FireReviewTypeParameters.Protection) == 0)
                    missing.Add(FireProtectionParameters.Provided);
                if (CarriesSmokeProtection && (Source.Present & FireReviewTypeParameters.SmokeSeal) == 0)
                    missing.Add(SmokeProtectionParameters.Provided);
                if (CarriesRating && (Source.Present & FireReviewTypeParameters.Rating) == 0)
                    missing.Add(FireRatingParameters.Provided);
                if (CarriesMaterial)
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
            // 嵌板種類 is written first because it decides which of the two answers below is asked
            // for at all. The tool may have proposed 玻璃 from the panel's material, but nothing is
            // written until the user leaves that choice standing (決議 16、D1).
            if (IsCurtainPanel && !Same(_panelKind, Source.PanelKind))
                yield return FireReviewParameterEdit.OfText(Source.TypeUniqueId, CurtainPanelKindParameters.Provided, _panelKind);

            // 設計防火保護 is a Type parameter: 防火門窗 is a property of the 型號, so one tick
            // answers for every instance of it in the project.
            // A Type that never carried the parameter reads as null; leaving such a row alone
            // must write nothing, but ticking it still writes, so the failure names the missing
            // binding instead of silently doing nothing.
            if (CarriesProtection && (Source.ProvidedProtection ?? false) != _protection)
                yield return FireReviewParameterEdit.OfYesNo(Source.TypeUniqueId, FireProtectionParameters.Provided, _protection);

            // 遮煙性能 is written the same way and for the same reason: it is a property of the 型號
            // (第1條第45款 is a test on the 構造), not of one installed leaf.
            if (CarriesSmokeProtection && (Source.ProvidedSmokeProtection ?? false) != _smokeProtection)
                yield return FireReviewParameterEdit.OfYesNo(Source.TypeUniqueId, SmokeProtectionParameters.Provided, _smokeProtection);

            // 一片實心嵌板 answers by 設計防火時效, because the 交接帶 of 第79條第4項／第79條之3第2項 is
            // measured by the panels' own rating (帷幕牆規格 §6); a 玻璃 one answers 防火門窗 above instead.
            if (CarriesRating && !Same(_rating, Source.ProvidedRating))
                yield return FireReviewParameterEdit.OfText(Source.TypeUniqueId, FireRatingParameters.Provided, _rating);

            // 結構材料 and 防火被覆厚度 exist to derive that rating, so they follow the same question:
            // every 主要構造, and a 帷幕嵌板 once it is declared 實心.
            if (!CarriesMaterial) yield break;

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
