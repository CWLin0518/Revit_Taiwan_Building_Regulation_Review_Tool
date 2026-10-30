using System;
using BuildingRegulationReview.Domain.Common;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_CarriesValueWithoutError()
    {
        var result = Result.Success("ready");

        Assert.True(result.IsSuccess);
        Assert.Equal("ready", result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_CarriesErrorAndRejectsValueAccess()
    {
        var error = new Error("setup.invalid", "設定無效");
        var result = Result.Failure<string>(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}

