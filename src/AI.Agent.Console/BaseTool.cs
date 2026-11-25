namespace AI.Agent.ConsoleApp
{
    public abstract class BaseTool
    {
        public BaseTool(string name, string desc, object toolDef)
        {
            Name = name;
            Description = desc;
            ToolDefination = toolDef;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public object ToolDefination { get; set; }

        public abstract Task<object?> ExceuteAsync(
            ExecutionContext executionContext,
            Dictionary<string, object?>? kwargs = null);
    }
}
