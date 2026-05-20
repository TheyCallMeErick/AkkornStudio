using AkkornStudio.Nodes;

namespace AkkornStudio.Ddl.Compilers;

internal sealed class AlterViewOutputCompiler : IDdlOutputCompiler
{
    public NodeType OutputType => NodeType.AlterViewOutput;

    public void Compile(
        IReadOnlyList<NodeInstance> outputNodes,
        DdlOutputCompilationContext context,
        ICollection<IDdlExpression> statements
    )
    {
        foreach (NodeInstance outputNode in outputNodes)
        {
            try
            {
                Connection? input = context.Graph.GetSingleInputConnection(outputNode.Id, "view");
                if (input is null)
                {
                    context.AddError(
                        "E-DDL-ALTERVIEW-OUTPUT-VIEW",
                        "AlterViewOutput requer input 'view' conectado.",
                        outputNode.Id
                    );
                    continue;
                }

                NodeInstance viewNode = context.Graph.NodeMap[input.FromNodeId];
                if (viewNode.Type != NodeType.ViewDefinition)
                {
                    context.AddError(
                        "E-DDL-ALTERVIEW-OUTPUT-TYPE",
                        "AlterViewOutput.view deve vir de ViewDefinition.",
                        outputNode.Id
                    );
                    continue;
                }

                statements.Add(context.CompileAlterViewDefinition(viewNode));
            }
            catch (Exception ex)
            {
                context.AddError("E-DDL-COMPILE-ALTERVIEW", ex.Message, outputNode.Id);
            }
        }
    }
}
