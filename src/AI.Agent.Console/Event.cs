namespace AI.Agent.ConsoleApp
{
    public class Event
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ExecutionId { get; set; }
        public long TimeStamp { get; set; } = DateTime.UtcNow.Ticks;
        public string Author { get; set; }
        public IEnumerable<IContentItem> Content { get; set; } = new List<IContentItem>();
    }
}
