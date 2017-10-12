﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Bot Builder SDK Github:
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

using Microsoft.Azure.Search;
using Microsoft.Bot.Connector;

namespace Microsoft.Bot.Sample.AspNetCore.SearchDialogs
{
    // TODO: dependency-inject a SearchIndexClient instance instead of this
    // static wrapper to give callers control over the search client
    public static class SearchDialogIndexClient
    {
        private static readonly ISearchIndexClient searchClient;
        private static SearchSchema schema;

        static SearchDialogIndexClient()
        {
            var indexName = SettingsUtils.GetAppSettings("SearchDialogsIndexName");
            var adminKey = SettingsUtils.GetAppSettings("SearchDialogsServiceAdminKey");
            if (adminKey != null)
            {
                var adminClient = new SearchServiceClient(SettingsUtils.GetAppSettings("SearchDialogsServiceName"),
                                                                      new SearchCredentials(adminKey));
                schema = new SearchSchema().AddFields(adminClient.Indexes.Get(indexName).Fields);
            }
            var client = new SearchServiceClient(SettingsUtils.GetAppSettings("SearchDialogsServiceName"),
                                                                 new SearchCredentials(SettingsUtils.GetAppSettings("SearchDialogsServiceKey")));
            searchClient = client.Indexes.GetClient(indexName);
        }

        public static ISearchIndexClient Client
        {
            get { return searchClient; }
        }

        public static SearchSchema Schema
        {
            get { return schema; }
            set { schema = value; }
        }
   }
}
