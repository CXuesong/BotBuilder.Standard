﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Sample.AspNetCore.AlarmBot.Models;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Internals.Fibers;
using System.Linq;

namespace Microsoft.Bot.Sample.AspNetCore.AlarmBot.Dialogs
{
    /// <summary>
    /// Entities for the built-in alarm LUIS model.
    /// </summary>
    public static partial class BuiltIn
    {
        public static partial class Alarm
        {
            public const string Alarm_State = "builtin.alarm.alarm_state";
            public const string Duration = "builtin.alarm.duration";
            public const string Start_Date = "builtin.alarm.start_date";
            public const string Start_Time = "builtin.alarm.start_time";
            public const string Title = "builtin.alarm.title";
        }
    }

    /// <summary>
    /// The top-level natural language dialog for the alarm sample.
    /// </summary>
    [DataContract]
    public sealed class AlarmLuisDialog : LuisDialog<object>
    {
        [DataMember] private readonly IAlarmService service;
        [DataMember] private readonly IEntityToType entityToType;
        [DataMember] private readonly IClock clock;
        public AlarmLuisDialog(IAlarmService service, IEntityToType entityToType, ILuisService luis, IClock clock)
            : base(luis)
        {
            SetField.NotNull(out this.service, nameof(service), service);
            SetField.NotNull(out this.entityToType, nameof(entityToType), entityToType);
            SetField.NotNull(out this.clock, nameof(clock), clock);
        }

        [LuisIntent("builtin.intent.none")]
        public async Task None(IDialogContext context, LuisResult result)
        {
            string message = $"Sorry I did not understand: " + string.Join(", ", result.Intents.Select(i => i.Intent));
            await context.PostAsync(message);
            context.Wait(MessageReceived);
        }

        public bool TryFindTitle(LuisResult result, out string title)
        {
            EntityRecommendation entity;
            if (result.TryFindEntity(BuiltIn.Alarm.Title, out entity))
            {
                title = entity.Entity;
                return true;
            }

            title = null;
            return false;
        }

        [LuisIntent("builtin.intent.alarm.delete_alarm")]
        public async Task DeleteAlarm(IDialogContext context, LuisResult result)
        {
            string title;
            TryFindTitle(result, out title);
            try
            {
                await this.service.DeleteAsync(title);
            }
            catch (AlarmNotFoundException)
            {
                await context.PostAsync("did not find alarm");
            }

            context.Wait(MessageReceived);
        }

        [LuisIntent("builtin.intent.alarm.find_alarm")]
        public async Task FindAlarm(IDialogContext context, LuisResult result)
        {
            string title;
            TryFindTitle(result, out title);
            try
            {
                await this.service.UpsertAsync(title, null, null);
            }
            catch (AlarmNotFoundException)
            {
                await context.PostAsync("did not find alarm");
            }

            context.Wait(MessageReceived);
        }

        [LuisIntent("builtin.intent.alarm.set_alarm")]
        public async Task SetAlarm(IDialogContext context, IAwaitable<IMessageActivity> activity, LuisResult result)
        {
            string title;
            bool? state = null;
            DateTime? when = null;

            TryFindTitle(result, out title);

            EntityRecommendation entity;
            if (result.TryFindEntity(BuiltIn.Alarm.Alarm_State, out entity))
            {
                state = entity.Entity.Equals("on", StringComparison.OrdinalIgnoreCase);
            }

            var now = this.clock.Now;

            IEnumerable<Range<DateTime>> ranges;
            if (entityToType.TryMapToDateRanges(now, result.Entities, out ranges))
            {
                using (var enumerator = ranges.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        when = enumerator.Current.Start;
                    }
                }
            }

            await this.service.UpsertAsync(title, when, state);

            context.Wait(MessageReceived);
        }

        [LuisIntent("builtin.intent.alarm.snooze")]
        public async Task AlarmSnooze(IDialogContext context, LuisResult result)
        {
            string title;
            TryFindTitle(result, out title);
            try
            {
                await this.service.SnoozeAsync(title);
            }
            catch (AlarmNotFoundException)
            {
                await context.PostAsync("did not find alarm");
            }

            context.Wait(MessageReceived);
        }

        [LuisIntent("builtin.intent.alarm.turn_off_alarm")]
        public async Task TurnOffAlarm(IDialogContext context, LuisResult result)
        {
            string title;
            TryFindTitle(result, out title);
            try
            {
                await this.service.UpsertAsync(title, null, state: false);
            }
            catch (AlarmNotFoundException)
            {
                await context.PostAsync("did not find alarm");
            }

            context.Wait(MessageReceived);
        }

        [LuisIntent("builtin.intent.alarm.time_remaining")]
        [LuisIntent("builtin.intent.alarm.alarm_other")]
        public async Task AlarmOther(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Sorry, I don't know how to handle that.");
            context.Wait(MessageReceived);
        }
    }
}