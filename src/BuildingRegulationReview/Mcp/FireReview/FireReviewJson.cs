using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ReviewPackages;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.FireReview;
using BuildingRegulationReview.Mcp.Json;

namespace BuildingRegulationReview.Mcp.FireReview
{
    /// <summary>
    /// The fire review's results as JSON, built from the same pure objects the review window renders
    /// — <see cref="ReviewTable"/>, <see cref="ReviewReadinessReport"/>, <see cref="ReviewEntryReport"/>
    /// — so an agent reads exactly what a user would see, in a form it can check field by field.
    /// </summary>
    /// <remarks>
    /// Every status is given twice: the enum name (stable, for comparisons) and the Chinese label the
    /// table shows (for reporting). Elements are given by UniqueId and, when it can be read off the
    /// UniqueId, by ElementId — the id other Revit tools and the user work with.
    /// </remarks>
    internal static class FireReviewJson
    {
        public static JsonObject Package(ReviewPackage package, string label) => new JsonObject
        {
            ["packageId"] = JsonValue.Of(package.PackageId),
            ["label"] = label,
            ["status"] = JsonValue.Of(package.Status),
            ["statusText"] = ReviewPackageProgress.Describe(package.Status),
            ["areaPlanUniqueId"] = package.AreaPlanUniqueId,
            ["sourceFloorPlanUniqueId"] = package.SourceFloorPlanUniqueId,
            ["boundaryRevision"] = package.BoundaryRevision,
            ["ruleSetId"] = package.RuleSetId,
            ["ruleSetVersion"] = package.RuleSetVersion,
            ["lastReviewRunId"] = package.LastReviewRunId,
            ["updatedAtUtc"] = JsonValue.Of(package.UpdatedAtUtc)
        };

        public static JsonValue RuleSet(RuleSet rules) => rules == null
            ? JsonValue.Null
            : new JsonObject { ["ruleSetId"] = rules.RuleSetId, ["version"] = rules.Version, ["title"] = rules.Title };

        public static JsonObject Readiness(ReviewReadinessReport report)
        {
            var items = new JsonArray();
            foreach (var item in report.Items)
            {
                items.Add(new JsonObject
                {
                    ["severity"] = JsonValue.Of(item.Severity),
                    ["condition"] = JsonValue.Of(item.Condition),
                    ["conditionText"] = item.ConditionText,
                    ["code"] = item.Code,
                    ["message"] = item.Message,
                    ["fix"] = item.Fix,
                    ["detail"] = item.Detail
                });
            }

            return new JsonObject
            {
                ["canRun"] = report.CanRun,
                ["message"] = report.Message,
                ["needsRuleSetConfirmation"] = report.NeedsRuleSetConfirmation,
                ["ruleSetChanges"] = report.RuleSetChanges,
                // 規則集只回一份：頂層的 ruleSet。它的 title 是一整段兩千多字的說明文，前置檢查這裡再
                // 放一份只是把同一段話送兩次；needsRuleSetConfirmation 與 ruleSetChanges 已經說完前置
                // 檢查關心的事（B-08）。
                ["items"] = items
            };
        }

        /// <summary>
        /// The log, warnings and errors first, cut at <paramref name="limit"/> entries with the cut said
        /// out loud. <c>total</c>、<c>errors</c>、<c>warnings</c> always count every entry, so a reader
        /// can tell from a trimmed log how much it is not seeing (B-04).
        /// </summary>
        public static JsonObject Log(IEnumerable<ReviewLogEntry> entries, int limit = ReviewLogDigest.DefaultLimit)
        {
            var digest = ReviewLogDigest.Of(entries, limit);
            var list = new JsonArray();
            foreach (var entry in digest.Entries)
            {
                list.Add(new JsonObject
                {
                    ["severity"] = JsonValue.Of(entry.Severity),
                    ["stage"] = JsonValue.Of(entry.Stage),
                    ["code"] = entry.Code,
                    ["message"] = entry.UserMessage,
                    ["suggestion"] = entry.Suggestion,
                    ["technicalDetail"] = entry.TechnicalDetail,
                    ["elementUniqueId"] = entry.ElementUniqueId,
                    ["elementId"] = ReviewElementReference.ElementIdOf(entry.ElementUniqueId)
                });
            }

            return new JsonObject
            {
                ["errors"] = digest.Errors,
                ["warnings"] = digest.Warnings,
                ["total"] = digest.Total,
                ["listed"] = digest.Entries.Count,
                ["truncated"] = digest.Truncated,
                ["entries"] = list
            };
        }

        public static JsonObject Counts(ReviewStatusCounts counts) => new JsonObject
        {
            ["total"] = counts.Total,
            ["pass"] = counts.Pass,
            ["fail"] = counts.Fail,
            ["pending"] = counts.Unknown,
            ["notApplicable"] = counts.NotApplicable,
            ["insufficientData"] = counts[ReviewStatus.InsufficientData],
            ["manualReview"] = counts[ReviewStatus.ManualReview],
            ["notRun"] = counts[ReviewStatus.NotRun],
            ["text"] = counts.Text
        };

        /// <summary>
        /// The 檢討表: the verdict, every section with its 統計, and the rows <paramref name="filter"/>
        /// lets through, at most <paramref name="limit"/> of them. The 統計 always count every row —
        /// filtering only decides which rows are listed, exactly as in the window.
        /// </summary>
        public static JsonObject Table(ReviewTable table, ReviewTableFilter filter, int limit, bool includeGroups)
        {
            var marks = CurtainWallMarkNumbers.Assign(table);
            var view = filter.Apply(table, marks);

            var sections = new JsonArray();
            foreach (var sectionView in view.Sections)
            {
                var section = sectionView.Section;
                var json = new JsonObject
                {
                    ["checkType"] = section.CheckType,
                    ["title"] = section.Title,
                    ["status"] = JsonValue.Of(section.Status),
                    ["statusText"] = ReviewStatusText.Label(section.Status),
                    ["verdict"] = JsonValue.Of(section.Verdict),
                    ["verdictText"] = ReviewVerdictText.Label(section.Verdict),
                    ["counts"] = Counts(section.Counts),
                    ["staleCount"] = section.StaleCount,
                    ["shownCount"] = sectionView.ShownCount
                };

                if (includeGroups)
                {
                    var groups = new JsonArray();
                    foreach (var group in section.Groups)
                    {
                        groups.Add(new JsonObject
                        {
                            ["grouping"] = JsonValue.Of(group.Grouping),
                            ["key"] = group.Key,
                            ["label"] = group.Label,
                            ["status"] = JsonValue.Of(group.Status),
                            ["statusText"] = ReviewStatusText.Label(group.Status),
                            ["counts"] = Counts(group.Counts),
                            ["staleCount"] = group.StaleCount
                        });
                    }
                    json["groups"] = groups;
                }

                sections.Add(json);
            }

            // Straight off the table rather than off the view's groups: a 構件 row is grouped both by
            // category and by Type, so walking the groups would list it twice.
            var matching = table.Entries
                .Where(entry => filter.Matches(entry, CurtainWallMarkNumbers.Of(marks, entry.ResultId)))
                .ToList();
            var entries = new JsonArray();
            foreach (var entry in matching.Take(limit))
                entries.Add(Entry(entry, CurtainWallMarkNumbers.Of(marks, entry.ResultId)));

            return new JsonObject
            {
                ["runId"] = JsonValue.Of(table.RunId),
                ["packageId"] = JsonValue.Of(table.PackageId),
                ["state"] = JsonValue.Of(table.Run.State),
                ["startedAtUtc"] = JsonValue.Of(table.Run.StartedAtUtc),
                ["completedAtUtc"] = JsonValue.Of(table.Run.CompletedAtUtc),
                ["ruleSetId"] = table.RuleSetId,
                ["ruleSetVersion"] = table.RuleSetVersion,
                ["verdict"] = JsonValue.Of(table.Verdict),
                ["verdictText"] = ReviewVerdictText.Label(table.Verdict),
                ["counts"] = Counts(table.Counts),
                ["isStale"] = table.IsStale,
                ["staleReasons"] = JsonValue.Array(table.StaleReasons),
                ["sections"] = sections,
                ["filter"] = Filter(filter),
                ["matchedCount"] = matching.Count,
                ["listedCount"] = Math.Min(matching.Count, limit),
                ["truncated"] = matching.Count > limit,
                ["entries"] = entries
            };
        }

        public static JsonObject Entry(ReviewTableEntry entry, string markNumber) => new JsonObject
        {
            ["resultId"] = JsonValue.Of(entry.ResultId),
            ["checkType"] = entry.CheckType,
            ["headline"] = ReviewEntryReport.Headline(entry, markNumber),
            ["status"] = JsonValue.Of(entry.EffectiveStatus),
            ["statusText"] = entry.StatusText,
            ["computedStatus"] = JsonValue.Of(entry.ComputedStatus),
            ["isOverridden"] = entry.IsOverridden,
            ["overrideNeedsReconfirmation"] = entry.OverrideNeedsReconfirmation,
            ["isStale"] = entry.IsStale,
            ["zoneId"] = entry.ZoneId,
            ["zoneName"] = entry.ZoneName,
            ["category"] = entry.CategoryLabel,
            ["typeName"] = entry.TypeName,
            ["openingKind"] = entry.OpeningKind,
            ["markNumber"] = markNumber,
            ["isLinked"] = entry.IsLinked,
            ["elementIds"] = JsonValue.Array(entry.LocateUniqueIds.Select(ReviewElementReference.ElementIdOf).Where(id => id != null)),
            ["uniqueIds"] = JsonValue.Array(entry.LocateUniqueIds),
            ["actual"] = ReviewValueText.Format(entry.ActualValue),
            ["required"] = ReviewValueText.Format(entry.RequiredValue),
            ["ruleId"] = entry.RuleId,
            ["ruleVersion"] = entry.RuleVersion,
            ["legalReference"] = entry.LegalReference,
            ["message"] = entry.Message
        };

        public static JsonArray Detail(IReadOnlyList<ReviewDetailSection> sections)
        {
            var array = new JsonArray();
            foreach (var section in sections.Where(s => !s.IsEmpty))
            {
                var lines = new JsonArray();
                foreach (var line in section.Lines)
                    lines.Add(new JsonObject { ["label"] = line.Label, ["value"] = line.Value, ["emphasis"] = line.Emphasis });
                array.Add(new JsonObject { ["title"] = section.Title, ["lines"] = lines });
            }
            return array;
        }

        public static JsonObject Outcome(FireReviewOutcome outcome)
        {
            var performance = outcome.Performance;
            return new JsonObject
            {
                ["kind"] = JsonValue.Of(outcome.Kind),
                ["message"] = outcome.Message,
                ["error"] = outcome.Error?.Message,
                ["overridesNeedingReconfirmation"] = outcome.CarryOver?.Count(OverrideCarryOverOutcome.NeedsReconfirmation) ?? 0,
                ["performance"] = performance == null ? JsonValue.Null : new JsonObject
                {
                    ["summary"] = performance.Summary,
                    ["candidateCount"] = performance.CandidateCount,
                    ["prescanSeconds"] = performance.Prescan.TotalSeconds,
                    ["reviewSeconds"] = performance.Review.TotalSeconds,
                    ["exceeded"] = performance.Exceeded
                }
            };
        }

        /// <summary>
        /// 寫入的結果。<paramref name="rolledBack"/> 是 dryRun：那一批寫入在工具回傳前就被整個
        /// <c>TransactionGroup</c> 復原了，所以 <c>saved</c> 回 false 才是模型的實情——領域物件
        /// <c>FireReviewSaveResult.Saved</c> 仍然是 true（它看得見自己寫進去的東西），判斷流程讀的也還是
        /// 它，這裡只負責不讓代理把試跑讀成「已存檔」（B-05）。<c>mark.summary</c> 照舊保留：標示確實算過
        /// 一遍，那串數字是驗證要比對的。
        /// </summary>
        public static JsonValue Saved(FireReviewSaveResult saved, bool rolledBack = false)
        {
            if (saved == null) return JsonValue.Null;
            return new JsonObject
            {
                ["saved"] = saved.Saved && !rolledBack,
                ["rolledBack"] = rolledBack,
                ["note"] = rolledBack ? "試跑：計算與標示都已整批復原，模型未變更。" : null,
                ["error"] = saved.Error,
                ["mark"] = saved.Mark == null ? JsonValue.Null : new JsonObject
                {
                    ["summary"] = saved.Mark.Summary,
                    ["isRolledBack"] = saved.Mark.IsRolledBack,
                    ["viewUniqueId"] = saved.Mark.ViewUniqueId,
                    ["viewElementId"] = ReviewElementReference.ElementIdOf(saved.Mark.ViewUniqueId)
                }
            };
        }

        public static JsonObject Filter(ReviewTableFilter filter) => new JsonObject
        {
            ["statuses"] = JsonValue.Array(filter.Bands.Select(band => band.ToString())),
            ["checkType"] = filter.CheckType,
            ["search"] = filter.Search,
            ["staleOnly"] = filter.StaleOnly
        };

        public static JsonObject ParameterSet(FireReviewParameterDraft draft, string scope)
        {
            var types = new JsonArray();
            foreach (var row in draft.Rows) types.Add(TypeRow(row));

            var zones = new JsonArray();
            foreach (var zone in draft.Zones) zones.Add(ZoneRow(zone));

            return new JsonObject
            {
                ["scope"] = scope,
                ["types"] = types,
                ["zones"] = zones,
                ["project"] = draft.Project == null ? JsonValue.Null : ProjectRow(draft.Project),
                ["floors"] = new JsonObject
                {
                    ["floorsAboveGround"] = draft.Floors.IsEmpty ? JsonValue.Null : draft.Floors.FloorsAboveGround,
                    ["warnings"] = JsonValue.Array(draft.Floors.Warnings)
                },
                ["warnings"] = JsonValue.Array(draft.Set.Types.Warnings)
            };
        }

        /// <summary>One Type as the panel's row shows it: what the model holds, what is asked of it, what the clauses derive.</summary>
        public static JsonObject TypeRow(FireReviewTypeRowViewModel row)
        {
            var source = row.Source;
            var derivation = row.Derivation;
            return new JsonObject
            {
                ["typeUniqueId"] = source.TypeUniqueId,
                ["typeElementId"] = ReviewElementReference.ElementIdOf(source.TypeUniqueId),
                ["category"] = JsonValue.Of(source.Category),
                ["categoryLabel"] = source.CategoryLabel,
                ["name"] = source.DisplayName,
                ["instancesInScope"] = source.InstanceCount,
                ["instancesInProject"] = source.ProjectInstanceCount,
                ["dimensionMeters"] = source.DimensionMeters,
                ["isSubstituted"] = row.IsSubstituted,
                ["substitutedFrom"] = row.IsSubstituted ? row.SubstitutionSource : null,
                ["values"] = new JsonObject
                {
                    ["panelKind"] = row.IsCurtainPanel ? row.PanelKind : null,
                    ["panelKindIsProposed"] = row.PanelKindIsProposed,
                    ["material"] = row.Material,
                    ["coverCm"] = row.CoverCm,
                    ["rating"] = row.Rating,
                    ["fireProtection"] = source.ProvidedProtection,
                    ["smokeProtection"] = source.ProvidedSmokeProtection,
                    ["insulation"] = source.ProvidedInsulation
                },
                ["asks"] = new JsonObject
                {
                    ["panelKind"] = row.CanEditPanelKind,
                    ["material"] = row.CanEditMaterial,
                    ["coverCm"] = row.CanEditMaterial && row.NeedsCover,
                    ["rating"] = row.CanEditRating,
                    ["fireProtection"] = row.CarriesProtection && row.IsEditable,
                    ["smokeProtection"] = row.CarriesSmokeProtection && row.IsEditable,
                    ["insulation"] = row.CarriesProtection && row.IsEditable
                },
                ["derived"] = new JsonObject
                {
                    ["kind"] = JsonValue.Of(derivation.Kind),
                    ["rating"] = derivation.HasRating ? derivation.ParameterText : null,
                    ["basis"] = row.DerivedBasis,
                    ["canApply"] = row.CanApplyDerived,
                    ["differsFromRating"] = row.RatingDiffers
                },
                ["missingParameters"] = row.MissingParameters
            };
        }

        public static JsonObject ZoneRow(FireReviewZoneRowViewModel zone)
        {
            var source = zone.Source;
            return new JsonObject
            {
                ["elementUniqueId"] = source.ElementUniqueId,
                ["elementId"] = ReviewElementReference.ElementIdOf(source.ElementUniqueId),
                ["name"] = source.DisplayName,
                ["level"] = source.LevelName,
                ["areaScheme"] = source.AreaSchemeName,
                ["areaSquareMeters"] = source.AreaSquareMeters,
                ["values"] = new JsonObject
                {
                    ["use"] = zone.Use,
                    ["sprinklered"] = source.Sprinklered,
                    ["floorNumber"] = source.FloorNumber,
                    ["linksRefugeFloor"] = source.LinksRefugeFloor,
                    ["cannotBeSubdivided"] = source.CannotBeSubdivided
                },
                ["derivedFloorNumber"] = zone.DerivedFloorNumber,
                ["limit"] = zone.LimitText,
                ["isAtrium"] = zone.IsAtrium,
                ["isArticle79_1Use"] = zone.IsArticle79_1Use,
                ["missingParameters"] = JsonValue.Array(source.MissingParameters)
            };
        }

        public static JsonObject ProjectRow(FireReviewProjectViewModel project)
        {
            var source = project.Source;
            return new JsonObject
            {
                ["elementUniqueId"] = source.ElementUniqueId,
                ["values"] = new JsonObject
                {
                    ["fireResistiveConstruction"] = source.FireResistiveConstruction,
                    ["buildingUse"] = source.BuildingUse,
                    ["floorsAboveGround"] = source.FloorsAboveGround
                },
                ["buildingUseNote"] = project.BuildingUseNote,
                ["fireResistiveWarning"] = project.HasFireResistiveWarning ? project.FireResistiveWarning : null,
                ["missingParameters"] = JsonValue.Array(source.MissingParameters)
            };
        }

        public static JsonObject Edit(FireReviewParameterEdit edit) => new JsonObject
        {
            ["elementUniqueId"] = edit.ElementUniqueId,
            ["elementId"] = ReviewElementReference.ElementIdOf(edit.ElementUniqueId),
            ["parameter"] = edit.ParameterName,
            ["value"] = edit.ToString()
        };
    }
}
