using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Rules;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// The rule set the add-in ships with (<c>Data\fire-review-rules.json</c> next to the DLL). The
    /// file is the authored <see cref="RuleSetDocument"/> shape; it is schema-checked and compiled by
    /// <see cref="RuleSetCompiler"/>, so a broken file stops the review with every problem listed
    /// rather than running on half a rule set (spec 11.1「規則集存在」).
    /// </summary>
    /// <remarks>
    /// Spec 19 item 2 (條文、版本、生效日期) is still open: the shipped file is provisional and says so
    /// in its title. Whoever maintains the rules replaces the file and raises its version; packages
    /// locked to the old version then ask the user before moving (spec 13.1 規則版本).
    /// </remarks>
    internal static class FireReviewRuleSetSource
    {
        public const string FileName = "fire-review-rules.json";

        /// <summary>The jurisdiction a review is evaluated for; rules of another jurisdiction never apply.</summary>
        public const string Jurisdiction = "TW";

        public static string DefaultPath =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Data", FileName);

        public static Result<CompiledRuleSet> Load() => Load(DefaultPath);

        public static Result<CompiledRuleSet> Load(string path)
        {
            if (!File.Exists(path))
            {
                return Result.Failure<CompiledRuleSet>(new Error(ReviewErrorCode.RuleMissing,
                    "找不到外掛的規則檔 " + FileName + "。", Path.GetFileName(path)));
            }

            RuleSetFile file;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8))))
                    file = (RuleSetFile)new DataContractJsonSerializer(typeof(RuleSetFile)).ReadObject(stream);
            }
            catch (Exception exception) when (exception is SerializationException || exception is IOException || exception is InvalidCastException)
            {
                return Result.Failure<CompiledRuleSet>(new Error(ReviewErrorCode.RuleSchemaInvalid,
                    "規則檔不是有效的 JSON。", exception.Message));
            }

            return RuleSetCompiler.Load(file?.ToDocument());
        }

        [DataContract]
        private sealed class RuleSetFile
        {
            [DataMember(Name = "schemaVersion")] public string SchemaVersion { get; set; }
            [DataMember(Name = "ruleSetId")] public string RuleSetId { get; set; }
            [DataMember(Name = "version")] public string Version { get; set; }
            [DataMember(Name = "title")] public string Title { get; set; }
            [DataMember(Name = "rules")] public List<RuleFile> Rules { get; set; }

            public RuleSetDocument ToDocument() => new RuleSetDocument
            {
                SchemaVersion = SchemaVersion ?? string.Empty,
                RuleSetId = RuleSetId ?? string.Empty,
                Version = Version ?? string.Empty,
                Title = Title ?? string.Empty,
                Rules = (Rules ?? new List<RuleFile>()).Select(r => r?.ToDocument()).ToList()
            };
        }

        [DataContract]
        private sealed class RuleFile
        {
            [DataMember(Name = "ruleId")] public string RuleId { get; set; }
            [DataMember(Name = "version")] public string Version { get; set; }
            [DataMember(Name = "category")] public string Category { get; set; }
            [DataMember(Name = "legalReference")] public string LegalReference { get; set; }
            [DataMember(Name = "effectiveDate")] public string EffectiveDate { get; set; }
            [DataMember(Name = "jurisdiction")] public string Jurisdiction { get; set; }
            [DataMember(Name = "priority")] public int Priority { get; set; }
            [DataMember(Name = "appliesWhen")] public string AppliesWhen { get; set; }
            [DataMember(Name = "requiredValue")] public string RequiredValue { get; set; }
            [DataMember(Name = "exemptions")] public List<string> Exemptions { get; set; }
            [DataMember(Name = "evidenceFields")] public List<string> EvidenceFields { get; set; }
            [DataMember(Name = "severity")] public string Severity { get; set; }

            public RuleDocument ToDocument() => new RuleDocument
            {
                RuleId = RuleId ?? string.Empty,
                Version = Version ?? string.Empty,
                Category = Category ?? string.Empty,
                LegalReference = LegalReference ?? string.Empty,
                EffectiveDate = EffectiveDate ?? string.Empty,
                Jurisdiction = Jurisdiction ?? string.Empty,
                Priority = Priority,
                AppliesWhen = AppliesWhen ?? string.Empty,
                RequiredValue = RequiredValue ?? string.Empty,
                Exemptions = Exemptions ?? new List<string>(),
                EvidenceFields = EvidenceFields ?? new List<string>(),
                Severity = Severity ?? string.Empty
            };
        }
    }
}
