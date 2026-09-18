namespace BuildingRegulationReview.Application.Abstractions;

public interface IApplicationModule
{
    string Name { get; }
    void RegisterServices(IServiceRegistry services);
}

