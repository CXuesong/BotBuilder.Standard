# Notes on the .NET Core 2.0 Ported Bot Builder SDK

>   For Half Moon. Though I might have wandered much, much too far from the Tribe or the Clans.

This folder contains ported .NET Core 2.0 Bot Builder SDK. This ported library set is maintained by an individual and is not official, thus I DO NOT SUGGEST YOU TO USE IT IN PRODUCTION ENVIRONMENT. Still, well, PR is welcomed ​:joy_cat:​

You may install the SDK parts by running the following commands in the [Package Manager Console](https://docs.nuget.org/docs/start-here/using-the-package-manager-console)

​	To install `Microsoft.Bot.Connector` for ASP.NET Core

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Connector.AspNetCore -Pre
```

​	To install `Microsoft.Bot.Builder`

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Builder -Pre
Install-Package CXuesong.Ported.Microsoft.Bot.Builder.Autofac -Pre
```

​	To install `Microsoft.Bot.Builder.FormFlow.Json`

```powershell
Install-Package CXuesong.Ported.Microsoft.Bot.Builder.FormFlow.Json -Pre
```

Note: The packages since version 3.9.0-int0 targets .NET Standard/Core 2.0. Earlier versions targets 1.x and were you to use them, see [this note](README_PORTABLE.md) for reference.

This ported library set in this folder is not complete yet. For now, it contains

-   Microsoft.Bot.Connector.Standard
-   Microsoft.Bot.Connector.AspNetCore
-   Microsoft.Bot.Builder.Standard
-   Microsoft.Bot.Builder.Autofac.Standard
-   Microsoft.Bot.Builder.FormFlow.Json.Standard

I also ported some working examples to .NET Core 2.0. including

-   Microsoft.Bot.Sample.AspNetCore.SimpleEchoBot
-   Microsoft.Bot.Sample.AspNetCore.EchoBot
-   Microsoft.Bot.Sample.AspNetCore.AlarmBot
-   Microsoft.Bot.Sample.AspNetCore.PizzaBot
-   Microsoft.Bot.Sample.AspNetCore.AnnotatedSandwichBot

Since I haven't ported any of the unit tests, there might still exist bugs in the library. And it only supports .NET Core. Migration of Microsoft.Bot.Connector(.NetFramework) is needed before this ported library can be consumed on .NET Framework.

I set up the ASP.NET Core projects in VS 2017, so you may need VS 2017 to properly open the whole solution. The library targets at .NET Standard 2.0 (though some parts target at 1.6 / 1.4), so you need .NET Core 2.0 to consume the library. It seems very unlikely that this project would be made compatible with .NET Framework.

## Implementation Details

### Microsoft.Bot.Connector

Same as [.NET Core 1.x](README_PORTABLE.md#microsoftbotconnector).

### Microsoft.Bot.Builder & Microsoft.Bot.Builder.Autofac

This is still where the hard work lies. I had to try out the example projects, find the bugs, and fix them. Some bugs may not be so obvious, and I can not guarantee that I've fixed them all. If you find new bugs, please open an issue or PR. Thank you!

#### Things other than serialization

I have stripped `static` qualifier from `Microsoft.Bot.Builder.Dialogs.Conversation`, simply because it needs credential information, which cannot be loaded via `MicrosoftAppCredentials`'s parameterless constructor in .NET Core. Now `Conversation` should be registered as an singleton in `ConfigureServices`. You may take a look at [Samples/Microsoft.Bot.Sample.AspNetCore.EchoBot/Startup.cs](Samples/Microsoft.Bot.Sample.AspNetCore.EchoBot/Startup.cs). When you need  `Conversation`, just use DI.

I have ported Chronic to .NET Standard. The package is `CXuesong.Ported.Chronic`.

#### Things about serialization

This branch is forked from `portable` branch in the same repos, which targets at .NET Core 1.x. Before .NET Standard/Core 2.0, `BinaryFormatter` is not available, so [some effort](README_PORTABLE.md#things-about-serialization) has been taken to make the data store work, using `JsonSerializer`.

The primary problem is that, though re-introduced in 2.0, it seems that MS still [does not recommended](https://docs.microsoft.com/en-us/dotnet/standard/serialization/binary-serialization) to use `BinaryFormatter`. In the documentation, they say

>   As the nature of binary serialization allows the modification of private members inside an object and therefore changing the state of it, other serialization frameworks like JSON.NET which operate on the public API surface are recommended.

And by [making most of the core library types non-serializable](https://github.com/dotnet/corefx/issues/19119), it seems that the Binary Serialization is only intended to be used for compatibility purpose. From [the list of serializable types](https://docs.microsoft.com/en-us/dotnet/standard/serialization/binary-serialization#serializable-types) in MS's documentation, exceptions other than `Exception` and `AggregateException`, delegates, `Type`, `MemberInfo`, etc. are not serializable. So I wrote some `ISerializationSurrogate` implementations to take over the serialization logic for these types in [Microsoft.Bot.Builder.Standard/Fibers/NetStandardSerialization.cs](Library/Microsoft.Bot.Builder.Standard/Fibers/NetStandardSerialization.cs). Currently, the following types in CLR or FX are surrogated to be serializable

* Type (and derived types)
* MemberInfo (and derived types)
* Delegate (and derived types)
* Regex

If you are working with your own model classes, marking them with `[Serializable]` and optionally implementing `ISerializable` is enough.

If you used to be a .NET Core 1.x user of this ported library, no, there is no more `JsonSerializer`, `[DataContract]`, or `[DataMember]` now. Just do as what the [official documentation](https://docs.microsoft.com/en-us/bot-framework/dotnet/bot-builder-dotnet-dialogs#echo-bot-example) say: mark your dialog classes with `[Serializable]`, and use `[NonSerialized]` for fields if needed. Though there are limitations on supported serializable types, `BinaryFormatter` won't require you to have a special constructor or something else, and polymorphism is fully supported, so long as the type is serializable.

I hope you will be able to enjoy the Bot Framework on .NET Core, for one more time \*^_^\*

CXuesong a.k.a. forest93
