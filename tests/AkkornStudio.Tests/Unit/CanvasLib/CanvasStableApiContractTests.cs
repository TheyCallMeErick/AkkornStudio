using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AkkornStudio.CanvasKit;

namespace AkkornStudio.Tests.Unit.CanvasLib;

public sealed class CanvasStableApiContractTests
{
    private const string StableApiDocFingerprintPrefix = "Stable API v1 fingerprint:";
    private const string ExpectedStableApiV1Fingerprint = "D8E962797F3337F8EECBE977C867830BCB548ABCEDF50487C4A806A5CE417225";

    [Fact]
    public void StableApiV1_RequiredTypesExistAndArePublic()
    {
        Type[] required =
        [
            typeof(ICanvasTableNode),
            typeof(ICanvasLayerNode),
            typeof(CanvasAutoJoinSemantics),
            typeof(CanvasViewportMath),
            typeof(CanvasViewportPoint),
            typeof(CanvasViewportSize),
            typeof(CanvasViewportNodeFrame),
            typeof(CanvasSelectionBounds),
            typeof(CanvasTableHighlightEngine),
            typeof(CanvasWireGeometry),
            typeof(CanvasWireStylePolicy),
            typeof(CanvasWireDashKind),
            typeof(CanvasLayerOrdering),
            typeof(CanvasSubEditorMode),
            typeof(CanvasSubEditorSessionState),
            typeof(CanvasSubEditorStateMachine),
        ];

        foreach (Type type in required)
        {
            Assert.True(type.IsPublic, $"Expected public stable API type: {type.FullName}");
        }
    }

    [Fact]
    public void ICanvasTableNode_ContractMatchesStableApi()
    {
        Type t = typeof(ICanvasTableNode);

        AssertProperty(t, nameof(ICanvasTableNode.IsTableSource), typeof(bool), canWrite: false);
        AssertProperty(t, nameof(ICanvasTableNode.IsHighlighted), typeof(bool), canWrite: true);
        AssertProperty(t, nameof(ICanvasTableNode.FullName), typeof(string), canWrite: false);
        AssertProperty(t, nameof(ICanvasTableNode.Title), typeof(string), canWrite: false);
        AssertProperty(t, nameof(ICanvasTableNode.Alias), typeof(string), canWrite: false);
    }

    [Fact]
    public void ICanvasLayerNode_ContractMatchesStableApi()
    {
        Type t = typeof(ICanvasLayerNode);

        AssertProperty(t, nameof(ICanvasLayerNode.IsSelected), typeof(bool), canWrite: false);
        AssertProperty(t, nameof(ICanvasLayerNode.ZOrder), typeof(int), canWrite: false);
    }

    [Fact]
    public void CanvasTableHighlightEngine_MethodsMatchStableApi()
    {
        Type t = typeof(CanvasTableHighlightEngine);

        AssertMethod(t, nameof(CanvasTableHighlightEngine.ApplyHighlight), typeof(void), typeof(IEnumerable<ICanvasTableNode>), typeof(string));
        AssertMethod(t, nameof(CanvasTableHighlightEngine.MatchesHighlightedTable), typeof(bool), typeof(ICanvasTableNode), typeof(string), typeof(string));
        AssertMethod(t, nameof(CanvasTableHighlightEngine.NormalizeTableReference), typeof(string), typeof(string));
    }

    [Fact]
    public void CanvasWireAndLayer_MethodsMatchStableApi()
    {
        AssertMethod(
            typeof(CanvasWireGeometry),
            nameof(CanvasWireGeometry.BuildBezierPath),
            typeof(string),
            typeof(double),
            typeof(double),
            typeof(double),
            typeof(double)
        );

        AssertMethod(
            typeof(CanvasWireStylePolicy),
            nameof(CanvasWireStylePolicy.ResolveThickness),
            typeof(double),
            typeof(double),
            typeof(bool),
            typeof(double)
        );

        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.OrderByZ), 1, parameterCount: 1);
        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.BringToFront), 1, parameterCount: 1);
        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.SendToBack), 1, parameterCount: 1);
        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.BringForward), 1, parameterCount: 1);
        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.SendBackward), 1, parameterCount: 1);
        AssertGenericMethod(typeof(CanvasLayerOrdering), nameof(CanvasLayerOrdering.BuildNormalizedMap), 1, parameterCount: 1);
    }

    [Fact]
    public void CanvasAutoJoinSemantics_MethodsMatchStableApi()
    {
        AssertMethod(
            typeof(CanvasAutoJoinSemantics),
            nameof(CanvasAutoJoinSemantics.TrySplitJoinClauseOnEquality),
            typeof(bool),
            typeof(string),
            typeof(string).MakeByRefType(),
            typeof(string).MakeByRefType()
        );

        AssertMethod(
            typeof(CanvasAutoJoinSemantics),
            nameof(CanvasAutoJoinSemantics.BuildSuggestionPairKey),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string)
        );

        AssertMethod(
            typeof(CanvasAutoJoinSemantics),
            nameof(CanvasAutoJoinSemantics.TryParseQualifiedColumn),
            typeof(bool),
            typeof(string),
            typeof(string).MakeByRefType(),
            typeof(string).MakeByRefType()
        );

        AssertMethod(
            typeof(CanvasAutoJoinSemantics),
            nameof(CanvasAutoJoinSemantics.MatchesSource),
            typeof(bool),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string)
        );
    }

    [Fact]
    public void CanvasViewportMath_MethodsMatchStableApi()
    {
        AssertMethod(
            typeof(CanvasViewportMath),
            nameof(CanvasViewportMath.TryGetSelectionBounds),
            typeof(bool),
            typeof(IEnumerable<CanvasViewportNodeFrame>),
            typeof(CanvasSelectionBounds).MakeByRefType()
        );

        AssertMethod(
            typeof(CanvasViewportMath),
            nameof(CanvasViewportMath.ComputeCenterPan),
            typeof(CanvasViewportPoint),
            typeof(CanvasSelectionBounds),
            typeof(CanvasViewportSize),
            typeof(double)
        );

        AssertMethod(
            typeof(CanvasViewportMath),
            nameof(CanvasViewportMath.ComputeFit),
            typeof(ValueTuple<double, CanvasViewportPoint>),
            typeof(CanvasSelectionBounds),
            typeof(CanvasViewportSize),
            typeof(double),
            typeof(double),
            typeof(double)
        );
    }

    [Fact]
    public void CanvasSubEditorStateMachine_MethodsMatchStableApi()
    {
        Type t = typeof(CanvasSubEditorStateMachine);

        AssertMethod(t, nameof(CanvasSubEditorStateMachine.EnterCte), typeof(CanvasSubEditorSessionState), typeof(string));
        AssertMethod(t, nameof(CanvasSubEditorStateMachine.EnterView), typeof(CanvasSubEditorSessionState), typeof(string));
        AssertMethod(t, nameof(CanvasSubEditorStateMachine.Exit), typeof(CanvasSubEditorSessionState));

        AssertProperty(typeof(CanvasSubEditorSessionState), nameof(CanvasSubEditorSessionState.Mode), typeof(CanvasSubEditorMode), canWrite: true);
        AssertProperty(typeof(CanvasSubEditorSessionState), nameof(CanvasSubEditorSessionState.DisplayName), typeof(string), canWrite: true);
        AssertProperty(typeof(CanvasSubEditorSessionState), nameof(CanvasSubEditorSessionState.IsActive), typeof(bool), canWrite: false);
        AssertProperty(typeof(CanvasSubEditorSessionState), nameof(CanvasSubEditorSessionState.IsViewEditor), typeof(bool), canWrite: false);
    }

    [Fact]
    public void StableApiV1_FingerprintMustMatchExpectedBaseline()
    {
        string fingerprint = BuildStableApiV1Fingerprint();

        Assert.True(
            string.Equals(fingerprint, ExpectedStableApiV1Fingerprint, StringComparison.Ordinal),
            $"Stable API fingerprint changed. Expected '{ExpectedStableApiV1Fingerprint}', actual '{fingerprint}'. "
            + "If this change is intentional, update the expected fingerprint in this test "
            + "and update the Stable API v1 section in docs/CANVAS_LIBRARY_EXTRACTION.md."
        );
    }

    [Fact]
    public void StableApiV1_DocumentationMustContainExpectedFingerprint()
    {
        string docsPath = ResolveRepositoryFilePath("docs", "CANVAS_LIBRARY_EXTRACTION.md");
        string docsText = File.ReadAllText(docsPath);
        string expectedLine = $"{StableApiDocFingerprintPrefix} {ExpectedStableApiV1Fingerprint}";

        Assert.Contains(expectedLine, docsText, StringComparison.Ordinal);
    }

    private static void AssertMethod(Type type, string methodName, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }

    private static void AssertGenericMethod(Type type, string methodName, int genericArity, int parameterCount)
    {
        MethodInfo? method = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(m =>
                m.Name == methodName
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == genericArity
                && m.GetParameters().Length == parameterCount);

        Assert.NotNull(method);
    }

    private static void AssertProperty(Type type, string propertyName, Type propertyType, bool canWrite)
    {
        PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(propertyType, property.PropertyType);
        Assert.Equal(canWrite, property.SetMethod is not null);
    }

    private static string BuildStableApiV1Fingerprint()
    {
        List<string> lines = [];

        AppendInterfaceSurface(lines, typeof(ICanvasTableNode));
        AppendInterfaceSurface(lines, typeof(ICanvasLayerNode));
        AppendStaticClassSurface(lines, typeof(CanvasAutoJoinSemantics));
        AppendStaticClassSurface(lines, typeof(CanvasViewportMath));
        AppendRecordSurface(lines, typeof(CanvasViewportPoint));
        AppendRecordSurface(lines, typeof(CanvasViewportSize));
        AppendRecordSurface(lines, typeof(CanvasViewportNodeFrame));
        AppendRecordSurface(lines, typeof(CanvasSelectionBounds));
        AppendStaticClassSurface(lines, typeof(CanvasTableHighlightEngine));
        AppendStaticClassSurface(lines, typeof(CanvasWireGeometry));
        AppendStaticClassSurface(lines, typeof(CanvasWireStylePolicy));
        AppendStaticClassSurface(lines, typeof(CanvasLayerOrdering));
        AppendEnumSurface(lines, typeof(CanvasWireDashKind));
        AppendEnumSurface(lines, typeof(CanvasSubEditorMode));
        AppendRecordSurface(lines, typeof(CanvasSubEditorSessionState));
        AppendStaticClassSurface(lines, typeof(CanvasSubEditorStateMachine));

        string payload = string.Join("\n", lines.OrderBy(l => l, StringComparer.Ordinal));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static void AppendInterfaceSurface(List<string> lines, Type type)
    {
        lines.Add($"TYPE|{type.FullName}|INTERFACE");

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            lines.Add($"PROP|{type.FullName}|{property.PropertyType.FullName}|{property.Name}|GET={property.GetMethod is not null}|SET={property.SetMethod is not null}");
        }
    }

    private static void AppendRecordSurface(List<string> lines, Type type)
    {
        lines.Add($"TYPE|{type.FullName}|RECORD");

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == "EqualityContract")
                continue;

            lines.Add($"PROP|{type.FullName}|{property.PropertyType.FullName}|{property.Name}|GET={property.GetMethod is not null}|SET={property.SetMethod is not null}");
        }
    }

    private static void AppendStaticClassSurface(List<string> lines, Type type)
    {
        lines.Add($"TYPE|{type.FullName}|STATIC");

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName)
                continue;

            string genericArity = method.IsGenericMethodDefinition
                ? $"`{method.GetGenericArguments().Length}"
                : string.Empty;

            string parameters = string.Join(",",
                method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));

            lines.Add($"METHOD|{type.FullName}|{method.ReturnType.FullName}|{method.Name}{genericArity}|{parameters}");
        }
    }

    private static void AppendEnumSurface(List<string> lines, Type type)
    {
        lines.Add($"TYPE|{type.FullName}|ENUM");

        foreach (string name in Enum.GetNames(type))
        {
            lines.Add($"ENUM|{type.FullName}|{name}");
        }
    }

    private static string ResolveRepositoryFilePath(params string[] segments)
    {
        string baseDirectory = AppContext.BaseDirectory;
        DirectoryInfo? current = new(baseDirectory);

        while (current is not null)
        {
            string candidateSolution = Path.Combine(current.FullName, "files.sln");
            if (File.Exists(candidateSolution))
                return Path.Combine(current.FullName, Path.Combine(segments));

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not resolve repository root from test base directory.");
    }
}
