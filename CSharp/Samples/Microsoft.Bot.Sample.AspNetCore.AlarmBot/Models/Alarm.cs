﻿using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Connector;

namespace Microsoft.Bot.Sample.AspNetCore.AlarmBot.Models
{
    [DataContract]
    public sealed class Alarm : IEquatable<Alarm>, IAlarmable
    {
        // these are the basic properties of an alarm
        [DataMember] public string Title { get; set; }
        [DataMember] public DateTime? When { get; set; }
        [DataMember] public bool State { get; set; }

        // these are the properties necessary to handle a external event and proactively post a message to the user
        public delegate Task NextDelegate(Alarm alarm, DateTime now, CancellationToken token);
        [DataMember] public NextDelegate Next { get; set; }
        [DataMember] public ConversationReference Cookie { get; set; }
        public override string ToString()
        {
            var state = this.State ? "enabled" : "disabled";
            return $"Alarm({this.Title}: {state} at {this.When})";
        }
        public bool Equals(Alarm other)
        {
            return other != null
                && object.Equals(this.Title, other.Title)
                && this.When == other.When
                && this.State == other.State;
        }
        public override bool Equals(object other)
        {
            return Equals(other as Alarm);
        }
        public override int GetHashCode()
        {
            return this.Title != null ? this.Title.GetHashCode() : 0;
        }
        bool IAlarmable.TryFindNext(DateTime now, out DateTime next)
        {
            if (this.State)
            {
                if (this.When.HasValue && now < this.When)
                {
                    next = this.When.Value;
                    return true;
                }
            }

            next = default(DateTime);
            return false;
        }
        async Task IAlarmable.NextAsync(DateTime now, CancellationToken token)
        {
            await this.Next(this, now, token);
        }
    }
}