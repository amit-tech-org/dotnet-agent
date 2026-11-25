namespace AI.Agent.ConsoleApp
{
    public class LlmResponse
    {
        public List<IContentItem> Content { get; set; } = new List<IContentItem>();
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object?> UsageMetadata { get; set; } = new Dictionary<string, object?>();
    }
}