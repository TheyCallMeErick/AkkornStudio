using Avalonia;
using System.Text.Json;
using AkkornStudio.Nodes;
using AkkornStudio.UI.Serialization;
using AkkornStudio.UI.ViewModels;
using Xunit;

namespace AkkornStudio.Tests.Unit.Serialization;

public class CanvasSerializerSaveLoadEnhancementsTests
{
    [Fact]
    public async Task SaveToFileAsync_CompressesLargePayload_AndLoadStillWorks()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vsaq_cmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "canvas.vsaq");

        try
        {
            var vm = new CanvasViewModel();
            vm.InitializeDemoNodes();
            vm.Nodes[0].Parameters["blob"] = new string('x', CanvasSerializer.CompressionThresholdBytes * 2);

            await CanvasSerializer.SaveToFileAsync(path, vm, description: "large-payload");

            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length > 2);
            Assert.Equal(0x1F, bytes[0]);
            Assert.Equal(0x8B, bytes[1]);
            Assert.True(CanvasSerializer.IsValidFile(path));

            var loadedVm = new CanvasViewModel();
            CanvasLoadResult result = await CanvasSerializer.LoadFromFileAsync(path, loadedVm);
            Assert.True(result.Success);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveToFileAsync_OverwriteCreatesAutomaticBackup()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vsaq_bak_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "canvas.vsaq");

        try
        {
            var vm = new CanvasViewModel();

            await CanvasSerializer.SaveToFileAsync(path, vm, description: "first-save");
            vm.Nodes.Add(new NodeViewModel("public.extra", [], new Point(400, 200)));
            await CanvasSerializer.SaveToFileAsync(path, vm, description: "second-save");

            string backupDir = Path.Combine(dir, ".vsaq_backups");
            Assert.True(Directory.Exists(backupDir));
            Assert.NotEmpty(Directory.EnumerateFiles(backupDir, "*.bak"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalVersionHistory_CanRestoreOlderVersion()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vsaq_ver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "canvas.vsaq");

        try
        {
            var vm = new CanvasViewModel();
            await CanvasSerializer.SaveToFileAsync(path, vm, description: "v1");

            await Task.Delay(10);
            vm.Nodes.Add(new NodeViewModel("public.new_table", [], new Point(500, 260)));
            await CanvasSerializer.SaveToFileAsync(path, vm, description: "v2");

            IReadOnlyList<LocalFileVersionInfo> versions = CanvasSerializer.GetLocalFileVersions(path);
            Assert.True(versions.Count >= 2);

            LocalFileVersionInfo oldest = versions.OrderBy(v => v.CreatedAt).First();
            await CanvasSerializer.RestoreLocalVersionAsync(path, oldest.VersionPath);

            var meta = CanvasSerializer.ReadMeta(path);
            Assert.NotNull(meta);
            Assert.Equal("v1", meta?.Description);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RestoreLocalVersionAsync_WithCorruptedVersionPayload_DoesNotOverwriteTargetFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vsaq_ver_bad_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string targetPath = Path.Combine(dir, "canvas.vsaq");
        string corruptedVersionPath = Path.Combine(dir, "corrupted-version.vsaq");

        try
        {
            var vm = new CanvasViewModel();
            await CanvasSerializer.SaveToFileAsync(targetPath, vm, description: "baseline");
            byte[] before = await File.ReadAllBytesAsync(targetPath);

            await File.WriteAllTextAsync(corruptedVersionPath, "{ \"Version\": 9999, \"Nodes\": [");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CanvasSerializer.RestoreLocalVersionAsync(targetPath, corruptedVersionPath)
            );

            byte[] after = await File.ReadAllBytesAsync(targetPath);
            Assert.Equal(before, after);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetLocalFileVersions_WhenTimestampsTie_ReturnsDeterministicOrder()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"vsaq_ver_tie_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string targetPath = Path.Combine(dir, "canvas.vsaq");

        try
        {
            var vm = new CanvasViewModel();
            await CanvasSerializer.SaveToFileAsync(targetPath, vm, description: "seed");

            string historyDir = Path.Combine(dir, ".vsaq_history", "canvas");
            Directory.CreateDirectory(historyDir);

            string stamp = "20260101010101001";
            string aPath = Path.Combine(historyDir, $"{stamp}_a.vsaq");
            string bPath = Path.Combine(historyDir, $"{stamp}_b.vsaq");
            await File.WriteAllTextAsync(aPath, "{}");
            await File.WriteAllTextAsync(bPath, "{}");

            IReadOnlyList<LocalFileVersionInfo> versions = CanvasSerializer.GetLocalFileVersions(targetPath);

            int aIndex = versions
                .Select((v, i) => (v, i))
                .First(pair => pair.v.VersionId == $"{stamp}_a")
                .i;
            int bIndex = versions
                .Select((v, i) => (v, i))
                .First(pair => pair.v.VersionId == $"{stamp}_b")
                .i;

            Assert.True(bIndex < aIndex);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SerializeDeserialize_TableSource_PreservesEffectivePinTypes()
    {
        var source = new CanvasViewModel();
        source.Nodes.Clear();
        source.Connections.Clear();

        source.Nodes.Add(new NodeViewModel(
            "dbo.Sancao",
            [
                ("id", PinDataType.Number),
                ("descricao", PinDataType.Text),
                ("ativo", PinDataType.Boolean)
            ],
            new Point(120, 80)
        ));

        string json = CanvasSerializer.Serialize(source);

        var loaded = new CanvasViewModel();
        loaded.Nodes.Clear();
        loaded.Connections.Clear();

        CanvasLoadResult result = CanvasSerializer.Deserialize(json, loaded);

        Assert.True(result.Success);
        NodeViewModel table = Assert.Single(loaded.Nodes);
        Assert.Equal(NodeType.TableSource, table.Type);

        Assert.Equal(PinDataType.Number, table.OutputPins.Single(p => p.Name == "id").EffectiveDataType);
        Assert.Equal(PinDataType.Text, table.OutputPins.Single(p => p.Name == "descricao").EffectiveDataType);
        Assert.Equal(PinDataType.Boolean, table.OutputPins.Single(p => p.Name == "ativo").EffectiveDataType);
    }

    [Fact]
    public void Deserialize_LegacyColumnRefColumns_UsesColumnLookupToRecoverPinTypes()
    {
        var source = new CanvasViewModel();
        source.Nodes.Clear();
        source.Connections.Clear();

        source.Nodes.Add(new NodeViewModel(
            "dbo.Sancao",
            [
                ("id", PinDataType.Number),
                ("descricao", PinDataType.Text)
            ],
            new Point(120, 80)
        ));

        string json = CanvasSerializer.Serialize(source);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement node = root.GetProperty("Nodes")[0];

        string mutatedJson =
            $$"""
            {
              "Version": {{root.GetProperty("Version").GetInt32()}},
              "DatabaseProvider": "{{root.GetProperty("DatabaseProvider").GetString()}}",
              "ConnectionName": "{{root.GetProperty("ConnectionName").GetString()}}",
              "Zoom": {{root.GetProperty("Zoom").GetDouble()}},
              "PanX": {{root.GetProperty("PanX").GetDouble()}},
              "PanY": {{root.GetProperty("PanY").GetDouble()}},
              "Nodes": [
                {
                  "NodeId": "{{node.GetProperty("NodeId").GetString()}}",
                  "NodeType": "{{node.GetProperty("NodeType").GetString()}}",
                  "X": {{node.GetProperty("X").GetDouble()}},
                  "Y": {{node.GetProperty("Y").GetDouble()}},
                  "Alias": null,
                  "TableFullName": "dbo.Sancao",
                  "Parameters": {},
                  "PinLiterals": {},
                  "Columns": [
                    { "Name": "id", "Type": "ColumnRef" },
                    { "Name": "descricao", "Type": "ColumnRef" }
                  ]
                }
              ],
              "Connections": [],
              "SelectBindings": [],
              "WhereBindings": []
            }
            """;

        IReadOnlyDictionary<string, IReadOnlyList<(string Name, PinDataType Type)>> lookup =
            new Dictionary<string, IReadOnlyList<(string Name, PinDataType Type)>>(StringComparer.OrdinalIgnoreCase)
            {
                ["dbo.Sancao"] = [
                    ("id", PinDataType.Number),
                    ("descricao", PinDataType.Text)
                ]
            };

        var loaded = new CanvasViewModel();
        loaded.Nodes.Clear();
        loaded.Connections.Clear();

        CanvasLoadResult result = CanvasSerializer.Deserialize(mutatedJson, loaded, lookup);

        Assert.True(result.Success);
        NodeViewModel table = Assert.Single(loaded.Nodes);
        Assert.Equal(PinDataType.Number, table.OutputPins.Single(p => p.Name == "id").EffectiveDataType);
        Assert.Equal(PinDataType.Text, table.OutputPins.Single(p => p.Name == "descricao").EffectiveDataType);
    }

    [Fact]
    public void SerializeDeserialize_PreservesPreviewParameterInputs()
    {
        var source = new CanvasViewModel();
        source.RememberPreviewParameterInputs(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Postgres|localhost|5432|sales|named:customer_id"] = "42",
            ["Postgres|localhost|5432|sales|pos:1:?"] = "active",
        });

        string json = CanvasSerializer.Serialize(source);

        var loaded = new CanvasViewModel();
        CanvasLoadResult result = CanvasSerializer.Deserialize(json, loaded);

        Assert.True(result.Success);
        Assert.Equal("42", loaded.PreviewParameterInputs["Postgres|localhost|5432|sales|named:customer_id"]);
        Assert.Equal("active", loaded.PreviewParameterInputs["Postgres|localhost|5432|sales|pos:1:?"]);
    }

    [Fact]
    public void InsertSubgraph_WhenConnectionRebuildFails_RollsBackInsertedNodesAndConnections()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();

        List<SavedNode> nodes =
        [
            new SavedNode(
                NodeId: "n1",
                NodeType: "TableSource",
                X: 10,
                Y: 20,
                ZOrder: null,
                Alias: null,
                TableFullName: "public.orders",
                Parameters: new Dictionary<string, string>(),
                PinLiterals: new Dictionary<string, string>(),
                Columns: [new SavedColumn("id", "Integer")]
            ),
            new SavedNode(
                NodeId: "n2",
                NodeType: "TableSource",
                X: 30,
                Y: 40,
                ZOrder: null,
                Alias: null,
                TableFullName: "public.customers",
                Parameters: new Dictionary<string, string>(),
                PinLiterals: new Dictionary<string, string>(),
                Columns: [new SavedColumn("id", "Integer")]
            ),
        ];

        List<SavedConnection> invalidConnections =
        [
            new SavedConnection(
                FromNodeId: "n1",
                FromPinName: "id",
                ToNodeId: "n2",
                ToPinName: "missing_pin")
        ];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            CanvasSerializer.InsertSubgraph(
                nodes,
                invalidConnections,
                vm,
                new Point(100, 100))
        );

        Assert.Contains("cannot resolve pins", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void InsertSubgraph_WhenConnectionTargetsExternalNode_ThrowsExplicitError()
    {
        var vm = new CanvasViewModel();
        vm.Nodes.Clear();
        vm.Connections.Clear();

        List<SavedNode> nodes =
        [
            new SavedNode(
                NodeId: "n1",
                NodeType: "TableSource",
                X: 10,
                Y: 20,
                ZOrder: null,
                Alias: null,
                TableFullName: "public.orders",
                Parameters: new Dictionary<string, string>(),
                PinLiterals: new Dictionary<string, string>(),
                Columns: [new SavedColumn("id", "Integer")]
            ),
        ];

        List<SavedConnection> externalConnection =
        [
            new SavedConnection(
                FromNodeId: "n1",
                FromPinName: "id",
                ToNodeId: "external_node",
                ToPinName: "id")
        ];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            CanvasSerializer.InsertSubgraph(
                nodes,
                externalConnection,
                vm,
                new Point(100, 100))
        );

        Assert.Contains("missing destination node", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external_node", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void PruneOldFiles_WhenEnumerationFails_RaisesWarning()
    {
        var warnings = new List<string>();
        CanvasSerializer.WarningRaised += warnings.Add;
        try
        {
            var prune = typeof(CanvasSerializer).GetMethod(
                "PruneOldFiles",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );
            Assert.NotNull(prune);

            string invalidDir = "invalid\0dir";
            prune!.Invoke(null, [invalidDir, 1]);

            Assert.Contains(
                warnings,
                w => w.Contains("Could not enumerate prune candidates", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            CanvasSerializer.WarningRaised -= warnings.Add;
        }
    }

    [Fact]
    public void TryConnect_WhenCompatibilityEvaluationThrows_RaisesWarningAndReturnsFalse()
    {
        var warnings = new List<string>();
        CanvasSerializer.WarningRaised += warnings.Add;
        try
        {
            var toNode = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(20, 20));
            PinViewModel toPin = Assert.Single(toNode.InputPins, p => p.Name == "left");
            var connections = new List<ConnectionViewModel>();

            var tryConnect = typeof(CanvasSerializer).GetMethod(
                "TryConnect",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                binder: null,
                types:
                [
                    typeof(ICollection<ConnectionViewModel>),
                    typeof(PinViewModel),
                    typeof(PinViewModel),
                    typeof(SavedConnection)
                ],
                modifiers: null
            );
            Assert.NotNull(tryConnect);

            var savedConnection = new SavedConnection(
                FromNodeId: "n1",
                FromPinName: "id",
                ToNodeId: "n2",
                ToPinName: "id"
            );

            // Force EvaluateConnection path to throw by sending null source pin.
            object? result = tryConnect!.Invoke(null, [connections, null!, toPin, savedConnection]);
            Assert.False(Assert.IsType<bool>(result));
            Assert.Empty(connections);
            Assert.Contains(
                warnings,
                w => w.Contains("compatibility evaluation error", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            CanvasSerializer.WarningRaised -= warnings.Add;
        }
    }

    [Fact]
    public void ApplyWireMetadata_WhenBreakpointHasNaNOrInfinity_SkipsInvalidPointsAndRaisesWarning()
    {
        var warnings = new List<string>();
        CanvasSerializer.WarningRaised += warnings.Add;
        try
        {
            var from = new NodeViewModel("public.orders", [("id", PinDataType.Integer)], new Point(0, 0));
            var to = new NodeViewModel(NodeDefinitionRegistry.Get(NodeType.Equals), new Point(100, 0));
            PinViewModel fromPin = Assert.Single(from.OutputPins, p => p.Name == "id");
            PinViewModel toPin = Assert.Single(to.InputPins, p => p.Name == "left");
            var connection = new ConnectionViewModel(fromPin, default, default) { ToPin = toPin };

            var applyWireMetadata = typeof(CanvasSerializer).GetMethod(
                "ApplyWireMetadata",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            );
            Assert.NotNull(applyWireMetadata);

            var savedConnection = new SavedConnection(
                FromNodeId: "n1",
                FromPinName: "id",
                ToNodeId: "n2",
                ToPinName: "left",
                RoutingMode: CanvasWireRoutingMode.Orthogonal.ToString(),
                Breakpoints:
                [
                    new SavedWireBreakpoint(double.NaN, 20),
                    new SavedWireBreakpoint(50, double.PositiveInfinity),
                    new SavedWireBreakpoint(120, 80),
                ]
            );

            applyWireMetadata!.Invoke(null, [connection, savedConnection]);

            Assert.Single(connection.Breakpoints);
            Assert.Equal(new Point(120, 80), connection.Breakpoints[0].Position);
            Assert.Contains(
                warnings,
                w => w.Contains("Skipped invalid wire breakpoint", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            CanvasSerializer.WarningRaised -= warnings.Add;
        }
    }
}
