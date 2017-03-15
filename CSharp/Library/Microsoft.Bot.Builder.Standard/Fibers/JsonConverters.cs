using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Bot.Builder.Dialogs.Internals;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Scorables.Internals;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Bot.Builder.Fibers
{
    /// <summary>
    /// Return the resolved instance, if it can be resolved by type.
    /// This class is NOT thread-safe.
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
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            writer.WritePropertyName("$resolve");
            writer.WriteValue(value.GetType().AssemblyQualifiedName);
            writer.WriteEndObject();
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType != JsonToken.StartObject)
            {
                // It's not an object so we can fall back to default behavior.
                disabled = true;
                try
                {
                    //Debug.WriteLine("Fallback to default behavior: " + objectType + "; Path: " + reader.Path);
                    var result = serializer.Deserialize(reader, objectType);
                    Debug.Assert(!disabled);
                    return result;
                }
                finally
                {
                    disabled = false;
                }
            }
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
            // For AutoFac, since System.Object is always registered with all of the concrete classes automatically,
            // This situation can happen when there's a property with delaration value type of System.Object .
            // In this case, CanConvert(typeof(System.Object)) can return true, which is not what we want for the most cases.
            disabled = true;
            try
            {
                //Debug.WriteLine("Fallback to default behavior: " + objectType + "; Path: " + reader.Path);
                var result = serializer.Deserialize(token.CreateReader(), objectType);
                Debug.Assert(!disabled);
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
            // Typical types that return true
            // FrameFactory`1[DialogTask]
            // WaitFactory`1[DialogTask]
            // NullWait`1[DialogTask]
            if (disabled)
            {
                // This allows subsequent resolution of child nodes…
                disabled = false;
                return false;
            }
            var result = resolver.CanResolve(objectType, null);
            // Debug.WriteLineIf(result, "ResolvableObjectJsonConverter, Use IResolve: " + objectType);
            return result;
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

    // The following block may be removed after the release of Newtonsoft.Json 10.x
    /// <summary>
    /// Converts a <see cref="Regex"/> to and from JSON and BSON. This object is NOT thread-safe.
    /// </summary>
    // The following block is taken from the RC version of Newtonsoft.Json 10,
    // which allows Regex properties to be null.
    // See JamesNK/Newtonsoft.Json@b83e1fab4c3eb5074547bece3c1bfbefa2ac0a41 for more information.
    // https://github.com/JamesNK/Newtonsoft.Json/commit/b83e1fab4c3eb5074547bece3c1bfbefa2ac0a41
    // Also, this converter allows us to set a Regex instance to a property with declared type of Object.
    // The default imeplementation will deserialize such Regex into JObject…
    internal class RegexConverterEx : JsonConverter
    {
        private const string PatternName = "Pattern";
        private const string OptionsName = "Options";
        private const string RegexTypeName = "System.Text.RegularExpressions.Regex";
        private bool disabled;

        /// <summary>
        /// Writes the JSON representation of the object.
        /// </summary>
        /// <param name="writer">The <see cref="JsonWriter"/> to write to.</param>
        /// <param name="value">The value.</param>
        /// <param name="serializer">The calling serializer.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            Regex regex = (Regex)value;

#pragma warning disable 618
            BsonWriter bsonWriter = writer as BsonWriter;
            if (bsonWriter != null)
            {
                WriteBson(bsonWriter, regex);
            }
#pragma warning restore 618
            else
            {
                WriteJson(writer, regex, serializer);
            }
        }

        private bool HasFlag(RegexOptions options, RegexOptions flag)
        {
            return ((options & flag) == flag);
        }

#pragma warning disable 618
        private void WriteBson(BsonWriter writer, Regex regex)
        {
            // Regular expression - The first cstring is the regex pattern, the second
            // is the regex options string. Options are identified by characters, which 
            // must be stored in alphabetical order. Valid options are 'i' for case 
            // insensitive matching, 'm' for multiline matching, 'x' for verbose mode, 
            // 'l' to make \w, \W, etc. locale dependent, 's' for dotall mode 
            // ('.' matches everything), and 'u' to make \w, \W, etc. match unicode.

            string options = null;

            if (HasFlag(regex.Options, RegexOptions.IgnoreCase))
            {
                options += "i";
            }

            if (HasFlag(regex.Options, RegexOptions.Multiline))
            {
                options += "m";
            }

            if (HasFlag(regex.Options, RegexOptions.Singleline))
            {
                options += "s";
            }

            options += "u";

            if (HasFlag(regex.Options, RegexOptions.ExplicitCapture))
            {
                options += "x";
            }

            writer.WriteRegex(regex.ToString(), options);
        }
#pragma warning restore 618

        private void WriteJson(JsonWriter writer, Regex regex, JsonSerializer serializer)
        {
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
        }

        /// <summary>
        /// Reads the JSON representation of the object.
        /// </summary>
        /// <param name="reader">The <see cref="JsonReader"/> to read from.</param>
        /// <param name="objectType">Type of the object.</param>
        /// <param name="existingValue">The existing value of object being read.</param>
        /// <param name="serializer">The calling serializer.</param>
        /// <returns>The object value.</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    var obj = (JObject) JToken.ReadFrom(reader);
                    if ((string) obj["$type"] == RegexTypeName)
                        return ReadRegexObject(obj, serializer);
                    return FallbackDeserialization(obj.CreateReader(), objectType, serializer);
                case JsonToken.String:
                    if (objectType == typeof(Regex))
                        return ReadRegexString(reader);
                    return FallbackDeserialization(reader, objectType, serializer);
                case JsonToken.Null:
                    return null;
                default:
                    if (objectType != typeof(Regex))
                        return FallbackDeserialization(reader, objectType, serializer);
                    break;
            }
            throw new JsonSerializationException("Unexpected token when reading Regex.");
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

        private object ReadRegexString(JsonReader reader)
        {
            string regexText = (string)reader.Value;
            int patternOptionDelimiterIndex = regexText.LastIndexOf('/');

            string patternText = regexText.Substring(1, patternOptionDelimiterIndex - 1);
            string optionsText = regexText.Substring(patternOptionDelimiterIndex + 1);

            RegexOptions options = RegexOptions.None;
            foreach (char c in optionsText)
            {
                switch (c)
                {
                    case 'i':
                        options |= RegexOptions.IgnoreCase;
                        break;
                    case 'm':
                        options |= RegexOptions.Multiline;
                        break;
                    case 's':
                        options |= RegexOptions.Singleline;
                        break;
                    case 'x':
                        options |= RegexOptions.ExplicitCapture;
                        break;
                }
            }

            return new Regex(patternText, options);
        }

        private Regex ReadRegexObject(JObject obj, JsonSerializer serializer)
        {
            var pattern = (string) obj.GetValue(PatternName, StringComparison.OrdinalIgnoreCase);
            var options = obj.GetValue(OptionsName, StringComparison.OrdinalIgnoreCase);
            var optionsValue = options == null ? RegexOptions.None : serializer.Deserialize<RegexOptions>(options.CreateReader());
            return new Regex(pattern, optionsValue);
        }

        /// <summary>
        /// Determines whether this instance can convert the specified object type.
        /// </summary>
        /// <param name="objectType">Type of the object.</param>
        /// <returns>
        /// 	<c>true</c> if this instance can convert the specified object type; otherwise, <c>false</c>.
        /// </returns>
        public override bool CanConvert(Type objectType)
        {
            if (disabled)
            {
                disabled = false;
                return false;
            }
            return objectType == typeof(Regex) || objectType == typeof(object);
        }
    }
}
