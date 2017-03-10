// 
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
using Microsoft.Bot.Builder.Scorables.Internals;
using Microsoft.Bot.Connector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Compatibility;

namespace Microsoft.Bot.Builder.Scorables.Internals
{
    [DataContract]
    public abstract class AttributeString : Attribute, IEquatable<AttributeString>
    {
        protected abstract string Text { get; }

        public override string ToString()
        {
            return $"{this.GetType().Name}({this.Text})";
        }

        bool IEquatable<AttributeString>.Equals(AttributeString other)
        {
            return other != null
                && object.Equals(this.Text, other.Text);
        }

        public override bool Equals(object other)
        {
            return base.Equals(other as AttributeString);
        }

        public override int GetHashCode()
        {
            return this.Text.GetHashCode();
        }
    }
}

namespace Microsoft.Bot.Builder.Scorables
{
    /// <summary>
    /// This attribute is used to specify the regular expression pattern to be used 
    /// when applying the regular expression scorable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    [DataContract]
    public sealed class RegexPatternAttribute : AttributeString
    {
        /// <summary>
        /// The regular expression pattern.
        /// </summary>
        public readonly string Pattern;

        /// <summary>
        /// Construct the <see cref="RegexPatternAttribute"/>. 
        /// </summary>
        /// <param name="pattern">The regular expression pattern.</param>
        public RegexPatternAttribute(string pattern)
        {
            SetField.NotNull(out this.Pattern, nameof(pattern), pattern);
        }

        protected override string Text
        {
            get
            {
                return this.Pattern;
            }
        }
    }
}

namespace Microsoft.Bot.Builder.Scorables.Internals
{
    public sealed class RegexMatchScorableFactory : IScorableFactory<IResolver, ScorableMatch>
    {
        private readonly Func<string, Regex> make;

        public RegexMatchScorableFactory(Func<string, Regex> make)
        {
            SetField.NotNull(out this.make, nameof(make), make);
        }

        IScorable<IResolver, ScorableMatch> IScorableFactory<IResolver, ScorableMatch>.ScorableFor(IEnumerable<MethodInfo> methods)
        {
            var scorableByMethod = methods.ToDictionary(m => m, m => new MethodScorable(m));

            var specs =
                from method in scorableByMethod.Keys
                from pattern in InheritedAttributes.For<RegexPatternAttribute>(method)
                select new { method, pattern };

            // for a given regular expression pattern, fold the corresponding method scorables together to enable overload resolution
            var scorables =
                from spec in specs
                group spec by spec.pattern into patterns
                let method = patterns.Select(m => scorableByMethod[m.method]).ToArray().Fold(BindingComparer.Instance)
                let regex = this.make(patterns.Key.Pattern)
                select new RegexMatchScorable<IBinding, IBinding>(regex, method);

            var all = scorables.ToArray().Fold();

            return all;
        }
    }

    /// <summary>
    /// Static helper methods for RegexMatchScorable.
    /// </summary>
    public static partial class RegexMatchScorable
    {

        /// <summary>
        /// Calculate a normalized 0-1 score for a regular expression match.
        /// </summary>
        public static double ScoreFor(Match match, int originalTextLength)
        {
            var numerator = match.Value.Length;
            var denominator = originalTextLength;
            var score = ((double)numerator) / denominator;
            return score;
        }
    }

    /// <summary>
    /// Scorable to represent a regular expression match against an activity's text.
    /// </summary>
    [DataContract]
    public sealed class RegexMatchScorable<InnerState, InnerScore> : ResolverScorable<RegexMatchScorable<InnerState, InnerScore>.Scope, ScorableMatch, InnerState, InnerScore>
    {
        private readonly Regex regex;

        public sealed class Scope : ResolverScope<InnerScore>
        {
            public readonly Regex Regex;
            public readonly ScorableMatch Match;

            public Scope(Regex regex, ScorableMatch match, IResolver inner)
                : base(inner)
            {
                SetField.NotNull(out this.Regex, nameof(regex), regex);
                if (match.IsEmpty) throw new ArgumentNullException(nameof(match));
                Match = match;
            }

            public override bool TryResolve(Type type, object tag, out object value)
            {
                var name = tag as string;
                if (name != null)
                {
                    var capture = this.Match.Match.Groups[name];
                    if (capture != null && capture.Success)
                    {
                        if (type.IsAssignableFrom(typeof(Capture)))
                        {
                            value = capture;
                            return true;
                        }
                        else if (type.IsAssignableFrom(typeof(string)))
                        {
                            value = capture.Value;
                            return true;
                        }
                    }
                }

                if (type.IsAssignableFrom(typeof(Regex)))
                {
                    value = this.Regex;
                    return true;
                }

                if (type.IsAssignableFrom(typeof(Match)))
                {
                    value = this.Match;
                    return true;
                }

                var captures = this.Match.Match.Captures;
                if (type.IsAssignableFrom(typeof(CaptureCollection)))
                {
                    value = captures;
                    return true;
                }

                // i.e. for IActivity
                return base.TryResolve(type, tag, out value);
            }
        }

        public RegexMatchScorable(Regex regex, IScorable<IResolver, InnerScore> inner)
            : base(inner)
        {
            SetField.NotNull(out this.regex, nameof(regex), regex);
        }

        public override string ToString()
        {
            return $"{this.GetType().Name}({this.regex}, {this.inner})";
        }

        protected override async Task<Scope> PrepareAsync(IResolver resolver, CancellationToken token)
        {
            IMessageActivity message;
            if (!resolver.TryResolve(null, out message))
            {
                return null;
            }

            var text = message.Text;
            if (text == null)
            {
                return null;
            }

            var match = this.regex.Match(text);
            if (!match.Success)
            {
                return null;
            }

            var scope = new Scope(this.regex, new ScorableMatch(match, text.Length), resolver);
            scope.Item = resolver;
            scope.Scorable = this.inner;
            scope.State = await this.inner.PrepareAsync(scope, token);
            return scope;
        }

        protected override ScorableMatch GetScore(IResolver resolver, Scope state)
        {
            return state.Match;
        }
    }

    //public sealed class MatchComparer : IComparer<Match>
    //{
    //    public static readonly IComparer<Match> Instance = new MatchComparer();

    //    private MatchComparer()
    //    {
    //    }

    //    int IComparer<Match>.Compare(Match one, Match two)
    //    {
    //        Func<Match, Pair<bool, double>> PairFor = match => Pair.Create
    //        (
    //            match.Success,
    //            RegexMatchScorable.ScoreFor(match)
    //        );

    //        var pairOne = PairFor(one);
    //        var pairTwo = PairFor(two);
    //        return pairOne.CompareTo(pairTwo);
    //    }
    //}

    /// <summary>
    /// A combination of regex Match and original text length, used for scoring.
    /// </summary>
    public struct ScorableMatch : IEquatable<ScorableMatch>, IComparable<ScorableMatch>
    {
        public readonly Match Match;

        public readonly int OriginalTextLength;

        public readonly double Score;

        public bool IsEmpty => Match == null;

        public ScorableMatch(Match match, int originalTextLength)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (originalTextLength <= 0) throw new ArgumentOutOfRangeException(nameof(originalTextLength));
            Match = match;
            OriginalTextLength = originalTextLength;
            Score = RegexMatchScorable.ScoreFor(match, originalTextLength);
        }

        /// <inheritdoc />
        public bool Equals(ScorableMatch other)
        {
            return Equals(Match, other.Match) && OriginalTextLength == other.OriginalTextLength;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is ScorableMatch && Equals((ScorableMatch) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Match?.GetHashCode() ?? 0) * 397) ^ OriginalTextLength;
            }
        }

        /// <inheritdoc />
        public int CompareTo(ScorableMatch other)
        {
            // CXuesong: Referred from MatchComparer class.
            if (this.Match.Success != other.Match.Success) return this.Match.Success ? 1 : -1;
            return this.Score.CompareTo(other.Score);
        }
    }
}
