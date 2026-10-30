using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.Revit.WriteBack;

/// <summary>
/// The ownership mark the tool puts on every element it creates, and the only thing that makes an
/// element eligible for update or deletion (spec 10.4: 刪除範圍只限相同 Package ID 的工具生成元素).
/// It holds the <see cref="ManagedElementKey"/> token and the signature of what was last written.
/// </summary>
/// <remarks>
/// The mark lives in Extensible Storage rather than in a shared parameter. A parameter has to be
/// bound to a category before it can be written, and Area Boundary Lines are not a category Revit
/// lets a project parameter reach — so a parameter-only mark would leave exactly the elements whose
/// idempotency matters most with nothing to identify them by. Extensible Storage attaches to any
/// element, needs no project set-up, and cannot be edited away by hand.
/// <para>
/// When <see cref="ManagedElementKey.KeyParameterName"/> and
/// <see cref="ManagedElementKey.SignatureParameterName"/> do happen to exist on an element as
/// writable text parameters, the same values are mirrored into them, so a project that binds them
/// gets the key where a schedule can see it. That mirror is a convenience: nothing reads it back
/// unless the storage is missing.
/// </para>
/// </remarks>
public static class ManagedElementMark
{
    /// <summary>Writes, or overwrites, the mark. Requires an open transaction.</summary>
    public static void Write(Element element, ManagedElementKey key, string signature) =>
        Write(element, key.ToToken(), signature);

    /// <summary>
    /// Marks one of the package's containers — the Drafting View, the colour scheme (spec 10.5
    /// items 3 and 4). The same storage, a token of the other shape.
    /// </summary>
    public static void Write(Element element, ManagedOutputKey key, string signature) =>
        Write(element, key.ToToken(), signature);

    /// <summary>
    /// Marks a red Filled Region of the review view (spec 11.4 item 4), with the Package ID, Run ID and
    /// Zone ID its token carries.
    /// </summary>
    public static void Write(Element element, Application.Reviews.ReviewMarkKey key, string signature) =>
        Write(element, key.ToToken(), signature);

    private static void Write(Element element, string token, string signature)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));

        var schema = ManagedElementSchema.GetOrCreate();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(ManagedElementSchema.KeyField), token);
        entity.Set(schema.GetField(ManagedElementSchema.SignatureField), signature ?? string.Empty);
        element.SetEntity(entity);

        MirrorToParameter(element, ManagedElementKey.KeyParameterName, token);
        MirrorToParameter(element, ManagedElementKey.SignatureParameterName, signature ?? string.Empty);
    }

    /// <summary>
    /// Reads the mark back. Falls back to the text parameters so an element written before the
    /// storage existed is still recognised as the tool's rather than as somebody's hand-drawn work.
    /// </summary>
    public static bool TryRead(Element element, out string keyToken, out string signature)
    {
        keyToken = string.Empty;
        signature = string.Empty;
        if (element is null) return false;

        var schema = ManagedElementSchema.Lookup();
        if (schema is not null)
        {
            var entity = element.GetEntity(schema);
            if (entity is not null && entity.IsValid())
            {
                keyToken = entity.Get<string>(schema.GetField(ManagedElementSchema.KeyField)) ?? string.Empty;
                signature = entity.Get<string>(schema.GetField(ManagedElementSchema.SignatureField)) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(keyToken)) return true;
            }
        }

        keyToken = ReadParameter(element, ManagedElementKey.KeyParameterName) ?? string.Empty;
        signature = ReadParameter(element, ManagedElementKey.SignatureParameterName) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(keyToken);
    }

    /// <summary>
    /// Whether this package may touch the element. Checked again at the moment of deletion, not only
    /// when the preview was built: the model is live and the Editor is modeless, so the answer has to
    /// come from the element in front of us.
    /// </summary>
    public static bool IsOwnedBy(Element element, Guid packageId) =>
        TryRead(element, out var token, out _) && ManagedOwnership.BelongsTo(token, packageId);

    private static void MirrorToParameter(Element element, string parameterName, string value)
    {
        try
        {
            var parameter = element.LookupParameter(parameterName);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String) return;
            parameter.Set(value);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // The mark is already in storage; a parameter that refuses the value changes nothing.
        }
    }

    private static string? ReadParameter(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        return parameter is not null && parameter.HasValue && parameter.StorageType == StorageType.String
            ? parameter.AsString()
            : null;
    }
}

internal static class ManagedElementSchema
{
    internal const string KeyField = "Key";
    internal const string SignatureField = "Signature";

    private static readonly Guid Id = new Guid("5c1f7b2e-0d94-4a36-9a8b-6f1c2d3e4a57");
    private const string Name = "BCR_ManagedElement_v1";

    internal static Schema? Lookup() => Schema.Lookup(Id);

    internal static Schema GetOrCreate()
    {
        var existing = Lookup();
        if (existing is not null) return existing;

        var builder = new SchemaBuilder(Id);
        builder.SetSchemaName(Name);
        builder.SetDocumentation("Ownership mark for elements generated by the fire compartment review tool, schema 1.0.");
        builder.SetVendorId("BCRV");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(KeyField, typeof(string));
        builder.AddSimpleField(SignatureField, typeof(string));
        return builder.Finish();
    }
}
