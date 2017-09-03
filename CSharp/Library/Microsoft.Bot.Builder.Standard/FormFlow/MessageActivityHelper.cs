﻿using Microsoft.Bot.Connector;

namespace Microsoft.Bot.Builder.FormFlow.Advanced
{
    internal class MessageActivityHelper
    {
        internal static string GetSanitizedTextInput(IMessageActivity activity)
        {
            var text = (activity != null ? activity.Text : null);

            var result = text == null ? "" : text.Trim();
            if (result.StartsWith("\""))
            {
                result = result.Substring(1);
            }
            if (result.EndsWith("\""))
            {
                result = result.Substring(0, result.Length - 1);
            }

            return result;
        }

        internal static IMessageActivity BuildMessageWithText(string text)
        {
            return new Activity
            {
                Type = ActivityTypes.Message,
                Text = text
            };
        }
    }
}
