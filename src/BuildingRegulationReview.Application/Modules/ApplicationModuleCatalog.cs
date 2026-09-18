using System;
using System.Collections.Generic;
using BuildingRegulationReview.Application.Abstractions;

namespace BuildingRegulationReview.Application.Modules;

public sealed class ApplicationModuleCatalog
{
    private readonly IReadOnlyList<IApplicationModule> _modules;

    public ApplicationModuleCatalog(IEnumerable<IApplicationModule> modules)
    {
        if (modules is null) throw new ArgumentNullException(nameof(modules));
        _modules = new List<IApplicationModule>(modules);
    }

    public IReadOnlyList<IApplicationModule> Modules => _modules;

    public void RegisterAll(IServiceRegistry services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        foreach (var module in _modules) module.RegisterServices(services);
    }
}

