using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LoqNova.Lib;
using LoqNova.Lib.Automation.Steps;

namespace LoqNova.Avalonia.Services;

/// <summary>
/// Reads and replaces the configuration of a real backend step.
///
/// Steps expose their state through <c>IAutomationStep&lt;T&gt;</c> and offer the full
/// set of legal values through <c>GetAllStatesAsync()</c>, so this works against the
/// backend's own contract instead of inventing a parallel config object. A step is
/// reconfigured by constructing a new instance of the same type with the new state,
/// which is precisely what every step's own <c>DeepCopy()</c> does.
/// </summary>
internal static class StepConfiguration
{
    /// <summary>Describes a step's current configuration for display.</summary>
    public static string Describe(IAutomationStep step)
    {
        var state = GetState(step);

        return state?.ToString() ?? string.Empty;
    }

    /// <summary>The step's state value, or null when it exposes none.</summary>
    public static object? GetState(IAutomationStep step)
    {
        var stateProperty = FindStateProperty(step.GetType());

        return stateProperty?.GetValue(step);
    }

    /// <summary>
    /// Every value the step accepts, taken from the backend.
    ///
    /// GetAllStatesAsync is asynchronous and returns Task&lt;T[]&gt;, so the result is
    /// awaited rather than treated as a collection. Reflection is confined to the
    /// backend's own contract; a step that throws is reported as having no selectable
    /// values rather than taking the page down with it.
    /// </summary>
    public static async Task<IReadOnlyList<object?>> GetStatesAsync(IAutomationStep step)
    {
        if (FindStatesMethod(step.GetType()) is not { } method)
        {
            return [];
        }

        try
        {
            if (method.Invoke(step, null) is not Task task)
            {
                return [];
            }

            await task.ConfigureAwait(false);

            // The awaited result carries the array; reflection cannot type it for us.
            var property = task.GetType().GetProperty("Result");
            var result = property?.GetValue(task);

            if (result is not System.Collections.IEnumerable values)
            {
                return [];
            }

            var list = new List<object?>();

            foreach (var value in values)
            {
                list.Add(value);
            }

            return list;
        }
        catch (Exception)
        {
            // A step that cannot enumerate its states simply offers no configuration.
            return [];
        }
    }

    /// <summary>
    /// Builds a new step of the same type carrying <paramref name="state"/>, so the
    /// editor can change configuration without mutating an immutable backend object.
    /// </summary>
    public static IAutomationStep? WithState(IAutomationStep step, object? state)
    {
        if (state is null)
        {
            return null;
        }

        var stateType = FindStateProperty(step.GetType())?.PropertyType;

        if (stateType is null)
        {
            return null;
        }

        var constructor = step.GetType().GetConstructors()
            .FirstOrDefault(c =>
            {
                var parameters = c.GetParameters();

                return parameters.Length == 1
                       && parameters[0].ParameterType == stateType;
            });

        if (constructor is null)
        {
            return null;
        }

        return constructor.Invoke([state]) as IAutomationStep;
    }

    /// <summary>The value type a step's state is built on, for enum-backed pickers.</summary>
    public static Type? GetStateType(IAutomationStep step) =>
        FindStateProperty(step.GetType())?.PropertyType;

    private static PropertyInfo? FindStateProperty(Type stepType)
    {
        for (var type = stepType; type is not null; type = type.BaseType)
        {
            var property = type.GetProperty("State", BindingFlags.Public | BindingFlags.Instance);

            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static MethodInfo? FindStatesMethod(Type stepType)
    {
        for (var type = stepType; type is not null; type = type.BaseType)
        {
            var method = type.GetMethod("GetAllStatesAsync", BindingFlags.Public | BindingFlags.Instance);

            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }
}
