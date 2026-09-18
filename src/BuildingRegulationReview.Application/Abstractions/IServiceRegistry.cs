using System;

namespace BuildingRegulationReview.Application.Abstractions;

public interface IServiceRegistry
{
    void AddSingleton<TService>(TService instance) where TService : class;
    void AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class;
}

