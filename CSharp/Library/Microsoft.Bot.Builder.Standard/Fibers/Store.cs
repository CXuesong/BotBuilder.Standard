﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Bot Builder SDK GitHub:
// https://github.com/Microsoft/BotBuilder
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Bot.Builder.Fibers;
using Microsoft.Bot.Builder.Scorables.Internals;
using Newtonsoft.Json;

namespace Microsoft.Bot.Builder.Internals.Fibers
{

    //public sealed class FormatterStore<T> : IStore<T>
    //{
    //    private readonly Stream stream;
    //    private readonly IFormatter formatter;
    //    public FormatterStore(Stream stream, IFormatter formatter)
    //    {
    //        SetField.NotNull(out this.stream, nameof(stream), stream);
    //        SetField.NotNull(out this.formatter, nameof(formatter), formatter);
    //    }

    //    void IStore<T>.Reset()
    //    {
    //        this.stream.SetLength(0);
    //    }

    //    bool IStore<T>.TryLoad(out T item)
    //    {
    //        if (this.stream.Length > 0)
    //        {
    //            this.stream.Position = 0;
    //            using (var gzip = new GZipStream(this.stream, CompressionMode.Decompress, leaveOpen: true))
    //            {
    //                item = (T)this.formatter.Deserialize(gzip);
    //                return true;
    //            }
    //        }

    //        item = default(T);
    //        return false;
    //    }

    //    void IStore<T>.Save(T item)
    //    {
    //        this.stream.Position = 0;
    //        using (var gzip = new GZipStream(this.stream, CompressionMode.Compress, leaveOpen: true))
    //        {
    //            formatter.Serialize(gzip, item);
    //        }

    //        this.stream.SetLength(this.stream.Position);
    //    }

    //    void IStore<T>.Flush()
    //    {
    //        this.stream.Flush();
    //    }
    //}

    public sealed class DataContractStore<T> : IStore<T>
    {
        private readonly Stream stream;
        private readonly JsonSerializer serializer;

        public DataContractStore(Stream stream, IResolver resolver)
        {
            SetField.NotNull(out this.stream, nameof(stream), stream);
            SetField.CheckNull(nameof(resolver), resolver);

            // CXuesong: Tweak the settings later.
            //           - Done.
            serializer = new JsonSerializer
            {
                TypeNameHandling = TypeNameHandling.Objects,
                ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                Converters =
                {
                    DelegateJsonConverter.Default,
                    MethodInfoJsonConverter.Default,
                    new ResolvableObjectJsonConverter(resolver),
                    new RegexConverterEx(),
                },
            };
        }

        void IStore<T>.Reset()
        {
            this.stream.SetLength(0);
        }

        bool IStore<T>.TryLoad(out T item)
        {
            if (this.stream.Length > 0)
            {
                this.stream.Position = 0;
                using (var gzip = new GZipStream(this.stream, CompressionMode.Decompress, leaveOpen: true))
                using (var reader = new StreamReader(gzip, Encoding.UTF8, true, 1024, true))
                {
#if DEBUG
                    // For sake of debugging the JSON.
                    var s = reader.ReadToEnd();
                    using (var sr = new StringReader(s))
                        item = (T) serializer.Deserialize(sr, typeof(T));
#else

                    item = (T)serializer.Deserialize(reader, typeof(T));
#endif
                    return true;
                }
            }

            item = default(T);
            return false;
        }

        void IStore<T>.Save(T item)
        {
            // CXuesong: Hint
            //      T   Can be
            // Microsoft.Bot.Builder.Internals.Fibers.Fiber<Microsoft.Bot.Builder.Dialogs.Internals.DialogTask>
            this.stream.Position = 0;
            using (var gzip = new GZipStream(this.stream, CompressionMode.Compress, leaveOpen: true))
            using (var writer = new StreamWriter(gzip, Encoding.UTF8, 1024, true))
            {
#if DEBUG
                // For sake of debugging the JSON.
                using (var sw = new StringWriter())
                {
                    serializer.Serialize(sw, item);
                    writer.Write(sw.ToString());
                }
#else
                serializer.Serialize(writer, item);
#endif
            }

            this.stream.SetLength(this.stream.Position);
        }

        void IStore<T>.Flush()
        {
            this.stream.Flush();
        }
    }

    public sealed class ErrorResilientStore<T> : IStore<T>
    {
        private readonly IStore<T> store;

        public ErrorResilientStore(IStore<T> store)
        {
            SetField.NotNull(out this.store, nameof(store), store);
        }

        void IStore<T>.Reset()
        {
            this.store.Reset();
        }

        bool IStore<T>.TryLoad(out T item)
        {
            try
            {
                return this.store.TryLoad(out item);
            }
            catch (Exception)
            {
                // exception in loading the serialized data
                item = default(T);
                return false;
            }
        }

        void IStore<T>.Save(T item)
        {
            this.store.Save(item);
        }

        void IStore<T>.Flush()
        {
            this.store.Flush();
        }
    }

    public sealed class FactoryStore<T> : IStore<T>
    {
        private readonly IStore<T> store;
        private readonly Func<T> factory;

        public FactoryStore(IStore<T> store, Func<T> factory)
        {
            SetField.NotNull(out this.store, nameof(store), store);
            SetField.NotNull(out this.factory, nameof(factory), factory);
        }

        void IStore<T>.Reset()
        {
            this.store.Reset();
        }

        bool IStore<T>.TryLoad(out T item)
        {
            if (this.store.TryLoad(out item))
            {
                return true;
            }

            item = this.factory();
            return false;
        }

        void IStore<T>.Save(T item)
        {
            this.store.Save(item);
        }

        void IStore<T>.Flush()
        {
            this.store.Flush();
        }
    }
}
