using System.Linq;
using System.Reflection;

namespace TransformChangesDebugger.API.Extensions
{
    public static class MethodBaseExtensions
    {
        public static string ResolveFullName(this MethodBase method)
        {
            if (method == null) return string.Empty;
            
            return $"{method.ReflectedType.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(o => $"{o.ParameterType}").ToArray())})";
        }
    }
}