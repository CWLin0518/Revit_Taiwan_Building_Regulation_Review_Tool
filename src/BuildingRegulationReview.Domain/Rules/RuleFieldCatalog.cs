using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules.Expressions;

namespace BuildingRegulationReview.Domain.Rules;

/// <summary>
/// One field a rule expression may read. A field is the only way data enters an expression: there
/// are no variables, calls or member access beyond this whitelist (spec 11.2 "白名單欄位").
/// </summary>
public sealed class RuleFieldDefinition
{
    public RuleFieldDefinition(string name, RuleValueType type, string description, IEnumerable<RuleCategory> availableIn)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Field name is required.", nameof(name));
        var trimmed = name.Trim();
        if (!IsWellFormedName(trimmed))
            throw new ArgumentException($"Field name '{trimmed}' must be scope.name made of letters, digits and underscores.", nameof(name));
        if (availableIn is null) throw new ArgumentNullException(nameof(availableIn));

        var categories = availableIn.Distinct().ToList();
        if (categories.Count == 0) throw new ArgumentException("A field must be available to at least one rule category.", nameof(availableIn));

        Name = trimmed;
        Type = type;
        Description = description?.Trim() ?? string.Empty;
        AvailableIn = new ReadOnlyCollection<RuleCategory>(categories);
    }

    public string Name { get; }
    public RuleValueType Type { get; }
    public string Description { get; }

    /// <summary>The rule categories whose subjects can supply this field.</summary>
    public IReadOnlyList<RuleCategory> AvailableIn { get; }

    public bool IsAvailableIn(RuleCategory category) => AvailableIn.Contains(category);

    private static bool IsWellFormedName(string name)
    {
        var parts = name.Split('.');
        return parts.Length >= 2 && parts.All(part =>
            part.Length > 0 &&
            (char.IsLetter(part[0]) || part[0] == '_') &&
            part.All(c => char.IsLetterOrDigit(c) || c == '_'));
    }
}

/// <summary>The whitelist of fields rule expressions may read.</summary>
public sealed class RuleFieldCatalog
{
    private readonly Dictionary<string, RuleFieldDefinition> _byName;

    public RuleFieldCatalog(IEnumerable<RuleFieldDefinition> fields)
    {
        if (fields is null) throw new ArgumentNullException(nameof(fields));

        var list = fields.ToList();
        if (list.Any(x => x is null)) throw new ArgumentException("The catalog cannot hold a missing field.", nameof(fields));
        var duplicate = list.GroupBy(x => x.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Field '{duplicate.Key}' is defined more than once.", nameof(fields));

        Fields = new ReadOnlyCollection<RuleFieldDefinition>(list);
        _byName = list.ToDictionary(x => x.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<RuleFieldDefinition> Fields { get; }

    public RuleFieldDefinition? Find(string name) =>
        name is not null && _byName.TryGetValue(name.Trim(), out var field) ? field : null;

    /// <summary>
    /// The fields the review can supply today. <c>building.*</c> and <c>zone.*</c> describe the
    /// compartment and are open to every category; <c>element.*</c> only exists for fire-resistance
    /// subjects, <c>opening.*</c> only for opening-protection subjects, <c>junction.*</c> only for
    /// compartment-continuity subjects and <c>shaft.*</c> only for 第79條之2 垂直區劃 subjects.
    /// </summary>
    public static RuleFieldCatalog Default { get; } = CreateDefault();

    private static RuleFieldCatalog CreateDefault()
    {
        var all = new[]
        {
            RuleCategory.CompartmentArea,
            RuleCategory.FireResistance,
            RuleCategory.OpeningProtection,
            RuleCategory.CompartmentContinuity,
            RuleCategory.VerticalCompartment
        };
        var element = new[] { RuleCategory.FireResistance };
        var opening = new[] { RuleCategory.OpeningProtection };
        var junction = new[] { RuleCategory.CompartmentContinuity };
        var shaft = new[] { RuleCategory.VerticalCompartment };
        var boolean = RuleValueType.Boolean;
        var text = RuleValueType.Text;
        var number = RuleValueType.Quantity(ReviewUnit.None);

        return new RuleFieldCatalog(new[]
        {
            new RuleFieldDefinition("building.fireResistiveConstruction", boolean, "是否為防火構造建築物", all),
            new RuleFieldDefinition("building.use", text, "建築物用途類組", all),
            new RuleFieldDefinition("building.floorsAboveGround", number, "地上層數", all),
            new RuleFieldDefinition("building.height", RuleValueType.Quantity(ReviewUnit.Meter), "建築物高度", all),

            new RuleFieldDefinition("zone.id", text, "區劃 Zone ID", all),
            new RuleFieldDefinition("zone.use", text, "區劃用途", all),
            new RuleFieldDefinition("zone.area", RuleValueType.Quantity(ReviewUnit.SquareMeter), "區劃面積（Revit Area）", all),
            new RuleFieldDefinition("zone.sprinklered", boolean, "是否設有自動滅火設備", all),
            new RuleFieldDefinition("zone.levelName", text, "所在樓層名稱", all),
            new RuleFieldDefinition("zone.floorNumber", number, "所在樓層序（地上為正、地下為負）", all),

            // 第83條 第一款至第三款以室內裝修的耐燃等級決定區劃面積上限（100／200／500 ㎡）。等級是
            // 設計者宣告的事實，不是模型量得的，所以是一個輸入欄位；寫不出等級的區劃落回第一款的
            // 100 ㎡，沒有填的區劃則是資料不足，不會被當成符合第一款（規格 11.3）。
            new RuleFieldDefinition("zone.interiorFinish", text,
                "由區劃內牆面與天花板類型推導的室內裝修耐燃等級（無／耐燃一級／耐燃一級含底材）", all),

            new RuleFieldDefinition("element.category", text, "構件類別（Walls／Columns／StructuralFraming／Floors）", element),
            new RuleFieldDefinition("element.typeName", text, "構件 Type 名稱", element),
            new RuleFieldDefinition("element.isCompartmentBoundary", boolean, "是否構成區劃邊界", element),
            new RuleFieldDefinition("element.isStructural", boolean, "是否為結構構件", element),
            new RuleFieldDefinition("element.providedFireRating", RuleValueType.Quantity(ReviewUnit.Minute), "設計／認證防火時效", element),

            new RuleFieldDefinition("opening.kind", text, "開口種類（Door／Window／CurtainPanel）", opening),
            new RuleFieldDefinition("opening.isHosted", boolean, "是否為 Hosted 開口", opening),
            new RuleFieldDefinition("opening.hostUniqueId", text, "Host 牆 UniqueId", opening),
            new RuleFieldDefinition("opening.hostIsCompartmentBoundary", boolean, "Host 牆是否為區劃邊界", opening),
            new RuleFieldDefinition("opening.area", RuleValueType.Quantity(ReviewUnit.SquareMeter), "開口面積", opening),
            new RuleFieldDefinition("opening.providedFireProtection", text, "設計防火保護（是／否）", opening),

            // 防火區劃與帷幕牆交接（docs/regulations/curtain-wall-fire-compartment.md §5.2）。
            // 主體是「交接處」而非單一元素：一個交接處由區劃牆或區劃樓地板、帷幕牆與兩者相交
            // 的那一段牆面共同構成，因此長度、高度、突出深度都是交接處自己的量測值。
            new RuleFieldDefinition("junction.kind", text, "交接種類（WallToCurtainWall／FloorToCurtainWall／CurtainPanelOther）", junction),
            new RuleFieldDefinition("junction.zoneId", text, "所屬區劃 Zone ID", junction),
            new RuleFieldDefinition("junction.curtainWallUniqueId", text, "帷幕牆 UniqueId", junction),
            new RuleFieldDefinition("junction.hostUniqueId", text, "區劃牆或區劃樓地板 UniqueId", junction),
            new RuleFieldDefinition("junction.hostLegalReference", text, "區劃來源條文（第79條／第83條）", junction),
            new RuleFieldDefinition("junction.hostRequiredFireRating", RuleValueType.Quantity(ReviewUnit.Minute), "區劃牆或樓地板之要求防火時效", junction),
            new RuleFieldDefinition("junction.minFireRating", RuleValueType.Quantity(ReviewUnit.Minute), "交接帶內嵌板之最小設計防火時效", junction),
            new RuleFieldDefinition("junction.continuousFireRatedLength", RuleValueType.Quantity(ReviewUnit.Meter), "交點兩側連續具時效之外牆面長度總和", junction),
            new RuleFieldDefinition("junction.continuousFireRatedHeight", RuleValueType.Quantity(ReviewUnit.Meter), "層間連續具時效之外牆面高度總和", junction),
            new RuleFieldDefinition("junction.projectionDepth", RuleValueType.Quantity(ReviewUnit.Meter), "區劃牆或樓地板突出帷幕牆外牆面之深度", junction),
            new RuleFieldDefinition("junction.hasUnprotectedOpening", boolean, "交接帶內是否有未受防護開口", junction),

            // 第79條之2 垂直區劃（docs/regulations/vertical-compartment.md §5.2）。主體是「一項要求」
            // 而非一個元素：管道間的同一扇維修門同時被要求一小時防火時效與遮煙性能，而規則的
            // requiredValue 只能寫一個比較，所以受檢主體是（設備, 要求）這一對，由 shaft.requirement
            // 指名是哪一項要求。這也是三條規則彼此互斥、不會落入引擎 Conflict 路徑的原因。
            new RuleFieldDefinition("shaft.requirement", text,
                "受檢要求（HoistwaySmokeSeal／ShaftDoorRating／ShaftDoorSmokeSeal）", shaft),
            new RuleFieldDefinition("shaft.elementUniqueId", text, "受檢防火設備（門窗或嵌板）UniqueId", shaft),
            new RuleFieldDefinition("shaft.providedFireRating", RuleValueType.Quantity(ReviewUnit.Minute),
                "該防火設備之設計／認證防火時效", shaft),
            new RuleFieldDefinition("shaft.providedSmokeProtection", text, "該防火設備是否具遮煙性能（是／否）", shaft),
            new RuleFieldDefinition("shaft.elevatorLobbyProtected", boolean,
                "第2項：昇降機道前是否設有併同區劃、且出入口具遮煙性能之昇降機間", shaft)
        });
    }
}
