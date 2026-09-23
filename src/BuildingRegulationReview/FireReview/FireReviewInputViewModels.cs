using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// Shared plumbing for the panel's editable rows: change notification, and the conversions
    /// between what the user types and what a Revit parameter takes.
    /// </summary>
    /// <remarks>
    /// 是／否 is written as 1／0 rather than as the words. The shared parameter file declares these
    /// as YESNO, which Revit stores as an integer, and the writer parses the text it is given — so
    /// handing it 「是」 would fail the parse and be reported as a refusal the user cannot act on.
    /// </remarks>
    internal abstract class FireReviewEditableRow : INotifyPropertyChanged
    {
        internal const string YesText = "是";
        internal const string NoText = "否";

        public IReadOnlyList<string> YesNoChoices { get; } = new[] { "", YesText, NoText };

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The edits this row would make, or nothing when the user changed nothing.</summary>
        public abstract IEnumerable<FireReviewParameterEdit> Edits();

        public bool IsDirty => Edits().Any();

        protected static string TextOf(bool? value) => value == true ? YesText : value == false ? NoText : "";

        protected static bool? YesNoOf(string text) =>
            text == YesText ? true : text == NoText ? false : (bool?)null;

        /// <summary>1 or 0 for a Yes/No parameter, or an empty string to clear it.</summary>
        protected static string YesNoParameterText(bool? value) =>
            value == true ? "1" : value == false ? "0" : "";

        protected static string TextOf(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

        /// <summary>The integer the user typed, or null when the box is blank or not a whole number.</summary>
        protected static int? IntegerOf(string text) =>
            !string.IsNullOrWhiteSpace(text) &&
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : (int?)null;

        protected static bool Same(string typed, string stored) =>
            string.Equals((typed ?? "").Trim(), (stored ?? "").Trim(), StringComparison.Ordinal);

        protected bool Set(ref string field, string value, [CallerMemberName] string name = null)
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return false;
            field = value ?? "";
            Raise(name);
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// One 防火區劃 as the panel edits it. These are instance parameters on one Area, so a row
    /// reaches that Area and nothing else — unlike the Type rows, which reach the whole project.
    /// </summary>
    internal sealed class FireReviewZoneRowViewModel : FireReviewEditableRow
    {
        private string _use;
        private string _sprinklered;
        private string _floorNumber;

        public FireReviewZoneRowViewModel(FireReviewZoneRow source, int? derivedFloorNumber = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            DerivedFloorNumber = derivedFloorNumber;
            _use = source.Use ?? "";
            _sprinklered = TextOf(source.Sprinklered);
            _floorNumber = TextOf(source.FloorNumber);
        }

        public FireReviewZoneRow Source { get; }

        /// <summary>What the levels' elevations say this zone's storey number is; null when unknown.</summary>
        public int? DerivedFloorNumber { get; }

        public string DerivedFloorText => DerivedFloorNumber.HasValue
            ? TextOf(DerivedFloorNumber)
            : "—";

        /// <summary>True when the derived storey number is not what the box currently holds.</summary>
        public bool FloorNumberDiffers =>
            DerivedFloorNumber.HasValue && IntegerOf(_floorNumber) != DerivedFloorNumber;

        /// <summary>Fills the box from the model's levels; does not write to the model.</summary>
        public void ApplyDerivedFloorNumber()
        {
            if (DerivedFloorNumber.HasValue) FloorNumber = TextOf(DerivedFloorNumber);
        }

        public string DisplayName => Source.DisplayName;
        public string LevelName => Source.LevelName ?? "—";
        public string AreaSchemeName => Source.AreaSchemeName ?? "—";
        public string AreaText => Source.AreaText;

        /// <summary>防火檢討_區劃用途. Free text; 「樓梯間」 is the one value a rule reads.</summary>
        public string Use
        {
            get => _use;
            set => Set(ref _use, value);
        }

        /// <summary>防火檢討_自動滅火設備 — decides whether the area limit is 1500 or 3000 m².</summary>
        public string Sprinklered
        {
            get => _sprinklered;
            set
            {
                if (Set(ref _sprinklered, value)) Raise(nameof(LimitText));
            }
        }

        /// <summary>The limit this zone is judged against, so the consequence of the tick is visible.</summary>
        public string LimitText => YesNoOf(_sprinklered) switch
        {
            true => "上限 3000 m²",
            false => "上限 1500 m²",
            _ => "未填，無法判定上限"
        };

        /// <summary>防火檢討_所在樓層序 — 第70條 counts storeys from the top with it.</summary>
        public string FloorNumber
        {
            get => _floorNumber;
            set
            {
                if (Set(ref _floorNumber, value)) Raise(nameof(FloorNumberDiffers));
            }
        }

        public string MissingParameters => Source.MissingParameters.Count == 0
            ? ""
            : "缺少參數：" + string.Join("、", Source.MissingParameters);

        public bool HasMissingParameters => Source.MissingParameters.Count > 0;

        public override IEnumerable<FireReviewParameterEdit> Edits()
        {
            if (!Same(_use, Source.Use))
                yield return FireReviewParameterEdit.OfText(Source.ElementUniqueId, ReviewInputSources.ZoneUse, _use);

            if (YesNoOf(_sprinklered) != Source.Sprinklered)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.Sprinklered, YesNoParameterText(YesNoOf(_sprinklered)));
            }

            if (IntegerOf(_floorNumber) != Source.FloorNumber)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FloorNumber, TextOf(IntegerOf(_floorNumber)));
            }
        }
    }

    /// <summary>
    /// The Project Information facts. One element, so one row, rendered as a form.
    /// </summary>
    /// <remarks>
    /// 建築物高度 has no field here on purpose: the model's own extent is the height, measured at
    /// review time, so there is nothing to type and nothing that could drift out of date.
    /// </remarks>
    internal sealed class FireReviewProjectViewModel : FireReviewEditableRow
    {
        private string _fireResistive;
        private string _buildingUse;
        private string _floorsAboveGround;

        public FireReviewProjectViewModel(FireReviewProjectRow source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            _fireResistive = TextOf(source.FireResistiveConstruction);
            _buildingUse = source.BuildingUse ?? "";
            _floorsAboveGround = TextOf(source.FloorsAboveGround);
        }

        public FireReviewProjectRow Source { get; }

        /// <summary>
        /// 防火檢討_防火構造建築物 — every rule's applicability hangs on it, so the panel says what
        /// happens when it is not ticked rather than leaving the user to discover it in the results.
        /// </summary>
        public string FireResistive
        {
            get => _fireResistive;
            set
            {
                if (Set(ref _fireResistive, value)) Raise(nameof(FireResistiveWarning));
            }
        }

        public string FireResistiveWarning => YesNoOf(_fireResistive) == true
            ? ""
            : "未勾選時，四類規則的適用條件都不成立，整份檢討會判「資料不足」。";

        public bool HasFireResistiveWarning => FireResistiveWarning.Length > 0;

        public string BuildingUse
        {
            get => _buildingUse;
            set => Set(ref _buildingUse, value);
        }

        /// <summary>地上層數 — 第70條 needs it to count a storey's position from the top.</summary>
        public string FloorsAboveGround
        {
            get => _floorsAboveGround;
            set => Set(ref _floorsAboveGround, value);
        }

        public string MissingParameters => Source.MissingParameters.Count == 0
            ? ""
            : "缺少參數：" + string.Join("、", Source.MissingParameters);

        public bool HasMissingParameters => Source.MissingParameters.Count > 0;

        public override IEnumerable<FireReviewParameterEdit> Edits()
        {
            if (YesNoOf(_fireResistive) != Source.FireResistiveConstruction)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FireResistiveConstruction,
                    YesNoParameterText(YesNoOf(_fireResistive)));
            }

            if (!Same(_buildingUse, Source.BuildingUse))
                yield return FireReviewParameterEdit.OfText(Source.ElementUniqueId, ReviewInputSources.BuildingUse, _buildingUse);

            if (IntegerOf(_floorsAboveGround) != Source.FloorsAboveGround)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FloorsAboveGround, TextOf(IntegerOf(_floorsAboveGround)));
            }
        }
    }
}
