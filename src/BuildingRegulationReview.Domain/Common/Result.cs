using System;

namespace BuildingRegulationReview.Domain.Common;

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new ArgumentException("A successful result cannot contain an error.", nameof(error));
        if (!isSuccess && error == Error.None)
            throw new ArgumentException("A failed result must contain an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Error error) => Result<T>.Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(true, Error.None) => _value = value;
    private Result(Error error) : base(false, error) => _value = default;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}

