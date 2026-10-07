using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BuildingRegulationReview.Mcp.Json;
using BuildingRegulationReview.Mcp.Tools;

namespace BuildingRegulationReview.Mcp
{
    /// <summary>
    /// A tool whose work runs in Revit's API context. Subclasses implement <see cref="Execute"/> as if
    /// they were inside an ExternalEvent handler — which they are.
    /// </summary>
    internal abstract class RevitMcpTool : IMcpTool
    {
        private readonly RevitMcpDispatcher _dispatcher;

        protected RevitMcpTool(RevitMcpDispatcher dispatcher) =>
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        public abstract string Name { get; }
        public abstract string Title { get; }
        public abstract string Description { get; }
        public abstract JsonObject InputSchema { get; }
        public abstract McpToolAnnotations Annotations { get; }

        /// <summary>How long the work may run once Revit has started it.</summary>
        protected virtual TimeSpan RunTimeout => TimeSpan.FromMinutes(2);

        public McpToolResult Call(McpToolArguments arguments, CancellationToken cancellation) =>
            _dispatcher.Invoke(Name, (application, token) => Execute(application, arguments, token), RunTimeout, cancellation);

        protected abstract McpToolResult Execute(UIApplication application, McpToolArguments arguments, CancellationToken cancellation);

        /// <summary>The active project document, or a tool error the agent can act on.</summary>
        protected static Document RequireProject(UIApplication application)
        {
            var document = application?.ActiveUIDocument?.Document;
            if (document == null) throw new McpToolException("Revit 目前沒有開啟中的專案。請先在 Revit 開啟要檢討的模型。");
            if (document.IsFamilyDocument) throw new McpToolException("目前的作用中文件是族群檔，請切換到專案模型。");
            return document;
        }

        /// <summary>
        /// Runs <paramref name="work"/> and, for a dry run, undoes everything it wrote: one
        /// TransactionGroup around it, rolled back at the end. The work sees its own writes, so a dry
        /// run answers exactly what the real call would — and the model is left as it was.
        /// </summary>
        protected static T WithDryRun<T>(Document document, bool dryRun, string name, Func<T> work)
        {
            if (!dryRun) return work();

            using (var group = new TransactionGroup(document, name + "（試跑，將復原）"))
            {
                group.Start();
                try
                {
                    return work();
                }
                finally
                {
                    if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                }
            }
        }
    }
}
