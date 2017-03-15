using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Scorables.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Bot.Builder.Fibers
{

    internal interface IContextualJsonConverter
    {
        bool CanConvert(Type objectType);

        bool TryConvertFromJson(JObject jobj, Type objectType, ref object value, JsonSerializer serializer);

        bool TryConvertToJson(JsonWriter writer, object value, JsonSerializer serializer);
    }

    internal class ContextualJsonConvertHandler : JsonConverter
    {
        private bool disabled;      // CXuesong: Looks not so pretty… But it works anyway.
        private int firstAbleConverterIndex = -1;

        public IList<IContextualJsonConverter> Converters { get; set; }

        public ContextualJsonConvertHandler(params IContextualJsonConverter[] converters)
        {
            Converters = converters;
        }

        public ContextualJsonConvertHandler(IList<IContextualJsonConverter> converters)
        {
            Converters = converters;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            if (value.GetType() == typeof(object))
            {
                // Rare case
                FallbackSerialization(writer, value, serializer);
                return;
            }
            for (int i = firstAbleConverterIndex; i < Converters.Count; i++)
            {
                if (Converters[i].TryConvertToJson(writer, value, serializer)) return;
            }
            FallbackSerialization(writer, value, serializer);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    return null;
                case JsonToken.StartObject:
                    var jobj = (JObject)JToken.ReadFrom(reader);
                    for (int i = firstAbleConverterIndex; i < Converters.Count; i++)
                    {
                        var value = existingValue;
                        if (Converters[i].TryConvertFromJson(jobj, objectType, ref value, serializer))
                            return value;
                    }
                    return FallbackDeserialization(jobj.CreateReader(), objectType, serializer);
            }
            return FallbackDeserialization(reader, objectType, serializer);
        }

        private void FallbackSerialization(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Debug.Assert(disabled == false);
            disabled = true;
            try
            {
                //Debug.WriteLine("Fallback to default behavior: " + objectType + "; Path: " + reader.Path);
                serializer.Serialize(writer, value);
                Debug.Assert(disabled == false);
            }
            finally
            {
                disabled = false;
            }
        }


        private object FallbackDeserialization(JsonReader reader, Type objectType, JsonSerializer serializer)
        {
            Debug.Assert(disabled == false);
            disabled = true;
            try
            {
                //Debug.WriteLine("Fallback to default behavior: " + objectType + "; Path: " + reader.Path);
                var result = serializer.Deserialize(reader, objectType);
                Debug.Assert(disabled == false);
                return result;
            }
            finally
            {
                disabled = false;
            }
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            if (disabled)
            {
                // This allows subsequent resolution of child nodes…
                disabled = false;
                return false;
            }
            if (objectType == typeof(object))
            {
                firstAbleConverterIndex = 0;
                return true;
            }
            for (int i = 0; i < Converters.Count; i++)
            {
                if (Converters[i].CanConvert(objectType))
                {
                    firstAbleConverterIndex = i;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Return the resolved instance, if it can be resolved by type.
    /// This class is NOT thread-safe.
    /// </summary>
    internal class ResolvableObjectJsonConverter : IContextualJsonConverter
    {
        private readonly IResolver resolver;

        public ResolvableObjectJsonConverter(IResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            this.resolver = resolver;
        }

        /// <inheritdoc />
        public bool CanConvert(Type objectType)
        {
            // In AutoFac, since System.Object is always registered with all of the concrete classes automatically,
            // This situation can happen when there's a property with delaration value type of System.Object .
            // In this case, CanConvert(typeof(System.Object)) can return true, which is not what we want for the most cases.

            // Typical types that return true
            // FrameFactory`1[DialogTask]
            // WaitFactory`1[DialogTask]
            // NullWait`1[DialogTask]
            var result = resolver.CanResolve(objectType, null);
            // Debug.WriteLineIf(result, "ResolvableObjectJsonConverter, Use IResolve: " + objectType);
            return result;
        }

        /// <inheritdoc />
        public bool TryConvertFromJson(JObject jobj, Type objectType, ref object value, JsonSerializer serializer)
        {
            var typeName = (string)jobj["$resolve"];
            if (typeName != null)
            {
                var type = Type.GetType(typeName, true);
                value = resolver.Resolve(type, null);
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public bool TryConvertToJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("$resolve");
            writer.WriteValue(value.GetType().AssemblyQualifiedName);
            writer.WriteEndObject();
            return true;
        }
    }

    /// <summary>
    /// A crude delegate serializer.
    /// </summary>
    internal class DelegateJsonConverter : JsonConverter
    {
        public static readonly DelegateJsonConverter Default = new DelegateJsonConverter();

        public class DelegateInfo : IEquatable<DelegateInfo>
        {
            public DelegateInfo(Type delegateType, MethodInfo method, object target)
            {
                if (delegateType == null) throw new ArgumentNullException(nameof(delegateType));
                if (method == null) throw new ArgumentNullException(nameof(method));
                Debug.Assert(method.IsStatic || target != null);
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
            public override string ToString()
            {
                return Method.DeclaringType + "|" + Method;
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
            if (reader.TokenType == JsonToken.Null) return null;
            var delegates = serializer.Deserialize<IEnumerable<DelegateInfo>>(reader);
            //Debug.WriteLine("DES: " + delegates.First());
            return Delegate.Combine(delegates.Select(di => di.ToDelegate()).ToArray());
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // TODO CXuesong: Implement the ability for user to add their custom JsonSerializer.
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            var type = value.GetType();
            var generated = type.GetTypeInfo().GetCustomAttribute<CompilerGeneratedAttribute>() != null;
            if (generated && !type.GetTypeInfo().IsSerializable) throw new ClosureCaptureException(value);
            var delegates = DelegateInfo.FromDelegate((Delegate)value);
            //Debug.WriteLine("SER: " + delegates.First());
            serializer.Serialize(writer, delegates);
        }
    }

    internal class MethodInfoJsonConverter : JsonConverter
    {
        public static readonly MethodInfoJsonConverter Default = new MethodInfoJsonConverter();

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            // Type|Method|BindingFlags||T1|T2|T3||A1|A2|A3
            var method = (MethodInfo) value;
            var sb = new StringBuilder(method.DeclaringType.AssemblyQualifiedName);
            sb.Append('|');
            sb.Append(method.Name);
            sb.Append('|');
            if (method.IsPublic) sb.Append('P');
            if (method.IsStatic) sb.Append('S');
            sb.Append("||");
            bool isFirst = true;
            if (method.IsGenericMethod)
            {
                foreach (var t in method.GetGenericArguments())
                {
                    if (isFirst) isFirst = false;
                    else sb.Append('|');
                    sb.Append(t.AssemblyQualifiedName);
                }
            }
            sb.Append("||");
            isFirst = true;
            foreach (var t in method.CachedParameterTypes())
            {
                if (isFirst) isFirst = false;
                else sb.Append('|');
                sb.Append(t.AssemblyQualifiedName);
            }
            writer.WriteValue(sb.ToString());
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var expr = reader.Value.ToString();
            var fields = expr.Split('|');
            var type = Type.GetType(fields[0], true);
            var flags = BindingFlags.DeclaredOnly;
            flags |= fields[2].Contains('P') ? BindingFlags.Public : BindingFlags.NonPublic;
            flags |= fields[2].Contains('S') ? BindingFlags.Static : BindingFlags.Instance;
            var methodName = fields[1];
            List<Type> genericArgs = null, args = null;
            foreach (var field in fields.Skip(4))
            {
                if (field == "")
                {
                    args = new List<Type>();
                    continue;
                }
                var t = Type.GetType(field, true);
                if (args != null)
                {
                    args.Add(t);
                }
                else
                {
                    if (genericArgs == null) genericArgs = new List<Type>();
                    genericArgs.Add(t);
                }
            }
            var genericArgsArray = genericArgs?.ToArray();
            try
            {
                return type.GetMembers(flags).OfType<MethodInfo>().Where(m => m.Name == methodName).Select(m =>
                {
                    if (m.IsGenericMethod != (genericArgs != null)) return null;
                    if (genericArgs != null && genericArgs.Count != m.GetGenericArguments().Length) return null;
                    try
                    {
                        var built = genericArgs != null ? m.MakeGenericMethod(genericArgsArray) : m;
                        return args.SequenceEqual(built.CachedParameterTypes()) ? built : null;
                    }
                    catch (ArgumentException)
                    {
                        return null;
                    }
                }).First(m => m != null);
            }
            catch (InvalidOperationException)
            {
                throw new MissingMethodException("Missing method: " + expr);
            }
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return typeof(MethodInfo).IsAssignableFrom(objectType);
        }
    }

    internal class ContextualRegexConverter : IContextualJsonConverter
    {
        private const string PatternName = "Pattern";
        private const string OptionsName = "Options";
        private const string RegexTypeName = "System.Text.RegularExpressions.Regex";

        public static readonly ContextualRegexConverter Default = new ContextualRegexConverter();

        private Regex ReadRegexObject(JObject obj, JsonSerializer serializer)
        {
            var pattern = (string) obj.GetValue(PatternName, StringComparison.OrdinalIgnoreCase);
            var options = obj.GetValue(OptionsName, StringComparison.OrdinalIgnoreCase);
            var optionsValue = options == null ? RegexOptions.None : serializer.Deserialize<RegexOptions>(options.CreateReader());
            return new Regex(pattern, optionsValue);
        }

        public bool CanConvert(Type objectType)
        {
            return objectType == typeof(Regex);
        }

        /// <inheritdoc />
        public bool TryConvertFromJson(JObject jobj, Type objectType, ref object value, JsonSerializer serializer)
        {
            if (objectType == typeof(Regex) || (string) jobj["$type"] == RegexTypeName)
            {
                value = ReadRegexObject(jobj, serializer);
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public bool TryConvertToJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var regex = (Regex) value;
            var resolver = serializer.ContractResolver as DefaultContractResolver;
            writer.WriteStartObject();
            if (serializer.TypeNameHandling != TypeNameHandling.None)
            {
                writer.WritePropertyName("$type");
                writer.WriteValue(RegexTypeName);
            }
            writer.WritePropertyName((resolver != null) ? resolver.GetResolvedPropertyName(PatternName) : PatternName);
            writer.WriteValue(regex.ToString());
            writer.WritePropertyName((resolver != null) ? resolver.GetResolvedPropertyName(OptionsName) : OptionsName);
            serializer.Serialize(writer, regex.Options);
            writer.WriteEndObject();
            return true;
        }
    }

    internal class ContextualEnumConverter : IContextualJsonConverter
    {
        public static readonly ContextualEnumConverter Default = new ContextualEnumConverter();

        /// <inheritdoc />
        public bool CanConvert(Type objectType)
        {
            return objectType.GetTypeInfo().IsEnum;
        }

        /// <inheritdoc />
        public bool TryConvertFromJson(JObject jobj, Type objectType, ref object value, JsonSerializer serializer)
        {
            var typeName = (string) jobj["$enum"];
            if (typeName == null) return false;
            var type = Type.GetType(typeName, true);
            value = Enum.Parse(type, (string) jobj["$value"], true);
            return true;
        }

        /// <inheritdoc />
        public bool TryConvertToJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            if (serializer.TypeNameHandling != TypeNameHandling.None)
            {
                writer.WritePropertyName("$enum");
                writer.WriteValue(value.GetType().AssemblyQualifiedName);
            }
            writer.WritePropertyName("$value");
            writer.WriteValue(Enum.Format(value.GetType(), value, "G"));
            writer.WriteEndObject();
            return true;
        }
    }
}

