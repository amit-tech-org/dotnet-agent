namespace AI.Agent.ConsoleApp
{
    public class ExecutionContext
    {
        public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
        public List<Event> Events { get; set; } = new List<Event>();
        public int CurrentStep { get; set; }
        public Dictionary<string, object> State { get; set; }
        public object FinalResult { get; set; }
        public void AddEvent(Event @event) 
        {
            Events.Add(@event);        
        }
        public void IncrementStep()
        {
            CurrentStep++;
        }
    }
}
