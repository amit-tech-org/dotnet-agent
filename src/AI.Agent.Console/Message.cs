namespace AI.Agent.ConsoleApp
{
    public class Message : IContentItem
    {
        public ContentType Type { get; } = ContentType.Message;
        public Role Role { get; set; }
        public string Content { get; set; }
    }
}
