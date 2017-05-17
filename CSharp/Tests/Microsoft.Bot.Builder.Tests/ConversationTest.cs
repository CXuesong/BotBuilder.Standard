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

using Autofac;
using Microsoft.Bot.Builder.ConnectorEx;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Internals;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Connector;
using Microsoft.Rest;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.Bot.Builder.Tests
{
    public class MockConnectorFactory : IConnectorClientFactory
    {
        protected readonly IBotDataStore<BotData> memoryDataStore = new InMemoryDataStore();
        protected readonly string botId;
        public StateClient StateClient;

        public MockConnectorFactory(string botId)
        {
            SetField.NotNull(out this.botId, nameof(botId), botId);
        }

        public IConnectorClient MakeConnectorClient()
        {
            var client = new Mock<ConnectorClient>();
            client.CallBase = true;
            return client.Object;
        }

        public IStateClient MakeStateClient()
        {
            if (this.StateClient == null)
            {
                this.StateClient = MockIBots(this).Object;
            }
            return this.StateClient;
        }

        protected IAddress AddressFrom(string channelId, string userId, string conversationId)
        {
            var address = new Address
            (
                this.botId,
                channelId,
                userId ?? "AllUsers",
                conversationId ?? "AllConversations",
                "InvalidServiceUrl"
            );
            return address;
        }
        protected async Task<HttpOperationResponse<object>> UpsertData(string channelId, string userId, string conversationId, BotStoreType storeType, BotData data)
        {
            var _result = new HttpOperationResponse<object>();
            _result.Request = new HttpRequestMessage();
            try
            {
                var address = AddressFrom(channelId, userId, conversationId);
                await memoryDataStore.SaveAsync(address, storeType, data, CancellationToken.None);
            }
            catch (HttpException e)
            {
                _result.Body = e.Data;
                _result.Response = new HttpResponseMessage { StatusCode = HttpStatusCode.PreconditionFailed };
                return _result;
            }
            catch (Exception)
            {
                _result.Response = new HttpResponseMessage { StatusCode = HttpStatusCode.InternalServerError };
                return _result;
            }

            _result.Body = data;
            _result.Response = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            return _result;
        }

        protected async Task<HttpOperationResponse<object>> GetData(string channelId, string userId, string conversationId, BotStoreType storeType)
        {
            var _result = new HttpOperationResponse<object>();
            _result.Request = new HttpRequestMessage();
            BotData data;
            var address = AddressFrom(channelId, userId, conversationId);
            data = await memoryDataStore.LoadAsync(address, storeType, CancellationToken.None);
            _result.Body = data;
            _result.Response = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            return _result;
        }

        public Mock<StateClient> MockIBots(MockConnectorFactory mockConnectorFactory)
        {
            var botsClient = new Moq.Mock<StateClient>(MockBehavior.Loose);

            botsClient.Setup(d => d.BotState.SetConversationDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BotData>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, BotData, Dictionary<string, List<string>>, CancellationToken>(async (channelId, conversationId, data, headers, token) =>
                {
                    return await mockConnectorFactory.UpsertData(channelId, null, conversationId, BotStoreType.BotConversationData, data);
                });

            botsClient.Setup(d => d.BotState.GetConversationDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, Dictionary<string, List<string>>, CancellationToken>(async (channelId, conversationId, headers, token) =>
                {
                    return await mockConnectorFactory.GetData(channelId, null, conversationId, BotStoreType.BotConversationData);
                });


            botsClient.Setup(d => d.BotState.SetUserDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BotData>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
              .Returns<string, string, BotData, Dictionary<string, List<string>>, CancellationToken>(async (channelId, userId, data, headers, token) =>
              {
                  return await mockConnectorFactory.UpsertData(channelId, userId, null, BotStoreType.BotUserData, data);
              });

            botsClient.Setup(d => d.BotState.GetUserDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, Dictionary<string, List<string>>, CancellationToken>(async (channelId, userId, headers, token) =>
                {
                    return await mockConnectorFactory.GetData(channelId, userId, null, BotStoreType.BotUserData);
                });

            botsClient.Setup(d => d.BotState.SetPrivateConversationDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BotData>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
             .Returns<string, string, string, BotData, Dictionary<string, List<string>>, CancellationToken>(async (channelId, conversationId, userId, data, headers, token) =>
             {
                 return await mockConnectorFactory.UpsertData(channelId, userId, conversationId, BotStoreType.BotPrivateConversationData, data);
             });

            botsClient.Setup(d => d.BotState.GetPrivateConversationDataWithHttpMessagesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, List<string>>>(), It.IsAny<CancellationToken>()))
             .Returns<string, string, string, Dictionary<string, List<string>>, CancellationToken>(async (channelId, conversationId, userId, headers, token) =>
             {
                 return await mockConnectorFactory.GetData(channelId, userId, conversationId, BotStoreType.BotPrivateConversationData);
             });

            return botsClient;
        }
    }

    public class AlwaysNeedInputHintChannelCapability : IChannelCapability
    {
        private readonly IChannelCapability inner;
        public AlwaysNeedInputHintChannelCapability(IChannelCapability inner)
        {
            SetField.NotNull(out this.inner, nameof(inner), inner);
        }

        public bool NeedsInputHint()
        {
            return true;
        }

        public bool SupportsKeyboards(int buttonCount)
        {
            return this.inner.SupportsKeyboards(buttonCount);
        }

        public bool SupportsSpeak()
        {
            return this.inner.SupportsSpeak();
        }
    }

    public abstract class ConversationTestBase
    {
        [Flags]
        public enum Options { None, InMemoryBotDataStore, NeedsInputHint };

        public static IContainer Build(Options options, params object[] singletons)
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule(new DialogModule_MakeRoot());

            // make a "singleton" MockConnectorFactory per unit test execution
            IConnectorClientFactory factory = null;
            builder
                .Register((c, p) => factory ?? (factory = new MockConnectorFactory(c.Resolve<IAddress>().BotId)))
                .As<IConnectorClientFactory>()
                .InstancePerLifetimeScope();

            var r =
              builder
              .Register<Queue<IMessageActivity>>(c => new Queue<IMessageActivity>())
              .AsSelf()
              .InstancePerLifetimeScope();

            // truncate AlwaysSendDirect_BotToUser/IConnectorClient with null implementation
            builder
                .RegisterType<BotToUserQueue>()
                .Keyed<IBotToUser>(typeof(AlwaysSendDirect_BotToUser))
                .InstancePerLifetimeScope();

            if (options.HasFlag(Options.InMemoryBotDataStore))
            {
                //Note: memory store will be single instance for the bot
                builder.RegisterType<InMemoryDataStore>()
                    .AsSelf()
                    .SingleInstance();

                builder.Register(c => new CachingBotDataStore(c.Resolve<InMemoryDataStore>(), CachingBotDataStoreConsistencyPolicy.ETagBasedConsistency))
                    .As<IBotDataStore<BotData>>()
                    .AsSelf()
                    .InstancePerLifetimeScope();
            }

            if (options.HasFlag(Options.NeedsInputHint))
            {
                builder.Register(c => new AlwaysNeedInputHintChannelCapability(new ChannelCapability(c.Resolve<IAddress>())))
                    .AsImplementedInterfaces()
                    .InstancePerLifetimeScope();
            }

            foreach (var singleton in singletons)
            {
                builder
                    .Register(c => singleton)
                    .Keyed(FiberModule.Key_DoNotSerialize, singleton.GetType());
            }

            return builder.Build();
        }
    }


    [TestClass]
    public sealed class ConversationTest : ConversationTestBase
    {
        [TestMethod]
        public async Task InMemoryBotDataStoreTest()
        {
            var chain = Chain.PostToChain().Select(m => m.Text).ContinueWith<string, string>(async (context, result) =>
                {
                    int t = 0;
                    context.UserData.TryGetValue("count", out t);
                    if (t > 0)
                    {
                        int value;
                        Assert.IsTrue(context.ConversationData.TryGetValue("conversation", out value));
                        Assert.AreEqual(t - 1, value);
                        Assert.IsTrue(context.UserData.TryGetValue("user", out value));
                        Assert.AreEqual(t + 1, value);
                        Assert.IsTrue(context.PrivateConversationData.TryGetValue("PrivateConversationData", out value));
                        Assert.AreEqual(t + 2, value);
                    }

                    context.ConversationData.SetValue("conversation", t);
                    context.UserData.SetValue("user", t + 2);
                    context.PrivateConversationData.SetValue("PrivateConversationData", t + 3);
                    context.UserData.SetValue("count", ++t);
                    return Chain.Return($"{t}:{await result}");
                }).PostToUser();
            Func<IDialog<object>> MakeRoot = () => chain;

            using (new FiberTestBase.ResolveMoqAssembly(chain))
            using (var container = Build(Options.InMemoryBotDataStore, chain))
            {
                var msg = DialogTestBase.MakeTestMessage();
                msg.Text = "test";
                using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                {
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));

                    await Conversation.SendAsync(scope, msg);
                    var reply = scope.Resolve<Queue<IMessageActivity>>().Dequeue();
                    Assert.AreEqual("1:test", reply.Text);
                    var store = scope.Resolve<CachingBotDataStore>();
                    Assert.AreEqual(0, store.cache.Count);
                    var dataStore = scope.Resolve<InMemoryDataStore>();
                    Assert.AreEqual(3, dataStore.store.Count);
                }

                for (int i = 0; i < 10; i++)
                {
                    using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                    {
                        scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));
                        await Conversation.SendAsync(scope, msg);
                        var reply = scope.Resolve<Queue<IMessageActivity>>().Dequeue();
                        Assert.AreEqual($"{i + 2}:test", reply.Text);
                        var store = scope.Resolve<CachingBotDataStore>();
                        Assert.AreEqual(0, store.cache.Count);
                        var dataStore = scope.Resolve<InMemoryDataStore>();
                        Assert.AreEqual(3, dataStore.store.Count);
                        string val = string.Empty;
                        Assert.IsTrue(scope.Resolve<IBotData>().PrivateConversationData.TryGetValue(DialogModule.BlobKey, out val));
                        Assert.AreNotEqual(string.Empty, val);
                    }
                }
            }
        }

        [TestMethod]
        public async Task InputHintTest()
        {
            var chain = Chain.PostToChain().Select(m => m.Text).ContinueWith<string, string>(async (context, result) =>
            {
                var text = await result;
                if (text.ToLower().StartsWith("inputhint"))
                {
                    var reply = context.MakeMessage();
                    reply.Text = "reply";
                    reply.InputHint = InputHints.ExpectingInput;
                    await context.PostAsync(reply);
                    return Chain.Return($"{text}");
                }
                else if (!text.ToLower().StartsWith("reset"))
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await context.PostAsync($"message:{i}");
                    }
                    return Chain.Return($"{text}");
                }
                else
                {
                    return Chain.From(() => new PromptDialog.PromptConfirm("Are you sure you want to reset the count?",
                            "Didn't get that!", 3, PromptStyle.Keyboard)).ContinueWith<bool, string>(async (ctx, res) =>
                            {
                                string reply;
                                if (await res)
                                {
                                    ctx.UserData.SetValue("count", 0);
                                    reply = "Reset count.";
                                }
                                else
                                {
                                    reply = "Did not reset count.";
                                }
                                return Chain.Return(reply);
                            });
                }

            }).PostToUser();
            Func<IDialog<object>> MakeRoot = () => chain;

            using (new FiberTestBase.ResolveMoqAssembly(chain))
            using (var container = Build(Options.InMemoryBotDataStore | Options.NeedsInputHint, chain))
            {


                var msg = DialogTestBase.MakeTestMessage();
                msg.Text = "test";

                using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                {
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));
                    await Conversation.SendAsync(scope, msg);
                    var queue = scope.Resolve<Queue<IMessageActivity>>();
                    Assert.IsTrue(queue.Count > 0);
                    while (queue.Count > 0)
                    {
                        var toUser = queue.Dequeue();
                        if (queue.Count > 0)
                        {
                            Assert.IsTrue(toUser.InputHint == InputHints.IgnoringInput);
                        }
                        else
                        {
                            Assert.IsTrue(toUser.InputHint == InputHints.AcceptingInput);
                        }
                    }
                }


                msg.Text = "inputhint";
                using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                {
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));
                    await Conversation.SendAsync(scope, msg);
                    var queue = scope.Resolve<Queue<IMessageActivity>>();
                    Assert.IsTrue(queue.Count == 2);
                    var toUser = queue.Dequeue();
                    Assert.AreEqual("reply", toUser.Text);
                    Assert.IsTrue(toUser.InputHint == InputHints.ExpectingInput);
                }

                msg.Text = "reset";
                using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                {
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));
                    await Conversation.SendAsync(scope, msg);
                    var queue = scope.Resolve<Queue<IMessageActivity>>();
                    Assert.IsTrue(queue.Count == 1);
                    var toUser = queue.Dequeue();
                    Assert.IsTrue(toUser.InputHint == InputHints.ExpectingInput);
                    Assert.IsNotNull(toUser.LocalTimestamp);
                }

            }
        }


        [TestMethod]
        public async Task SendResumeAsyncTest()
        {
            var chain = Chain.PostToChain().Select(m => m.Text).Switch(
                new RegexCase<IDialog<string>>(new Regex("^resume"), (context, data) => { context.UserData.SetValue("resume", true); return Chain.Return("resumed!"); }),
                new DefaultCase<string, IDialog<string>>((context, data) => { return Chain.Return(data); })).Unwrap().PostToUser();

            using (new FiberTestBase.ResolveMoqAssembly(chain))
            using (var container = Build(Options.InMemoryBotDataStore, chain))
            {
                var msg = DialogTestBase.MakeTestMessage();
                msg.Text = "testMsg";

                using (var scope = DialogModule.BeginLifetimeScope(container, msg))
                {
                    Func<IDialog<object>> MakeRoot = () => chain;
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));

                    await Conversation.SendAsync(scope, msg);
                    var reply = scope.Resolve<Queue<IMessageActivity>>().Dequeue();

                    var botData = scope.Resolve<IBotData>();
                    await botData.LoadAsync(default(CancellationToken));
                    var dataBag = scope.Resolve<Func<IBotDataBag>>()();
                    Assert.IsTrue(dataBag.ContainsKey(ResumptionContext.RESUMPTION_CONTEXT_KEY));
                    Assert.IsNotNull(scope.Resolve<ConversationReference>());
                }

                var conversationReference = msg.ToConversationReference();
                var continuationMessage = conversationReference.GetPostToBotMessage();
                using (var scope = DialogModule.BeginLifetimeScope(container, continuationMessage))
                {
                    Func<IDialog<object>> MakeRoot = () => { throw new InvalidOperationException(); };
                    scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));

                    await scope.Resolve<IPostToBot>().PostAsync(new Activity { Text = "resume" }, CancellationToken.None);

                    var reply = scope.Resolve<Queue<IMessageActivity>>().Dequeue();
                    Assert.AreEqual("resumed!", reply.Text);

                    var botData = scope.Resolve<IBotData>();
                    await botData.LoadAsync(default(CancellationToken));
                    Assert.IsTrue(botData.UserData.GetValue<bool>("resume"));
                }
            }
        }
    }
}
