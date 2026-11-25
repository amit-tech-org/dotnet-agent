using System.Runtime.Serialization;

namespace AI.Agent.ConsoleApp
{
    public enum ContentType
    {
        [EnumMember(Value = "message")]
        Message,
        [EnumMember(Value = "tool_call")]
        ToolCall,
        [EnumMember(Value = "tool_result")]
        ToolResult,
    }
    public enum Status
    {
        [EnumMember(Value = "success")]
        Success,
        [EnumMember(Value = "error")]
        Error
    }
    public enum Role
    {
        [EnumMember(Value = "system")]
        System,
        [EnumMember(Value = "user")]

        User,
        [EnumMember(Value = "assistant")]
        Assistant
    }
}
