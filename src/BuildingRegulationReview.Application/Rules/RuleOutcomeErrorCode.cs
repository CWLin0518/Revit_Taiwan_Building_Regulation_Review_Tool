using System;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.Application.Rules;

/// <summary>Maps the engine's non-routine outcomes to the spec 14 rule error codes the log records.</summary>
public static class RuleOutcomeErrorCode
{
    /// <summary>The code to log for this outcome, or null when it is an ordinary verdict.</summary>
    public static string? For(RuleOutcome outcome)
    {
        if (outcome is null) throw new ArgumentNullException(nameof(outcome));

        return outcome.Reason switch
        {
            RuleOutcomeReason.NoRule => ReviewErrorCode.RuleMissing,
            RuleOutcomeReason.Conflict => ReviewErrorCode.RuleConflict,
            RuleOutcomeReason.ComputationFailed => ReviewErrorCode.RuleComputationFailed,
            _ => null
        };
    }
}
