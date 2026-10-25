using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Novalist.AiBridge;

/// <summary>
/// Thin compatibility adapter for minor SDK shape differences. Only invokes a tight allow-list
/// of documented read operations. Never exposes arbitrary method dispatch to an MCP caller.
/// </summary>
internal static class HostReflection
{
    public static object? Property(object? target, params string[] names)
    {
        if (target is null) return null;
        foreach (var name in names)
        {
            var property = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is not null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target);
        }
        return null;
    }

    public static string Str(object? target, params string[] names)
    {
        var value = Property(target, names);
        return value?.ToString() ?? "";
    }

    public static bool Bool(object? target, params string[] names)
    {
        var value = Property(target, names);
        return value is bool flag && flag;
    }

    public static List<object> Items(object? value)
    {
        if (value is null || value is string) return new();
        if (value is IEnumerable enumerable) return enumerable.Cast<object>().ToList();
        foreach (var member in new[] { "Chapters", "Items", "Results", "Values" })
        {
            var contained = Property(value, member);
            if (contained is IEnumerable entries && contained is not string)
                return entries.Cast<object>().ToList();
        }
        return new();
    }

    public static int CompareOrder(object a, object b)
    {
        var orderA = Property(a, "Order", "SortOrder", "Position", "Index");
        var orderB = Property(b, "Order", "SortOrder", "Position", "Index");
        if (orderA is null || orderB is null) return 0;
        if (decimal.TryParse(orderA.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x)
            && decimal.TryParse(orderB.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
            return x.CompareTo(y);
        return 0;
    }

    /// <summary>Find a candidate safely; do not execute unknown write methods.</summary>
    public static async Task<(bool found, object? value)> TryReadAsync(
        object target, string[] methodNames, params object?[] args)
    {
        try { return (true, await ReadAsync(target, methodNames, args)); }
        catch (NotSupportedException) { return (false, null); }
    }

    /// <summary>
    /// Safe metadata-only diagnostics for public read methods. Do not expose
    /// exception stack traces, file paths, project titles or content.
    /// </summary>
    public static object SafeApiDescription(object? target)
    {
        if (target is null) return new { available = false, methods = Array.Empty<string>(), properties = Array.Empty<string>() };
        var type = target.GetType();
        // Include public interface methods too: some SDK implementations use
        // explicit interface implementations not visible as public concrete methods.
        var allMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Concat(type.GetInterfaces().SelectMany(i => i.GetMethods()));
        var methods = allMethods
            .Where(m => m.Name.StartsWith("Get", StringComparison.OrdinalIgnoreCase)
                     || m.Name.StartsWith("List", StringComparison.OrdinalIgnoreCase)
                     || m.Name.StartsWith("Read", StringComparison.OrdinalIgnoreCase))
            .Where(m => new[] { "Chapter", "Scene", "Book", "Draft" }
                .Any(term => m.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters()
                .Select(p => p.ParameterType.Name + " " + p.Name + (p.HasDefaultValue ? "=optional" : "")))
                + ") -> " + m.ReturnType.Name)
            .Distinct().OrderBy(s => s).Take(100).ToArray();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => new[] { "Chapter", "Book", "Draft" }
                .Any(term => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name + " : " + p.PropertyType.Name)
            .OrderBy(s => s).Take(50).ToArray();
        return new { available = true, methods, properties };
    }

    public static async Task<object?> ReadAsync(object target, string[] methodNames, params object?[] args)
    {
        foreach (var name in methodNames)
        {
            var targetType = target.GetType();
            var candidates = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Concat(targetType.GetInterfaces().SelectMany(i => i.GetMethods()))
                .Where(method => method.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(method => method.GetParameters().Length);
            foreach (var method in candidates)
            {
                if (!TryBind(method, args, out var supplied)) continue;
                try
                {
                    var output = method.Invoke(target, supplied);
                    if (output is Task task)
                    {
                        await task.ConfigureAwait(false);
                        return task.GetType().IsGenericType ? Property(task, "Result") : null;
                    }
                    // Handle ValueTask<T> without binding any particular T at compile time.
                    if (output is not null && output.GetType().FullName?.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) == true)
                    {
                        var asTask = output.GetType().GetMethod("AsTask", Type.EmptyTypes);
                        if (asTask?.Invoke(output, null) is Task awaited)
                        {
                            await awaited.ConfigureAwait(false);
                            return Property(awaited, "Result");
                        }
                    }
                    return output;
                }
                catch (TargetInvocationException e) when (e.InnerException is not null)
                {
                    throw new InvalidOperationException($"Novalist SDK {name} failed: {e.InnerException.Message}", e.InnerException);
                }
            }
        }
        throw new NotSupportedException("The installed Novalist SDK does not expose the expected read method: "
            + string.Join(" / ", methodNames) + ". Confirm Novalist 3.6 and SDK 15.");
    }

    private static bool TryBind(MethodInfo method, object?[] args, out object?[] supplied)
    {
        var p = method.GetParameters();
        supplied = new object?[p.Length];
        if (p.Length < args.Length) return false;
        for (var i = 0; i < p.Length; i++)
        {
            if (i < args.Length)
            {
                if (!TryConvert(args[i], p[i].ParameterType, out supplied[i])) return false;
            }
            else if (p[i].ParameterType == typeof(CancellationToken)) supplied[i] = CancellationToken.None;
            else if (p[i].HasDefaultValue) supplied[i] = p[i].DefaultValue;
            else return false;
        }
        return true;
    }

    private static bool TryConvert(object? value, Type destination, out object? converted)
    {
        converted = value;
        if (value is null) return !destination.IsValueType || Nullable.GetUnderlyingType(destination) is not null;
        if (destination.IsInstanceOfType(value)) return true;
        var text = value.ToString() ?? "";
        if (destination == typeof(string)) { converted = text; return true; }
        if (destination == typeof(Guid) && Guid.TryParse(text, out var guid)) { converted = guid; return true; }
        if (destination == typeof(int) && int.TryParse(text, out var number)) { converted = number; return true; }
        return false;
    }
}
