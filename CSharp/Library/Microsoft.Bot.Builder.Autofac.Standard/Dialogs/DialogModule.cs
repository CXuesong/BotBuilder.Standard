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

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading;
using Autofac;
using Microsoft.Bot.Builder.Autofac.Base;
using Microsoft.Bot.Builder.Base;
using Microsoft.Bot.Builder.ConnectorEx;
using Microsoft.Bot.Builder.History;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Scorables;
using Microsoft.Bot.Builder.Scorables.Internals;
using Microsoft.Bot.Connector;
using Microsoft.Extensions.Configuration;
using Module = Autofac.Module;

namespace Microsoft.Bot.Builder.Dialogs.Internals
{
    /// <summary>
    /// Autofac module for Dialog components.
    /// </summary>
    public sealed class DialogModule : Module
    {
        public const string BlobKey = "DialogState";
        public static readonly object LifetimeScopeTag = typeof(DialogModule);

        public static readonly object Key_DeleteProfile_Regex = new object();
        public static readonly object Key_Dialog_Router = new object();

        private readonly MicrosoftAppCredentials credentials;
        private readonly IConfiguration configurationRoot;

        public DialogModule(MicrosoftAppCredentials credentials, IConfiguration configurationRoot)
        {
            // CXuesong: Inject configurationRoot for sake of ConnectorClientFactory.ctor.
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (configurationRoot == null) throw new ArgumentNullException(nameof(configurationRoot));
            this.credentials = credentials;
            this.configurationRoot = configurationRoot;
        }

        public static ILifetimeScope BeginLifetimeScope(ILifetimeScope scope, IMessageActivity message)
        {
            var inner = scope.BeginLifetimeScope(LifetimeScopeTag);
            inner.Resolve<IMessageActivity>(TypedParameter.From(message));
            return inner;
        }

        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);

            builder.RegisterModule(new FiberModule<DialogTask>());

            // singleton components

            builder
                .Register(c => new ResourceManager("Microsoft.Bot.Builder.Resource.Resources", typeof(Resource.Resources).GetTypeInfo().Assembly))
                .As<ResourceManager>()
                .SingleInstance();

            // every lifetime scope is driven by a message

            builder
                .Register((c, p) => p.TypedAs<IMessageActivity>())
                .AsSelf()
                .AsImplementedInterfaces()
                .InstancePerMatchingLifetimeScope(LifetimeScopeTag);

            // make the address and cookie available for the lifetime scope

            builder
                .Register(c => Address.FromActivity(c.Resolve<IActivity>()))
                .AsImplementedInterfaces()
                .InstancePerMatchingLifetimeScope(LifetimeScopeTag);

#pragma warning disable CS0618
            builder
                .RegisterType<ResumptionCookie>()
                .AsSelf()
                .InstancePerMatchingLifetimeScope(LifetimeScopeTag);
#pragma warning restore CS0618

            builder
                .Register(c => c.Resolve<IActivity>().ToConversationReference())
                .AsSelf()
                .InstancePerMatchingLifetimeScope(LifetimeScopeTag);

            // components not marked as [Serializable]
            // CXuesong: This is likely to cause null credential, because it simply doesn't load them in PCL.
            //builder
            //    .RegisterType<MicrosoftAppCredentials>()
            //    .AsSelf()
            //    .SingleInstance();
            // So it's injected from DialogModule.ctor.
            builder.RegisterInstance(credentials).SingleInstance();

            builder
                // not resolving IEqualityComparer<IAddress> from container because it's a very local policy
                // and yet too broad of an interface.  could explore using tags for registration overrides.
                .Register(c => new LocalMutualExclusion<IAddress>(new ConversationAddressComparer()))
                .As<IScope<IAddress>>()
                .SingleInstance();

            builder
                .Register(c => new ConnectorClientFactory(c.Resolve<IAddress>(), c.Resolve<MicrosoftAppCredentials>(),
                    configurationRoot))
                .As<IConnectorClientFactory>()
                .InstancePerLifetimeScope();

            builder
                .Register(c => c.Resolve<IConnectorClientFactory>().MakeConnectorClient())
                .As<IConnectorClient>()
                .InstancePerLifetimeScope();

            builder
                .Register(c => c.Resolve<IConnectorClientFactory>().MakeStateClient())
                .As<IStateClient>()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<ChannelCapability>()
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<ConnectorStore, IBotDataStore<BotData>>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<InMemoryDataStore, IBotDataStore<BotData>>()
                .SingleInstance();

            builder
                .RegisterKeyedType<CachingBotDataStore, IBotDataStore<BotData>>()
                .WithParameter((pi, c) => pi.ParameterType == typeof(CachingBotDataStoreConsistencyPolicy),
                                (pi, c) => CachingBotDataStoreConsistencyPolicy.ETagBasedConsistency)
                .InstancePerLifetimeScope();

            builder
                .RegisterAdapterChain<IBotDataStore<BotData>>
                (
                    typeof(ConnectorStore),
                    typeof(CachingBotDataStore)
                )
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<JObjectBotData, IBotData>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<DialogTaskManagerBotDataLoader, IBotData>()
                .InstancePerLifetimeScope();

            builder
                .RegisterAdapterChain<IBotData>
                (
                    typeof(JObjectBotData),
                    typeof(DialogTaskManagerBotDataLoader)
                )
                .InstancePerLifetimeScope();

            builder
                .Register((c, p) => new BotDataBagStream(p.TypedAs<IBotDataBag>(), p.TypedAs<string>()))
                .As<Stream>()
                .InstancePerDependency();

            builder
                .Register(c => new DialogTaskManager(DialogModule.BlobKey,
                                                     c.Resolve<JObjectBotData>(),
                                                     c.Resolve<IStackStoreFactory<DialogTask>>(),
                                                     c.Resolve<Func<IDialogStack, CancellationToken, IDialogContext>>(),
                                                     c.Resolve<IEventProducer<IActivity>>()))
                .AsSelf()
                .As<IDialogTaskManager>()
                .As<IDialogTasks>()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<DialogSystem>()
                .As<IDialogSystem>();

            builder
                .RegisterType<DialogContext>()
                .As<IDialogContext>()
                .InstancePerDependency();

            builder
                .Register(c =>
                {
                    var cc = c.Resolve<IComponentContext>();

                    Func<string, IBotDataBag, IStore<IFiberLoop<DialogTask>>> make = (taskId, botDataBag) =>
                    {
                        var stream = cc.Resolve<Stream>(TypedParameter.From(botDataBag), TypedParameter.From(taskId));
                        return cc.Resolve<IStore<IFiberLoop<DialogTask>>>(TypedParameter.From(stream));
                    };

                    return make;
                })
                .As<Func<string, IBotDataBag, IStore<IFiberLoop<DialogTask>>>>()
                .InstancePerDependency();


            builder.Register(c => c.Resolve<IDialogTaskManager>().DialogTasks[0])
                .As<IDialogStack>()
                .As<IDialogTask>()
                .InstancePerLifetimeScope();

            // Scorable implementing "/deleteprofile"
            builder
                .Register(c => new Regex("^(\\s)*/deleteprofile", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace))
                .Keyed<Regex>(Key_DeleteProfile_Regex)
                .SingleInstance();

            builder
                .Register(c => new DeleteProfileScorable(c.Resolve<IDialogStack>(), c.Resolve<IBotData>(), c.Resolve<IBotToUser>(), c.ResolveKeyed<Regex>(Key_DeleteProfile_Regex)))
                .As<IScorable<IActivity, double>>()
                .InstancePerLifetimeScope();

            // scorable implementing "end conversation"
            builder
                .RegisterInstance(EndConversationEvent.MakeScorable())
                .As<IScorable<IResolver, double>>()
                .SingleInstance();

            builder
                .Register(c =>
                {
                    var cc = c.Resolve<IComponentContext>();
                    Func<IActivity, IResolver> make = activity =>
                    {
                        var resolver = NoneResolver.Instance;
                        resolver = new EnumResolver(resolver);
                        resolver = new AutofacResolver(cc, resolver);
                        resolver = new ArrayResolver(resolver,
                            activity,
                            cc.Resolve<IBotToUser>(),
                            cc.Resolve<IBotData>(),
                            cc.Resolve<IDialogSystem>());
                        resolver = new ActivityResolver(resolver);
                        resolver = new EventActivityValueResolver(resolver);
                        resolver = new InvokeActivityValueResolver(resolver);
                        return resolver;
                    };
                    return make;
                })
                .AsSelf()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<DialogRouter>()
                .Keyed<IScorable<IActivity, double>>(Key_Dialog_Router)
                .InstancePerLifetimeScope();

            builder
                .RegisterType<EventQueue<IActivity>>()
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<ReactiveDialogTask>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder
                .Register(c => new ScoringEventLoop<double>(c.Resolve<ReactiveDialogTask>(), c.Resolve<ReactiveDialogTask>(), c.Resolve<IEventConsumer<IActivity>>(), c.ResolveKeyed<IScorable<IActivity, double>>(Key_Dialog_Router)))
                .As<IEventLoop>()
                .InstancePerLifetimeScope();

            // register IDataBag that is used for to load/save ResumptionData
            builder
                .Register(c =>
                {
                    var cc = c.Resolve<IComponentContext>();
                    Func<IBotDataBag> make = () =>
                    {
                        return cc.Resolve<IBotData>().PrivateConversationData;
                    };
                    return make;
                })
                .As<Func<IBotDataBag>>()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<ResumptionContext>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder
                .RegisterType<LocaleFinder>()
                .AsSelf()
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();

            // IPostToBot services

            builder
                .RegisterKeyedType<NullPostToBot, IPostToBot>()
                .SingleInstance();

            builder
                .RegisterKeyedType<PassPostToBot, IPostToBot>()
                .InstancePerDependency();

            builder
                .RegisterKeyedType<EventLoopDialogTask, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<PersistentDialogTask, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<ExceptionTranslationDialogTask, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<SerializeByConversation, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<SetAmbientThreadCulture, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<PostUnhandledExceptionToUser, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<LogPostToBot, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<QueueDrainingDialogTask, IPostToBot>()
                .InstancePerLifetimeScope();

            builder
                .RegisterAdapterChain<IPostToBot>
                (
                    typeof(EventLoopDialogTask),
                    typeof(SetAmbientThreadCulture),
                    typeof(QueueDrainingDialogTask),
                    typeof(PersistentDialogTask),
                    typeof(ExceptionTranslationDialogTask),
                    typeof(SerializeByConversation),
                    typeof(PostUnhandledExceptionToUser),
                    typeof(LogPostToBot)
                )
                .InstancePerLifetimeScope();

            // other

            builder
                .RegisterType<NullActivityLogger>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder
                .RegisterType<KeyboardCardMapper>()
                .AsImplementedInterfaces()
                .SingleInstance();

            builder
                .RegisterType<SetLocalTimestampMapper>()
                .AsImplementedInterfaces()
                .SingleInstance();

            // IBotToUser services
            builder
                .RegisterType<InputHintQueue>()
                .AsImplementedInterfaces()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<NullBotToUser, IBotToUser>()
                .SingleInstance();

            builder
                .RegisterKeyedType<PassBotToUser, IBotToUser>()
                .InstancePerDependency();

            builder
                .RegisterKeyedType<AlwaysSendDirect_BotToUser, IBotToUser>()
                .AsSelf()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<AutoInputHint_BotToUser, IBotToUser>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<MapToChannelData_BotToUser, IBotToUser>()
                .InstancePerLifetimeScope();

            builder
                .RegisterKeyedType<LogBotToUser, IBotToUser>()
                .InstancePerLifetimeScope();

#pragma warning disable CS1587
            /// <see cref="LogBotToUser"/> is composed around <see cref="MapToChannelData_BotToUser"/> is composed around
            /// <see cref="AlwaysSendDirect_BotToUser"/>.  The complexity of registering each component is pushed to a separate
            /// registration method, and each of these components are replaceable without re-registering
            /// the entire adapter chain by registering a new component with the same component key.
#pragma warning restore CS1587
            builder
                .RegisterAdapterChain<IBotToUser>
                (
                    typeof(AlwaysSendDirect_BotToUser),
                    typeof(AutoInputHint_BotToUser),
                    typeof(MapToChannelData_BotToUser),
                    typeof(LogBotToUser)
                )
                .InstancePerLifetimeScope();
        }
    }

    public sealed class DialogModule_MakeRoot : Module
    {
        private readonly MicrosoftAppCredentials credentials;
        private readonly IConfiguration configurationRoot;

        public DialogModule_MakeRoot(MicrosoftAppCredentials credentials, IConfiguration configurationRoot)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (configurationRoot == null) throw new ArgumentNullException(nameof(configurationRoot));
            this.credentials = credentials;
            this.configurationRoot = configurationRoot;
        }

        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);

            builder.RegisterModule(new DialogModule(credentials, configurationRoot));

            // TODO: let dialog resolve its dependencies from container
            builder
                .Register((c, p) => p.TypedAs<Func<IDialog<object>>>())
                .AsSelf()
                .InstancePerMatchingLifetimeScope(DialogModule.LifetimeScopeTag);
        }

        public static void Register(ILifetimeScope scope, Func<IDialog<object>> MakeRoot)
        {
            // TODO: let dialog resolve its dependencies from container
            scope.Resolve<Func<IDialog<object>>>(TypedParameter.From(MakeRoot));
        }
    }
}
