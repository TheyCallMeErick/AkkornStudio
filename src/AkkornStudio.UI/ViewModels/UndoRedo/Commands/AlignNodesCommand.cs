using Avalonia;

namespace AkkornStudio.UI.ViewModels.UndoRedo.Commands;

public sealed class AlignNodesCommand(IReadOnlyList<NodeViewModel> nodes, AlignMode mode)
    : ICanvasCommand
{
    private const double DefaultNodeW = 230;
    private const double MinimumNodeH = 74;
    private const double PinRowH = 22;

    private readonly IReadOnlyList<NodeViewModel> _nodes =
        nodes ?? throw new ArgumentNullException(nameof(nodes));
    private readonly IReadOnlyList<Point> _before = nodes.Select(n => n.Position).ToArray();
    private readonly IReadOnlyList<Point> _after = ComputeTargets(nodes, mode);

    public string Description => "Align nodes";

    public void Execute(CanvasViewModel _)
    {
        for (int i = 0; i < _nodes.Count; i++)
            _nodes[i].Position = _after[i];
    }

    public void Undo(CanvasViewModel _)
    {
        for (int i = 0; i < _nodes.Count; i++)
            _nodes[i].Position = _before[i];
    }

    private static IReadOnlyList<Point> ComputeTargets(
        IReadOnlyList<NodeViewModel> nodes,
        AlignMode mode
    )
    {
        if (nodes.Count == 0)
            return [];

        var nodeIndexes = new Dictionary<NodeViewModel, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < nodes.Count; i++)
            nodeIndexes[nodes[i]] = i;

        static double W(NodeViewModel n) =>
            double.IsFinite(n.Width) && n.Width > 0 ? n.Width : DefaultNodeW;

        static double H(NodeViewModel n)
        {
            int pinRows = Math.Max(n.InputPins.Count, n.OutputPins.Count);
            return MinimumNodeH + (pinRows * PinRowH);
        }

        Point[] result = [.. nodes.Select(n => n.Position)];

        switch (mode)
        {
            case AlignMode.Left:
            {
                double anchor = nodes.Min(n => n.Position.X);
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(anchor, nodes[i].Position.Y);
                break;
            }
            case AlignMode.Right:
            {
                double anchor = nodes.Max(n => n.Position.X + W(n));
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(anchor - W(nodes[i]), nodes[i].Position.Y);
                break;
            }
            case AlignMode.Top:
            {
                double anchor = nodes.Min(n => n.Position.Y);
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(nodes[i].Position.X, anchor);
                break;
            }
            case AlignMode.Bottom:
            {
                double anchor = nodes.Max(n => n.Position.Y + H(n));
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(nodes[i].Position.X, anchor - H(nodes[i]));
                break;
            }
            case AlignMode.CenterH:
            {
                double cy = nodes.Average(n => n.Position.Y + H(n) / 2.0);
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(nodes[i].Position.X, cy - H(nodes[i]) / 2.0);
                break;
            }
            case AlignMode.CenterV:
            {
                double cx = nodes.Average(n => n.Position.X + W(n) / 2.0);
                for (int i = 0; i < nodes.Count; i++)
                    result[i] = new Point(cx - W(nodes[i]) / 2.0, nodes[i].Position.Y);
                break;
            }
            case AlignMode.DistributeH:
            {
                var sorted = nodes.OrderBy(n => n.Position.X).ToList();
                double left = sorted.First().Position.X;
                double right = sorted.Last().Position.X + W(sorted.Last());
                double totalW = sorted.Sum(n => W(n));
                double gap = (right - left - totalW) / Math.Max(sorted.Count - 1, 1);
                double cursor = left;
                for (int i = 0; i < sorted.Count; i++)
                {
                    int idx = nodeIndexes[sorted[i]];
                    result[idx] = new Point(cursor, nodes[idx].Position.Y);
                    cursor += W(sorted[i]) + gap;
                }
                break;
            }
            case AlignMode.DistributeV:
            {
                var sorted = nodes.OrderBy(n => n.Position.Y).ToList();
                double top = sorted.First().Position.Y;
                double bottom = sorted.Last().Position.Y + H(sorted.Last());
                double totalH = sorted.Sum(n => H(n));
                double gap = (bottom - top - totalH) / Math.Max(sorted.Count - 1, 1);
                double cursor = top;
                for (int i = 0; i < sorted.Count; i++)
                {
                    int idx = nodeIndexes[sorted[i]];
                    result[idx] = new Point(nodes[idx].Position.X, cursor);
                    cursor += H(sorted[i]) + gap;
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported align mode.");
        }

        return result;
    }
}
