using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// What 防火參數批次設定 is editing: one row per Type, per 區劃 and for the project, each holding
    /// what has been typed so far. No WPF in here — the panel binds to these rows, and the MCP tools
    /// fill the same rows and ask them for their edits, so both write exactly the same values
    /// (docs/adr/0004).
    /// </summary>
    internal sealed class FireReviewParameterDraft
    {
        public FireReviewParameterDraft(FireReviewParameterSet set)
        {
            Set = set ?? throw new ArgumentNullException(nameof(set));
            foreach (var row in set.Types.Rows) Rows.Add(new FireReviewTypeRowViewModel(row));

            Floors = set.Floors;
            foreach (var zone in set.Zones) Zones.Add(new FireReviewZoneRowViewModel(zone, Floors.For(zone.LevelId)));

            if (set.Project != null) Project = new FireReviewProjectViewModel(set.Project);
            PushBuildingUse();
        }

        public FireReviewParameterSet Set { get; }

        public ObservableCollection<FireReviewTypeRowViewModel> Rows { get; } =
            new ObservableCollection<FireReviewTypeRowViewModel>();

        public ObservableCollection<FireReviewZoneRowViewModel> Zones { get; } =
            new ObservableCollection<FireReviewZoneRowViewModel>();

        /// <summary>Null when the document has no readable Project Information.</summary>
        public FireReviewProjectViewModel Project { get; }

        public FloorNumbering Floors { get; }

        /// <summary>
        /// Hands every zone the 用途類組 the project row currently holds, so the limit each zone shows
        /// accounts for 第83條's Ｈ－２組 proviso. Display only — the value is written from the
        /// project row, once, and never from a zone.
        /// </summary>
        public void PushBuildingUse()
        {
            if (Project == null) return;
            foreach (var zone in Zones) zone.BuildingUse = Project.BuildingUse;
        }

        /// <summary>
        /// Fills every zone's 所在樓層序 from the levels, and 地上層數 with the count that goes with it.
        /// Both go into the rows, not into the model: 第70條 divides one by the other, so a storey
        /// number accepted without the matching count would compute a position nobody meant.
        /// </summary>
        /// <returns>What was filled; null when the model has no levels to number storeys by.</returns>
        public FloorDerivation DeriveFloors()
        {
            if (Floors.IsEmpty) return null;

            var filled = 0;
            var unknown = new List<string>();
            foreach (var zone in Zones)
            {
                if (zone.DerivedFloorNumber.HasValue)
                {
                    zone.ApplyDerivedFloorNumber();
                    filled++;
                }
                else unknown.Add(zone.DisplayName);
            }

            if (Project != null) Project.FloorsAboveGround = Floors.FloorsAboveGround.ToString(CultureInfo.InvariantCulture);
            return new FloorDerivation(filled, unknown, Floors.FloorsAboveGround);
        }

        /// <summary>Only what differs from the model, grouped as 寫入模型 reports it.</summary>
        /// <remarks>
        /// 驗證清單 C-05：做不到的清除（以及整數格裡的非數字）不混在 edits 裡送去碰運氣，而是另外收在
        /// Blocked。類型列不會有這種格子——它的是否類欄位走 OfYesNo（勾或不勾，沒有「清除」這個狀態），
        /// 被覆厚度是長度（可以退回 0），其餘是文字參數（空字串就真的是空白）。
        /// </remarks>
        public FireReviewParameterEditBatch CollectEdits() =>
            new FireReviewParameterEditBatch(
                Rows.SelectMany(r => r.Edits()).ToList(),
                Zones.SelectMany(z => z.Edits()).ToList(),
                Project == null ? new List<FireReviewParameterEdit>() : Project.Edits().ToList(),
                Rows.Where(r => r.IsDirty).ToList(),
                Zones.Count(z => z.IsDirty),
                Rows.Count(r => r.PanelKindIsProposed),
                Zones.SelectMany(z => z.Blocked())
                    .Concat(Project == null ? Enumerable.Empty<FireReviewBlockedEdit>() : Project.Blocked())
                    .ToList());
    }

    internal sealed class FloorDerivation
    {
        public FloorDerivation(int filled, IReadOnlyList<string> unknownZones, int floorsAboveGround)
        {
            Filled = filled;
            UnknownZones = unknownZones;
            FloorsAboveGround = floorsAboveGround;
        }

        public int Filled { get; }

        /// <summary>The zones whose level could not be numbered, by display name.</summary>
        public IReadOnlyList<string> UnknownZones { get; }

        public int FloorsAboveGround { get; }
    }

    /// <summary>The edits one press of 寫入模型 sends, and what they reach.</summary>
    internal sealed class FireReviewParameterEditBatch
    {
        public FireReviewParameterEditBatch(
            IReadOnlyList<FireReviewParameterEdit> typeEdits,
            IReadOnlyList<FireReviewParameterEdit> zoneEdits,
            IReadOnlyList<FireReviewParameterEdit> projectEdits,
            IReadOnlyList<FireReviewTypeRowViewModel> dirtyTypes,
            int dirtyZoneCount,
            int proposedPanelKindCount,
            IReadOnlyList<FireReviewBlockedEdit> blocked)
        {
            TypeEdits = typeEdits;
            ZoneEdits = zoneEdits;
            ProjectEdits = projectEdits;
            DirtyTypes = dirtyTypes;
            DirtyZoneCount = dirtyZoneCount;
            ProposedPanelKindCount = proposedPanelKindCount;
            Blocked = blocked ?? new List<FireReviewBlockedEdit>();
        }

        public IReadOnlyList<FireReviewParameterEdit> TypeEdits { get; }
        public IReadOnlyList<FireReviewParameterEdit> ZoneEdits { get; }
        public IReadOnlyList<FireReviewParameterEdit> ProjectEdits { get; }

        public IReadOnlyList<FireReviewParameterEdit> All => TypeEdits.Concat(ZoneEdits).Concat(ProjectEdits).ToList();

        public bool IsEmpty => TypeEdits.Count == 0 && ZoneEdits.Count == 0 && ProjectEdits.Count == 0;

        public IReadOnlyList<FireReviewTypeRowViewModel> DirtyTypes { get; }

        /// <summary>Type parameters reach every instance in the project, not only the view's.</summary>
        public int AffectedInstances => DirtyTypes.Sum(r => r.Source.ProjectInstanceCount);

        public int DirtyZoneCount { get; }

        /// <summary>
        /// 帷幕嵌板 rows whose 嵌板種類 is the tool's proposal, left as it was. A proposal is written
        /// like any other value once 寫入模型 is pressed (決議 16、D3), so it is counted out loud.
        /// </summary>
        public int ProposedPanelKindCount { get; }

        /// <summary>
        /// Cells whose content is not being written because it cannot be (驗證清單 C-05). Never silent:
        /// 寫入模型 names them, and the MCP tool reports them as the reason that field was ignored.
        /// </summary>
        public IReadOnlyList<FireReviewBlockedEdit> Blocked { get; }
    }
}
