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

using System;
using System.Linq;
using Microsoft.Bot.Builder.Calling.ObjectModel.Misc;
using Newtonsoft.Json.Linq;

namespace Microsoft.Bot.Builder.Calling.ObjectModel.Contracts
{
    /// <summary>
    /// By default Json.net doesn't know how to deserialize JSON data into Interfaces or abstract classes.
    /// This custom Converter helps deserialize "actions" specified in JSON into respective concrete "action" classes.
    /// </summary>
    public class ActionConverter : JsonCreationConverter<ActionBase>
    {
        protected override ActionBase Create(Type objectType, JObject jsonObject)
        {
            var actionProperties = jsonObject.Properties().Where(p => p != null && p.Name != null && String.Equals(p.Name, "action", StringComparison.OrdinalIgnoreCase));
            string type = null;

            if (actionProperties.Count() == 1)
            {
                type = (string)actionProperties.First();
            }
            else
            {
                throw new ArgumentException(String.Format("Expected single action."));
            }

            if (String.Equals(type, ValidActions.AnswerAction, StringComparison.OrdinalIgnoreCase))
            {
                return new Answer();
            }
            else if (String.Equals(type, ValidActions.HangupAction, StringComparison.OrdinalIgnoreCase))
            {
                return new Hangup();
            }
            else if (String.Equals(type, ValidActions.RejectAction, StringComparison.OrdinalIgnoreCase))
            {
                return new Reject();
            }
            else if (String.Equals(type, ValidActions.PlayPromptAction, StringComparison.OrdinalIgnoreCase))
            {
                return new PlayPrompt();
            }
            else if (String.Equals(type, ValidActions.RecordAction, StringComparison.OrdinalIgnoreCase))
            {
                return new Record();
            }
            else if (String.Equals(type, ValidActions.RecognizeAction, StringComparison.OrdinalIgnoreCase))
            {
                return new Recognize();
            }

            throw new ArgumentException(String.Format("The given action '{0}' is not supported!", type));
        }
    }
}
