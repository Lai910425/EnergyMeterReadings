using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EnergyMeter.Api.Infrastructure;

/// <summary>
/// Query-string / route handling for DateTime and DateTime?, using the same rules as the JSON converter.
/// Without this, ASP.NET Core would silently accept "2026-11-01T08:00:00" (no zone) and interpret it
/// differently from the JSON body path.
/// </summary>
public sealed class UtcDateTimeModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var name = bindingContext.ModelName;
        var valueResult = bindingContext.ValueProvider.GetValue(name);
        if (valueResult == ValueProviderResult.None)
        {
            // Nothing supplied: leave it to [Required] / nullability to decide.
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(name, valueResult);
        var raw = valueResult.FirstValue;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.CompletedTask;
        }

        if (UtcTimestamp.TryParse(raw, out var utc))
        {
            bindingContext.Result = ModelBindingResult.Success(utc);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(name, $"'{raw}' is not valid: {name} {UtcTimestamp.ExpectedFormatMessage}.");
        }

        return Task.CompletedTask;
    }
}

public sealed class UtcDateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Metadata.UnderlyingOrModelType == typeof(DateTime)
            ? new UtcDateTimeModelBinder()
            : null;
    }
}
