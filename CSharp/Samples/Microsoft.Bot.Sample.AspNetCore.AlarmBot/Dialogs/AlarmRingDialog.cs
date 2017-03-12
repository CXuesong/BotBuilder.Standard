﻿using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Bot.Sample.AspNetCore.AlarmBot.Models;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Internals.Fibers;

namespace Microsoft.Bot.Sample.AspNetCore.AlarmBot.Dialogs
{
    /// <summary>
    /// The child dialog called when an external event occurs (the passage of time, when the alarm rings)
    /// to proactively notify the user with a prompt to ask whether to snooze the alarm.
    /// </summary>
    [DataContract]
    public sealed class AlarmRingDialog : IDialog<object>
    {
        [DataMember] private readonly string title;
        [DataMember] private readonly IAlarmService service;
        [DataMember] private readonly IAlarmRenderer renderer;
        public AlarmRingDialog(string title, IAlarmService service, IAlarmRenderer renderer)
        {
            this.title = title;
            SetField.NotNull(out this.service, nameof(service), service);
            SetField.NotNull(out this.renderer, nameof(renderer), renderer);
        }

        async Task IDialog<object>.StartAsync(IDialogContext context)
        {
            await context.PostAsync($"The alarm with title '{this.title}' is alarming");
            PromptDialog.Confirm(context, AfterPromptForSnoozing, "Do you want to snooze this alarm?");
        }

        public async Task AfterPromptForSnoozing(IDialogContext context, IAwaitable<bool> snooze)
        {
            try
            {
                if (await snooze)
                {
                    await this.service.SnoozeAsync(this.title);
                }
                else
                {

                }
            }
            catch (TooManyAttemptsException)
            {
            }

            context.Done<object>(null);
        }
    }
}