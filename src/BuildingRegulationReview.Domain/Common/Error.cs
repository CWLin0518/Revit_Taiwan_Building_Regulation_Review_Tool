using System;

namespace BuildingRegulationReview.Domain.Common;

public sealed class Error : IEquatable<Error>
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public Error(string code, string message, string? technicalDetail = null)
    {
        if (code is null) throw new ArgumentNullException(nameof(code));
        if (message is null) throw new ArgumentNullException(nameof(message));

        Code = code;
        Message = message;
        TechnicalDetail = technicalDetail;
    }

    public string Code { get; }
    public string Message { get; }
    public string? TechnicalDetail { get; }

    public bool Equals(Error? other) =>
        other is not null &&
        string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        string.Equals(TechnicalDetail, other.TechnicalDetail, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Error);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Code);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Message);
            hash = (hash * 31) + (TechnicalDetail is null ? 0 : StringComparer.Ordinal.GetHashCode(TechnicalDetail));
            return hash;
        }
    }
    public override string ToString() => string.IsNullOrWhiteSpace(Code) ? Message : $"{Code}: {Message}";
}
