using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Microsoft.Bot.Builder.Compatibility
{
    internal static class TypeExtensions
    {
        public static bool IsAssignableFrom(this Type self, Type t)
        {
            if (self == null) throw new ArgumentNullException(nameof(self));
            if (t == null) throw new ArgumentNullException(nameof(t));
            return self.IsAssignableFrom(t);
        }
    }
}
