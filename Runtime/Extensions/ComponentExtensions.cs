using System.Linq;
using UnityEngine;

namespace TransformChangesDebugger.API.Extensions
{
    public static class ComponentExtensions
    {
        public static bool HasTag(this Component component)
        {
            return !string.IsNullOrWhiteSpace(component.tag) && component.tag != "Untagged";
        }
        
        public static string GetFullPath(this Component component)
        {
            return string.Join("/", component.GetComponentsInParent<Transform>().Select(t => t.name).Reverse().ToArray());
        }
    }
}