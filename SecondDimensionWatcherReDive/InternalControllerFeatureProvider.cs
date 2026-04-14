using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SecondDimensionWatcherReDive;

internal sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo)
    {
        if (!typeInfo.IsClass || typeInfo.IsAbstract || typeInfo.ContainsGenericParameters
            || typeInfo.IsDefined(typeof(NonControllerAttribute)))
            return false;

        return typeof(ControllerBase).IsAssignableFrom(typeInfo);
    }
}
