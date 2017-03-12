using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Dialogs.Internals;
using Microsoft.Bot.Builder.Internals.Fibers;

namespace Microsoft.Bot.Sample.AspNetCore.AlarmBot.Models
{
    /// <summary>
    /// This service allows an alarm to be rendered to the user's conversational channel.
    /// </summary>
    public interface IAlarmRenderer
    {
        Task RenderAsync(IBotToUser botToUser, string title, DateTime now);
    }

    [DataContract]
    public sealed class AlarmRenderer : IAlarmRenderer
    {
        [DataMember] private readonly IAlarmScheduler scheduler;
        [DataMember] private readonly IAlarmActions actions;
        public AlarmRenderer(IAlarmScheduler scheduler, IAlarmActions actions)
        {
            SetField.NotNull(out this.scheduler, nameof(scheduler), scheduler);
            SetField.NotNull(out this.actions, nameof(actions), actions);
        }
        async Task IAlarmRenderer.RenderAsync(IBotToUser botToUser, string title, DateTime now)
        {
            Alarm alarm;
            if (this.scheduler.TryFindAlarm(title, out alarm))
            {
                var card = new HeroCard();
                card.Title = alarm.Title ?? "Default Alarm";
                card.Subtitle = alarm.State
                    ? (alarm.When.HasValue ? $"{alarm.When}" : "not set")
                    : "disabled";

                IAlarmable query = alarm;
                DateTime next;
                if (query.TryFindNext(now, out next))
                {
                    var remaining = next.Subtract(now);
                    bool today = now.Date == next.Date;
                    card.Text = $"There is {remaining:dd\\.hh\\:mm\\:ss} remaining before this alarm rings.";
                }

                var buttons = this.actions.ActionsFor(alarm);
                card.Buttons = buttons.ToArray();

                var message = botToUser.MakeMessage();
                message.Attachments = new[] { card.ToAttachment() };

                await botToUser.PostAsync(message);
            }
            else
            {
                throw new AlarmNotFoundException();
            }
        }
    }
}