using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LightestNight.System.EventSourcing.SqlStreamStore.Subscriptions
{
    public static class ExtendsAssembly
    {
        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return (e.Types ?? Enumerable.Empty<Type>()).Where(type => type != null);
            }
        }
    }
}