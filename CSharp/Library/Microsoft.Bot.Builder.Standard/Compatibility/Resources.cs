using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text;

namespace Microsoft.Bot.Builder.Compatibility
{
    // CXuesong: Unfortunately, we do not have an IResourceWriter in .NET Standard 1.4,
    //           so we have to declare one to keep the abstraction.

    public interface IResourceWriter : IDisposable
    {
        void AddResource(string name, string value);

        void Generate();
    }

    internal class BuiltInResourceWriter : IResourceWriter
    {
        private readonly ResourceWriter writer;

        public BuiltInResourceWriter(ResourceWriter writer)
        {
            Debug.Assert(writer != null);
            this.writer = writer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            writer.Dispose();
        }

        /// <inheritdoc />
        public void AddResource(string name, string value)
        {
            writer.AddResource(name, value);
        }

        /// <inheritdoc />
        public void Generate()
        {
            writer.Generate();
        }
    }

    public static class ResourceUtility
    {
        /// <summary>
        /// Adapts the specified <see cref="ResourceWriter"/> into <see cref="IResourceWriter"/>
        /// that can be consumed by this library.
        /// </summary>
        public static IResourceWriter MakeCompatible(this ResourceWriter resourceWriter)
        {
            if (resourceWriter == null) throw new ArgumentNullException(nameof(resourceWriter));
            return new BuiltInResourceWriter(resourceWriter);
        }

        private static readonly MethodInfo ResourceManager_GetResourceSet;
        private static readonly MethodInfo ResourceSet_GetEnumerator;

        static ResourceUtility()
        {
            ResourceManager_GetResourceSet = typeof(ResourceManager).GetMethod("GetResourceSet");
            ResourceSet_GetEnumerator = ResourceManager_GetResourceSet.ReturnType.GetMethod("GetEnumerator");
        }

        public static IDictionaryEnumerator GetResourceSetEnumerator(this ResourceManager resourceManager, CultureInfo culture, bool createIfNotExists, bool tryParents)
        {
            var resourceSet = ResourceManager_GetResourceSet.Invoke(resourceManager,
                new object[] {culture, createIfNotExists, tryParents});
            return (IDictionaryEnumerator) ResourceSet_GetEnumerator.Invoke(resourceSet, null);
        }
    }
}
