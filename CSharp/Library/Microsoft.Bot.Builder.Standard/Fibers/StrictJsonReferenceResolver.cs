using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Bot.Builder.Scorables.Internals;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Bot.Builder.Fibers
{
    /// <summary>
    /// This ReferenceResolver throws a KeyNotFoundException if the id specified in $ref does not exist.
    /// The default implementation (Newtonsoft.Json.Serialization.DefaultReferenceResolver) just silently passes null.
    /// This class is used for debugging.
    /// </summary>
    internal class StrictJsonReferenceResolver : IReferenceResolver
    {
        private readonly Dictionary<string, object> refValueDict = new Dictionary<string, object>();
        private readonly Dictionary<object, string> valueRefDict = new Dictionary<object, string>();

        private int counter = 1000;

        public StrictJsonReferenceResolver()
        {

        }

        /// <inheritdoc />
        public void AddReference(object context, string reference, object value)
        {
            // JsonReader
            refValueDict.Add(reference, value);
        }

        /// <inheritdoc />
        public object ResolveReference(object context, string reference)
        {
            // Called by JsonReader
            return refValueDict[reference];
        }

        /// <inheritdoc />
        public bool IsReferenced(object context, object value)
        {
            // JsonWriter
            return valueRefDict.ContainsKey(value);
        }

        /// <inheritdoc />
        public string GetReference(object context, object value)
        {
            // JsonWriter
            string result;
            // Try in the directionary first.
            if (!valueRefDict.TryGetValue(value, out result))
            {
                result = counter.ToString();
                counter++;
                valueRefDict.Add(value, result);
            }
            return result;
        }

        public void Clear()
        {
            valueRefDict.Clear();
            refValueDict.Clear();
            counter = 1000;
        }
    }
}
