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

using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Luis.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.Bot.Builder.Luis.BuiltIn.DateTime;

namespace Microsoft.Bot.Builder.Luis
{
    /// <summary>
    /// An abtraction to map from a LUIS <see cref="EntityRecommendation"/> to specific CLR types. 
    /// </summary>
    public interface IEntityToType
    {
        /// <summary>
        /// Try to map LUIS <see cref="EntityRecommendation"/> instances to a <see cref="TimeSpan"/>, relative to now.
        /// </summary>
        /// <param name="now">The now reference <see cref="DateTime"/>.</param>
        /// <param name="entities">A list of possibly-relevant <see cref="EntityRecommendation"/> instances.</param>
        /// <param name="span">The output <see cref="TimeSpan"/>.</param>
        /// <returns>True if the mapping may have been successful, false otherwise.</returns>
        bool TryMapToTimeSpan(DateTime now, IEnumerable<EntityRecommendation> entities, out TimeSpan span);

        /// <summary>
        /// Try to map LUIS <see cref="EntityRecommendation"/> instances to a list of <see cref="DateTime"/> ranges, relative to now.
        /// </summary>
        /// <param name="now">The now reference <see cref="DateTime"/>.</param>
        /// <param name="entities">A list of possibly-relevant <see cref="EntityRecommendation"/> instances.</param>
        /// <param name="ranges">The output <see cref="DateTime"/> ranges.</param>
        /// <returns>True if the mapping may have been successful, false otherwise.</returns>
        bool TryMapToDateRanges(DateTime now, IEnumerable<EntityRecommendation> entities, out IEnumerable<Range<DateTime>> ranges);
    }

    public sealed class StrictEntityToType : IEntityToType
    {
        private readonly IResolutionParser parser;
        private readonly ICalendarPlus calendar;

        public StrictEntityToType(IResolutionParser parser, ICalendarPlus calendar)
        {
            SetField.NotNull(out this.parser, nameof(parser), parser);
            SetField.NotNull(out this.calendar, nameof(calendar), calendar);
        }

        bool IEntityToType.TryMapToDateRanges(DateTime now, IEnumerable<EntityRecommendation> entities, out IEnumerable<Range<DateTime>> ranges)
        {
            var resolutions = this.parser.ParseResolutions(entities);
            var dateTimes = resolutions.OfType<DateTimeResolution>().ToArray();

            if (dateTimes.Length > 0)
            {
                // possibly infinite
                var merged = dateTimes
                    .Select(r => Interpret(r, now, this.calendar.Calendar, this.calendar.WeekRule, this.calendar.FirstDayOfWeek, this.calendar.HourFor))
                    .Aggregate((l, r) => l.SortedMerge(r));

                ranges = merged;
                return true;
            }
            else
            {
                ranges = null;
                return false;
            }
        }

        bool IEntityToType.TryMapToTimeSpan(DateTime now, IEnumerable<EntityRecommendation> entities, out TimeSpan span)
        {
            span = default(TimeSpan);
            return false;
        }

        /// <summary>
        /// Interpret a parsed DateTimeResolution to provide a series of DateTime ranges
        /// </summary>
        /// <param name="resolution">The DateTimeResolution parsed from a LUIS response.</param>
        /// <param name="now">The reference point of "now".</param>
        /// <param name="calendar">The calendar to use for date math.</param>
        /// <param name="rule">The calendar week rule to use for date math.</param>
        /// <param name="firstDayOfWeek">The first day of the week to use for date math.</param>
        /// <param name="HourFor">The hour that corresponds to the <see cref="DayPart"/> enumeration.</param>
        /// <returns>A potentially infinite series of DateTime ranges.</returns>
        public static IEnumerable<Range<DateTime>> Interpret(DateTimeResolution resolution, DateTime now, Calendar calendar, CalendarWeekRule rule, DayOfWeek firstDayOfWeek, Func<DayPart, int> HourFor)
        {
            // remove any millisecond components
            now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);

            switch (resolution.Reference)
            {
                case Reference.PAST_REF:
                    yield return Range.From(DateTime.MinValue, now);
                    yield break;
                case Reference.PRESENT_REF:
                    yield return Range.From(now, now);
                    yield break;
                case Reference.FUTURE_REF:
                    yield return Range.From(now, DateTime.MaxValue);
                    yield break;
                case null:
                    break;
                default:
                    throw new NotImplementedException();
            }

            var start = now;

            // TODO: maybe clamp to prevent divergence
            while (start < DateTime.MaxValue)
            {
                var after = start;

                while (true)
                {
                    // for each date component in decreasing order of significance:
                    // if it's not a variable (-1) or missing (null) component, then
                    //      add a unit of that component to "start"
                    //      round down to the component's granularity
                    //      calculate the "after" based on the size of that component

                    if (resolution.Year >= 0 && start.Year != resolution.Year)
                    {
                        if (start.Year < resolution.Year)
                        {
                            start = start.AddYears(1);
                            start = new DateTime(start.Year, 1, 1, 0, 0, 0, 0, start.Kind);
                            after = start.AddYears(1);
                            continue;
                        }
                        else
                        {
                            yield break;
                        }
                    }

                    if (resolution.Month >= 0 && start.Month != resolution.Month)
                    {
                        start = start.AddMonths(1);
                        start = new DateTime(start.Year, start.Month, 1, 0, 0, 0, 0, start.Kind);
                        after = start.AddMonths(1);
                        continue;
                    }

                    var week = calendar.GetWeekOfYear(start, rule, firstDayOfWeek);
                    if (resolution.Week >= 0 && week != resolution.Week)
                    {
                        start = start.AddDays(7);
                        start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);

                        while (start.DayOfWeek != firstDayOfWeek)
                        {
                            start = start.AddDays(-1);
                        }

                        after = start.AddDays(7);
                        continue;
                    }

                    if (resolution.DayOfWeek != null && start.DayOfWeek != resolution.DayOfWeek)
                    {
                        start = start.AddDays(1);
                        start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);
                        after = start.AddDays(1);
                        continue;
                    }

                    if (resolution.Day >= 0 && start.Day != resolution.Day)
                    {
                        start = start.AddDays(1);
                        start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);
                        after = start.AddDays(1);
                        continue;
                    }

                    if (resolution.DayPart != null && start.Hour != HourFor(resolution.DayPart.Value))
                    {
                        var hourStart = HourFor(resolution.DayPart.Value);
                        var hourAfter = HourFor(resolution.DayPart.Value.Next());
                        var hourDelta = hourAfter - hourStart;
                        if (hourDelta < 0)
                        {
                            hourDelta += 24;
                        }

                        start = start.AddHours(1);
                        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, 0, start.Kind);
                        after = start.AddHours(hourDelta);
                        continue;
                    }

                    if (resolution.Hour >= 0 && start.Hour != resolution.Hour)
                    {
                        start = start.AddHours(1);
                        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, 0, start.Kind);
                        after = start.AddHours(1);
                        continue;
                    }

                    if (resolution.Minute >= 0 && start.Minute != resolution.Minute)
                    {
                        start = start.AddMinutes(1);
                        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, 0, start.Kind);
                        after = start.AddMinutes(1);
                        continue;
                    }

                    if (resolution.Second >= 0 && start.Second != resolution.Second)
                    {
                        start = start.AddSeconds(1);
                        start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second, 0, start.Kind);
                        after = start.AddSeconds(1);
                        continue;
                    }

                    // if all of the components were variable or missing,
                    // then in order of increasing component granularity,
                    // if the component is variable rather than missing, then increment by that granularity
                    if (start == after)
                    {
                        if (resolution.Second < 0)
                        {
                            after = start.AddSeconds(1);
                        }
                        else if (resolution.Minute < 0)
                        {
                            after = start.AddMinutes(1);
                        }
                        else if (resolution.Hour < 0)
                        {
                            after = start.AddHours(1);
                        }
                        else if (resolution.Day < 0)
                        {
                            after = start.AddDays(1);
                        }
                        else if (resolution.Week < 0)
                        {
                            after = start.AddDays(7);
                        }
                        else if (resolution.Month < 0)
                        {
                            after = start.AddMonths(1);
                        }
                        else if (resolution.Year < 0)
                        {
                            after = start.AddYears(1);
                        }
                        else
                        {
                            // a second is our minimum granularity
                            after = start.AddSeconds(1);
                        }
                    }

                    if (start >= now)
                    {
                        yield return new Range<DateTime>(start, after);
                    }

                    start = after;
                }
            }
        }
    }
}
