using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Bot.Builder.Dialogs.Internals;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Scorables.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Builder.Fibers
{
    /// <summary>
    /// Return the resolved instance, if it can be resolved by type.
    /// </summary>
    internal class ResolvableObjectJsonConverter : JsonConverter
    {
        private readonly IResolver resolver;
        private bool disabled;      // CXuesong: Looks not so pretty… But it works anyway.

        public ResolvableObjectJsonConverter(IResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            this.resolver = resolver;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("$resolve");
            writer.WriteValue(value.GetType().AssemblyQualifiedName);
            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var token = JToken.ReadFrom(reader);
            var typeName = (string) token["$resolve"];
            if (typeName != null)
            {
                var type = Type.GetType(typeName, true);
                return resolver.Resolve(type, null);
            }
            // Falls back to default behavior
            // This can sometimes happen, when a new service has been registered in the resolver
            // After the serialization.
            disabled = true;
            try
            {
                return serializer.Deserialize(token.CreateReader(), objectType);
            }
            finally
            {
                disabled = false;
            }
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            // Typical types that return true
            // FrameFactory`1[DialogTask]
            // WaitFactory`1[DialogTask]
            // NullWait`1[DialogTask]
            if (disabled) return false;
            var result = resolver.CanResolve(objectType, null);
            Debug.WriteLineIf(result, "ResolvableObjectJsonConverter, Use IResolve: " + objectType);
            return result;
        }
    }

    /// <summary>
    /// A crude delegate serializer.
    /// </summary>
    internal class DelegateJsonConverter : JsonConverter
    {
        public static readonly DelegateJsonConverter Default = new DelegateJsonConverter();

        private class DelegateInfo : IEquatable<DelegateInfo>
        {
            public DelegateInfo(Type delegateType, MethodInfo method, object target)
            {
                if (method == null) throw new ArgumentNullException(nameof(method));
                DelegateType = delegateType;
                Method = method;
                Target = target;
            }

            public static IEnumerable<DelegateInfo> FromDelegate(Delegate d)
            {
                if (d == null) throw new ArgumentNullException(nameof(d));
                return d.GetInvocationList().Select(d1 => new DelegateInfo(d1.GetType(), d1.GetMethodInfo(), d1.Target));
            }

            public object Target { get; }

            public MethodInfo Method { get; }

            public Type DelegateType { get; }

            public Delegate ToDelegate()
            {
                return Method.CreateDelegate(DelegateType, Target);
            }

            /// <inheritdoc />
            public bool Equals(DelegateInfo other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Target == other.Target && Equals(Method, other.Method) && DelegateType == other.DelegateType;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((DelegateInfo) obj);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Target?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (Method?.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ (DelegateType?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Delegate).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var delegates = serializer.Deserialize<IEnumerable<DelegateInfo>>(reader);
            return Delegate.Combine(delegates.Select(di => di.ToDelegate()).ToArray());
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // TODO CXuesong: Implement the ability for user to add their custom JsonSerializer.
            var type = value.GetType();
            var generated = type.GetTypeInfo().GetCustomAttribute<CompilerGeneratedAttribute>() != null;
            if (generated && !type.GetTypeInfo().IsSerializable) throw new ClosureCaptureException(value);
            serializer.Serialize(writer, DelegateInfo.FromDelegate((Delegate) value));
        }
    }

    internal class MethodInfoJsonConverter : JsonConverter
    {
        public static readonly MethodInfoJsonConverter Default = new MethodInfoJsonConverter();

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var method = (MethodInfo) value;
            var sb = new StringBuilder(method.DeclaringType.AssemblyQualifiedName);
            sb.Append('|');
            sb.Append(method.Name);
            sb.Append('|');
            if (method.IsPublic) sb.Append('P');
            if (method.IsPrivate) sb.Append('p');
            if (method.IsStatic) sb.Append('S');
            foreach (var t in method.CachedParameterTypes())
            {
                sb.Append('|');
                sb.Append(t);
            }
            writer.WriteValue(sb.ToString());
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var fields = existingValue.ToString().Split('|');
            var type = Type.GetType(fields[0], true);
            var paramTypes = fields.Skip(2).Select(Type.GetType).ToArray();
            BindingFlags flags = 0;
            if (fields[2].Contains('P')) flags |= BindingFlags.Public;
            if (fields[2].Contains('p')) flags |= BindingFlags.NonPublic;
            if (fields[2].Contains('S'))
                flags |= BindingFlags.Static;
            else
                flags |= BindingFlags.Instance;
            try
            {
                return type.GetMembers(flags).OfType<MethodInfo>().Where(m => m.Name == fields[1])
                    .First(m => paramTypes.SequenceEqual(m.CachedParameterTypes()));
            }
            catch (InvalidOperationException)
            {
                throw new MissingMethodException("Missing method: " + existingValue);
            }
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return typeof(MethodInfo).IsAssignableFrom(objectType);
        }
    }
}
