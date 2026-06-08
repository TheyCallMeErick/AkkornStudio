using AkkornStudio.Core;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Caching;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Indexing;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Processing;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Rules;
using AkkornStudio.Ddl.SchemaAnalysis.Application.Validation;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Contracts;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Enums;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Normalization;
using AkkornStudio.Ddl.SchemaAnalysis.Domain.Validation;
using AkkornStudio.Ddl.SchemaAnalysis.Infrastructure.Hashing;
using AkkornStudio.Metadata;

namespace AkkornStudio.Ddl.SchemaAnalysis.Application;

public sealed class SchemaAnalysisService
{
    public const int SpecVersion = 1;

    private static readonly SchemaRuleCode[] OrderedRuleCodes =
    [
        SchemaRuleCode.FK_CATALOG_INCONSISTENT,
        SchemaRuleCode.MISSING_FK,
        SchemaRuleCode.NAMING_CONVENTION_VIOLATION,
        SchemaRuleCode.LOW_SEMANTIC_NAME,
        SchemaRuleCode.MISSING_REQUIRED_COMMENT,
        SchemaRuleCode.NF1_HINT_MULTI_VALUED,
        SchemaRuleCode.NF2_HINT_PARTIAL_DEPENDENCY,
        SchemaRuleCode.NF3_HINT_TRANSITIVE_DEPENDENCY,
    ];

    private readonly SchemaAnalysisProfileNormalizer _profileNormalizer;
    private readonly SchemaMetadataValidator _metadataValidator;
    private readonly SchemaMetadataIndexBuilder _indexBuilder;
    private readonly SchemaAnalysisHashingService _hashingService;
    private readonly SchemaAnalysisContractValidator _contractValidator;
    private readonly IReadOnlyDictionary<SchemaRuleCode, ISchemaAnalysisRule> _rules;
    private readonly SchemaIssueDeduplicator _issueDeduplicator;
    private readonly SchemaIssueOrderer _issueOrderer;
    private readonly SchemaSuggestionFactory _suggestionFactory;
    private readonly ISchemaAnalysisCache? _cache;

    public SchemaAnalysisService(
        IEnumerable<ISchemaAnalysisRule> rules,
        SchemaAnalysisProfileNormalizer? profileNormalizer = null,
        SchemaMetadataValidator? metadataValidator = null,
        SchemaMetadataIndexBuilder? indexBuilder = null,
        SchemaAnalysisHashingService? hashingService = null,
        SchemaAnalysisContractValidator? contractValidator = null,
        SchemaIssueDeduplicator? issueDeduplicator = null,
        SchemaIssueOrderer? issueOrderer = null,
        SchemaSuggestionFactory? suggestionFactory = null,
        ISchemaAnalysisCache? cache = null
    )
    {
        _profileNormalizer = profileNormalizer ?? new SchemaAnalysisProfileNormalizer();
        _metadataValidator = metadataValidator ?? new SchemaMetadataValidator();
        _indexBuilder = indexBuilder ?? new SchemaMetadataIndexBuilder();
        _hashingService = hashingService ?? new SchemaAnalysisHashingService();
        _contractValidator = contractValidator ?? new SchemaAnalysisContractValidator();
        _issueDeduplicator = issueDeduplicator ?? new SchemaIssueDeduplicator();
        _issueOrderer = issueOrderer ?? new SchemaIssueOrderer();
        _suggestionFactory = suggestionFactory ?? new SchemaSuggestionFactory();
        _cache = cache;
        _rules = rules.ToDictionary(static rule => rule.RuleCode);
    }

    public async Task<SchemaAnalysisResult> AnalyzeAsync(
        DbMetadata metadata,
        SchemaAnalysisProfile? rawProfile,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);

        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

        SchemaAnalysisProfileNormalizationResult profileNormalization = _profileNormalizer.Normalize(rawProfile);
        SchemaAnalysisProfile profile = profileNormalization.Profile;

        SchemaMetadataValidationResult metadataValidation = _metadataValidator.Validate(metadata);
        if (!metadataValidation.IsValid)
        {
            return BuildFailedResult(
                metadata,
                profile,
                startedAtUtc,
                diagnostics: profileNormalization.Diagnostics.Concat(metadataValidation.Diagnostics).ToList()
            );
        }

        string metadataFingerprint = _hashingService.ComputeMetadataFingerprint(metadata);
        string profileContentHash = _hashingService.ComputeProfileContentHash(profile);

        SchemaAnalysisCacheKey cacheKey = new(
            metadataFingerprint,
            profileContentHash,
            metadata.Provider,
            SpecVersion
        );

        if (_cache is not null && profile.CacheTtlSeconds > 0 && _cache.TryGet(cacheKey, out SchemaAnalysisResult? cachedResult))
        {
            return MaterializeCacheHit(cachedResult!, startedAtUtc);
        }

        SchemaMetadataIndexSnapshot indices = _indexBuilder.Build(metadata, profile);
        SchemaAnalysisExecutionContext context = new(
            metadata,
            profile,
            indices,
            metadataFingerprint,
            profileContentHash
        );

        List<SchemaRuleExecutionDiagnostic> diagnostics =
        [
            .. profileNormalization.Diagnostics,
            .. metadataValidation.Diagnostics,
        ];

        using CancellationTokenSource timeoutCts = new(profile.TimeoutMs);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token
        );

        IReadOnlyList<RuleExecutionPlanItem> executableRules = BuildExecutionPlan(profile);
        RuleExecutionAggregate executionAggregate = await ExecuteRulesAsync(
            executableRules,
            context,
            profile,
            linkedCts.Token,
            timeoutCts
        );

        diagnostics.AddRange(executionAggregate.Diagnostics);

        if (executionAggregate.HasFatalFailure)
        {
            return BuildFailedResult(
                metadata,
                profile,
                startedAtUtc,
                diagnostics: diagnostics
            );
        }

        IReadOnlyList<SchemaIssue> dedupedIssues = _issueDeduplicator.Deduplicate(executionAggregate.Issues);
        IReadOnlyList<SchemaIssue> truncatedIssues = executionAggregate.TimedOut && !profile.AllowPartialOnTimeout
            ? []
            : dedupedIssues.Take(profile.MaxIssues).ToList();
        IReadOnlyList<SchemaIssue> orderedIssues = _issueOrderer.Order(truncatedIssues);
        IReadOnlyList<SchemaIssue> finalIssues = orderedIssues
            .Select(issue => issue with
            {
                Suggestions = _suggestionFactory.CreateSuggestions(
                    issue,
                    metadata.Provider,
                    profile,
                    ResolveExistingConstraintNames(indices, metadata.Provider, issue.SchemaName)
                ),
            })
            .ToList();

        SchemaAnalysisSummary summary = BuildSummary(finalIssues, metadata);
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        SchemaAnalysisPartialState partialState = BuildPartialState(
            executionAggregate.TimedOut,
            executionAggregate.Cancelled,
            executionAggregate.RuleFailurePartial,
            executionAggregate.CompletedRules
        );
        SchemaAnalysisStatus status = DetermineStatus(
            diagnostics,
            partialState,
            hasMaterializedIssues: executionAggregate.Issues.Count > 0,
            allowPartialOnTimeout: profile.AllowPartialOnTimeout
        );

        SchemaAnalysisResult result = new(
            AnalysisId: Guid.NewGuid().ToString("N"),
            Status: status,
            Provider: metadata.Provider,
            DatabaseName: metadata.DatabaseName,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            DurationMs: Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
            MetadataFingerprint: metadataFingerprint,
            ProfileContentHash: profileContentHash,
            ProfileVersion: profile.Version,
            PartialState: partialState,
            Issues: finalIssues,
            Diagnostics: OrderDiagnostics(diagnostics),
            Summary: summary
        );

        _contractValidator.EnsureValid(result, profile);

        if (_cache is not null && profile.CacheTtlSeconds > 0)
        {
            _cache.Set(cacheKey, SanitizeForCache(result), TimeSpan.FromSeconds(profile.CacheTtlSeconds));
        }

        return result;
    }

    private IReadOnlyList<RuleExecutionPlanItem> BuildExecutionPlan(SchemaAnalysisProfile profile)
    {
        List<RuleExecutionPlanItem> plan = [];

        for (int index = 0; index < OrderedRuleCodes.Length; index++)
        {
            SchemaRuleCode ruleCode = OrderedRuleCodes[index];
            SchemaRuleSetting setting = profile.RuleSettings[ruleCode];
            if (!setting.Enabled || !_rules.TryGetValue(ruleCode, out ISchemaAnalysisRule? rule))
            {
                continue;
            }

            plan.Add(new RuleExecutionPlanItem(index, ruleCode, setting, rule));
        }

        return plan;
    }

    private async Task<RuleExecutionAggregate> ExecuteRulesAsync(
        IReadOnlyList<RuleExecutionPlanItem> executableRules,
        SchemaAnalysisExecutionContext context,
        SchemaAnalysisProfile profile,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts
    )
    {
        if (ShouldExecuteInParallel(profile, executableRules))
        {
            return await ExecuteRulesInParallelAsync(executableRules, context, profile, cancellationToken, timeoutCts);
        }

        return await ExecuteRulesSequentiallyAsync(executableRules, context, profile, cancellationToken, timeoutCts);
    }

    private async Task<RuleExecutionAggregate> ExecuteRulesSequentiallyAsync(
        IReadOnlyList<RuleExecutionPlanItem> executableRules,
        SchemaAnalysisExecutionContext context,
        SchemaAnalysisProfile profile,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts
    )
    {
        List<RuleExecutionOutcome> outcomes = [];

        foreach (RuleExecutionPlanItem rule in executableRules)
        {
            RuleExecutionOutcome outcome = await ExecuteRuleCoreAsync(
                rule,
                context,
                profile,
                cancellationToken,
                timeoutCts
            );
            outcomes.Add(outcome);

            if (outcome.IsTerminal)
            {
                break;
            }
        }

        return AggregateOutcomes(outcomes);
    }

    private async Task<RuleExecutionAggregate> ExecuteRulesInParallelAsync(
        IReadOnlyList<RuleExecutionPlanItem> executableRules,
        SchemaAnalysisExecutionContext context,
        SchemaAnalysisProfile profile,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts
    )
    {
        using SemaphoreSlim gate = new(Math.Max(1, profile.MaxDegreeOfParallelism));

        Task<RuleExecutionOutcome>[] tasks = executableRules
            .Select(rule => ExecuteRuleWithGateAsync(rule, context, profile, gate, cancellationToken, timeoutCts))
            .ToArray();

        RuleExecutionOutcome[] outcomes = await Task.WhenAll(tasks);
        return AggregateOutcomes(outcomes);
    }

    private async Task<RuleExecutionOutcome> ExecuteRuleWithGateAsync(
        RuleExecutionPlanItem rule,
        SchemaAnalysisExecutionContext context,
        SchemaAnalysisProfile profile,
        SemaphoreSlim gate,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts
    )
    {
        bool enteredGate = false;

        try
        {
            await gate.WaitAsync(cancellationToken);
            enteredGate = true;
            return await ExecuteRuleCoreAsync(rule, context, profile, cancellationToken, timeoutCts);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [],
                RuleExecutionTerminalState.TimedOut,
                Completed: false
            );
        }
        catch (OperationCanceledException)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [],
                RuleExecutionTerminalState.Cancelled,
                Completed: false
            );
        }
        finally
        {
            if (enteredGate)
            {
                gate.Release();
            }
        }
    }

    private async Task<RuleExecutionOutcome> ExecuteRuleCoreAsync(
        RuleExecutionPlanItem rule,
        SchemaAnalysisExecutionContext context,
        SchemaAnalysisProfile profile,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SchemaRuleExecutionResult executionResult = await rule.Rule.ExecuteAsync(context, cancellationToken);

            IReadOnlyList<SchemaIssue> orderedRuleIssues = executionResult.Issues
                .OrderByDescending(static issue => issue.Confidence)
                .ThenBy(static issue => issue.IssueId, StringComparer.Ordinal)
                .Take(rule.Setting.MaxIssues)
                .ToList();

            IReadOnlyList<SchemaIssue> thresholdFilteredIssues = orderedRuleIssues
                .Where(issue => issue.Confidence >= Math.Max(profile.MinConfidenceGlobal, rule.Setting.MinConfidence))
                .ToList();

            return new RuleExecutionOutcome(
                rule.Index,
                thresholdFilteredIssues,
                executionResult.Diagnostics,
                RuleExecutionTerminalState.None,
                Completed: true
            );
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [],
                RuleExecutionTerminalState.TimedOut,
                Completed: false
            );
        }
        catch (OperationCanceledException)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [],
                RuleExecutionTerminalState.Cancelled,
                Completed: false
            );
        }
        catch (Exception) when (profile.AllowPartialOnRuleFailure)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [
                    new SchemaRuleExecutionDiagnostic(
                        Code: "ANL-RULE-FAILED",
                        Message: "Uma regra falhou e a execucao seguiu conforme politica de parcial.",
                        RuleCode: rule.RuleCode,
                        State: RuleExecutionState.Failed,
                        IsFatal: false
                    ),
                ],
                RuleExecutionTerminalState.RuleFailurePartial,
                Completed: false
            );
        }
        catch (Exception)
        {
            return new RuleExecutionOutcome(
                rule.Index,
                [],
                [
                    new SchemaRuleExecutionDiagnostic(
                        Code: "ANL-RULE-FAILED",
                        Message: "Uma regra falhou e a execucao seguiu conforme politica de parcial.",
                        RuleCode: rule.RuleCode,
                        State: RuleExecutionState.Failed,
                        IsFatal: true
                    ),
                ],
                RuleExecutionTerminalState.FatalFailure,
                Completed: false
            );
        }
    }

    private static bool ShouldExecuteInParallel(
        SchemaAnalysisProfile profile,
        IReadOnlyCollection<RuleExecutionPlanItem> executableRules
    )
    {
        return profile.EnableParallelRules
            && profile.MaxDegreeOfParallelism > 1
            && executableRules.Count > 1;
    }

    private static RuleExecutionAggregate AggregateOutcomes(IEnumerable<RuleExecutionOutcome> outcomes)
    {
        List<SchemaIssue> issues = [];
        List<SchemaRuleExecutionDiagnostic> diagnostics = [];
        int completedRules = 0;
        bool timedOut = false;
        bool cancelled = false;
        bool ruleFailurePartial = false;
        bool hasFatalFailure = false;

        foreach (RuleExecutionOutcome outcome in outcomes.OrderBy(static outcome => outcome.Index))
        {
            diagnostics.AddRange(outcome.Diagnostics);

            if (outcome.Completed)
            {
                issues.AddRange(outcome.Issues);
                completedRules++;
                continue;
            }

            switch (outcome.TerminalState)
            {
                case RuleExecutionTerminalState.RuleFailurePartial:
                    ruleFailurePartial = true;
                    break;

                case RuleExecutionTerminalState.TimedOut:
                    timedOut = true;
                    diagnostics.Add(
                        new SchemaRuleExecutionDiagnostic(
                            Code: "ANL-TIMEOUT",
                            Message: "A execucao atingiu o timeout global configurado.",
                            RuleCode: null,
                            State: RuleExecutionState.TimedOut,
                            IsFatal: false
                        )
                    );
                    return new RuleExecutionAggregate(
                        issues,
                        diagnostics,
                        completedRules,
                        timedOut,
                        cancelled,
                        ruleFailurePartial,
                        hasFatalFailure
                    );

                case RuleExecutionTerminalState.Cancelled:
                    cancelled = true;
                    return new RuleExecutionAggregate(
                        issues,
                        diagnostics,
                        completedRules,
                        timedOut,
                        cancelled,
                        ruleFailurePartial,
                        hasFatalFailure
                    );

                case RuleExecutionTerminalState.FatalFailure:
                    hasFatalFailure = true;
                    return new RuleExecutionAggregate(
                        issues,
                        diagnostics,
                        completedRules,
                        timedOut,
                        cancelled,
                        ruleFailurePartial,
                        hasFatalFailure
                    );
            }
        }

        return new RuleExecutionAggregate(
            issues,
            diagnostics,
            completedRules,
            timedOut,
            cancelled,
            ruleFailurePartial,
            hasFatalFailure
        );
    }

    private static SchemaAnalysisResult BuildFailedResult(
        DbMetadata metadata,
        SchemaAnalysisProfile profile,
        DateTimeOffset startedAtUtc,
        IReadOnlyList<SchemaRuleExecutionDiagnostic> diagnostics
    )
    {
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        return new SchemaAnalysisResult(
            AnalysisId: Guid.NewGuid().ToString("N"),
            Status: SchemaAnalysisStatus.Failed,
            Provider: metadata.Provider,
            DatabaseName: metadata.DatabaseName,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            DurationMs: Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
            MetadataFingerprint: string.Empty,
            ProfileContentHash: string.Empty,
            ProfileVersion: profile.Version,
            PartialState: new SchemaAnalysisPartialState(false, "NONE", 0, OrderedRuleCodes.Length),
            Issues: [],
            Diagnostics: OrderDiagnostics(diagnostics),
            Summary: BuildSummary([], metadata)
        );
    }

    private static SchemaAnalysisResult MaterializeCacheHit(
        SchemaAnalysisResult cachedResult,
        DateTimeOffset startedAtUtc
    )
    {
        DateTimeOffset completedAtUtc = DateTimeOffset.UtcNow;
        List<SchemaRuleExecutionDiagnostic> diagnostics =
        [
            .. cachedResult.Diagnostics,
            new SchemaRuleExecutionDiagnostic(
                Code: "ANL-CACHE-HIT",
                Message: "Resultado retornado do cache valido.",
                RuleCode: null,
                State: RuleExecutionState.Completed,
                IsFatal: false
            ),
        ];

        return cachedResult with
        {
            AnalysisId = Guid.NewGuid().ToString("N"),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = Math.Max(0, (long)(completedAtUtc - startedAtUtc).TotalMilliseconds),
            Diagnostics = OrderDiagnostics(diagnostics),
        };
    }

    private static SchemaAnalysisResult SanitizeForCache(SchemaAnalysisResult result)
    {
        IReadOnlyList<SchemaRuleExecutionDiagnostic> persistentDiagnostics = result.Diagnostics
            .Where(static diagnostic => !IsTransientDiagnostic(diagnostic))
            .ToList();

        return result with
        {
            Diagnostics = OrderDiagnostics(persistentDiagnostics),
        };
    }

    private static SchemaAnalysisSummary BuildSummary(IReadOnlyList<SchemaIssue> issues, DbMetadata metadata)
    {
        var detector = new SchemaPatternDetector();
        var patterns = detector.DetectPatterns(metadata);

        Dictionary<SchemaRuleCode, int> perRule = issues
            .GroupBy(static issue => issue.RuleCode)
            .ToDictionary(static group => group.Key, static group => group.Count());

        Dictionary<string, int> perTable = issues
            .Where(static issue => !string.IsNullOrWhiteSpace(issue.TableName))
            .GroupBy(static issue => string.IsNullOrWhiteSpace(issue.SchemaName) ? issue.TableName! : $"{issue.SchemaName}.{issue.TableName}")
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        double score = 100.0;
        int criticalCount = issues.Count(i => i.Severity == SchemaIssueSeverity.Critical);
        int warningCount = issues.Count(i => i.Severity == SchemaIssueSeverity.Warning);
        score -= (criticalCount * 5.0) + (warningCount * 2.0);
        score = Math.Max(0.0, score);

        int quickWinCount = issues.Count(i => i.Suggestions.Count > 0);
        Dictionary<string, double> areaScores = new();

        return new SchemaAnalysisSummary(
            TotalIssues: issues.Count,
            InfoCount: issues.Count(static issue => issue.Severity == SchemaIssueSeverity.Info),
            WarningCount: warningCount,
            CriticalCount: criticalCount,
            QuickWinCount: quickWinCount,
            OverallScore: score,
            PerRuleCount: perRule,
            PerTableCount: perTable,
            AreaScores: areaScores,
            ObservedPatterns: patterns
        );
    }

    private static IReadOnlySet<string> ResolveExistingConstraintNames(
        SchemaMetadataIndexSnapshot indices,
        DatabaseProvider provider,
        string? schemaName
    )
    {
        string canonicalSchema = SchemaCanonicalizer.Normalize(provider, schemaName) ?? string.Empty;
        return indices.ConstraintNamesBySchema.TryGetValue(canonicalSchema, out IReadOnlySet<string>? names)
            ? names
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static SchemaAnalysisPartialState BuildPartialState(
        bool timedOut,
        bool cancelled,
        bool ruleFailurePartial,
        int completedRules
    )
    {
        if (timedOut)
        {
            return new SchemaAnalysisPartialState(true, "TIMEOUT", completedRules, OrderedRuleCodes.Length);
        }

        if (cancelled)
        {
            return new SchemaAnalysisPartialState(true, "CANCELLED", completedRules, OrderedRuleCodes.Length);
        }

        if (ruleFailurePartial)
        {
            return new SchemaAnalysisPartialState(true, "RULE_FAILURE", completedRules, OrderedRuleCodes.Length);
        }

        return new SchemaAnalysisPartialState(false, "NONE", completedRules, OrderedRuleCodes.Length);
    }

    private static SchemaAnalysisStatus DetermineStatus(
        IReadOnlyList<SchemaRuleExecutionDiagnostic> diagnostics,
        SchemaAnalysisPartialState partialState,
        bool hasMaterializedIssues,
        bool allowPartialOnTimeout
    )
    {
        if (diagnostics.Any(static diagnostic => diagnostic.IsFatal))
        {
            return SchemaAnalysisStatus.Failed;
        }

        if (partialState.IsPartial && partialState.ReasonCode == "TIMEOUT")
        {
            return allowPartialOnTimeout ? SchemaAnalysisStatus.Partial : SchemaAnalysisStatus.Failed;
        }

        if (partialState.IsPartial && partialState.ReasonCode == "RULE_FAILURE")
        {
            return SchemaAnalysisStatus.Partial;
        }

        if (partialState.IsPartial && partialState.ReasonCode == "CANCELLED")
        {
            return hasMaterializedIssues ? SchemaAnalysisStatus.Partial : SchemaAnalysisStatus.Cancelled;
        }

        if (diagnostics.Any(static diagnostic =>
                diagnostic.Code == "ANL-METADATA-PARTIAL"
                || diagnostic.Code == "ANL-RULE-FAILED"
                || diagnostic.Code == "ANL-RULE-MAX-ISSUES-TRUNCATED"
                || diagnostic.Code == "ANL-GLOBAL-MAX-ISSUES-TRUNCATED"))
        {
            return SchemaAnalysisStatus.CompletedWithWarnings;
        }

        return SchemaAnalysisStatus.Completed;
    }

    private static IReadOnlyList<SchemaRuleExecutionDiagnostic> OrderDiagnostics(
        IEnumerable<SchemaRuleExecutionDiagnostic> diagnostics
    )
    {
        return diagnostics
            .OrderByDescending(static diagnostic => diagnostic.IsFatal)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.RuleCode.HasValue ? 0 : 1)
            .ThenBy(static diagnostic => diagnostic.RuleCode)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsTransientDiagnostic(SchemaRuleExecutionDiagnostic diagnostic)
    {
        return diagnostic.Code is "ANL-CACHE-HIT";
    }

    private enum RuleExecutionTerminalState
    {
        None,
        TimedOut,
        Cancelled,
        RuleFailurePartial,
        FatalFailure,
    }

    private sealed record RuleExecutionPlanItem(
        int Index,
        SchemaRuleCode RuleCode,
        SchemaRuleSetting Setting,
        ISchemaAnalysisRule Rule
    );

    private sealed record RuleExecutionOutcome(
        int Index,
        IReadOnlyList<SchemaIssue> Issues,
        IReadOnlyList<SchemaRuleExecutionDiagnostic> Diagnostics,
        RuleExecutionTerminalState TerminalState,
        bool Completed
    )
    {
        public bool IsTerminal => TerminalState is not RuleExecutionTerminalState.None;
    }

    private sealed record RuleExecutionAggregate(
        IReadOnlyList<SchemaIssue> Issues,
        IReadOnlyList<SchemaRuleExecutionDiagnostic> Diagnostics,
        int CompletedRules,
        bool TimedOut,
        bool Cancelled,
        bool RuleFailurePartial,
        bool HasFatalFailure
    );
}
