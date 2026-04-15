using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Abstractions;

public interface IToolExecutorBuilder
{
    IToolExecutorBuilder AddTool<TTool>() where TTool : class, ITool;

    IToolExecutor Build();
}
