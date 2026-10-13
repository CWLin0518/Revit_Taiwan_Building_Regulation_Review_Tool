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
    /// One cell the panel will not write, and why — in the user's words, not the Revit API's.
    /// </summary>
    /// <remarks>
    /// 驗證清單 C-05。帶著元素與參數名，MCP 的 <c>ignored</c> 才能把這個理由放到對的那一筆上；只回
    /// 一句話的話，`fire_review_set_parameters` 只能給出「值與模型相同，或面板不會問這個欄位」那句
    /// 預設理由，對一個做不到的清除來說是錯的。
    /// </remarks>
    internal sealed class FireReviewBlockedEdit
    {
        public FireReviewBlockedEdit(string elementUniqueId, string parameterName, string name, string reason)
        {
            ElementUniqueId = elementUniqueId;
            ParameterName = parameterName;
            Name = name;
            Reason = reason;
        }

        public string ElementUniqueId { get; }
        public string ParameterName { get; }

        /// <summary>The row as the panel lists it, e.g. a 區劃 name or 專案資訊.</summary>
        public string Name { get; }

        public string Reason { get; }

        public override string ToString() => $"{Name}：{Reason}";
    }

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
        // 取自 Application 的同一組常數，而不是在這裡再打一次：下沉後的
        // FireReviewParameterLimits.YesNoCellProblem 要靠「是／否」逐字相符才認得出一格是不是空的，
        // 兩邊各寫一份遲早會對不起來（C-05）。
        internal const string YesText = FireReviewParameterLimits.YesText;
        internal const string NoText = FireReviewParameterLimits.NoText;

        public IReadOnlyList<string> YesNoChoices { get; } = new[] { "", YesText, NoText };

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>The edits this row would make, or nothing when the user changed nothing.</summary>
        public abstract IEnumerable<FireReviewParameterEdit> Edits();

        /// <summary>
        /// Cells whose current content cannot reach the model, so 寫入模型 can say so before it writes
        /// instead of the writer refusing them afterwards.
        /// </summary>
        /// <remarks>
        /// 驗證清單 C-05：使用者把「所在樓層序」或「自動滅火設備」清空再按「寫入模型」，拿到的是一句
        /// 看不懂的「資料型別（Integer）無法接受這個值。」——而且是按下去之後才看到。Revit API 沒有
        /// 「取消設值」，這兩類欄位本來就清不回未填（<see cref="FireReviewParameterLimits"/>），所以
        /// 面板在產生編輯時就認出這種格子：不產生那筆註定失敗的編輯，但一定要把它列出來講清楚。
        /// 預設沒有這種格子——只有存著 Integer／YESNO 的列需要覆寫。
        /// </remarks>
        public virtual IEnumerable<FireReviewBlockedEdit> Blocked() => Array.Empty<FireReviewBlockedEdit>();

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

        /// <summary>
        /// The entry that clears 區劃用途. Its label says what an empty box means rather than merely
        /// that it is empty: a 區劃 outside the two sections below needs no 用途 at all, and the field
        /// reads as 一般區劃 either way, so leaving it blank is an answer and not an omission.
        /// </summary>
        public static ZoneUseChoice Blank { get; } = new ZoneUseChoice("", "（未填＝一般區劃，不必填）", false);

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

        /// <summary>
        /// The heading above the 第79條之2 uses, which are exempt from the limit outright. It names the
        /// second consequence too: 昇降機道 and 管道間 do not merely escape the area limit, they put
        /// that 區劃's 防火設備 under 第79條之2第1項's 遮煙性能 and 維修門時效 requirements
        /// (<see cref="Checks.VerticalCompartmentInputs.UseOf"/>). Someone choosing a word here is
        /// switching checks on, not only off, and the list is the only place that says so.
        /// </summary>
        internal const string VerticalCompartmentHeading = "── 第79條之2 垂直區劃（免面積上限，另有附加檢討） ──";

        /// <summary>
        /// The heading above 第79條之1's six uses. It says the limit still applies, because that is the
        /// difference between the two sections and the whole reason they are drawn apart (決議 11).
        /// </summary>
        internal const string Article79_1Heading = "── 第79條之1（上限仍適用，須人工確認） ──";

        /// <summary>
        /// What the 區劃用途 box is for, said once and shared by the cell's ToolTip and the batch
        /// window so the two can never drift apart. Three things, in the order someone filling the
        /// panel needs them: that most 區劃 need nothing here, what the two sections actually do, and
        /// that a near miss is silent. The last one is the reason this text exists at all — the field
        /// is compared 逐字 (<see cref="ZoneUses.IsVerticalCompartment"/>), so 「電梯井」 or 「梯間」
        /// reads as an ordinary 區劃: the area limit is applied and 第79條之2's extra checks never run,
        /// with nothing on the report to say a word was misspelled
        /// (docs/regulations/zone-use-vocabulary.md §2.1).
        /// </summary>
        internal const string UseHintText =
            "一般區劃不必填，留空即可——空白就是「一般區劃，照面積上限檢討」。\n\n" +
            "只有兩種情形需要填，且必須與清單逐字相符：\n" +
            "• 第79條之2 的五種垂直區劃（挑空、昇降階梯間、樓梯間、昇降機道、管道間）：" +
            "第79條與第83條的面積上限都免適用；填「昇降機道」或「管道間」還會另外開啟" +
            "第79條之2第1項的附加檢討（防火設備的遮煙性能、維修門的一小時防火時效）。\n" +
            "• 第79條之1 的六種用途（觀眾席、生產線、教室、體育館、零售市場、停車空間）：" +
            "面積上限照樣適用，數字不變，只多一列待人工確認的免除判定，並開放「無法區劃分隔」欄。\n\n" +
            "其餘用途可自行輸入，不影響判定，只會印在檢討表的證據欄位。\n" +
            "注意：填「電梯井」「梯間」這類近似字不會比中，該區劃會被當成一般區劃檢討，" +
            "附加檢討也不會跑，而且不會有任何警告。";

        /// <summary>
        /// <see cref="UseHintText"/> for the cell's ToolTip. An instance property because the
        /// DataGridCell's DataContext is the row, and one binding keeps the text in a single place.
        /// </summary>
        public string UseHint => UseHintText;

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
        /// 否 is 不適用 (第79條之1規格 §3.5). A tick box could not even show which of the three a row
        /// holds — an unticked box would read as 否 on a row nobody has answered, silently answering
        /// for the designer — and its only two gestures both write. The list at least shows 未填 as
        /// 未填 and lets the user leave it that way.
        /// <para>
        /// What the list still cannot do is take a written value back to 未填: Revit's YESNO
        /// parameter is an Integer and the API has no 「取消設值」, so the panel blocks that edit and
        /// says so (驗證清單 C-05, <see cref="FireReviewParameterLimits"/>). Clearing is a Revit-side
        /// job — deleting the parameter's value on the Area itself.
        /// </para>
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
        /// (第79條之1規格 §7.3).
        /// </summary>
        /// <remarks>
        /// A row that already holds a value stays editable whatever its 用途, because 是 and 否 are
        /// the one correction the panel can still offer on a value left behind by a since-changed
        /// 用途. It is no longer offered as a way to clear that value: Revit cannot take a YESNO
        /// parameter back to 未填, so the panel blocks a blank here and says the clearing has to be
        /// done in Revit (驗證清單 C-05). Disabling the box instead would leave the stale value with
        /// no route at all from the panel.
        /// <para>
        /// A stale value on a non-qualifying 用途 does not change a verdict —
        /// <c>Article79_1Exemption.For</c> reads the 用途 and answers 不適用 for all three states —
        /// but it is still recorded as that result's <c>zone.cannotBeSubdivided</c> evidence, so
        /// being able to correct it is worth keeping.
        /// </para>
        /// </remarks>
        public bool CanEditCannotBeSubdivided => IsArticle79_1Use || _cannotBeSubdivided.Length > 0;

        public string MissingParameters => Source.MissingParameters.Count == 0
            ? ""
            : "缺少參數：" + string.Join("、", Source.MissingParameters);

        public bool HasMissingParameters => Source.MissingParameters.Count > 0;

        public override IEnumerable<FireReviewParameterEdit> Edits()
        {
            if (!Same(_use, Source.Use))
                yield return FireReviewParameterEdit.OfText(Source.ElementUniqueId, ReviewInputSources.ZoneUse, _use);

            // 這四格都是 YESNO／Integer，清不回未填（C-05），所以先問 Blocked 認不認得這一格：認得就
            // 不產生那筆註定被寫入器拒絕的編輯，由 Blocked 去把理由說給使用者聽。
            if (YesNoOf(_sprinklered) != Source.Sprinklered && SprinkleredProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.Sprinklered, YesNoParameterText(YesNoOf(_sprinklered)));
            }

            if (IntegerOf(_floorNumber) != Source.FloorNumber && FloorNumberProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FloorNumber, TextOf(IntegerOf(_floorNumber)));
            }

            if (YesNoOf(_linksRefugeFloor) != Source.LinksRefugeFloor && LinksRefugeFloorProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.LinksRefugeFloor, YesNoParameterText(YesNoOf(_linksRefugeFloor)));
            }

            // 第79條之1. Written for whatever the box holds, including on a row whose 用途 is no longer
            // one of the six: that is how a value left behind by a changed 用途 gets corrected here
            // rather than only in Revit. Correcting it means writing 是 or 否 — taking it back to 未填
            // is what C-05 found Revit cannot do, and Blocked says so instead of pretending.
            if (YesNoOf(_cannotBeSubdivided) != Source.CannotBeSubdivided && CannotBeSubdividedProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.CannotBeSubdivided, YesNoParameterText(YesNoOf(_cannotBeSubdivided)));
            }
        }

        // 這四格的攔阻判斷住在 Application（FireReviewParameterLimits），面板只負責說出哪一格、
        // 現在存著什麼、使用者打了什麼。規則下沉後才測得到——測試專案不 reference 這個外掛專案。
        private string SprinkleredProblem =>
            FireReviewParameterLimits.YesNoCellProblem("自動滅火設備", Source.Sprinklered, _sprinklered);

        private string FloorNumberProblem =>
            FireReviewParameterLimits.IntegerCellProblem("所在樓層序", Source.FloorNumber, _floorNumber);

        private string LinksRefugeFloorProblem =>
            FireReviewParameterLimits.YesNoCellProblem("避難層通達", Source.LinksRefugeFloor, _linksRefugeFloor);

        private string CannotBeSubdividedProblem =>
            FireReviewParameterLimits.YesNoCellProblem("無法區劃分隔", Source.CannotBeSubdivided, _cannotBeSubdivided);

        public override IEnumerable<FireReviewBlockedEdit> Blocked()
        {
            if (SprinkleredProblem != null)
                yield return Block(ReviewInputSources.Sprinklered, SprinkleredProblem);
            if (FloorNumberProblem != null)
                yield return Block(ReviewInputSources.FloorNumber, FloorNumberProblem);
            if (LinksRefugeFloorProblem != null)
                yield return Block(ReviewInputSources.LinksRefugeFloor, LinksRefugeFloorProblem);
            if (CannotBeSubdividedProblem != null)
                yield return Block(ReviewInputSources.CannotBeSubdivided, CannotBeSubdividedProblem);
        }

        private FireReviewBlockedEdit Block(string parameterName, string reason) =>
            new FireReviewBlockedEdit(Source.ElementUniqueId, parameterName, DisplayName, reason);
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

        /// <summary>
        /// 驗證清單 C-07：三態分開講。原本只要值不是 是 就說「會判資料不足」，但明確填 否 時四條規則的
        /// 適用條件是已知為假，每一列都判「不適用」而不是資料不足——而「不適用」在檢討表上讀起來像
        /// 「沒有要檢討的事」，與這句紅字想傳達的意思正好相反，所以那一句得點明不適用不代表合格。
        /// </summary>
        public string FireResistiveWarning => FireResistiveConstructionNotice.For(YesNoOf(_fireResistive));

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
            if (YesNoOf(_fireResistive) != Source.FireResistiveConstruction && FireResistiveProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FireResistiveConstruction,
                    YesNoParameterText(YesNoOf(_fireResistive)));
            }

            if (!Same(_buildingUse, Source.BuildingUse))
                yield return FireReviewParameterEdit.OfText(Source.ElementUniqueId, ReviewInputSources.BuildingUse, _buildingUse);

            if (IntegerOf(_floorsAboveGround) != Source.FloorsAboveGround && FloorsAboveGroundProblem == null)
            {
                yield return FireReviewParameterEdit.OfText(
                    Source.ElementUniqueId, ReviewInputSources.FloorsAboveGround, TextOf(IntegerOf(_floorsAboveGround)));
            }
        }

        // 防火構造建築物 是 YESNO、地上層數 是 Integer，兩者都清不回未填（C-05）。建築物用途類組是
        // 文字參數，設成空字串就真的是空白，所以不在這裡。
        private string FireResistiveProblem => FireReviewParameterLimits.YesNoCellProblem(
            "防火構造建築物", Source.FireResistiveConstruction, _fireResistive);

        private string FloorsAboveGroundProblem => FireReviewParameterLimits.IntegerCellProblem(
            "地上層數", Source.FloorsAboveGround, _floorsAboveGround);

        public override IEnumerable<FireReviewBlockedEdit> Blocked()
        {
            if (FireResistiveProblem != null)
                yield return Block(ReviewInputSources.FireResistiveConstruction, FireResistiveProblem);
            if (FloorsAboveGroundProblem != null)
                yield return Block(ReviewInputSources.FloorsAboveGround, FloorsAboveGroundProblem);
        }

        private FireReviewBlockedEdit Block(string parameterName, string reason) =>
            new FireReviewBlockedEdit(Source.ElementUniqueId, parameterName, "專案資訊", reason);
    }
}
