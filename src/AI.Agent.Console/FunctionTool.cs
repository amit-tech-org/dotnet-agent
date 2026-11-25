using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AI.Agent.ConsoleApp
{
    public class FunctionTool : BaseTool
    {
        private readonly Delegate _func;
        private readonly bool _needsContext;

        public FunctionTool(
            Delegate func,
            string? name = null,
            string? description = null,
            object? toolDefinition = null)
            : base(
                ResolveName(func, name),
                ResolveDescription(func, description),
                toolDefinition ?? GenerateDefinition(
                    func,
                    ResolveName(func, name),
                    ResolveDescription(func, description)))
        {
            ArgumentNullException.ThrowIfNull(func);
            _func = func;
            _needsContext = func.Method.GetParameters().Any(IsContextParameter);
        }

        public override async Task<object?> ExceuteAsync(
            ExecutionContext executionContext,
            Dictionary<string, object?>? kwargs = null)
        {
            object? result = Invoke(executionContext, kwargs);
            return await UnwrapAsync(result);
        }

        private object? Invoke(ExecutionContext executionContext, Dictionary<string, object?>? kwargs)
        {
            kwargs ??= new Dictionary<string, object?>();
            ParameterInfo[] parameters = _func.Method.GetParameters();
            var arguments = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                if (IsContextParameter(parameter))
                {
                    arguments[i] = executionContext;
                    continue;
                }

                if (!kwargs.TryGetValue(parameter.Name!, out object? value))
                {
                    if (!parameter.HasDefaultValue)
                    {
                        throw new ArgumentException($"Missing argument '{parameter.Name}' for tool '{Name}'.");
                    }

                    arguments[i] = parameter.DefaultValue;
                    continue;
                }

                arguments[i] = ConvertArgument(value, parameter.ParameterType);
            }

            return _func.DynamicInvoke(arguments);
        }

        private static async Task<object?> UnwrapAsync(object? result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                Type taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    return taskType.GetProperty("Result")!.GetValue(task);
                }

                return null;
            }

            Type? resultType = result?.GetType();
            if (resultType is not null
                && resultType.IsGenericType
                && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>))
            {
                Task genericTask = (Task)resultType.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(result, null)!;
                await genericTask.ConfigureAwait(false);
                return genericTask.GetType().GetProperty("Result")!.GetValue(genericTask);
            }

            if (result is ValueTask valueTask)
            {
                await valueTask.ConfigureAwait(false);
                return null;
            }

            return result;
        }

        private static Dictionary<string, object?> GenerateDefinition(Delegate func, string name, string description)
        {
            var definition = new ToolDefinition
            {
                Function = new FunctionDefinition
                {
                    Name = name,
                    Description = description,
                    Parameters = FunctionToInputSchema(func)
                }
            };

            return ToolDefinitionFactory.Create(definition, LlmProvider.OpenAI);
        }

        private static JsonSchema FunctionToInputSchema(Delegate func)
        {
            var properties = new Dictionary<string, JsonSchema>();
            var required = new List<string>();

            foreach (ParameterInfo parameter in func.Method.GetParameters())
            {
                if (IsContextParameter(parameter) || parameter.Name is null)
                {
                    continue;
                }

                var schema = new JsonSchema
                {
                    Type = ToJsonType(parameter.ParameterType)
                };

                string? parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrWhiteSpace(parameterDescription))
                {
                    schema.Description = parameterDescription;
                }

                Type parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                if (parameterType.IsEnum)
                {
                    schema.Type = "string";
                    schema.Enum = Enum.GetNames(parameterType).ToList();
                }

                properties[parameter.Name] = schema;
                if (!parameter.HasDefaultValue)
                {
                    required.Add(parameter.Name);
                }
            }

            return new JsonSchema
            {
                Type = "object",
                Properties = properties,
                Required = required
            };
        }

        private static string ResolveName(Delegate func, string? name)
        {
            ArgumentNullException.ThrowIfNull(func);
            return string.IsNullOrWhiteSpace(name) ? func.Method.Name : name.Trim();
        }

        private static string ResolveDescription(Delegate func, string? description)
        {
            ArgumentNullException.ThrowIfNull(func);
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }

            return func.Method.GetCustomAttribute<DescriptionAttribute>()?.Description?.Trim() ?? "";
        }

        private static bool IsContextParameter(ParameterInfo parameter)
        {
            return parameter.ParameterType == typeof(ExecutionContext)
                || string.Equals(parameter.Name, "context", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToJsonType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum)
            {
                return "string";
            }

            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
            {
                return "string";
            }

            if (type == typeof(bool))
            {
                return "boolean";
            }

            if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
            {
                return "integer";
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return "number";
            }

            if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            {
                return "array";
            }

            return "object";
        }

        private static object? ConvertArgument(object? value, Type targetType)
        {
            if (value is null)
            {
                return null;
            }

            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsInstanceOfType(value))
            {
                return value;
            }

            if (value is JsonElement element)
            {
                return JsonSerializer.Deserialize(element.GetRawText(), targetType);
            }

            if (underlying.IsEnum)
            {
                return value is string name
                    ? Enum.Parse(underlying, name, ignoreCase: true)
                    : Enum.ToObject(underlying, value);
            }

            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
    }
}
