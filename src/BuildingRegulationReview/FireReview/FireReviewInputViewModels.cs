using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildingRegulationReview.Application.Checks;
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
    /// One entry in the 區劃用途 dropdown: either a use the panel offers, or the heading that
    /// separates the two articles' lists.
    /// </summary>
    /// <remarks>
    /// The list carries two kinds of word and they have different consequences, so a flat list would
    /// mislead: the 第79條之2 垂直區劃 are exempt from the area limit outright, while 第79條之1's six
    /// names leave the limit fully in force and only mark the 區劃 as one a person may release by hand
    /// (第79條之1規格 §7.3、決議 11). A heading is an item like any other, made unselectable rather
    /// than filtered out, so the sections read in the dropdown itself and not only in the tooltip.
    /// <para>
    /// <see cref="ToString"/> is what an editable ComboBox writes into its text box when an item is
    /// picked, so a heading returns an empty string: if one were ever reached by keyboard it clears
    /// the box rather than typing its own label in as a 區劃用途.
    /// </para>
    /// </remarks>
    internal sealed class ZoneUseChoice
    {
        private ZoneUseChoice(string text, string label, bool isHeading)
        {
            Text = text;
            Label = label;
            IsHeading = isHeading;
        }

        /// <summary>The value this entry fills 區劃用途 with; empty for the blank entry and headings.</summary>
        public string Text { get; }

        /// <summary>What the dropdown row reads.</summary>
        public string Label { get; }

        public bool IsHeading { get; }

        /// <summary>False for a heading, which the ComboBox therefore refuses to select.</summary>
        public bool IsSelectable => !IsHeading;

        /// <summary>The entry that clears 區劃用途.</summary>
        public static ZoneUseChoice Blank { get; } = new ZoneUseChoice("", "（未填）", false);

        public static ZoneUseChoice Of(string use) => new ZoneUseChoice(use, use, false);

        public static ZoneUseChoice Heading(string label) => new ZoneUseChoice("", label, true);

        public override string ToString() => Text;
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
        private string _buildingUse;
        private string _spannedFloors;
        private string _linksRefugeFloor;
        private string _cannotBeSubdivided;

        public FireReviewZoneRowViewModel(FireReviewZoneRow source, int? derivedFloorNumber = null, string buildingUse = null)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            DerivedFloorNumber = derivedFloorNumber;
            _use = source.Use ?? "";
            _sprinklered = TextOf(source.Sprinklered);
            _floorNumber = TextOf(source.FloorNumber);
            _buildingUse = buildingUse ?? "";
            _spannedFloors = TextOf(source.SpannedFloors);
            _linksRefugeFloor = TextOf(source.LinksRefugeFloor);
            _cannotBeSubdivided = TextOf(source.CannotBeSubdivided);
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

        /// <summary>
        /// 防火檢討_區劃用途. Still free text — the box only offers the words that change an answer:
        /// the 第79條之2 垂直區劃, and the six 第79條之1 names (see <see cref="ZoneUses"/>).
        /// </summary>
        public string Use
        {
            get => _use;
            set
            {
                if (!Set(ref _use, value)) return;
                Raise(nameof(LimitText));
                Raise(nameof(IsAtrium));
                Raise(nameof(IsArticle79_1Use));
                Raise(nameof(CanEditCannotBeSubdivided));
            }
        }

        /// <summary>
        /// True while this row's 區劃用途 is 挑空 — the only use 第79條之2第3項 is written for, and so
        /// the only one whose 連跨樓層數 and 避難層通達 boxes mean anything. Follows the box, not the
        /// model, so switching a row to 挑空 lights the two columns up before anything is written.
        /// </summary>
        public bool IsAtrium => string.Equals(_use, ZoneUses.Atrium, StringComparison.Ordinal);

        /// <summary>
        /// True while this row's 區劃用途 is one of the six uses 第79條之1 names, the only ones whose
        /// 無法區劃分隔 box means anything. Follows the box rather than the model, exactly like
        /// <see cref="IsAtrium"/>, so switching a row to 觀眾席 opens that column before anything is
        /// written.
        /// </summary>
        public bool IsArticle79_1Use => ZoneUses.IsArticle79_1Use(_use);

        /// <summary>The heading above the 第79條之2 uses, which are exempt from the limit outright.</summary>
        internal const string VerticalCompartmentHeading = "── 第79條之2 垂直區劃（免面積上限） ──";

        /// <summary>
        /// The heading above 第79條之1's six uses. It says the limit still applies, because that is the
        /// difference between the two sections and the whole reason they are drawn apart (決議 11).
        /// </summary>
        internal const string Article79_1Heading = "── 第79條之1（上限仍適用，須人工確認） ──";

        private static readonly IReadOnlyList<ZoneUseChoice> Choices = BuildUseChoices();

        /// <summary>
        /// What the 區劃用途 box offers, in two labelled sections; anything else can still be typed in.
        /// One shared list — every row offers the same words, and the entries hold no row state.
        /// </summary>
        public IReadOnlyList<ZoneUseChoice> UseChoices => Choices;

        private static IReadOnlyList<ZoneUseChoice> BuildUseChoices()
        {
            var choices = new List<ZoneUseChoice> { ZoneUseChoice.Blank, ZoneUseChoice.Heading(VerticalCompartmentHeading) };
            choices.AddRange(ZoneUses.VerticalCompartments.Select(ZoneUseChoice.Of));
            choices.Add(ZoneUseChoice.Heading(Article79_1Heading));
            choices.AddRange(ZoneUses.Article79_1Uses.Select(ZoneUseChoice.Of));
            return choices;
        }

        /// <summary>防火檢討_自動滅火設備 — doubles the area limit in both 第79條 and 第83條.</summary>
        public string Sprinklered
        {
            get => _sprinklered;
            set
            {
                if (Set(ref _sprinklered, value)) Raise(nameof(LimitText));
            }
        }

        /// <summary>
        /// 建築物用途類組 as the 專案資訊 tab currently holds it, because 第83條第一款、第二款 double
        /// their limit for Ｈ－２組. Pushed in by the panel; not written from this row.
        /// </summary>
        public string BuildingUse
        {
            get => _buildingUse;
            set
            {
                if (Set(ref _buildingUse, value)) Raise(nameof(LimitText));
            }
        }

        /// <summary>
        /// The limit this zone would be judged against, so the consequence of each box is visible
        /// before the review runs — including which article is deciding, which the storey picks.
        /// </summary>
        public string LimitText => Limit.Description;

        /// <summary>Built from what the row currently holds, so an unsaved edit shows its effect.</summary>
        internal ZoneAreaLimit Limit =>
            ZoneAreaLimit.ForZone(IntegerOf(_floorNumber), YesNoOf(_sprinklered), _buildingUse, _use);

        /// <summary>防火檢討_所在樓層序 — 第70條 counts storeys from the top with it.</summary>
        public string FloorNumber
        {
            get => _floorNumber;
            set
            {
                if (Set(ref _floorNumber, value))
                {
                    Raise(nameof(FloorNumberDiffers));
                    Raise(nameof(LimitText));
                }
            }
        }

        /// <summary>
        /// 防火檢討_連跨樓層數 — 第79條之2第3項第二款「連跨樓層數在三層以下」. Blank means nobody said,
        /// and so does 0: a Revit Integer parameter has no blank state, so a number below 1 is read
        /// as 未填 rather than let 「連跨 0 層」 pass 「三層以下」 (see
        /// <see cref="AtriumExemption.StatedSpannedFloors"/>).
        /// </summary>
        public string SpannedFloors
        {
            get => _spannedFloors;
            set => Set(ref _spannedFloors, value);
        }

        /// <summary>防火檢討_避難層通達 — 第79條之2第3項第一款之「避難層通達其直上層或直下層」.</summary>
        public string LinksRefugeFloor
        {
            get => _linksRefugeFloor;
            set => Set(ref _linksRefugeFloor, value);
        }

        /// <summary>
        /// 防火檢討_無法區劃分隔 — （丙）of 第79條之1: whether this 區劃 is a part that, by the
        /// building's construction or its equipment, cannot be subdivided. Only a designer can state
        /// it, which is why the tool asks rather than derives it (第79條之1規格 §6).
        /// </summary>
        /// <remarks>
        /// Offered as the same 空白／是／否 list the other YESNO columns use, not as a tick box. A tick
        /// box has two states and this field has three that the review tells apart: 未填 is 資料不足,
        /// 否 is 不適用 (第79條之1規格 §3.5). With a tick box, clearing an accidental 是 back to 未填
        /// would be impossible from the panel — unticking writes 否, which is a different answer.
        /// </remarks>
        public string CannotBeSubdivided
        {
            get => _cannotBeSubdivided;
            set
            {
                if (Set(ref _cannotBeSubdivided, value)) Raise(nameof(CanEditCannotBeSubdivided));
            }
        }

        /// <summary>
        /// Whether the 無法區劃分隔 box takes an edit: only for the six uses 第79條之1 names, because
        /// the field has no meaning for any other 區劃 and a fillable box would suggest it did
        /// (第79條之1規格 §7.3). A row that already holds a value stays editable whatever its use, so a
        /// value left behind by a since-changed 用途 can still be cleared here rather than only in Revit.
        /// </summary>
        public bool CanEditCannotBeSubdivided => IsArticle79_1Use || _cannotBeSubdivided.Length > 0;

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

            // 第79條之2第3項. The typed number goes through StatedSpannedFloors before it is compared
            // and before it is written, so a 0 or a negative is an erasure rather than a 連跨 0 層
            // that would silently satisfy 「三層以下」.
            var spanned = AtriumExemption.StatedSpannedFloors(IntegerOf(_spannedFloors));
            if (spanned != Source.SpannedFloors)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.SpannedFloors, TextOf(spanned));
            }

            if (YesNoOf(_linksRefugeFloor) != Source.LinksRefugeFloor)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.LinksRefugeFloor, YesNoParameterText(YesNoOf(_linksRefugeFloor)));
            }

            // 第79條之1. Written for whatever the box holds, including on a row whose 用途 is no longer
            // one of the six: that is how a value left behind by a changed 用途 gets cleared.
            if (YesNoOf(_cannotBeSubdivided) != Source.CannotBeSubdivided)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.CannotBeSubdivided, YesNoParameterText(YesNoOf(_cannotBeSubdivided)));
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

        /// <summary>
        /// 建築物用途類組 — 第83條第一款、第二款 double their 區劃 area limit for Ｈ－２組. Still free
        /// text, because no list of ours decides what a building is; the box offers 第3-3條's codes
        /// because those are the values the review can read (see <see cref="BuildingUseGroups"/>),
        /// and any spelling of one of them is read as that group.
        /// </summary>
        public string BuildingUse
        {
            get => _buildingUse;
            set
            {
                if (Set(ref _buildingUse, value)) Raise(nameof(BuildingUseNote));
            }
        }

        /// <summary>What the 用途類組 box offers; anything else can still be typed in.</summary>
        public IReadOnlyList<string> BuildingUseChoices { get; } =
            new[] { "" }.Concat(BuildingUseGroups.All).ToList();

        /// <summary>
        /// How the review will read the box — said out loud, because the box takes any text and only
        /// 第3-3條's codes change an answer. A respelled group says so rather than being silently
        /// rewritten; text that names no group says what the consequence is.
        /// </summary>
        public string BuildingUseNote
        {
            get
            {
                var typed = (_buildingUse ?? "").Trim();
                if (typed.Length == 0)
                    return "第83條第一款、第二款的Ｈ－２組但書會讀它；未填時十一層以上的區劃面積判「資料不足」。";

                var code = BuildingUseGroups.Canonical(typed);
                if (code is null)
                    return $"「{typed}」不是第3-3條的使用類組，檢討時會當成非Ｈ－２組，十一層以上的區劃面積依較嚴的上限判定。";

                return BuildingUseGroups.IsRespelled(typed)
                    ? $"讀作 {code}（全形、各種破折號與「類」「第」「組」字樣都認得，檢討的證據欄會同時記下你填的字）。"
                    : $"讀作 {code}。";
            }
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
