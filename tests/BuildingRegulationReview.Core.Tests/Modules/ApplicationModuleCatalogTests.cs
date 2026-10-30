using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.Abstractions;
using BuildingRegulationReview.Application.Modules;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Modules;

public sealed class ApplicationModuleCatalogTests
{
    [Fact]
    public void RegisterAll_InvokesEveryModuleInOrder()
    {
        var calls = new List<string>();
        var catalog = new ApplicationModuleCatalog(new IApplicationModule[]
        {
            new RecordingModule("setup", calls),
            new RecordingModule("view-package", calls),
        });

        catalog.RegisterAll(new StubRegistry());

        Assert.Equal(new[] { "setup", "view-package" }, calls);
    }

    private sealed class RecordingModule : IApplicationModule
    {
        private readonly IList<string> _calls;
        public RecordingModule(string name, IList<string> calls) { Name = name; _calls = calls; }
        public string Name { get; }
        public void RegisterServices(IServiceRegistry services) => _calls.Add(Name);
    }

    private sealed class StubRegistry : IServiceRegistry
    {
        public void AddSingleton<TService>(TService instance) where TService : class { }
        public void AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class { }
    }
}

