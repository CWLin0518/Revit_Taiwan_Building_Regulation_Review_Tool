using System;
using System.Collections.Generic;
using BuildingRegulationReview.Domain.ReviewPackages;

namespace BuildingRegulationReview.Application.ReviewPackages;

public interface IReviewPackageRepository
{
    ReviewPackage? Get(Guid packageId);
    IReadOnlyList<ReviewPackage> GetAll();
    void Save(ReviewPackage package);
    bool Delete(Guid packageId);
}
