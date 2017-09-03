﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Bot.Builder.Tests
{
    [TestClass]
    public sealed class DialogModuleTests : DialogTestBase
    {
        [TestMethod]
        public async Task BeginLifetimeScope_WithConfigurationAction()
        {
            var echo = Chain.PostToChain().Select(m => m.Text).PostToUser();

            using (var container = Build(Options.ResolveDialogFromContainer))
            {
                var builder = new ContainerBuilder();
                builder
                    .RegisterInstance(echo)
                    .As<IDialog<object>>();
                builder.Update(container);

                Func<ILifetimeScope, Task> Test = async s =>
                {
                    await AssertScriptAsync(s,
                        "hello",
                        "hello",
                        "world",
                        "world");
                };

                using (var scope = container.BeginLifetimeScope())
                {
                    await Test(scope);
                    await Test(scope);
                }

                using (var scope = container.BeginLifetimeScope(b => { }))
                {
                    await Test(scope);
                    await Test(scope);
                }

                using (var scope = container.BeginLifetimeScope())
                using (var inner = scope.BeginLifetimeScope(b => { }))
                {
                    await Test(inner);
                    await Test(inner);
                }

                using (var scope = container.BeginLifetimeScope(b => { }))
                using (var inner = scope.BeginLifetimeScope())
                {
                    await Test(inner);
                    await Test(inner);
                }
            }
        }

        public interface IService
        {
        }

        public sealed class Service : IService
        {
        }

        // "https://github.com/autofac/Autofac/issues/852"
        [TestMethod]
        [Ignore]
        public void BeginLifetimeScope_Overloads()
        {
            var builder = new ContainerBuilder();

            var key = new object();

            builder
                .RegisterInstance(new Service())
                .Keyed<IService>(key)
                .SingleInstance();

            builder
                .Register(c =>
                {
                    var registrations = c.ComponentRegistry.Registrations.ToArray();
                    return Tuple.Create(registrations.Length);
                })
                .AsSelf();

            using (var container = builder.Build())
            {
                using (var scope = container.BeginLifetimeScope())
                {
                    var tuple = scope.Resolve<Tuple<int>>();
                    Assert.AreEqual(3, tuple.Item1);
                }

                using (var scope = container.BeginLifetimeScope(b =>
                {
                    // nothing in particular
                }))
                {
                    var tuple = scope.Resolve<Tuple<int>>();
                    // tuple.Item1 is incorrectly 1 instead of 3
                    Assert.AreEqual(3, tuple.Item1);
                }
            }
        }
    }
}
